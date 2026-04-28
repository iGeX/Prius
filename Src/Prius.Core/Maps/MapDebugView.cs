using System.Diagnostics;

namespace Prius.Core.Maps;

internal sealed class MapDebugView(IMap map)
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly IMap _map = map;

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    public MapEntryProxy[] Items => _map.Keys()
        .Select(key => new MapEntryProxy(key, _map.Get(key)))
        .ToArray();
}

[DebuggerDisplay("{ValueDisplay,nq}", Name = "[{_key}]")]
internal sealed class MapEntryProxy
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    // ReSharper disable once NotAccessedField.Local
    private readonly string _key;
    
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly MapValue _value;

    // ReSharper disable once ConvertToPrimaryConstructor
    public MapEntryProxy(string key, MapValue value)
    {
        _key = key;
        _value = value;
    }

    [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
    // ReSharper disable once UnusedMember.Global
    public object? Value => _value.IsMap ? _value.AsMap() : _value.AsValue();

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private string ValueDisplay => _value.IsMap 
        ? $"Map [{_value.AsMap().Keys().Count()}]" 
        : _value.IsEmpty ? "Empty" : _value.ToString();
}
