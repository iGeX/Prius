using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Prius.Core.Maps;

[DebuggerDisplay("{ToString()}")]
public readonly struct MapValue : IEquatable<MapValue>, IComparable<MapValue>
{
    private enum MapValueType : byte
    {
        Empty = 0,
        Map,
        String,
        Long,
        Boolean,
        Decimal,
        DateTimeOffset
    }

    [StructLayout(LayoutKind.Explicit)]
    private readonly struct ValueUnion
    {
        [FieldOffset(0)] public readonly long Long;
        [FieldOffset(0)] public readonly bool Boolean;
        [FieldOffset(0)] public readonly decimal Decimal;
        [FieldOffset(0)] public readonly DateTimeOffset DateTimeOffset;

        public ValueUnion(long val) : this() => Long = val;
        public ValueUnion(bool val) : this() => Boolean = val;
        public ValueUnion(decimal val) : this() => Decimal = val;
        public ValueUnion(DateTimeOffset val) : this() => DateTimeOffset = val;
    }

    private readonly object? _refContent;
    private readonly ValueUnion _valContent;
    private readonly MapValueType _type;

    public MapValue()
    {
        _refContent = null;
        _valContent = default;
        _type = MapValueType.Empty;
    }

    public MapValue(IMap? value)
    {
        if (value == null)
        {
            _refContent = null;
            _valContent = default;
            _type = MapValueType.Empty;
            return;
        }

        _refContent = value;
        _valContent = default;
        _type = MapValueType.Map;
    }

    public MapValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            _refContent = null;
            _valContent = default;
            _type = MapValueType.Empty;
            return;
        }

        _refContent = value;
        _valContent = default;
        _type = MapValueType.String;
    }

    public MapValue(long value)
    {
        _refContent = null;
        _valContent = new ValueUnion(value);
        _type = MapValueType.Long;
    }

    public MapValue(bool value)
    {
        _refContent = null;
        _valContent = new ValueUnion(value);
        _type = MapValueType.Boolean;
    }

    public MapValue(decimal value)
    {
        _refContent = null;
        _valContent = new ValueUnion(value);
        _type = MapValueType.Decimal;
    }

    public MapValue(DateTimeOffset value)
    {
        _refContent = null;
        _valContent = new ValueUnion(value);
        _type = MapValueType.DateTimeOffset;
    }

    public bool IsEmpty => _type == MapValueType.Empty;
    
    public bool IsMap => _type == MapValueType.Map;
    
    public bool IsString => _type == MapValueType.String;
    
    public bool IsLong => _type == MapValueType.Long;
    
    public bool IsBoolean => _type == MapValueType.Boolean;
    
    public bool IsDecimal => _type == MapValueType.Decimal;
    
    public bool IsDateTimeOffset => _type == MapValueType.DateTimeOffset;
    
    public bool IsValue => !IsEmpty && !IsMap;

    public static implicit operator MapValue(Empty _) => new();
    public static implicit operator MapValue(string value) => new(value);
    public static implicit operator MapValue(long value) => new(value);
    public static implicit operator MapValue(bool value) => new(value);
    public static implicit operator MapValue(decimal value) => new(value);
    public static implicit operator MapValue(DateTimeOffset value) => new(value);
    public static implicit operator string(MapValue value) => value.AsValue<string>();
    public static implicit operator long(MapValue value) => value.AsValue<long>();
    public static implicit operator bool(MapValue value) => value.AsValue<bool>();
    public static implicit operator decimal(MapValue value) => value.AsValue<decimal>();
    public static implicit operator DateTimeOffset(MapValue value) => value.AsValue<DateTimeOffset>();
    
    public MapValue this[string key]
    {
        get => IsMap ? ((IMap)_refContent!)[key] : Empty.Instance;
        set
        {
            if (!IsMap)
                return;
            ((IMap)_refContent!)[key] = value;
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IMap AsMap() => IsMap ? (IMap) _refContent! : DictionaryMap.New;

    private T MapTo<T>()
    {
        if (typeof(T) == typeof(bool))
        {
            var b = !((IMap)_refContent!).IsEmpty;
            return Unsafe.As<bool, T>(ref b);
        }

        if (typeof(T) != typeof(IMap))
            return default!;
        
        var m = (IMap)_refContent!;
        return Unsafe.As<IMap, T>(ref m);

    }
    
    private T LongTo<T>()
    {
        var val = _valContent.Long;

        if (typeof(T) == typeof(long)) return Unsafe.As<long, T>(ref val);
        if (typeof(T) == typeof(decimal)) { var d = (decimal)val; return Unsafe.As<decimal, T>(ref d); }
        if (typeof(T) == typeof(bool)) { var b = val != 0; return Unsafe.As<bool, T>(ref b); }
        if (typeof(T) == typeof(string)) { var s = val.ToString(); return Unsafe.As<string, T>(ref s); }
        if (typeof(T) == typeof(DateTimeOffset)) { var dt = DateTimeOffset.FromUnixTimeMilliseconds(val); return Unsafe.As<DateTimeOffset, T>(ref dt); }

        return default!;
    }

    private T DecimalTo<T>()
    {
        var val = _valContent.Decimal;

        if (typeof(T) == typeof(decimal)) return Unsafe.As<decimal, T>(ref val);
        if (typeof(T) == typeof(long)) { var l = (long)Math.Round(val); return Unsafe.As<long, T>(ref l); }
        if (typeof(T) == typeof(bool)) { var b = val != 0; return Unsafe.As<bool, T>(ref b); }
        if (typeof(T) == typeof(string)) { var s = val.ToString(CultureInfo.InvariantCulture); return Unsafe.As<string, T>(ref s); }

        return default!;
    }

    private T BoolTo<T>()
    {
        var val = _valContent.Boolean;

        if (typeof(T) == typeof(bool)) return Unsafe.As<bool, T>(ref val);
        if (typeof(T) == typeof(long)) { var l = val ? 1L : 0L; return Unsafe.As<long, T>(ref l); }
        if (typeof(T) == typeof(decimal)) { var d = val ? 1m : 0m; return Unsafe.As<decimal, T>(ref d); }
        if (typeof(T) == typeof(string)) { var s = val ? "1" : "0"; return Unsafe.As<string, T>(ref s); }

        return default!;
    }

    private T DateTimeTo<T>()
    {
        var val = _valContent.DateTimeOffset;

        if (typeof(T) == typeof(DateTimeOffset)) return Unsafe.As<DateTimeOffset, T>(ref val);
        if (typeof(T) == typeof(bool)) { var b = true; return Unsafe.As<bool, T>(ref b); }
        if (typeof(T) == typeof(long)) { var l = val.ToUnixTimeMilliseconds(); return Unsafe.As<long, T>(ref l); }
        if (typeof(T) == typeof(string)) { var s = val.ToString("O"); return Unsafe.As<string, T>(ref s); }

        return default!;
    }

    private T StringTo<T>()
    {
        if (typeof(T) == typeof(string)) 
        {
            var strRef = (string)_refContent!;
            return Unsafe.As<string, T>(ref strRef);
        }
        
        var stringContent = (string)_refContent!;
        var span = stringContent.AsSpan().Trim();

        if (typeof(T) == typeof(long)) 
        {
            var l = long.TryParse(span, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0L;
            return Unsafe.As<long, T>(ref l);
        }

        if (typeof(T) == typeof(decimal))
        {
            decimal d;
            if (span.Length < 128)
            {
                Span<char> buffer = stackalloc char[span.Length];
                span.CopyTo(buffer);
                
                for (var i = 0; i < buffer.Length; i++)
                    if (buffer[i] == ',') buffer[i] = '.';

                d = decimal.TryParse(buffer, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
            }
            else
            {
                var normalized = stringContent.Replace(',', '.');
                d = decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0m;
            }
            return Unsafe.As<decimal, T>(ref d);
        }

        if (typeof(T) == typeof(bool))
        {
            bool b;
            if (bool.TryParse(span, out var parsed))
                b = parsed;
            else if (span.Length == 0 || span.Equals("false".AsSpan(), StringComparison.OrdinalIgnoreCase) || span.Equals("0".AsSpan(), StringComparison.Ordinal))
                b = false;
            else
                b = true;
            return Unsafe.As<bool, T>(ref b);
        }

        if (typeof(T) == typeof(DateTimeOffset))
        {
            var dt = DateTimeOffset.TryParse(span, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed) ? parsed : DateTimeOffset.MinValue;
            return Unsafe.As<DateTimeOffset, T>(ref dt);
        }

        return default!;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public object? AsValue()
    {
        if (IsEmpty) 
            return null;
        if (IsMap) 
            return _refContent;
        return _type switch
        {
            MapValueType.String => _refContent,
            MapValueType.Long => _valContent.Long,
            MapValueType.Boolean => _valContent.Boolean,
            MapValueType.Decimal => _valContent.Decimal,
            MapValueType.DateTimeOffset => _valContent.DateTimeOffset,
            _ => null
        };
    }

    public T AsValue<T>() =>
        _type switch
        {
            MapValueType.Empty => default!,
            MapValueType.Map => MapTo<T>(),
            MapValueType.String => StringTo<T>(),
            MapValueType.Long => LongTo<T>(),
            MapValueType.Boolean => BoolTo<T>(),
            MapValueType.Decimal => DecimalTo<T>(),
            MapValueType.DateTimeOffset => DateTimeTo<T>(),
            _ => throw new ArgumentOutOfRangeException()
        };
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public string AsString() => AsValue<string>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long AsLong() => AsValue<long>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AsInt() => (int) AsValue<long>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool AsBool() => AsValue<bool>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public decimal AsDecimal() => AsValue<decimal>();
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTimeOffset AsDateTimeOffset() => AsValue<DateTimeOffset>();

    public void Switch(
        Action<Empty> onEmpty,
        Action<IMap> onMap,
        Action<string> onString,
        Action<long> onLong,
        Action<bool> onBool,
        Action<decimal> onDecimal,
        Action<DateTimeOffset> onDateTimeOffset)
    {
        switch (_type)
        {
            case MapValueType.Empty: onEmpty(Empty.Instance); break;
            case MapValueType.Map: onMap((IMap)_refContent!); break;
            case MapValueType.String: onString((string)_refContent!); break;
            case MapValueType.Long: onLong(_valContent.Long); break;
            case MapValueType.Boolean: onBool(_valContent.Boolean); break;
            case MapValueType.Decimal: onDecimal(_valContent.Decimal); break;
            case MapValueType.DateTimeOffset: onDateTimeOffset(_valContent.DateTimeOffset); break;
            default: throw new InvalidOperationException($"Unsupported type: {_type}");
        }
    }

    public T Match<T>(
        Func<Empty, T> onEmpty,
        Func<IMap, T> onMap,
        Func<string, T> onString,
        Func<long, T> onLong,
        Func<bool, T> onBool,
        Func<decimal, T> onDecimal,
        Func<DateTimeOffset, T> onDateTimeOffset) => _type switch
    {
        MapValueType.Empty => onEmpty(Empty.Instance),
        MapValueType.Map => onMap((IMap)_refContent!),
        MapValueType.String => onString((string)_refContent!),
        MapValueType.Long => onLong(_valContent.Long),
        MapValueType.Boolean => onBool(_valContent.Boolean),
        MapValueType.Decimal => onDecimal(_valContent.Decimal),
        MapValueType.DateTimeOffset => onDateTimeOffset(_valContent.DateTimeOffset),
        _ => throw new InvalidOperationException($"Unsupported type: {_type}")
    };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsSelfEmpty() =>
        _type switch
        {
            MapValueType.Empty => true,
            MapValueType.Map => ((IMap)_refContent!).IsEmpty,
            MapValueType.String => ((string)_refContent!).AsSpan().Trim().Length == 0,
            _ => false
        };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<char> GetStringSpan() => _type switch
    {
        MapValueType.String => ((string)_refContent!).AsSpan().Trim(),
        MapValueType.Long => LongTo<string>().AsSpan(),
        MapValueType.Boolean => BoolTo<string>().AsSpan(),
        MapValueType.Decimal => DecimalTo<string>().AsSpan(),
        MapValueType.DateTimeOffset => DateTimeTo<string>().AsSpan(),
        _ => ReadOnlySpan<char>.Empty
    };

    public bool Equals(MapValue other)
    {
        var selfEmpty = IsSelfEmpty();
        var otherEmpty = other.IsSelfEmpty();

        if (selfEmpty && otherEmpty) return true;
        if (selfEmpty || otherEmpty) return false;

        if (_type == MapValueType.Map || other._type == MapValueType.Map)
        {
            if (_type == MapValueType.Map && other._type == MapValueType.Map)
                return ((IMap)_refContent!).Equals((IMap)other._refContent!);
            return false;
        }

        return _type switch
        {
            MapValueType.String => ((string)_refContent!).AsSpan().Trim().Equals(other.AsValue<string>().AsSpan().Trim(), StringComparison.Ordinal),
            MapValueType.Long => _valContent.Long == other.AsValue<long>(),
            MapValueType.Boolean => _valContent.Boolean == other.AsValue<bool>(),
            MapValueType.Decimal => _valContent.Decimal == other.AsValue<decimal>(),
            MapValueType.DateTimeOffset => _valContent.DateTimeOffset == other.AsValue<DateTimeOffset>(),
            _ => false
        };
    }

    public override bool Equals(object? obj)
    {
        if (obj is MapValue other) return Equals(other);

        var selfEmpty = IsSelfEmpty();

        switch (obj)
        {
            case null:
                return selfEmpty;
            case IMap map when selfEmpty && map.IsEmpty:
                return true;
            case IMap map when _type == MapValueType.Map:
                return ((IMap)_refContent!).Equals(map);
            case IMap _:
                return false;
            case string s:
                {
                    var sSpan = s.AsSpan().Trim();
                    if (selfEmpty && sSpan.Length == 0) return true;
                    if (selfEmpty) return false;

                    return _type switch
                    {
                        MapValueType.String => ((string)_refContent!).AsSpan().Trim().Equals(sSpan, StringComparison.Ordinal),
                        _ => _type != MapValueType.Map && AsValue<string>().AsSpan().Equals(sSpan, StringComparison.Ordinal)
                    };
                }
        }

        if (selfEmpty) 
            return false;
        
        if (_type == MapValueType.Map) 
            return false;

        return _type switch
        {
            MapValueType.Long => obj is long l && _valContent.Long == l,
            MapValueType.Boolean => obj is bool b && _valContent.Boolean == b,
            MapValueType.Decimal => obj is decimal d && _valContent.Decimal == d,
            MapValueType.DateTimeOffset => obj is DateTimeOffset dto && _valContent.DateTimeOffset == dto,
            _ => false
        };
    }

    public override int GetHashCode()
    {
        if (IsSelfEmpty()) 
            return 0;
        
        return _type == MapValueType.Map 
            ? _refContent!.GetHashCode() 
            : GetStringSpan().GetSpanHashCode();
    }

    public int CompareTo(MapValue other)
    {
        if (Equals(other))
            return 0;
        
        if (IsEmpty || _type == MapValueType.Map || other.IsEmpty || other._type == MapValueType.Map) 
            return 1;

        if (_type == MapValueType.DateTimeOffset || other._type == MapValueType.DateTimeOffset)
            return AsValue<DateTimeOffset>().CompareTo(other.AsValue<DateTimeOffset>());

        return AsValue<decimal>().CompareTo(other.AsValue<decimal>());
    }

    public static bool operator ==(MapValue left, MapValue right) => left.Equals(right);
    public static bool operator !=(MapValue left, MapValue right) => !left.Equals(right);
    public static bool operator <(MapValue left, MapValue right) => left.CompareTo(right) < 0;
    public static bool operator >(MapValue left, MapValue right) => left.CompareTo(right) > 0;
    public static bool operator <=(MapValue left, MapValue right) => left.CompareTo(right) <= 0;
    public static bool operator >=(MapValue left, MapValue right) => left.CompareTo(right) >= 0;

    public override string ToString()
    {
        if (IsEmpty) 
            return string.Empty;
        return IsString ? (string)_refContent! : AsValue()?.ToString()!;
    }
}

[DebuggerDisplay("Empty")]
public class Empty
{
    public static Empty Instance { get; } = new();
    
    private Empty() { }

    public override bool Equals(object? obj) => obj is Empty;

    public override int GetHashCode() => 0;

    public override string ToString() => string.Empty;
}
