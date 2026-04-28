using System.Collections;
using System.Diagnostics;

namespace Prius.Core.Maps;

[DebuggerDisplay("Count = {_list.Count}")]
[DebuggerTypeProxy(typeof(MapDebugView))]
public sealed class ListMap(IList list) : IMap
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly IList _list = list;
    
    public static ListMap New => new(new List<object>());

    public bool IsEmpty => _list.Count is 0;
    
    public IEnumerable<MapValue> Values => _list.Cast<object>().Select(MapExtensions.ToMapValue);

    public MapValue Get(string key)
    {
        if (int.TryParse(key, out var index) && index >= 0 && index < _list.Count)
            return _list[index].ToMapValue();
        return Empty.Instance;
    }

    public void Put(string key, MapValue value)
    {
        if (!int.TryParse(key, out var index) || index < 0 || index >= _list.Count)
            return;

        value.Switch(
            _ => _list[index] = null,
            map => _list[index] = map.DeepCopy(),
            val => _list[index] = val
        );
    }

    public IEnumerable<string> Keys(bool? ascending = null)
    {
        var result = Enumerable.Range(0, _list.Count);
        if (ascending == false)
            result = result.OrderByDescending(k => k);

        return result.Select(i => i.ToString());
    }
    
    public bool Equals(IMap? other) => this.DeepEquals(other);

    public override bool Equals(object? obj) => obj is IMap other && this.DeepEquals(other);

    public override int GetHashCode() => this.MapHashCode();
}
