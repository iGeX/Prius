namespace Prius.Core.Maps;

public sealed class StackedMap(IEnumerable<IMap> maps) : IMap
{
    private readonly IEnumerable<IMap> _maps = maps ?? throw new ArgumentNullException(nameof(maps));

    public bool IsEmpty => !Keys().Any();
    
    public MapValue Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return Empty.Instance;
        
        foreach (var map in _maps.Reverse())
        {
            var result = map.Get(key);
            if (!result.IsEmpty)
                return result;
        }

        return Empty.Instance;
    }

    public void Put(string key, MapValue value)
    {
        foreach (var map in _maps.Reverse())
            map.Put(key, value);
    }

    public IEnumerable<string> Keys(bool? ascending = null)
    {
        var enm = _maps.SelectMany(m => m.Keys()).Distinct();
        if (ascending != null)
            enm = ascending.Value ? enm.OrderBy(k => k) : enm.OrderByDescending(k => k);
        return enm;
    }

    public IEnumerable<MapValue> Values => Keys().Select(Get);

    public static StackedMap New(params IMap[] maps) => new(maps);

    public bool Equals(IMap? other) => this.DeepEquals(other);

    public override bool Equals(object? obj) => obj is IMap other && this.DeepEquals(other);

    public override int GetHashCode() => this.MapHashCode();
}
