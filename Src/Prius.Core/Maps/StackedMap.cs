using System.Diagnostics;

namespace Prius.Core.Maps;

[DebuggerTypeProxy(typeof(MapDebugView))]
public sealed class StackedMap(IEnumerable<IMap> maps) : IMap
{
    public IEnumerable<IMap> Maps { get; } = maps ?? throw new ArgumentNullException(nameof(maps));

    public bool IsEmpty => !Keys().Any();
    
    public IEnumerable<string> Keys(bool? ascending = null)
    {
        var enm = Maps.SelectMany(m => m.Keys()).Distinct();
        if (ascending != null)
            enm = ascending.Value ? enm.OrderBy(k => k) : enm.OrderByDescending(k => k);
        return enm;
    }

    public MapValue this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) return Empty.Instance;
        
            foreach (var map in Maps.Reverse())
            {
                var result = map[key];
                if (!result.IsEmpty)
                    return result;
            }

            return Empty.Instance;
        }
        set
        {
            foreach (var map in Maps.Reverse())
                map[key] = value;
        }
    }

    public static StackedMap New(params IMap[] maps) => New((IEnumerable<IMap>)maps);
    
    public static StackedMap New(IEnumerable<IMap> maps) => new(maps);
    
    public bool Equals(IMap? other) => this.DeepEquals(other);

    public override bool Equals(object? obj) => obj is IMap other && this.DeepEquals(other);

    public override int GetHashCode() => this.MapHashCode();
}
