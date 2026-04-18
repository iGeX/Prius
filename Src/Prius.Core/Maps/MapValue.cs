namespace Prius.Core.Maps;

// ReSharper disable ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
public readonly struct MapValue : IEquatable<MapValue>
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

    private readonly object? _content;
    private readonly MapValueType _type;

    public MapValue(IMap value)
    {
        if (value == null)
        {
            _content = Empty.Instance;
            _type = MapValueType.Empty;
            return;
        }

        _content = value;
        _type = MapValueType.Map;
    }

    public MapValue(string value)
    {
        if (value == null)
        {
            _content = Empty.Instance;
            _type = MapValueType.Empty;
            return;
        }

        _content = value;
        _type = MapValueType.String;
    }

    public MapValue(long value)
    {
        _content = value;
        _type = MapValueType.Long;
    }

    public MapValue(bool value)
    {
        _content = value;
        _type = MapValueType.Boolean;
    }

    public MapValue(decimal value)
    {
        _content = value;
        _type = MapValueType.Decimal;
    }

    public MapValue(DateTimeOffset value)
    {
        _content = value;
        _type = MapValueType.DateTimeOffset;
    }

    public bool IsEmpty => _type == MapValueType.Empty;
    
    public bool IsMap => _type == MapValueType.Map;
    
    public bool IsString => _type == MapValueType.String;
    
    public bool IsLong => _type == MapValueType.Long;
    
    public bool IsBoolean => _type == MapValueType.Boolean;
    
    public bool IsDecimal => _type == MapValueType.Decimal;
    
    public bool IsDateTimeOffset => _type == MapValueType.DateTimeOffset;

    public static implicit operator MapValue(Empty _) => new();
    public static implicit operator MapValue(string value) => new(value);
    public static implicit operator MapValue(long value) => new(value);
    public static implicit operator MapValue(bool value) => new(value);
    public static implicit operator MapValue(decimal value) => new(value);
    public static implicit operator MapValue(DateTimeOffset value) => new(value);
    

    public static implicit operator string(MapValue value) => value._type == MapValueType.String 
        ? (string)value._content! 
        : value.AsValue<string>();

    public static implicit operator long(MapValue value) => value._type == MapValueType.Long 
        ? (long)value._content! 
        : value.AsValue<long>();

    public static implicit operator bool(MapValue value) => value._type == MapValueType.Boolean 
        ? (bool)value._content!
        : value.AsValue<bool>();

    public static implicit operator decimal(MapValue value) => value._type == MapValueType.Decimal 
        ? (decimal)value._content! 
        : value.AsValue<decimal>();

    public static implicit operator DateTimeOffset(MapValue value) => value._type == MapValueType.DateTimeOffset 
        ? (DateTimeOffset)value._content! 
        : value.AsValue<DateTimeOffset>();

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
            case MapValueType.Empty: onEmpty((Empty)(_content ?? Empty.Instance)); break;
            case MapValueType.Map: onMap((IMap)_content!); break;
            case MapValueType.String: onString((string)_content!); break;
            case MapValueType.Long: onLong((long)_content!); break;
            case MapValueType.Boolean: onBool((bool)_content!); break;
            case MapValueType.Decimal: onDecimal((decimal)_content!); break;
            case MapValueType.DateTimeOffset: onDateTimeOffset((DateTimeOffset)_content!); break;
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
        MapValueType.Empty => onEmpty((Empty)(_content ?? Empty.Instance)),
        MapValueType.Map => onMap((IMap)_content!),
        MapValueType.String => onString((string)_content!),
        MapValueType.Long => onLong((long)_content!),
        MapValueType.Boolean => onBool((bool)_content!),
        MapValueType.Decimal => onDecimal((decimal)_content!),
        MapValueType.DateTimeOffset => onDateTimeOffset((DateTimeOffset)_content!),
        _ => throw new InvalidOperationException($"Unsupported type: {_type}")
    };

    public bool Equals(MapValue other) => _type == other._type && Equals(_content, other._content);

    public override bool Equals(object? obj) => obj is MapValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_content, _type);

    public static bool operator ==(MapValue left, MapValue right) => left.Equals(right);

    public static bool operator !=(MapValue left, MapValue right) => !left.Equals(right);

    public override string ToString() => (IsString ? (string?) _content : _content?.ToString()) ?? string.Empty;
}
