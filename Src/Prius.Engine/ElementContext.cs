namespace Prius.Engine;

using System;
using System.Collections.Generic;
using Core.Maps;
using Abstractions;

public sealed class ElementContext : IElementContext
{
    private readonly VirtualBus _bus;
    private readonly IMap? _envPatch;
    private readonly IMap? _staticEnv;
    
    internal IMap Node { get; }
    private ElementContext? Parent { get; }
    
    public string AbsolutePath { get; }
    public string CallerSegment { get; }
    public string Key { get; }
    
    public bool IsEmpty => (_envPatch is null || _envPatch.IsEmpty) && (_staticEnv is null || _staticEnv.IsEmpty) && (Parent is null || Parent.IsEmpty);
    public bool CanWrite => false;

    internal ElementContext(
        VirtualBus bus,
        ElementContext? parent, 
        string callerSegment, 
        string absolutePath, 
        string key, 
        IMap? envPatch,
        IMap? staticEnv,
        IMap node)
    {
        _bus = bus;
        Parent = parent;
        CallerSegment = callerSegment;
        AbsolutePath = absolutePath;
        Key = key;
        _envPatch = envPatch is not null && !envPatch.IsEmpty ? envPatch : null;
        _staticEnv = staticEnv is not null && !staticEnv.IsEmpty ? staticEnv : null;
        Node = node;
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
        var current = this;

        while (current is not null)
        {
            if (current._envPatch is not null)
            {
                foreach (var key in current._envPatch.Keys(ascending))
                    uniqueKeys.Add(key);
            }
            
            if (current._staticEnv is not null)
            {
                foreach (var key in current._staticEnv.Keys(ascending))
                    uniqueKeys.Add(key);
            }

            current = current.Parent;
        }

        return uniqueKeys;
    }

    public bool Put(MapPath path, MapValue value, IMap? envPatch = null) => 
        _bus.DispatchPut(this, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => 
        _bus.DispatchGet(this, path, envPatch);

    public void PutAbsolute(MapPath absolutePath, MapValue value) => 
        _bus.DispatchPutAbsolute(absolutePath, value);
}
