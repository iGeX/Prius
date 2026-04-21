namespace Prius.Core.Maps;

internal sealed class ReadOnlyMap(IMap source) : IMap
{
    public bool IsEmpty => source.IsEmpty;

    public MapValue Get(string key) => source.Get(key).Match(
        empty => empty,
        map => new MapValue(new ReadOnlyMap(map)),
        value => value.ToMapValue()
    );
    
    public IEnumerable<MapValue> Values => source.Values.Select(v => v.Match(
        e => e,
        m => new MapValue(new ReadOnlyMap(m)),
        val => val.ToMapValue()
    ));

    public void Put(string key, MapValue value)
    {
    }

    public IEnumerable<string> Keys(bool? ascending = null) => source.Keys(ascending);
    
    public bool Equals(IMap? other) => this.DeepEquals(other);
    
    public override bool Equals(object? obj) => obj is IMap other && this.DeepEquals(other);
    
    public override int GetHashCode() => this.MapHashCode();
}
