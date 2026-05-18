namespace Prius.Engine;

using System;
using System.Collections.Concurrent;
using Core.Maps;
using Abstractions;

public sealed class VirtualBus
{
    private readonly ConcurrentDictionary<string, IReactor> _routeCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReactor>.AlternateLookup<ReadOnlySpan<char>> _cacheLookup;
    private readonly RoutingTrie _routingTrie;
    private readonly ReactorContext _rootContext;

    public VirtualBus(RoutingTrie routingTrie)
    {
        _routingTrie = routingTrie ?? throw new ArgumentNullException(nameof(routingTrie));
        _cacheLookup = _routeCache.GetAlternateLookup<ReadOnlySpan<char>>();
        _rootContext = new ReactorContext(this, string.Empty, string.Empty, DictionaryMap.New);
    }

    public void Put(MapPath path, MapValue value, IMap? envPatch = null) => 
        DispatchPut(_rootContext, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => 
        DispatchGet(_rootContext, path, envPatch);

    public void ClearCache() => 
        _routeCache.Clear();

    internal void DispatchPut(ReactorContext caller, MapPath relativePath, MapValue value, IMap? envPatch)
    {
        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var resolveResult = _routingTrie.Resolve(new MapPath(absolutePathString.AsSpan()));
        
        CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Reactor);

        var nextEnv = CombineEnv(caller.Env, envPatch);
        var subContext = new ReactorContext(this, absolutePathString, resolveResult.ReactorKey, nextEnv);

        resolveResult.Reactor.Put(subContext, resolveResult.RemainingPath, value);
    }

    internal MapValue DispatchGet(ReactorContext caller, MapPath relativePath, IMap? envPatch)
    {
        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var resolveResult = _routingTrie.Resolve(new MapPath(absolutePathString.AsSpan()));
        
        CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Reactor);

        var nextEnv = CombineEnv(caller.Env, envPatch);
        var subContext = new ReactorContext(this, absolutePathString, resolveResult.ReactorKey, nextEnv);

        return resolveResult.Reactor.Get(subContext, resolveResult.RemainingPath);
    }

    internal void DispatchNotify(ReactorContext caller, IMap changedKeys) => 
        throw new NotImplementedException();

    private void CacheResolvedRoute(ReadOnlySpan<char> absolutePath, IReactor reactor)
    {
        if (_cacheLookup.TryGetValue(absolutePath, out _))
            return;

        _cacheLookup.TryAdd(absolutePath, reactor);
    }

    private static string CombinePathsToString(string baseAbsolutePath, MapPath relativePath) => 
        string.IsNullOrEmpty(baseAbsolutePath) 
            ? relativePath.ToString() 
            : new MapPath(baseAbsolutePath.AsSpan()) + relativePath;

    private static IMap CombineEnv(IMap currentEnv, IMap? patch) => 
        patch is null || patch.IsEmpty 
            ? currentEnv 
            : new StackedMap([currentEnv, patch]);
}
