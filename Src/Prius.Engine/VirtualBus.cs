namespace Prius.Engine;

using System;
using System.Collections.Concurrent;
using Core.Maps;
using Abstractions;

public sealed class VirtualBus : IReactorContext
{
    private readonly ConcurrentDictionary<string, IReactor> _routeCache = new(StringComparer.Ordinal);
    
    private readonly ConcurrentDictionary<string, IReactor>.AlternateLookup<ReadOnlySpan<char>> _cacheLookup;
    
    private readonly RoutingTrie _routingTrie;

    private readonly ReactorContext _rootContext;

    public VirtualBus(RoutingTrie routingTrie)
    {
        _routingTrie = routingTrie ?? throw new ArgumentNullException(nameof(routingTrie));
        _cacheLookup = _routeCache.GetAlternateLookup<ReadOnlySpan<char>>();
        _rootContext = new ReactorContext(this, string.Empty, StackedMap.New());
    }
    
    private static StackedMap NextEnv(IMap env, IMap? envPatch) => envPatch is null || envPatch.IsEmpty ? StackedMap.New(env) : StackedMap.New(env, envPatch);

    internal void DispatchPut(ReactorContext caller, MapPath path, MapValue value, IMap? envPatch)
    {
        var absolutePath = new MapPath(caller.AbsolutePath) + path;
        if (!_cacheLookup.TryGetValue(absolutePath.AsSpan(), out var reactor))
        {
            reactor = _routingTrie.Resolve(absolutePath);
            _cacheLookup.TryAdd(absolutePath.AsSpan(), reactor);
        }

        var context = new ReactorContext(this, absolutePath, NextEnv(caller.Env, envPatch));
        
        reactor.Put(context, value);
    }

    internal MapValue DispatchGet(ReactorContext caller, MapPath path, IMap? envPatch)
    {
        var absolutePath = new MapPath(caller.AbsolutePath) + path;
        if (!_cacheLookup.TryGetValue(absolutePath.AsSpan(), out var reactor))
        {
            reactor = _routingTrie.Resolve(absolutePath);
            _cacheLookup.TryAdd(absolutePath.AsSpan(), reactor);
        }

        var context = new ReactorContext(this, absolutePath, NextEnv(caller.Env, envPatch));
        
        return reactor.Get(context);
    }
    
    internal void DispatchNotify(ReactorContext caller, IMap changedKeys) => throw new NotImplementedException();
    
    public void ClearCache() => _routeCache.Clear();
    
    public string Key => string.Empty;

    public IMap Env => DictionaryMap.New;

    public void Put(MapPath path, MapValue value, IMap? envPatch = null) => DispatchPut(_rootContext, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => DispatchGet(_rootContext, path, envPatch);
    
    public void Notify(IMap changedKeys) => throw new NotImplementedException();
}
