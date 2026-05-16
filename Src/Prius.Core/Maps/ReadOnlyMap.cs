using System.Diagnostics;

namespace Prius.Core.Maps;

[DebuggerTypeProxy(typeof(MapDebugView))]
internal sealed class ReadOnlyMap(IMap source) : IMap
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly IMap _source = source;
    
    public bool IsEmpty => _source.IsEmpty;

    public MapValue Get(string key) => _source.Get(key).Match(
        empty => empty,
        map => new MapValue(new ReadOnlyMap(map)),
        value => value.AsMapValue()
    );
    
    public IEnumerable<MapValue> Values => _source.Values.Select(v => v.Match(
        e => e,
        m => new MapValue(new ReadOnlyMap(m)),
        val => val.AsMapValue()
    ));

    public void Put(string key, MapValue value)
    {
    }

    public IEnumerable<string> Keys(bool? ascending = null) => _source.Keys(ascending);
    
    public bool Equals(IMap? other) => this.DeepEquals(other);
    
    public override bool Equals(object? obj) => obj is IMap other && this.DeepEquals(other);
    
    public override int GetHashCode() => this.MapHashCode();
}
