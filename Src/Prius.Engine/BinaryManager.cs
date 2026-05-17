using System.Collections.Concurrent;
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

    private readonly ConcurrentDictionary<string, Node> _nodes = new();
    private readonly string? _tempPath;
    private readonly CancellationTokenSource _cts = new();

    public BinaryManager(string? tempPath = null)
    {
        _tempPath = tempPath;
        if (!string.IsNullOrEmpty(_tempPath))
            Directory.CreateDirectory(_tempPath);
        
        Task.Run(() => SpillerLoop(_cts.Token));
    }

    private async Task SpillerLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(60000, ct);
            foreach (var node in _nodes.Values)
            {
                lock (node)
                {
                    if (DateTime.UtcNow - node.LastAccessed > TimeSpan.FromMinutes(1) && node.Data != null)
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
    }

    public void Store(string path, MapValue metadata, Stream stream)
    {
        using (stream)
        {
            var ms = new MemoryStream();
            stream.CopyTo(ms);
            _nodes[path] = new Node { Metadata = metadata, Data = ms.ToArray(), LastAccessed = DateTime.UtcNow };
        }
    }

    public void Delete(string path)
    {
        if (_nodes.TryRemove(path, out var node) && node.TempFileId.HasValue && !string.IsNullOrEmpty(_tempPath))
        {
            try { File.Delete(Path.Combine(_tempPath, $"{node.TempFileId}.bin")); } catch { /* ignored */ }
        }
    }

    public IBinaryAccessor Get(string path) => new Accessor(_nodes.TryGetValue(path, out var n) ? n : null, _tempPath);

    private sealed class Accessor(Node? node, string? tempPath) : IBinaryAccessor
    {
        public MapValue Metadata => node?.Metadata ?? Empty.Instance;
        public bool Exists => node != null;
        public Stream OpenStream()
        {
            if (node == null) throw new InvalidOperationException("Node not found");
            lock (node)
            {
                node.LastAccessed = DateTime.UtcNow;
                if (node.Data != null) return new MemoryStream(node.Data);
                if (node.TempFileId.HasValue && !string.IsNullOrEmpty(tempPath))
                    return File.OpenRead(Path.Combine(tempPath, $"{node.TempFileId}.bin"));
                throw new InvalidOperationException("Binary data not available");
            }
        }
    }

    public void Dispose() => _cts.Cancel();
}
