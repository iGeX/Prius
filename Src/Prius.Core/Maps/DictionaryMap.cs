using System.Collections;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Prius.Core.Maps;

[DebuggerDisplay("Count = {_dictionary.Count}")]
[DebuggerTypeProxy(typeof(MapDebugView))]
public sealed class DictionaryMap(IDictionary dictionary) : IMap
{
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly IDictionary _dictionary = dictionary;
    
    public static DictionaryMap New => new(new Dictionary<string, object?>());
    
    public bool IsEmpty => _dictionary.Count is 0;

    public bool CanWrite => true;

    private IEnumerable<string> StringKeys => 
        _dictionary.Keys.Cast<object?>().Select(o => o?.ToString() ?? string.Empty);
    
    public IEnumerable<string> Keys(bool? ascending = null)
    {
        var enm = StringKeys;
        if (ascending.HasValue)
            enm = ascending.Value ? enm.OrderBy(k => k) : enm.OrderByDescending(k => k);
        return enm;
    }

    public bool ContainsKey(string key) => _dictionary.Contains(key);

    public MapValue this[string key]
    {
        get => _dictionary[key].AsMapValue();
        set => value.Switch(
            onEmpty: _ => _dictionary.Remove(key),
            onMap: map => _dictionary[key] = map.DeepCopy(),
            onString: val => _dictionary[key] = val,
            onLong: val => _dictionary[key] = val,
            onBool: val => _dictionary[key] = val,
            onDecimal: val => _dictionary[key] = val,
            onDateTimeOffset: val => _dictionary[key] = val
        );
    }

    public bool Equals(IMap? other) => this.DeepEquals(other);
    
    public override bool Equals(object? obj) => obj is IMap other && this.DeepEquals(other);
    
    public override int GetHashCode() => this.MapHashCode();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DictionaryMap From(string key, MapValue value) => New.With(key, value);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DictionaryMap From(params (string Key, MapValue Value)[] items) => From((IEnumerable<(string Key, MapValue Value)>) items);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DictionaryMap From(IEnumerable<(string Key, MapValue Value)> items)
    {
        var map = New;
        foreach (var (key, value) in items)
            map[key] = value;
        return map;
    }
}
