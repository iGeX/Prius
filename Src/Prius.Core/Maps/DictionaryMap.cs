namespace Prius.Core.Maps;

public sealed class DictionaryMap(IDictionary dictionary) : IMap
{
    public static DictionaryMap New => new(new Dictionary<string, object?>());
    
    public bool IsEmpty => dictionary.Count is 0;
    
    private IEnumerable<string> StringKeys => 
        dictionary.Keys.Cast<object?>().Select(o => o?.ToString() ?? string.Empty);

    public IEnumerable<MapValue> Values => 
        dictionary.Values.Cast<object?>().Select(v => v.ToMapValue());

    public MapValue Get(string key) => dictionary[key].ToMapValue();

    public void Put(string key, MapValue value) =>
        value.Switch(
            onEmpty: _ => dictionary.Remove(key),
            onMap: map => dictionary[key] = map.DeepCopy(),
            onString: val => dictionary[key] = val,
            onLong: val => dictionary[key] = val,
            onBool: val => dictionary[key] = val,
            onDecimal: val => dictionary[key] = val,
            onDateTimeOffset: val => dictionary[key] = val
        );

    public IEnumerable<string> Keys(bool? ascending)
    {
        var enm = StringKeys;
        if (ascending.HasValue)
            enm = ascending.Value ? enm.OrderBy(k => k) : enm.OrderByDescending(k => k);
        return enm;
    }

    public bool Equals(IMap? other) => this.DeepEquals(other);
    
    public override bool Equals(object? obj) => obj is IMap other && this.DeepEquals(other);
    
    public override int GetHashCode() => this.MapHashCode();
}
