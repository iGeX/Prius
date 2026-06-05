namespace Prius.Engine;

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Core.Maps;
using Abstractions;

internal sealed class VirtualBus(RoutingTrie routingTrie) : IBusContext, IDisposable
{
    private readonly BusLockNode _lockRoot = new();
    
    private RoutingTrie _routingTrie = routingTrie ?? throw new ArgumentNullException(nameof(routingTrie));
    
    private readonly DictionaryMap _memoryRoot = DictionaryMap.New;

    public void UpdateTrie(RoutingTrie trie)
    {
        _lockRoot.Lock.EnterWriteLock();
        try
        {
            _routingTrie = trie;
        }
        finally
        {
            _lockRoot.Lock.ExitWriteLock();
        }
    }

    private const int MaxDispatchDepth = 128;

    public string AbsolutePath => string.Empty;
    public string CallerSegment => string.Empty;

    public int Depth => 0;
    public IElement? Owner => null;
    public IMap Node => _memoryRoot;
    public IMap? ParentNode => null;
    public RoutingNode MountNode => _routingTrie.Root;
    public IMap? StaticEnv => null;
    public MatchType MatchType => MatchType.None;

    public bool Put(MapPath path, MapValue value, IMap? envPatch = null) => DispatchPut(this, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => DispatchGet(this, path, envPatch);

    public bool DispatchPut(IBusContext caller, MapPath relativePath, MapValue value, IMap? envPatch)
    {
        if (caller.Depth >= MaxDispatchDepth)
            throw new InvalidOperationException("Maximum dispatch depth exceeded.");

        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var lockNodes = ArrayPool<BusLockNode>.Shared.Rent(MaxDispatchDepth);
        try
        {
            var lockCount = FillLockPath(absolutePathString, lockNodes);
            var span = lockNodes.AsSpan(0, lockCount);

            while (true)
            {
                for (var i = 0; i < lockCount; i++) span[i].Lock.EnterReadLock();
                int startWriteAt;
                try
                {
                    startWriteAt = GetStartWriteIndex(absolutePathString, lockCount);
                }
                finally
                {
                    for (var i = lockCount - 1; i >= 0; i--) 
                        span[i].Lock.ExitReadLock();
                }
                
                EnterLocks(span, startWriteAt, true);

                // Double-Check: verify if the path up to startWriteAt is still valid (all segments are maps)
                var isValid = true;
                IMap current = _memoryRoot;
                var checkPath = new MapPath(absolutePathString.AsSpan());
                for (var i = 0; i < startWriteAt; i++)
                {
                    var val = current[checkPath.Head];
                    if (!val.IsMap)
                    {
                        isValid = false;
                        break;
                    }
                    current = val.AsMap();
                    checkPath = checkPath.Tail;
                }

                if (isValid)
                {
                    try
                    {
                        if (relativePath.IsEmpty)
                        {
                            WriteToMemory(caller, relativePath, value);
                            return false;
                        }

                        var initialFallback = caller.MatchType == MatchType.DeepWildcard ? caller.Owner : null;
                        var initialFallbackEnv = initialFallback != null ? caller.StaticEnv : null;

                        var resolveResult = RoutingTrie.ResolveScoped(caller.MountNode, relativePath, initialFallback, initialFallbackEnv);
                        
                        if (resolveResult.Element is EmptyElement)
                        {
                            WriteToMemory(caller, relativePath, value);
                            return false;
                        }

                        var mountPathStr = GetMountPath(absolutePathString, resolveResult.RemainingPath);
                        var mountRelativePathStr = GetMountPath(relativePath.ToString(), resolveResult.RemainingPath);
                        
                        var memoryMaps = FindOrCreateMemoryMap(caller.Node, new MapPath(mountRelativePathStr.AsSpan()));
                        var parentMap = memoryMaps.Parent ?? caller.ParentNode;
                        
                        var callerSegment = string.IsNullOrEmpty(mountRelativePathStr) ? (caller as ElementContext)?.CallerSegment ?? string.Empty : new MapPath(mountRelativePathStr.AsSpan()).LastSegment;
                        var subContext = new ElementContext(this, resolveResult.Element, caller, caller.Depth + 1, callerSegment, mountPathStr, envPatch, resolveResult.StaticEnv, memoryMaps.Current, parentMap, resolveResult.MatchNode, resolveResult.MatchType);

                        return resolveResult.Element.Put(subContext, resolveResult.RemainingPath, value);
                    }
                    finally
                    {
                        ExitLocks(span, startWriteAt, true);
                    }
                }

                // If invalid, release locks and retry
                ExitLocks(span, startWriteAt, true);
            }
        }
        finally
        {
            ArrayPool<BusLockNode>.Shared.Return(lockNodes);
        }
    }

    private int GetStartWriteIndex(string absolutePath, int lockCount)
    {
        var startWriteAt = Math.Max(0, lockCount - 2);
        
        IMap current = _memoryRoot;
        var path = new MapPath(absolutePath.AsSpan());
        for (var i = 0; i < startWriteAt; i++)
        {
            var val = current[path.Head];
            if (!val.IsMap) return i;
            current = val.AsMap();
            path = path.Tail;
        }
        return startWriteAt;
    }

    private static void WriteToMemory(IBusContext caller, MapPath relativePath, MapValue value)
    {
        if (relativePath.IsEmpty)
        {
            if (caller.ParentNode != null)
            {
                var segment = new MapPath(caller.CallerSegment).LastSegment;
                caller.ParentNode.DeepPut(new MapPath(segment), value);
            }
            else if (value.IsMap)
            {
                var targetMap = caller.Node;
                var sourceMap = value.AsMap();
                foreach (var k in sourceMap.Keys())
                    targetMap.DeepPut(k, sourceMap[k]);
            }
        }
        else
            caller.Node.DeepPut(relativePath, value);
    }

    public MapValue DispatchGet(IBusContext caller, MapPath relativePath, IMap? envPatch)
    {
        if (caller.Depth >= MaxDispatchDepth)
            throw new InvalidOperationException("Maximum dispatch depth exceeded.");

        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var lockNodes = ArrayPool<BusLockNode>.Shared.Rent(MaxDispatchDepth);
        try
        {
            var lockCount = FillLockPath(absolutePathString, lockNodes);
            var span = lockNodes.AsSpan(0, lockCount);

            EnterLocks(span, lockCount, false);
            try
            {
                if (relativePath.IsEmpty)
                    return ReadFromMemory(caller, relativePath);
                
                var initialFallback = caller.MatchType == MatchType.DeepWildcard ? caller.Owner : null;
                var initialFallbackEnv = initialFallback != null ? caller.StaticEnv : null;

                var resolveResult = RoutingTrie.ResolveScoped(caller.MountNode, relativePath, initialFallback, initialFallbackEnv);
                
                if (resolveResult.Element is EmptyElement)
                    return ReadFromMemory(caller, relativePath);

                var mountPathStr = GetMountPath(absolutePathString, resolveResult.RemainingPath);
                var mountRelativePathStr = GetMountPath(relativePath.ToString(), resolveResult.RemainingPath);
                
                var memoryMaps = FindMemoryMap(caller.Node, new MapPath(mountRelativePathStr.AsSpan()));
                var currentMap = memoryMaps?.Current ?? DictionaryMap.New;
                var parentMap = memoryMaps?.Parent ?? caller.ParentNode;
                
                var callerSegment = string.IsNullOrEmpty(mountRelativePathStr) ? (caller as ElementContext)?.CallerSegment ?? string.Empty : new MapPath(mountRelativePathStr.AsSpan()).LastSegment;
                var subContext = new ElementContext(this, resolveResult.Element, caller, caller.Depth + 1, callerSegment, mountPathStr, envPatch, resolveResult.StaticEnv, currentMap, parentMap, resolveResult.MatchNode, resolveResult.MatchType);

                return resolveResult.Element.Get(subContext, resolveResult.RemainingPath);
            }
            finally
            {
                ExitLocks(span, lockCount, false);
            }
        }
        finally
        {
            ArrayPool<BusLockNode>.Shared.Return(lockNodes);
        }
    }

    private int FillLockPath(string absolutePath, BusLockNode[] buffer)
    {
        buffer[0] = _lockRoot;
        if (string.IsNullOrEmpty(absolutePath)) return 1;

        var path = new MapPath(absolutePath.AsSpan());
        var current = _lockRoot;
        var count = 1;
        while (!path.IsEmpty && count < buffer.Length)
        {
            current = current.GetChild(path.Head);
            buffer[count++] = current;
            path = path.Tail;
        }
        return count;
    }

    private static void EnterLocks(Span<BusLockNode> nodes, int startWriteAt, bool isWrite)
    {
        for (var i = 0; i < nodes.Length; i++)
        {
            var l = nodes[i].Lock;
            if (isWrite && i >= startWriteAt)
                l.EnterWriteLock();
            else
                l.EnterReadLock();
        }
    }

    private static void ExitLocks(Span<BusLockNode> nodes, int startWriteAt, bool isWrite)
    {
        for (var i = nodes.Length - 1; i >= 0; i--)
        {
            var l = nodes[i].Lock;
            if (isWrite && i >= startWriteAt)
                l.ExitWriteLock();
            else
                l.ExitReadLock();
        }
    }

    private static MapValue ReadFromMemory(IBusContext caller, MapPath relativePath)
    {
        if (relativePath.IsEmpty && caller.ParentNode != null)
            return caller.ParentNode[new MapPath(caller.CallerSegment).LastSegment];
        return caller.Node.DeepGet(relativePath);
    }

    private static string GetMountPath(string fullPath, MapPath remainingPath)
    {
        if (remainingPath.IsEmpty) return fullPath;
        var keepLength = fullPath.Length - remainingPath.Length;
        if (keepLength > 0 && fullPath[keepLength - 1] == '/')
            keepLength--;
        return fullPath.Substring(0, keepLength);
    }

    private static (IMap? Parent, IMap Current) FindOrCreateMemoryMap(IMap startMap, MapPath path)
    {
        if (path.IsEmpty) return (null, startMap);

        IMap? parent = null;
        var current = startMap;
        while (!path.IsEmpty)
        {
            var next = current[path.Head];
            if (!next.IsMap)
            {
                current[path.Head] = new MapValue(DictionaryMap.New);
                next = current[path.Head];
            }
            parent = current;
            current = next.AsMap();
            path = path.Tail;
        }
        return (parent, current);
    }

    private static (IMap? Parent, IMap Current)? FindMemoryMap(IMap startMap, MapPath path)
    {
        if (path.IsEmpty) return (null, startMap);

        IMap? parent = null;
        var current = startMap;
        while (!path.IsEmpty)
        {
            var next = current[path.Head];
            if (!next.IsMap)
                return null;
            parent = current;
            current = next.AsMap();
            path = path.Tail;
        }
        return (parent, current);
    }

    public void PutAbsolute(MapPath absolutePath, MapValue value)
    {
        if (absolutePath.IsEmpty)
            return;

        var pathStr = absolutePath.ToString();

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                Put(pathStr, value);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VirtualBus] Background PutAbsolute Error: {ex.Message}");
            }
        });
    }

    private static string CombinePathsToString(string baseAbsolutePath, MapPath relativePath) =>
        string.IsNullOrEmpty(baseAbsolutePath) 
            ? relativePath.ToString() 
            : new MapPath(baseAbsolutePath.AsSpan()) + relativePath;

    public bool IsEmpty => _memoryRoot.IsEmpty;
    
    public bool CanWrite => true;

    public IEnumerable<string> Keys(bool? ascending = null)
    {
        _lockRoot.Lock.EnterReadLock();
        try { return _memoryRoot.Keys(ascending); }
        finally { _lockRoot.Lock.ExitReadLock(); }
    }

    public bool ContainsKey(string key)
    {
        var lockNodes = ArrayPool<BusLockNode>.Shared.Rent(2);
        try
        {
            lockNodes[0] = _lockRoot;
            lockNodes[1] = _lockRoot.GetChild(key.AsSpan());
            var span = lockNodes.AsSpan(0, 2);
            EnterLocks(span, 2, false);
            try { return _memoryRoot.ContainsKey(key); }
            finally { ExitLocks(span, 2, false); }
        }
        finally
        {
            ArrayPool<BusLockNode>.Shared.Return(lockNodes);
        }
    }

    public MapValue this[string key]
    {
        get
        {
            var lockNodes = ArrayPool<BusLockNode>.Shared.Rent(2);
            try
            {
                lockNodes[0] = _lockRoot;
                lockNodes[1] = _lockRoot.GetChild(key.AsSpan());
                var span = lockNodes.AsSpan(0, 2);
                EnterLocks(span, 2, false);
                try { return _memoryRoot[key]; }
                finally { ExitLocks(span, 2, false); }
            }
            finally
            {
                ArrayPool<BusLockNode>.Shared.Return(lockNodes);
            }
        }
        set
        {
            var lockNodes = ArrayPool<BusLockNode>.Shared.Rent(2);
            try
            {
                lockNodes[0] = _lockRoot;
                lockNodes[1] = _lockRoot.GetChild(key.AsSpan());
                var span = lockNodes.AsSpan(0, 2);
                EnterLocks(span, 0, true);
                try { _memoryRoot[key] = value; }
                finally { ExitLocks(span, 0, true); }
            }
            finally
            {
                ArrayPool<BusLockNode>.Shared.Return(lockNodes);
            }
        }
    }

    public void Dispose() => _lockRoot.Dispose();

    private sealed class BusLockNode : IDisposable
    {
        public readonly ReaderWriterLockSlim Lock = new(LockRecursionPolicy.SupportsRecursion);
        private readonly ConcurrentDictionary<string, BusLockNode> _children = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, BusLockNode>.AlternateLookup<ReadOnlySpan<char>> _lookup;

        public BusLockNode() => _lookup = _children.GetAlternateLookup<ReadOnlySpan<char>>();

        public BusLockNode GetChild(ReadOnlySpan<char> segment)
        {
            if (_lookup.TryGetValue(segment, out var child))
                return child;

            var newChild = new BusLockNode();
            return _lookup.TryAdd(segment, newChild) ? newChild : _lookup[segment];
        }

        public void Dispose()
        {
            Lock.Dispose();
            foreach (var child in _children.Values)
                child.Dispose();
        }
    }
}
