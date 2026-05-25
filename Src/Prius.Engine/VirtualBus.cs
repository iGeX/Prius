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
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _nodeLocks = new(StringComparer.Ordinal);
    
    private RoutingTrie _routingTrie;
    private readonly ElementContext _rootContext;
    private readonly BusMemoryNode _memoryRoot = new();

    public VirtualBus(RoutingTrie routingTrie)
    {
        _routingTrie = routingTrie ?? throw new ArgumentNullException(nameof(routingTrie));
        _cacheLookup = _routeCache.GetAlternateLookup<ReadOnlySpan<char>>();
        _rootContext = new ElementContext(this, null, string.Empty, string.Empty, string.Empty, DictionaryMap.New, DictionaryMap.New);
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
        if (relativePath.IsEmpty)
            return false;

        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new SemaphoreSlim(1, 1));
        nodeLock.Wait();

        try
        {
            var resolveResult = _routingTrie.Resolve(new MapPath(absolutePathString.AsSpan()));
            
            if (resolveResult.Element is EmptyElement)
            {
                WriteToMemory(absolutePathString, value);
                return false;
            }

            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Element);

            var callerSegment = relativePath.Head;
            var subContext = new ElementContext(this, caller, callerSegment, absolutePathString, resolveResult.ElementKey, envPatch, resolveResult.StaticEnv);

            return resolveResult.Element.Put(subContext, resolveResult.RemainingPath, value);
        }
        finally
        {
            nodeLock.Release();
        }
    }

    internal MapValue DispatchGet(ElementContext caller, MapPath relativePath, IMap? envPatch)
    {
        if (relativePath.IsEmpty)
            return new MapValue();

        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new SemaphoreSlim(1, 1));
        nodeLock.Wait();

        try
        {
            var resolveResult = _routingTrie.Resolve(new MapPath(absolutePathString.AsSpan()));
            
            if (resolveResult.Element is EmptyElement) return ReadFromMemory(absolutePathString);

            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Element);

            var callerSegment = relativePath.Head;
            var subContext = new ElementContext(this, caller, callerSegment, absolutePathString, resolveResult.ElementKey, envPatch, resolveResult.StaticEnv);

            return resolveResult.Element.Get(subContext, resolveResult.RemainingPath);
        }
        finally
        {
            nodeLock.Release();
        }
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
        var path = new MapPath(absolutePath.AsSpan());
        var current = _memoryRoot;

        while (!path.IsEmpty)
        {
            var segment = path.Head;
            path = path.Tail;

            if (string.IsNullOrEmpty(segment)) continue;

            if (path.IsEmpty)
            {
                current[segment] = value;
                return;
            }

            current.Children ??= new Dictionary<string, BusMemoryNode>(StringComparer.Ordinal);
            
            if (!current.Children.TryGetValue(segment, out var nextNode))
            {
                nextNode = new BusMemoryNode();
                current.Children[segment] = nextNode;
            }
            current = nextNode;
        }
    }

    private MapValue ReadFromMemory(string absolutePath)
    {
        var path = new MapPath(absolutePath.AsSpan());
        var current = _memoryRoot;
        
        if (path.IsEmpty) return new MapValue(current);

        while (!path.IsEmpty)
        {
            var segment = path.Head;
            path = path.Tail;

            if (string.IsNullOrEmpty(segment)) continue;

            if (path.IsEmpty) return current[segment];

            if (current.Children == null || !current.Children.TryGetValue(segment, out current)) return Empty.Instance;
        }

        return Empty.Instance;
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
