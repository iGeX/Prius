namespace Prius.Engine;

using System;
using System.Collections.Generic;
using Core.Maps;
using Abstractions;

public sealed class ReactorContext : IReactorContext, IMap
{
    private readonly VirtualBus _bus;
    private readonly IMap? _envPatch;
    
    internal ReactorContext? Parent { get; }
    internal string Segment { get; }
    internal string AbsolutePath { get; }

    public string Key { get; }
    public bool IsEmpty => (_envPatch is null || _envPatch.IsEmpty) && (Parent is null || Parent.IsEmpty);
    public bool CanWrite => false;

    internal ReactorContext(
        VirtualBus bus, 
        ReactorContext? parent, 
        string segment, 
        string absolutePath, 
        string key, 
        IMap? envPatch)
    {
        _bus = bus;
        Parent = parent;
        Segment = segment;
        AbsolutePath = absolutePath;
        Key = key;
        _envPatch = envPatch is not null && !envPatch.IsEmpty ? envPatch : null;
    }

    public bool ContainsKey(string key)
    {
        if (_envPatch is not null && _envPatch.ContainsKey(key))
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
                foreach (var key in current._envPatch.Keys(ascending))
                    uniqueKeys.Add(key);

            current = current.Parent;
        }

        return uniqueKeys;
    }

    public void Put(MapPath path, MapValue value, IMap? envPatch = null) => 
        _bus.DispatchPut(this, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => 
        _bus.DispatchGet(this, path, envPatch);

    public void Notify(MapPath path, MapValue value) => 
        _bus.DispatchNotify(this, path, value);
}
