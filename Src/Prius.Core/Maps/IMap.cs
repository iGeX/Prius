namespace Prius.Core.Maps;

// ReSharper disable once ClassNeverInstantiated.Global
public class Empty
{
    public static Empty Instance { get; } = new();
    
    private Empty() { }

    public override bool Equals(object? obj) => obj is Empty;

    public override int GetHashCode() => 0;

    public override string ToString() => string.Empty;
}

public interface IMap : IEquatable<IMap>
{
    bool IsEmpty { get; }

    IEnumerable<MapValue> Values { get; }

    IEnumerable<string> Keys(bool? ascending = null);

    MapValue Get(string key);

    void Put(string key, MapValue value);
}
