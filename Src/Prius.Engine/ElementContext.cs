namespace Prius.Engine;

using System;
using System.Collections.Generic;
using Core.Maps;
using Abstractions;

internal interface IBusContext : IElementContext
{
    IElement? Owner { get; }
    IMap Node { get; }
    RoutingNode MountNode { get; }
    IMap? StaticEnv { get; }
    MatchType MatchType { get; }
    bool IsUnrolled { get; }
}

public sealed class ElementContext : IBusContext
{
    private readonly VirtualBus _bus;
    private readonly IMap? _envPatch;
    private readonly IMap? _staticEnv;
    private readonly IElement? _owner;
    private readonly IMap _node;
    private readonly RoutingNode _mountNode;
    private readonly MatchType _matchType;
    private readonly bool _isUnrolled;
    
    IElement? IBusContext.Owner => _owner;
    IMap IBusContext.Node => _node;
    RoutingNode IBusContext.MountNode => _mountNode;
    IMap? IBusContext.StaticEnv => _staticEnv;
    MatchType IBusContext.MatchType => _matchType;
    bool IBusContext.IsUnrolled => _isUnrolled;
    private IElementContext? Parent { get; }
    
    public string AbsolutePath { get; }
    public string CallerSegment { get; }
    public string Key { get; }
    
    public bool IsEmpty => (_envPatch is null || _envPatch.IsEmpty) && (_staticEnv is null || _staticEnv.IsEmpty) && (Parent is null || Parent.IsEmpty);
    public bool CanWrite => false;

    internal ElementContext(
        VirtualBus bus,
        IElement? owner,
        IElementContext? parent, 
        string callerSegment, 
        string absolutePath, 
        string key, 
        IMap? envPatch,
        IMap? staticEnv,
        IMap node,
        RoutingNode mountNode,
        MatchType matchType,
        bool isUnrolled)
    {
        _bus = bus;
        _owner = owner;
        Parent = parent;
        CallerSegment = callerSegment;
        AbsolutePath = absolutePath;
        Key = key;
        _envPatch = envPatch is not null && !envPatch.IsEmpty ? envPatch : null;
        _staticEnv = staticEnv is not null && !staticEnv.IsEmpty ? staticEnv : null;
        _node = node;
        _mountNode = mountNode;
        _matchType = matchType;
        _isUnrolled = isUnrolled;
    }

    public bool ContainsKey(string key)
    {
        if (_envPatch is not null && _envPatch.ContainsKey(key))
            return true;
            
        if (_staticEnv is not null && _staticEnv.ContainsKey(key))
            return true;

        if (Parent is not null)
            return Parent.ContainsKey(key);

        return false;
    }

    public MapValue this[string key]
    {
        get
        {
            if (_envPatch is not null && _envPatch.ContainsKey(key))
                return _envPatch[key];

            if (_staticEnv is not null && _staticEnv.ContainsKey(key))
                return _staticEnv[key];

            if (Parent is not null)
                return Parent[key];

            return new MapValue();
        }
        set
        {
        }
    }

    public IEnumerable<string> Keys(bool? ascending = null)
    {
        var uniqueKeys = new HashSet<string>(StringComparer.Ordinal);
        var current = this as IElementContext;

        while (current is ElementContext ctx)
        {
            if (ctx._envPatch is not null)
            {
                foreach (var key in ctx._envPatch.Keys(ascending))
                    uniqueKeys.Add(key);
            }
            
            if (ctx._staticEnv is not null)
            {
                foreach (var key in ctx._staticEnv.Keys(ascending))
                    uniqueKeys.Add(key);
            }

            current = ctx.Parent;
        }

        return uniqueKeys;
    }

    public bool Put(MapPath path, MapValue value, IMap? envPatch = null) => 
        _bus.DispatchPut(this, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => 
        _bus.DispatchGet(this, path, envPatch);

    public void PutAbsolute(MapPath absolutePath, MapValue value) => 
        _bus.PutAbsolute(absolutePath, value);
}
