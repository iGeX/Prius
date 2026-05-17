using System.Collections.Generic;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class BinaryManager : IBinaryManager, IDisposable
{
    private sealed class Node
    {
        public MapValue Metadata;
        public byte[]? Data;
        public Guid? TempFileId;
        public DateTime LastAccessed;
    }

    private readonly Dictionary<string, Node> _nodes = new();
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly string? _tempPath;
    private readonly CancellationTokenSource _cts = new();
    private readonly ITimeProvider _timeProvider;
    private readonly TimeSpan _spillInterval;
    private readonly long _maxMemory;

    public BinaryManager(string? tempPath = null, ITimeProvider? timeProvider = null, TimeSpan? spillInterval = null, long maxMemory = 10 * 1024 * 1024)
    {
        _tempPath = tempPath;
        _timeProvider = timeProvider ?? new DefaultTimeProvider();
        _spillInterval = spillInterval ?? TimeSpan.FromMinutes(1);
        _maxMemory = maxMemory;
        
        if (!string.IsNullOrEmpty(_tempPath))
            Directory.CreateDirectory(_tempPath);
        
        Task.Run(() => SpillerLoop(_cts.Token));
    }

    private bool TryDeleteFile(Node node)
    {
        if (!node.TempFileId.HasValue || string.IsNullOrEmpty(_tempPath)) return true;

        var path = Path.Combine(_tempPath, $"{node.TempFileId}.bin");
        for (var i = 0; i < 3; i++)
        {
            try { File.Delete(path); return true; }
            catch { Thread.Sleep(TimeSpan.FromMilliseconds(50 * Math.Pow(2, i))); }
        }
        
        lock (node) { node.LastAccessed = _timeProvider.UtcNow; }
        return false;
    }

    public void ForceSpill()
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var node in _nodes.Values)
            {
                lock (node)
                {
                    if (node.Data != null)
                    {
                        if (!string.IsNullOrEmpty(_tempPath))
                        {
                            var id = Guid.NewGuid();
                            File.WriteAllBytes(Path.Combine(_tempPath, $"{id}.bin"), node.Data);
                            node.TempFileId = id;
                            node.Data = null;
                        }
                        else
                        {
                            node.Data = null;
                        }
                    }
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private async Task SpillerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(_spillInterval, ct);
            
            _lock.EnterReadLock();
            var targets = new List<Node>();
            try
            {
                foreach (var node in _nodes.Values)
                {
                    lock (node)
                    {
                        if (_timeProvider.UtcNow - node.LastAccessed > _spillInterval && node.Data != null)
                            targets.Add(node);
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }

            foreach (var node in targets)
            {
                lock (node)
                {
                    if (node.Data != null)
                    {
                        if (!string.IsNullOrEmpty(_tempPath))
                        {
                            var id = Guid.NewGuid();
                            try { File.WriteAllBytes(Path.Combine(_tempPath, $"{id}.bin"), node.Data); }
                            catch { node.LastAccessed = _timeProvider.UtcNow; continue; }
                            
                            node.TempFileId = id;
                            node.Data = null;
                        }
                        else
                        {
                            node.Data = null;
                        }
                    }
                }
            }
        }
    }

    public void Store(MapPath path, MapValue metadata, Stream stream)
    {
        using (stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            var data = ms.ToArray();
            var node = new Node { Metadata = metadata, Data = data, LastAccessed = _timeProvider.UtcNow };
            
            _lock.EnterWriteLock();
            try { _nodes[path.ToString()] = node; }
            finally { _lock.ExitWriteLock(); }
        }
    }

    public void Delete(MapPath path)
    {
        var key = path.ToString();
        _lock.EnterReadLock();
        Node? node;
        try { _nodes.TryGetValue(key, out node); }
        finally { _lock.ExitReadLock(); }

        if (node == null) return;

        if (!TryDeleteFile(node)) return;

        _lock.EnterWriteLock();
        try { _nodes.Remove(key); }
        finally { _lock.ExitWriteLock(); }
    }

    public IBinaryAccessor Get(MapPath path) => new Accessor(this, path.ToString(), _tempPath, _maxMemory);

    private sealed class Accessor(BinaryManager manager, string path, string? tempPath, long maxMemory) : IBinaryAccessor
    {
        public MapValue Metadata
        {
            get
            {
                manager._lock.EnterReadLock();
                try { return manager._nodes.TryGetValue(path, out var n) ? n.Metadata : Empty.Instance; }
                finally { manager._lock.ExitReadLock(); }
            }
        }

        public bool Exists
        {
            get
            {
                manager._lock.EnterReadLock();
                try { return manager._nodes.ContainsKey(path); }
                finally { manager._lock.ExitReadLock(); }
            }
        }

        public Stream OpenStream()
        {
            manager._lock.EnterReadLock();
            Node? node;
            try { node = manager._nodes.GetValueOrDefault(path); }
            finally { manager._lock.ExitReadLock(); }

            if (node == null) throw new InvalidOperationException("Node not found");
            
            lock (node)
            {
                node.LastAccessed = manager._timeProvider.UtcNow;
                if (node.Data != null) return new MemoryStream(node.Data);
                
                if (node.TempFileId.HasValue && !string.IsNullOrEmpty(tempPath))
                {
                    var filePath = Path.Combine(tempPath, $"{node.TempFileId}.bin");
                    var data = File.ReadAllBytes(filePath);
                    
                    if (data.Length <= maxMemory)
                    {
                        node.Data = data;
                        manager.TryDeleteFile(node);
                        node.TempFileId = null;
                        return new MemoryStream(node.Data);
                    }
                    return File.OpenRead(filePath);
                }
                throw new InvalidOperationException("Binary data not available");
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _lock.Dispose();
    }
}
