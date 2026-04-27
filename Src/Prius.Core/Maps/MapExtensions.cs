using System.Collections;
using System.Globalization;
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
        
        for (var i = orderMap.Values.Count() - 1; i >= 0; i--)
        {
            var indexKey = i.ToIndexString();
            var value = orderMap.Get(indexKey);
        
            if (!value.IsEmpty)
                yield return value.AsValue<string>();
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T OtherTo<T>() => typeof(T) == typeof(string) ? (T)(object)string.Empty : default!;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T LongTo<T>(long l) => typeof(T) switch
    {
        var t when t == typeof(long) => (T)(object)l,
        var t when t == typeof(decimal) => (T)(object)(decimal)l,
        var t when t == typeof(bool) => (T)(object)(l != 0),
        var t when t == typeof(string) => (T)(object)l.ToString(CultureInfo.InvariantCulture),
        var t when t == typeof(DateTimeOffset) => (T)(object)DateTimeOffset.FromUnixTimeMilliseconds(l),
        _ => default!
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T DecimalTo<T>(decimal d) => typeof(T) switch
    {
        var t when t == typeof(decimal) => (T)(object)d,
        var t when t == typeof(long) => (T)(object)(long)Math.Round(d),
        var t when t == typeof(bool) => (T)(object)(d != 0),
        var t when t == typeof(string) => (T)(object)d.ToString(CultureInfo.InvariantCulture),
        _ => default!
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T BoolTo<T>(bool b) => typeof(T) switch
    {
        var t when t == typeof(bool) => (T)(object)b,
        var t when t == typeof(long) => (T)(object)(b ? 1L : 0L),
        var t when t == typeof(decimal) => (T)(object)(b ? 1m : 0m),
        var t when t == typeof(string) => (T)(object)(b ? "true" : "false"),
        _ => default!
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T DateTimeTo<T>(DateTimeOffset dt) => typeof(T) switch
    {
        var t when t == typeof(DateTimeOffset) => (T)(object)dt,
        var t when t == typeof(long) => (T)(object)dt.ToUnixTimeMilliseconds(),
        var t when t == typeof(string) => (T)(object)dt.ToString("O"),
        _ => default!
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static T StringTo<T>(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return (T)(object)string.Empty;
        if (typeof(T) == typeof(string)) 
            return (T)(object)s;
        
        var span = s.AsSpan().Trim();

        if (typeof(T) == typeof(long)) 
            return (T)(object)(long.TryParse(span, NumberStyles.Any, CultureInfo.InvariantCulture, out var l) ? l : 0L);

        if (typeof(T) == typeof(decimal))
        {
            if (span.Length < 128)
            {
                Span<char> buffer = stackalloc char[span.Length];
                span.CopyTo(buffer);
                
                for (var i = 0; i < buffer.Length; i++)
                    if (buffer[i] == ',') buffer[i] = '.';

                return (T)(object)(decimal.TryParse(buffer, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m);
            }
            
            var normalized = s.Replace(',', '.');
            return (T)(object)(decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var d2) ? d2 : 0m);
        }

        if (typeof(T) == typeof(bool))
        {
            if (bool.TryParse(s, out var b)) 
                return (T)(object)b;
            if (span.Equals("1".AsSpan(), StringComparison.Ordinal) || span.Equals("true".AsSpan(), StringComparison.OrdinalIgnoreCase)) 
                return (T)(object)true;
            return (T)(object)false;
        }

        if (typeof(T) == typeof(DateTimeOffset))
        {
            return (T)(object)(DateTimeOffset.TryParse(span, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt) 
                ? dt 
                : DateTimeOffset.MinValue);
        }

        return default!;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsValue(this MapValue mapValue) => 
        mapValue.IsString || mapValue.IsLong || mapValue.IsBoolean || mapValue.IsDecimal ||  mapValue.IsDateTimeOffset;
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static object? AsValue(this MapValue mapValue) => mapValue.Match(
            object? (_) => null, 
            object? (_) => null,
            s => s,
            l => l,
            b => b,
            d => d,
            dt => dt);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static T AsValue<T>(this MapValue val) =>
        val.Match(
            onEmpty: _ => OtherTo<T>(),
            onMap: _ => OtherTo<T>(),
            onString: StringTo<T>, 
            onLong: LongTo<T>,
            onBool: BoolTo<T>,
            onDecimal: DecimalTo<T>,
            onDateTimeOffset: DateTimeTo<T>
        );

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string AsString(this MapValue val) => val.AsValue<string>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long AsLong(this MapValue val) => val.AsValue<long>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int AsInt(this MapValue val) => (int) val.AsValue<long>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AsBool(this MapValue val) => val.AsValue<bool>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static decimal AsDecimal(this MapValue val) => val.AsValue<decimal>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DateTimeOffset AsDateTimeOffset(this MapValue val) => val.AsValue<DateTimeOffset>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IMap AsMap(this MapValue mapValue) => mapValue.Match(
        _ => EmptyMap.Instance, 
        m => m,
        _ => EmptyMap.Instance,
        _ => EmptyMap.Instance,
        _ => EmptyMap.Instance,
        _ => EmptyMap.Instance,
        _ => EmptyMap.Instance);
  
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
    public static void Put(this IMap? map, string key, IMap subMap) => map?.Put(key, new MapValue(subMap));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PutEmpty(this IMap? map, string key) => map?.Put(key, Empty.Instance);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IMap GetAll(this IMap? map, IEnumerable<string> keys)
    {
        if (map == null) 
            return EmptyMap.Instance;
        
        var dict = new Dictionary<string, object>();
        foreach (var key in keys)
        {
            var val = map.Get(key);
            if (!val.IsEmpty) 
                dict[key] = val;
        }
        return new DictionaryMap(dict);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void PutAll(this IMap? map, IEnumerable<string> keys, IMap source)
    {
        foreach (var key in keys)
            map?.Put(key, source.Get(key));
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, string key, MapValue value) where TMap : IMap
    {
        map.Put(key, value);
        return map;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, params (string Key, MapValue Value)[] items) where TMap : IMap
    {
        foreach (var (key, value) in items)
            map.Put(key, value);
        return map;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, string key, IMap subMap) where TMap : IMap
    {
        map.Put(key, subMap);
        return map;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TMap With<TMap>(this TMap map, params (string Key, IMap SubMap)[] items) where TMap : IMap
    {
        foreach (var (key, subMap) in items)
            map.Put(key, subMap);
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
                var value = source.Get(key);

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
    public static MapValue ToMapValue(this object? obj) => obj switch
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
    private static void DoSerialize(IMap map, Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        foreach (var key in map.Keys(true))
        {
            writer.WritePropertyName(key);
            writer.WriteMapValue(map.Get(key));
        }
        writer.WriteEndObject();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteMapValue(this Utf8JsonWriter writer, MapValue mapValue) => mapValue.Switch(
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
            map.Get(key).Switch(
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
                var valL = currentLeft.Get(key);
                var valR = currentRight.Get(key);

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

    public static MapValue DeepGet(this IMap map, MapPath path)
    {
        while (true)
        {
            if (path.IsEmpty) 
                return new MapValue(map);

            var current = map.Get(path.Head);
            if (path.Tail.IsEmpty) 
                return current;

            if (!current.IsMap) 
                return new MapValue();
            
            map = current.AsMap();
            path = path.Tail;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void DeepPut(this IMap map, MapPath path, IMap value) => map.DeepPut(path,  new MapValue(value));

    public static void DeepPut(this IMap map, MapPath path, MapValue value)
    {
        while (true)
        {
            if (path.IsEmpty) 
                return;

            if (path.Tail.IsEmpty)
            {
                map.Put(path.Head, value);
                return;
            }

            var nextValue = map.Get(path.Head);
            if (!nextValue.IsMap)
            {
                map.Put(path.Head, new MapValue(EmptyMap.Instance));
                map = map.Get(path.Head).AsMap();
                path = path.Tail;
                continue;
            }

            map = nextValue.AsMap();
            path = path.Tail;
        }
    }
}
