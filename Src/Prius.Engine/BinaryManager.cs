using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using System.Collections.Concurrent;

namespace Prius.Engine;

public sealed class BinaryManager : IBinaryManager, IDisposable
{
    private sealed class Node : IDisposable
    {
        public readonly object SyncRoot = new();
        public MapValue Metadata;
        public byte[]? Data;
        public Guid? TempFileId;
        public DateTime LastAccessed;
        public bool IsDisposed;

        public void Dispose() {}
    }

    private readonly ConcurrentDictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly string? _tempPath;
    private readonly CancellationTokenSource _cts = new();
    private readonly ITimeProvider _timeProvider;
    private readonly TimeSpan _spillInterval;
    private readonly long _maxMemory;
    
    private long _currentMemory;

    public BinaryManager(string? tempPath = null, ITimeProvider? timeProvider = null, TimeSpan? spillInterval = null, long maxMemory = 1024L * 1024 * 1024)
    {
        _tempPath = tempPath;
        _timeProvider = timeProvider ?? new DefaultTimeProvider();
        _spillInterval = spillInterval ?? TimeSpan.FromMinutes(5);
        _maxMemory = maxMemory;
        
        if (!string.IsNullOrEmpty(_tempPath))
            Directory.CreateDirectory(_tempPath);
        
        Task.Factory.StartNew(() => SpillerLoop(_cts.Token), _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    public void ForceSpill()
    {
        var nodes = _nodes.Values.ToList();
        foreach (var node in nodes)
            SpillNodeToDisk(node);
    }

    private void SpillNodeToDisk(Node node)
    {
        lock (node.SyncRoot)
        {
            if (node.IsDisposed || node.Data == null || string.IsNullOrEmpty(_tempPath)) return;

            var id = Guid.NewGuid();
            var filePath = Path.Combine(_tempPath, $"{id}.bin");

            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan)) fileStream.Write(node.Data, 0, node.Data.Length);

                node.TempFileId = id;
                Interlocked.Add(ref _currentMemory, -node.Data.Length);
                node.Data = null;
            }
            catch
            {
                TryDeleteFile(filePath);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        for (var i = 0; i < 3; i++)
        {
            try { if (File.Exists(path)) File.Delete(path); return; }
            catch { Thread.Sleep(10); }
        }
    }

    private async Task SpillerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_spillInterval, ct);

                var now = _timeProvider.UtcNow;
                var targets = _nodes.Values.Where(n => n.Data != null && now - n.LastAccessed > _spillInterval).ToList();

                foreach (var node in targets) SpillNodeToDisk(node);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                //ignored
            }
        }
    }

    public void Store(MapPath path, MapValue metadata, Stream stream)
    {
        var key = path.ToString();
        byte[] data;
        
        using (stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            data = ms.ToArray();
        }

        var node = new Node
        {
            Metadata = metadata,
            LastAccessed = _timeProvider.UtcNow
        };

        var totalAllocated = Interlocked.Add(ref _currentMemory, data.Length);
        if (totalAllocated <= _maxMemory)
            node.Data = data;
        else
        {
            Interlocked.Add(ref _currentMemory, -data.Length);
            if (!string.IsNullOrEmpty(_tempPath))
            {
                var diskFileId = Guid.NewGuid();
                var filePath = Path.Combine(_tempPath, $"{diskFileId}.bin");
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan)) fileStream.Write(data, 0, data.Length);
                node.TempFileId = diskFileId;
            }
            else 
            {
                node.Data = data;
                Interlocked.Add(ref _currentMemory, data.Length);
            }
        }

        _nodes.AddOrUpdate(key, node, (_, oldNode) =>
        {
            RemoveNode(oldNode);
            return node;
        });
    }

    private void RemoveNode(Node node)
    {
        lock (node.SyncRoot)
        {
            node.IsDisposed = true;
            if (node.Data != null)
            {
                Interlocked.Add(ref _currentMemory, -node.Data.Length);
                node.Data = null;
            }
            if (node.TempFileId.HasValue && !string.IsNullOrEmpty(_tempPath))
            {
                TryDeleteFile(Path.Combine(_tempPath, $"{node.TempFileId.Value}.bin"));
                node.TempFileId = null;
            }
        }
    }

    public void Delete(MapPath path)
    {
        if (_nodes.TryRemove(path.ToString(), out var node)) RemoveNode(node);
    }

    public IBinaryAccessor Get(MapPath path) => new Accessor(this, path.ToString());

    private sealed class Accessor(BinaryManager manager, string path) : IBinaryAccessor
    {
        public MapValue Metadata => manager._nodes.TryGetValue(path, out var n) ? n.Metadata : Empty.Instance;

        public bool Exists => manager._nodes.ContainsKey(path);

        public Stream OpenStream()
        {
            if (!manager._nodes.TryGetValue(path, out var node))
                throw new InvalidOperationException("Node not found");

            lock (node.SyncRoot)
            {
                if (node.IsDisposed)
                    throw new InvalidOperationException("Node is disposed");

                node.LastAccessed = manager._timeProvider.UtcNow;
                if (node.Data != null) 
                    return new MemoryStream(node.Data);

                if (!node.TempFileId.HasValue || string.IsNullOrEmpty(manager._tempPath))
                    throw new InvalidOperationException("Binary data not available");

                var filePath = Path.Combine(manager._tempPath, $"{node.TempFileId.Value}.bin");
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                    throw new InvalidOperationException("Binary data not available");

                if (Interlocked.Read(ref manager._currentMemory) + fileInfo.Length > manager._maxMemory) return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 4096, FileOptions.SequentialScan);

                byte[] data;
                try 
                {
                    data = File.ReadAllBytes(filePath);
                } 
                catch (Exception) 
                {
                    throw new InvalidOperationException("Binary data not available");
                }

                node.Data = data;
                TryDeleteFile(filePath);
                node.TempFileId = null;
                Interlocked.Add(ref manager._currentMemory, data.Length);
                return new MemoryStream(node.Data);
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        
        var nodes = _nodes.Values.ToList();
        _nodes.Clear();

        foreach (var node in nodes) 
            RemoveNode(node);

        if (string.IsNullOrEmpty(_tempPath) || !Directory.Exists(_tempPath)) 
            return;
        
        try
        {
            Directory.Delete(_tempPath, true);
        } 
        catch
        {
            //ignored
        }
    }
}