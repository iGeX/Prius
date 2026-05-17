using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class BinaryManager : IBinaryManager, IDisposable
{
    private sealed class Node : IDisposable
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public MapValue Metadata;
        public byte[]? Data;
        public Guid? TempFileId;
        public DateTime LastAccessed;

        public void Dispose() => Semaphore.Dispose();
    }

    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    private readonly ReaderWriterLockSlim _globalLock = new();
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
        List<Node> targets;
        _globalLock.EnterReadLock();
        try
        {
            targets = _nodes.Values.ToList();
        }
        finally
        {
            _globalLock.ExitReadLock();
        }

        foreach (var node in targets) 
            SpillNodeToDisk(node);
    }

    private void SpillNodeToDisk(Node node)
    {
        lock (node)
        {
            if (node.Data == null || string.IsNullOrEmpty(_tempPath)) return;

            var id = Guid.NewGuid();
            var filePath = Path.Combine(_tempPath, $"{id}.bin");

            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.SequentialScan))
                    fileStream.Write(node.Data, 0, node.Data.Length);

                node.TempFileId = id;
                Interlocked.Add(ref _currentMemory, -node.Data.Length);
                node.Data = null;
            }
            catch
            {
                if (File.Exists(filePath)) 
                    TryDeleteFile(filePath);
                throw;
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        for (var i = 0; i < 3; i++)
        {
            try { if (File.Exists(path)) File.Delete(path); return; }
            catch { Thread.Sleep(TimeSpan.FromMilliseconds(50 * Math.Pow(2, i))); }
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
                List<Node> targets;

                _globalLock.EnterReadLock();
                try
                {
                    targets = _nodes.Values
                        .Where(n => now - n.LastAccessed > _spillInterval && n.Data != null)
                        .ToList();
                }
                finally
                {
                    _globalLock.ExitReadLock();
                }

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
        byte[]? memoryData = null;
        Guid? diskFileId = null;
        
        using (stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            var data = ms.ToArray();

            var totalAllocated = Interlocked.Add(ref _currentMemory, data.Length);

            if (totalAllocated <= _maxMemory)
                memoryData = data;
            else
            {
                Interlocked.Add(ref _currentMemory, -data.Length);
                
                if (!string.IsNullOrEmpty(_tempPath))
                {
                    diskFileId = Guid.NewGuid();
                    var filePath = Path.Combine(_tempPath, $"{diskFileId.Value}.bin");
                    using var fileStream = new FileStream(
                        filePath, FileMode.Create, FileAccess.Write, FileShare.Read, 4096, FileOptions.SequentialScan);
                    fileStream.Write(data, 0, data.Length);
                }
            }
        }

        var node = new Node
        {
            Metadata = metadata,
            Data = memoryData,
            TempFileId = diskFileId,
            LastAccessed = _timeProvider.UtcNow
        };

        Node? oldNode;

        _globalLock.EnterWriteLock();
        try
        {
            _nodes.TryGetValue(key, out oldNode);
            _nodes[key] = node;
        }
        finally
        {
            _globalLock.ExitWriteLock();
        }

        if (oldNode == null) 
            return;
        lock (oldNode)
        {
            if (oldNode.Data != null) Interlocked.Add(ref _currentMemory, -oldNode.Data.Length);
            if (oldNode.TempFileId.HasValue && !string.IsNullOrEmpty(_tempPath))
                TryDeleteFile(Path.Combine(_tempPath, $"{oldNode.TempFileId.Value}.bin"));
        }
        oldNode.Dispose();
    }

    public void Delete(MapPath path)
    {
        var key = path.ToString();
        Node? node;

        _globalLock.EnterWriteLock();
        try
        {
            _nodes.Remove(key, out node);
        }
        finally
        {
            _globalLock.ExitWriteLock();
        }

        if (node == null) 
            return;
        
        lock (node)
        {
            if (node.Data != null) 
                Interlocked.Add(ref _currentMemory, -node.Data.Length);
            if (node.TempFileId.HasValue && !string.IsNullOrEmpty(_tempPath))
                TryDeleteFile(Path.Combine(_tempPath, $"{node.TempFileId.Value}.bin"));
        }
        node.Dispose();
    }

    public IBinaryAccessor Get(MapPath path) => new Accessor(this, path.ToString());

    private sealed class Accessor(BinaryManager manager, string path) : IBinaryAccessor
    {
        public MapValue Metadata
        {
            get
            {
                manager._globalLock.EnterReadLock();
                try
                {
                    return manager._nodes.TryGetValue(path, out var n) ? n.Metadata : Empty.Instance;
                }
                finally
                {
                    manager._globalLock.ExitReadLock();
                }
            }
        }

        public bool Exists
        {
            get
            {
                manager._globalLock.EnterReadLock();
                try
                {
                    return manager._nodes.ContainsKey(path);
                }
                finally
                {
                    manager._globalLock.ExitReadLock();
                }
            }
        }

        public Stream OpenStream()
        {
            manager._globalLock.EnterReadLock();
            Node? node;
            try
            {
                node = manager._nodes.GetValueOrDefault(path);
            }
            finally
            {
                manager._globalLock.ExitReadLock();
            }

            if (node == null)
                throw new InvalidOperationException("Node not found");

            node.Semaphore.Wait();
            try
            {
                lock (node)
                {
                    node.LastAccessed = manager._timeProvider.UtcNow;
                    if (node.Data != null) return new MemoryStream(node.Data);

                    if (!node.TempFileId.HasValue || string.IsNullOrEmpty(manager._tempPath))
                        throw new InvalidOperationException("Binary data not available");

                    var filePath = Path.Combine(manager._tempPath, $"{node.TempFileId.Value}.bin");
                    var data = File.ReadAllBytes(filePath);

                    if (Interlocked.Read(ref manager._currentMemory) + data.Length > manager._maxMemory)
                        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);
                    
                    node.Data = data;
                    TryDeleteFile(filePath);
                    node.TempFileId = null;
                    Interlocked.Add(ref manager._currentMemory, data.Length);
                    return new MemoryStream(node.Data);

                }
            }
            finally
            {
                node.Semaphore.Release();
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _globalLock.Dispose();
        
        List<Node> nodesToDispose;
        _globalLock.EnterWriteLock();
        try
        {
            nodesToDispose = _nodes.Values.ToList();
            _nodes.Clear();
        }
        finally { _globalLock.ExitWriteLock(); }

        foreach (var node in nodesToDispose) node.Dispose();

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
