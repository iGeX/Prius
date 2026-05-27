namespace Prius.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Core.Maps;
using Abstractions;

public static class VirtualBusFactory
{
    public static IElementContext Create(RoutingTrie routingTrie) => new VirtualBus(routingTrie);
}

internal sealed class VirtualBus : IBusContext
{
    private readonly ConcurrentDictionary<string, IElement> _routeCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IElement>.AlternateLookup<ReadOnlySpan<char>> _cacheLookup;
    private readonly ConcurrentDictionary<string, object> _nodeLocks = new(StringComparer.Ordinal);
    
    private RoutingTrie _routingTrie;
    private readonly DictionaryMap _memoryRoot = DictionaryMap.New;

    public VirtualBus(RoutingTrie routingTrie)
    {
        _routingTrie = routingTrie ?? throw new ArgumentNullException(nameof(routingTrie));
        _cacheLookup = _routeCache.GetAlternateLookup<ReadOnlySpan<char>>();
    }

    public void UpdateTrie(RoutingTrie trie)
    {
        _routingTrie = trie;
        ClearCache();
    }

    public string AbsolutePath => string.Empty;
    public string CallerSegment => string.Empty;
    public string Key => string.Empty;

    public IElement? Owner => null;
    public IMap Node => _memoryRoot;
    public RoutingNode MountNode => _routingTrie.Root;
    public IMap? StaticEnv => null;
    public MatchType MatchType => MatchType.None;
    public bool IsUnrolled => false;

    public bool Put(MapPath path, MapValue value, IMap? envPatch = null) => DispatchPut(this, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => DispatchGet(this, path, envPatch);

    private void ClearCache() => _routeCache.Clear();

    internal bool DispatchPut(IBusContext caller, MapPath relativePath, MapValue value, IMap? envPatch)
    {
        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new object());
        
        lock (nodeLock)
        {
            var initialFallback = caller.MatchType == MatchType.DeepWildcard ? caller.Owner : null;
            var initialFallbackEnv = initialFallback != null ? caller.StaticEnv : null;

            var resolveResult = _routingTrie.ResolveScoped(caller.MountNode, relativePath, initialFallback, initialFallbackEnv);
            
            if (resolveResult.Element is EmptyElement)
            {
                caller.Node.Put(relativePath, value);
                return false;
            }

            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Element);

            var mountPathStr = GetMountPath(absolutePathString, resolveResult.RemainingPath);
            var mountRelativePathStr = GetMountPath(relativePath.ToString(), resolveResult.RemainingPath);
            
            var memoryMap = FindOrCreateMemoryMap(caller.Node, new MapPath(mountRelativePathStr.AsSpan()));
            
            var isUnrolled = resolveResult.Element == initialFallback;
            var elementKey = string.IsNullOrEmpty(resolveResult.ElementKey) ? caller.Key : resolveResult.ElementKey;
            var callerSegment = string.IsNullOrEmpty(mountRelativePathStr) ? string.Empty : new MapPath(mountRelativePathStr.AsSpan()).Head;

            var subContext = new ElementContext(this, resolveResult.Element, caller, callerSegment, mountPathStr, elementKey, envPatch, resolveResult.StaticEnv, memoryMap, resolveResult.MatchNode, resolveResult.MatchType, isUnrolled);

            return resolveResult.Element.Put(subContext, resolveResult.RemainingPath, value);
        }
    }

    internal MapValue DispatchGet(IBusContext caller, MapPath relativePath, IMap? envPatch)
    {
        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        if (string.IsNullOrEmpty(absolutePathString)) 
            return new MapValue();

        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new object());
        
        lock (nodeLock)
        {
            var initialFallback = caller.MatchType == MatchType.DeepWildcard ? caller.Owner : null;
            var initialFallbackEnv = initialFallback != null ? caller.StaticEnv : null;

            var resolveResult = _routingTrie.ResolveScoped(caller.MountNode, relativePath, initialFallback, initialFallbackEnv);
            
            if (resolveResult.Element is EmptyElement) 
                return caller.Node.Get(relativePath);

            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Element);

            var mountPathStr = GetMountPath(absolutePathString, resolveResult.RemainingPath);
            var mountRelativePathStr = GetMountPath(relativePath.ToString(), resolveResult.RemainingPath);
            
            var memoryMap = FindOrCreateMemoryMap(caller.Node, new MapPath(mountRelativePathStr.AsSpan()));
            
            var isUnrolled = resolveResult.Element == initialFallback;
            var elementKey = string.IsNullOrEmpty(resolveResult.ElementKey) ? caller.Key : resolveResult.ElementKey;
            var callerSegment = string.IsNullOrEmpty(mountRelativePathStr) ? string.Empty : new MapPath(mountRelativePathStr.AsSpan()).Head;

            var subContext = new ElementContext(this, resolveResult.Element, caller, callerSegment, mountPathStr, elementKey, envPatch, resolveResult.StaticEnv, memoryMap, resolveResult.MatchNode, resolveResult.MatchType, isUnrolled);

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

    private static IMap FindOrCreateMemoryMap(IMap startMap, MapPath path)
    {
        var current = startMap;
        while (!path.IsEmpty)
        {
            var next = current[path.Head];
            if (!next.IsMap)
            {
                current[path.Head] = new MapValue(DictionaryMap.New);
                next = current[path.Head];
            }
            current = next.AsMap();
            path = path.Tail;
        }
        return current;
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
