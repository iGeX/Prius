using System.Collections;

namespace Prius.Core.Maps;

public sealed class ListMap(IList list) : IMap
{
    public static ListMap New => new(new List<object>());

    public bool IsEmpty => list.Count is 0;
    
    public IEnumerable<MapValue> Values => list.Cast<object>().Select(MapExtensions.ToMapValue);

    public MapValue Get(string key)
    {
        if (int.TryParse(key, out var index) && index >= 0 && index < list.Count)
            return list[index].ToMapValue();
        return Empty.Instance;
    }

    public void Put(string key, MapValue value)
    {
        if (!int.TryParse(key, out var index) || index < 0 || index >= list.Count)
            return;

        value.Switch(
            _ => list[index] = null,
            map => list[index] = map.DeepCopy(),
            val => list[index] = val
        );
    }

    public IEnumerable<string> Keys(bool? ascending = null)
    {
        var result = Enumerable.Range(0, list.Count);
        if (ascending == false)
            result = result.OrderByDescending(k => k);

        return result.Select(i => i.ToString());
    }
    
    public bool Equals(IMap? other) => this.DeepEquals(other);

    public override bool Equals(object? obj) => obj is IMap other && this.DeepEquals(other);

    public override int GetHashCode() => this.MapHashCode();
}
