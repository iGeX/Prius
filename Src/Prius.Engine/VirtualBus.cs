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
    private readonly Func<string, IRavenBroker> _brokerFallback;

    public VirtualBus(RoutingTrie routingTrie)
    {
        _routingTrie = routingTrie ?? throw new ArgumentNullException(nameof(routingTrie));
        _cacheLookup = _routeCache.GetAlternateLookup<ReadOnlySpan<char>>();
        _brokerFallback = _ => EmptyRavenBroker.Instance;
    }

    internal void DispatchWithBroker(MapPath path, MapValue value, IMap env, Func<ReactorContext, IRavenBroker> brokerFactory)
    {
        if (!_cacheLookup.TryGetValue(path.AsSpan(), out var reactor))
        {
            reactor = _routingTrie.Resolve(path);
            _cacheLookup.TryAdd(path.AsSpan(), reactor);
        }

        var context = new ReactorContext(this, path.ToString(), env, EmptyRavenBroker.Instance);
        var contextualContext = new ReactorContext(this, path.ToString(), env, brokerFactory(context));
        
        reactor.Put(contextualContext, value);
    }

    internal void DispatchPut(string absolutePath, MapValue value, IMap env, IMap? envPatch)
    {
        if (!_cacheLookup.TryGetValue(absolutePath.AsSpan(), out var reactor))
        {
            reactor = _routingTrie.Resolve(absolutePath);
            _cacheLookup.TryAdd(absolutePath.AsSpan(), reactor);
        }

        var nextEnv = envPatch == null || envPatch.IsEmpty 
            ? env 
            : StackedMap.New(env, envPatch).AsReadOnly();

        var context = new ReactorContext(this, absolutePath, nextEnv, EmptyRavenBroker.Instance);
        
        reactor.Put(context, value);
    }

    internal MapValue DispatchGet(string absolutePath, IMap env)
    {
        if (!_cacheLookup.TryGetValue(absolutePath.AsSpan(), out var reactor))
        {
            reactor = _routingTrie.Resolve(absolutePath);
            _cacheLookup.TryAdd(absolutePath.AsSpan(), reactor);
        }

        var context = new ReactorContext(this, absolutePath, env, EmptyRavenBroker.Instance);
        
        return reactor.Get(context);
    }

    internal void DispatchNotify(string absolutePath, IMap changedKeys)
    {
        var path = new MapPath(absolutePath.AsSpan());
        var parentPath = path.Tail;
        
        if (parentPath.IsEmpty)
            return;

        string parentStr = parentPath.ToString();

        if (!_cacheLookup.TryGetValue(parentStr.AsSpan(), out var parentReactor))
        {
            parentReactor = _routingTrie.Resolve(parentStr);
            _cacheLookup.TryAdd(parentStr.AsSpan(), parentReactor);
        }

        var context = new ReactorContext(this, parentStr, EmptyMap.Instance, EmptyRavenBroker.Instance);
        
        parentReactor.Put(context, new MapValue(changedKeys));
    }

    public void ClearCache() => _routeCache.Clear();
}
