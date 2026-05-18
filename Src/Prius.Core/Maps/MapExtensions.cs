using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Prius.Core.Json;

namespace Prius.Core.Maps;

public static class MapExtensions
{
    private static readonly string[] IndexCache = Enumerable.Range(0, 1024)
        .Select(i => i.ToString())
        .ToArray();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ToIndexString(this int i) => (uint)i < (uint)IndexCache.Length ? IndexCache[i] : i.ToString();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IEnumerable<string> GetReverseOrder(this IMap orderMap)
    {
        if (orderMap.IsEmpty) 
            yield break;
        
        for (var i = orderMap.Keys().Count() - 1; i >= 0; i--)
        {
            var indexKey = i.ToIndexString();
            var value = orderMap[indexKey];
        
            if (!value.IsEmpty)
                yield return value.AsValue<string>();
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TResult Match<TResult>(
        this MapValue mapValue,
        Func<Empty, TResult> onEmpty,
        Func<IMap, TResult> onMap,
        Func<object, TResult> onValue) => mapValue.Match(onEmpty,
            onMap,
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            s => onValue(s ?? string.Empty),
            l => onValue(l),
            b => onValue(b),
            d => onValue(d),
            dt => onValue(dt));
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Switch(
        this MapValue mapValue,
        Action<Empty> onEmpty,
        Action<IMap> onMap,
        Action<object> onValue) => mapValue.Switch(
            onEmpty,
            onMap,
            // ReSharper disable once NullCoalescingConditionIsAlwaysNotNullAccordingToAPIContract
            s => onValue(s ?? string.Empty),
            l => onValue(l),
            b => onValue(b),
            d => onValue(d),
            dt => onValue(dt));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Switch(
        this MapValue mapValue,
        Action<IMap> onMap,
        Action<object> onValue) =>
        mapValue.Switch(_ => { }, onMap, onValue);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IMap GetAll(this IMap? map, IEnumerable<string> keys)
    {
        if (map == null) 
            return DictionaryMap.New;
        
        var dict = new Dictionary<string, object>();
        foreach (var key in keys)
        {
            var val = map[key];
            if (!val.IsEmpty) 
                dict[key] = val;
        }
        return new DictionaryMap(dict);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PutAll(this IMap? map, IEnumerable<string> keys, IMap source)
    {
        foreach (var key in keys)
            map?[key] = source[key];
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, string key, MapValue value) where TMap : IMap
    {
        map[key] = value;
        return map;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, params (string Key, MapValue Value)[] items) where TMap : IMap
    {
        foreach (var (key, value) in items)
            map[key] = value;
        return map;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, string key, IMap subMap) where TMap : IMap
    {
        map[key] = new MapValue(subMap);
        return map;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, params (string Key, IMap SubMap)[] items) where TMap : IMap
    {
        foreach (var (key, subMap) in items)
            map[key] = new MapValue(subMap);
        return map;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, IMap subMap) where TMap : IMap
    {
        foreach (var key in subMap.Keys())
            map[key] = subMap[key];
        return map;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, params IMap[] subMaps) where TMap : IMap
    {
        foreach (var subMap in subMaps)
            map.With(subMap);
        return map;
    }
    
    public static Dictionary<string, object?> DeepCopy(this IMap map)
    {
        var result = new Dictionary<string, object?>();
        if (map.IsEmpty)
            return result;

        var stack = new Stack<(IMap Source, Dictionary<string, object?> Target)>();
        stack.Push((map, result));

        while (stack.Count > 0)
        {
            var (source, target) = stack.Pop();

            foreach (var key in source.Keys())
            {
                var value = source[key];

                value.Switch(
                    onEmpty: _ => { },
                    onMap: m =>
                    {
                        var nestedCopy = new Dictionary<string, object?>();
                        target[key] = nestedCopy;

                        if (!m.IsEmpty)
                            stack.Push((m, nestedCopy));
                    },
                    onString: s => target[key] = s,
                    onLong: l => target[key] = l,
                    onBool: b => target[key] = b,
                    onDecimal: d => target[key] = d,
                    onDateTimeOffset: dt => target[key] = dt
                );
            }
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static MapValue AsMapValue(this object? obj) => obj switch
    {
        null or Empty => Empty.Instance,
        IMap map => new MapValue(map),
        string str         => str,
        bool b             => b,
        int i              => i,
        long l             => l,
        decimal d          => d,
        DateTimeOffset dto => dto,
        DateTime dt        => new DateTimeOffset(dt),
        IDictionary dict => new MapValue(new DictionaryMap(dict)),
        IEnumerable enm  => new MapValue(new ListMap(enm.Cast<object?>().ToList())),
        IPocoModel poco  => new MapValue(new PocoModelMap(poco)),
        _ => obj.ToString() ?? string.Empty
    };

    public static void Serialize(this IMap map, Stream utf8Stream)
    {
        if (map is null)
            throw new ArgumentNullException(nameof(map));
        if (utf8Stream is null)
            throw new ArgumentNullException(nameof(utf8Stream));

        using var writer = new Utf8JsonWriter(utf8Stream, new JsonWriterOptions
        {
            Encoder = JsonDefaults.Options.Encoder,
            Indented = JsonDefaults.Options.WriteIndented,
            SkipValidation = true
        });

        DoSerialize(map, writer);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Serialize(this IMap map)
    {
        using var stream = new MemoryStream();
        map.Serialize(stream);
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string Serialize(this MapValue value) => value.Match(
        _ => string.Empty,
        map => map.Serialize(),
        val => val.ToString());  
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void DoSerialize(IMap map, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        foreach (var key in map.Keys(true))
        {
            writer.WritePropertyName(key);
            writer.WriteMapValue(map[key]);
        }
        writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteMapValue(this Utf8JsonWriter writer, MapValue mapValue) => mapValue.Switch(
        _ => writer.WriteNullValue(),
        map => DoSerialize(map, writer),
        writer.WriteStringValue,
        writer.WriteNumberValue,
        writer.WriteBooleanValue,
        writer.WriteNumberValue,
        dt => writer.WriteStringValue(dt.ToString("O")));
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int MapHashCode(this IMap map)
    {
        var hash = new HashCode();
        foreach (var key in map.Keys(true))
        {
            hash.Add(key);
            map[key].Switch(
                _ => hash.Add(0),
                m => hash.Add(m.MapHashCode()),
                v => hash.Add(v)
            );
        }
        return hash.ToHashCode();
    }

    public static bool DeepEquals(this IMap? left, IMap? right)
    {
        if (ReferenceEquals(left, right)) 
            return true;

        var leftIsNothing = left == null || left.IsEmpty;
        var rightIsNothing = right == null || right.IsEmpty;
    
        if (leftIsNothing && rightIsNothing) 
            return true;
    
        if (leftIsNothing || rightIsNothing) 
            return false;

        var stack = new Stack<(IMap Left, IMap Right)>();
        stack.Push((left!, right!));

        while (stack.Count > 0)
        {
            var (currentLeft, currentRight) = stack.Pop();

            var keysCountLeft = 0;
            foreach (var key in currentLeft.Keys())
            {
                keysCountLeft++;
                var valL = currentLeft[key];
                var valR = currentRight[key];

                if (!ValueEquals(valL, valR, stack))
                    return false;
            }
            
            var keysCountRight = 0;
            foreach (var _ in currentRight.Keys())
            {
                keysCountRight++;
                if (keysCountRight > keysCountLeft) 
                    return false;
            }

            if (keysCountLeft != keysCountRight)
                return false;
        }

        return true;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ValueEquals(MapValue valL, MapValue valR, Stack<(IMap, IMap)> stack) => valL.Match(
        onEmpty: _ => valR.IsEmpty || (valR.IsMap && valR.AsMap().IsEmpty),
        onMap: leftMap => 
        {
            var leftIsEmpty = leftMap.IsEmpty;
            var rightIsMap = valR.IsMap;
            var rightMap = rightIsMap ? valR.AsMap() : null;
            var rightIsEmpty = valR.IsEmpty || (rightIsMap && rightMap!.IsEmpty);

            if (leftIsEmpty) 
                return rightIsEmpty;
        
            if (rightIsEmpty) 
                return false;
            
            if (!rightIsMap) 
                return false;
            
            stack.Push((leftMap, rightMap!));
            return true;

        },
        onString: s => string.Equals(s, valR.AsValue<string>(), StringComparison.Ordinal),
        onLong: l => l == valR.AsValue<long>(),
        onBool: b => b == valR.AsValue<bool>(),
        onDecimal: d => d == valR.AsValue<decimal>(),
        onDateTimeOffset: dt => dt == valR.AsValue<DateTimeOffset>()
    );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IAsyncMap AsAsync(this IMap map) => new AsyncMapAdapter(map);
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IMap AsReadOnly(this IMap map) => new ReadOnlyMap(map);

    public static MapValue Get(this IMap map, MapPath path)
    {
        while (true)
        {
            if (path.IsEmpty) 
                return new MapValue(map);

            var current = map[path.Head];
            if (path.Tail.IsEmpty) 
                return current;

            if (!current.IsMap) 
                return new MapValue();
            
            map = current.AsMap();
            path = path.Tail;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Put(this IMap map, MapPath path, IMap value) => map.Put(path,  new MapValue(value));

    public static void Put(this IMap map, MapPath path, MapValue value)
    {
        while (true)
        {
            if (path.IsEmpty) 
                return;

            if (path.Tail.IsEmpty)
            {
                map[path.Head] = value;
                return;
            }

            var nextValue = map[path.Head];
            if (!nextValue.IsMap)
            {
                map[path.Head] = new MapValue(DictionaryMap.New);
                map = map[path.Head].AsMap();
                path = path.Tail;
                continue;
            }

            map = nextValue.AsMap();
            path = path.Tail;
        }
    }
    
    public static int GetSpanHashCode(this ReadOnlySpan<char> span)
    {
        unchecked
        {
            var hash1 = (5381 << 16) + 5381;
            var hash2 = hash1;

            for (var i = 0; i < span.Length; i += 2)
            {
                hash1 = ((hash1 << 5) + hash1) ^ span[i];
                if (i + 1 < span.Length)
                {
                    hash2 = ((hash2 << 5) + hash2) ^ span[i + 1];
                }
            }

            return hash1 + (hash2 * 1566083941);
        }
    }
}
