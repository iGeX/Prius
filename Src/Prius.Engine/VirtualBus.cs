namespace Prius.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Core.Maps;
using Abstractions;

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

    public const int MaxDispatchDepth = 128;

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

    private void ClearCache() => _routeCache.Clear();

    internal bool DispatchPut(IBusContext caller, MapPath relativePath, MapValue value, IMap? envPatch)
    {
        if (caller.Depth >= MaxDispatchDepth)
            throw new InvalidOperationException("Maximum dispatch depth exceeded.");

        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new object());
        
        lock (nodeLock)
        {
            if (relativePath.IsEmpty)
            {
                WriteToMemory(caller, relativePath, value);
                return false;
            }

            var initialFallback = caller.MatchType == MatchType.DeepWildcard ? caller.Owner : null;
            var initialFallbackEnv = initialFallback != null ? caller.StaticEnv : null;

            var resolveResult = _routingTrie.ResolveScoped(caller.MountNode, relativePath, initialFallback, initialFallbackEnv);
            
            if (resolveResult.Element is EmptyElement)
            {
                WriteToMemory(caller, relativePath, value);
                return false;
            }

            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Element);

            var mountPathStr = GetMountPath(absolutePathString, resolveResult.RemainingPath);
            var mountRelativePathStr = GetMountPath(relativePath.ToString(), resolveResult.RemainingPath);
            
            var memoryMaps = FindOrCreateMemoryMap(caller.Node, new MapPath(mountRelativePathStr.AsSpan()));
            var parentMap = memoryMaps.Parent ?? caller.ParentNode;
            
            var callerSegment = string.IsNullOrEmpty(mountRelativePathStr) ? string.Empty : new MapPath(mountRelativePathStr.AsSpan()).Head;
            var subContext = new ElementContext(this, resolveResult.Element, caller, caller.Depth + 1, callerSegment, mountPathStr, envPatch, resolveResult.StaticEnv, memoryMaps.Current, parentMap, resolveResult.MatchNode, resolveResult.MatchType);

            return resolveResult.Element.Put(subContext, resolveResult.RemainingPath, value);
        }
    }

    private static void WriteToMemory(IBusContext caller, MapPath relativePath, MapValue value)
    {
        if (relativePath.IsEmpty)
        {
            if (caller.ParentNode != null)
            {
                if (value.IsMap)
                    caller.ParentNode.Put(new MapPath(new MapPath(caller.CallerSegment).LastSegment), value);
                else
                    caller.ParentNode[new MapPath(caller.CallerSegment).LastSegment] = value;
            }
            else if (value.IsMap)
            {
                var targetMap = caller.Node;
                var sourceMap = value.AsMap();
                foreach (var k in sourceMap.Keys())
                    targetMap.Put(k, sourceMap[k]);
            }
        }
        else
            caller.Node.Put(relativePath, value);
    }

    internal MapValue DispatchGet(IBusContext caller, MapPath relativePath, IMap? envPatch)
    {
        if (caller.Depth >= MaxDispatchDepth)
            throw new InvalidOperationException("Maximum dispatch depth exceeded.");

        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new object());
        
        lock (nodeLock)
        {
            if(relativePath.IsEmpty)
                return ReadFromMemory(caller, relativePath);
            
            var initialFallback = caller.MatchType == MatchType.DeepWildcard ? caller.Owner : null;
            var initialFallbackEnv = initialFallback != null ? caller.StaticEnv : null;

            var resolveResult = _routingTrie.ResolveScoped(caller.MountNode, relativePath, initialFallback, initialFallbackEnv);
            
            if (resolveResult.Element is EmptyElement)
                return ReadFromMemory(caller, relativePath);

            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Element);

            var mountPathStr = GetMountPath(absolutePathString, resolveResult.RemainingPath);
            var mountRelativePathStr = GetMountPath(relativePath.ToString(), resolveResult.RemainingPath);
            
            var memoryMaps = FindOrCreateMemoryMap(caller.Node, new MapPath(mountRelativePathStr.AsSpan()));
            var parentMap = memoryMaps.Parent ?? caller.ParentNode;
            
            var callerSegment = string.IsNullOrEmpty(mountRelativePathStr) ? string.Empty : new MapPath(mountRelativePathStr.AsSpan()).Head;
            var subContext = new ElementContext(this, resolveResult.Element, caller, caller.Depth + 1, callerSegment, mountPathStr, envPatch, resolveResult.StaticEnv, memoryMaps.Current, parentMap, resolveResult.MatchNode, resolveResult.MatchType);

            return resolveResult.Element.Get(subContext, resolveResult.RemainingPath);
        }
    }

    private static MapValue ReadFromMemory(IBusContext caller, MapPath relativePath)
    {
        if (relativePath.IsEmpty && caller.ParentNode != null)
            return caller.ParentNode[new MapPath(caller.CallerSegment).LastSegment];
        return caller.Node.Get(relativePath);
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
