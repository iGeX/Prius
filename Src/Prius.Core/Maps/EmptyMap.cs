namespace Prius.Core.Maps;

public sealed class EmptyMap : IMap
{
    public static EmptyMap Instance { get; } = new();

    public bool IsEmpty => true;

    private EmptyMap() { }
    
    public IEnumerable<string> Keys(bool? ascending) => [];
    
    public IEnumerable<MapValue> Values => [];

    public MapValue Get(string key) => Empty.Instance;

    public void Put(string key, MapValue value)
    {
    }

    public bool Equals(IMap? other) => other is { IsEmpty: true };

    public override bool Equals(object? other) => other is IMap { IsEmpty: true };

    public override int GetHashCode() => 0;
}
