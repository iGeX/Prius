namespace Prius.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Core.Maps;
using Abstractions;

public sealed class VirtualBus : IMap
{
    private readonly ConcurrentDictionary<string, IElement> _routeCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IElement>.AlternateLookup<ReadOnlySpan<char>> _cacheLookup;
    private readonly ConcurrentDictionary<string, object> _nodeLocks = new(StringComparer.Ordinal);
    
    private RoutingTrie _routingTrie;
    private readonly ElementContext _rootContext;
    private readonly BusMemoryNode _memoryRoot = new();

    public VirtualBus(RoutingTrie routingTrie)
    {
        _routingTrie = routingTrie ?? throw new ArgumentNullException(nameof(routingTrie));
        _cacheLookup = _routeCache.GetAlternateLookup<ReadOnlySpan<char>>();
        _rootContext = new ElementContext(this, null, null, string.Empty, string.Empty, string.Empty, DictionaryMap.New, DictionaryMap.New, _memoryRoot);
    }

    public void UpdateTrie(RoutingTrie trie)
    {
        _routingTrie = trie;
        ClearCache();
    }

    public bool Put(MapPath path, MapValue value, IMap? envPatch = null) => DispatchPut(_rootContext, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => DispatchGet(_rootContext, path, envPatch);

    public void ClearCache() => _routeCache.Clear();

    internal bool DispatchPut(ElementContext caller, MapPath relativePath, MapValue value, IMap? envPatch)
    {
        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        if (string.IsNullOrEmpty(absolutePathString)) 
            return false;

        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new object());
        
        lock (nodeLock)
        {
            var resolveResult = _routingTrie.Resolve(new MapPath(absolutePathString.AsSpan()));
            
            if (resolveResult.Element is EmptyElement || resolveResult.Element == caller.Owner)
            {
                caller.Node.PutRelative(relativePath, value);
                return true;
            }

            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Element);

            var mountPathStr = GetMountPath(absolutePathString, resolveResult.RemainingPath);
            var mountRelativePathStr = GetMountPath(relativePath.ToString(), resolveResult.RemainingPath);
            
            var callerSegment = string.IsNullOrEmpty(mountRelativePathStr) ? string.Empty : new MapPath(mountRelativePathStr.AsSpan()).Head;
            var memoryNode = FindOrCreateMemoryNode(caller.Node, new MapPath(mountRelativePathStr.AsSpan()));
            var subContext = new ElementContext(this, resolveResult.Element, caller, callerSegment, mountPathStr, resolveResult.ElementKey, envPatch, resolveResult.StaticEnv, memoryNode);

            return resolveResult.Element.Put(subContext, resolveResult.RemainingPath, value);
        }
    }

    internal MapValue DispatchGet(ElementContext caller, MapPath relativePath, IMap? envPatch)
    {
        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        if (string.IsNullOrEmpty(absolutePathString)) 
            return new MapValue();

        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new object());
        
        lock (nodeLock)
        {
            var resolveResult = _routingTrie.Resolve(new MapPath(absolutePathString.AsSpan()));
            
            if (resolveResult.Element is EmptyElement || resolveResult.Element == caller.Owner) 
                return caller.Node.GetRelative(relativePath);

            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Element);

            var mountPathStr = GetMountPath(absolutePathString, resolveResult.RemainingPath);
            var mountRelativePathStr = GetMountPath(relativePath.ToString(), resolveResult.RemainingPath);
            
            var callerSegment = string.IsNullOrEmpty(mountRelativePathStr) ? string.Empty : new MapPath(mountRelativePathStr.AsSpan()).Head;
            var memoryNode = FindOrCreateMemoryNode(caller.Node, new MapPath(mountRelativePathStr.AsSpan()));
            var subContext = new ElementContext(this, resolveResult.Element, caller, callerSegment, mountPathStr, resolveResult.ElementKey, envPatch, resolveResult.StaticEnv, memoryNode);

            return resolveResult.Element.Get(subContext, resolveResult.RemainingPath);
        }
    }

    private static string GetMountPath(string fullPath, MapPath remainingPath)
    {
        if (remainingPath.IsEmpty) return fullPath;
        var keepLength = fullPath.Length - remainingPath.Length;
        if (keepLength > 0 && fullPath[keepLength - 1] == '/')
            keepLength--;
        return fullPath.Substring(0, keepLength);
    }

    private static BusMemoryNode FindOrCreateMemoryNode(BusMemoryNode startNode, MapPath path)
    {
        var current = startNode;
        while (!path.IsEmpty)
        {
            current = current.GetOrCreateChild(path.Head);
            path = path.Tail;
        }
        return current;
    }

    internal void DispatchPutAbsolute(MapPath absolutePath, MapValue value)
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

    private void WriteToMemory(string absolutePath, MapValue value)
    {
        _memoryRoot.PutRelative(new MapPath(absolutePath.AsSpan()), value);
    }

    private MapValue ReadFromMemory(string absolutePath)
    {
        return _memoryRoot.GetRelative(new MapPath(absolutePath.AsSpan()));
    }

    private void CacheResolvedRoute(ReadOnlySpan<char> absolutePath, IElement element)
    {
        if (_cacheLookup.TryGetValue(absolutePath, out _))
            return;

        _cacheLookup.TryAdd(absolutePath, element);
    }

    private static string CombinePathsToString(string baseAbsolutePath, MapPath relativePath) =>
        string.IsNullOrEmpty(baseAbsolutePath) 
            ? relativePath.ToString() 
            : new MapPath(baseAbsolutePath.AsSpan()) + relativePath;

    public bool IsEmpty => _memoryRoot.IsEmpty;
    
    public bool CanWrite => true;

    public IEnumerable<string> Keys(bool? ascending = null) => _memoryRoot.Keys(ascending);

    public bool ContainsKey(string key) => _memoryRoot.ContainsKey(key);

    public MapValue this[string key]
    {
        get => _memoryRoot[key];
        set => _memoryRoot[key] = value;
    }
}
