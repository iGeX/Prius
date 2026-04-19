using System.Text;
using System.Text.Json;

namespace Prius.Core.Maps;

public sealed class JsonReaderMap(ReadOnlyMemory<byte> data) : IMap
{
    public static JsonReaderMap From(string json) => string.IsNullOrWhiteSpace(json) 
        ? new JsonReaderMap("{}"u8.ToArray()) 
        : new JsonReaderMap(Encoding.UTF8.GetBytes(json));

    private DictionaryMap? _materialized;

    public bool IsEmpty
    {
        get
        {
            if (_materialized != null) 
                return _materialized.IsEmpty;
            var reader = new Utf8JsonReader(data.Span);
            return !reader.Read() || reader.TokenType == JsonTokenType.EndObject;
        }
    }

    public IEnumerable<MapValue> Values => Materialize().Values;

    public IEnumerable<string> Keys(bool? ascending = null) => Materialize().Keys(ascending);

    public MapValue Get(string key)
    {
        if (_materialized != null) return _materialized.Get(key);

        var reader = new Utf8JsonReader(data.Span);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return new MapValue();

        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals(key))
            {
                reader.Read();
                return ReadValue(ref reader);
            }
            reader.Skip();
        }

        return new MapValue();
    }

    public void Put(string key, MapValue value) => Materialize().Put(key, value);

    public bool Equals(IMap? other) => this.DeepEquals(other);

    private IMap Materialize()
    {
        if (_materialized != null) return _materialized;

        var map = DictionaryMap.New;
        var reader = new Utf8JsonReader(data.Span);

        if (reader.Read() && reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) 
                    continue;
                
                var key = reader.GetString()!;
                reader.Read();
                map.Put(key, ReadValue(ref reader));
            }
        }

        _materialized = map;
        return _materialized;
    }

    private MapValue ReadValue(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String: 
                return new MapValue(reader.GetString()!);
            case JsonTokenType.Number: 
                return new MapValue(reader.GetInt64());
            case JsonTokenType.True: 
                return new MapValue(true);
            case JsonTokenType.False: 
                return new MapValue(false);
            case JsonTokenType.StartObject:
            case JsonTokenType.StartArray:
                var start = (int)reader.TokenStartIndex;
                reader.Skip(); 
                var end = (int)reader.BytesConsumed;
                var length = end - start;
                
                return new MapValue(new JsonReaderMap(data.Slice(start, length)));
            case JsonTokenType.None:
            case JsonTokenType.EndObject:
            case JsonTokenType.EndArray:
            case JsonTokenType.PropertyName:
            case JsonTokenType.Comment:
            case JsonTokenType.Null:
            default: 
                return new MapValue();
        }
    }
}
