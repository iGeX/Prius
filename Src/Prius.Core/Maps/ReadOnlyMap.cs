using System.Diagnostics;

namespace Prius.Core.Maps;

[DebuggerTypeProxy(typeof(MapDebugView))]
internal sealed class ReadOnlyMap(IMap source) : IMap
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly IMap _source = source;
    
    public bool IsEmpty => _source.IsEmpty;

    public bool CanWrite => false;
    
    public IEnumerable<string> Keys(bool? ascending = null) => _source.Keys(ascending);

    public bool ContainsKey(string key) => _source.ContainsKey(key);

    public MapValue this[string key]
    {
        get => _source[key].Match(
            empty => empty,
            map => new MapValue(new ReadOnlyMap(map)),
            value => value.AsMapValue()
        );
        set { }
    }

    public bool Equals(IMap? other) => this.DeepEquals(other);
    
    public override bool Equals(object? obj) => obj is IMap other && this.DeepEquals(other);
    
    public override int GetHashCode() => this.MapHashCode();
}
