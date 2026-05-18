using System.Diagnostics;

namespace Prius.Core.Maps;

// ReSharper disable once ClassNeverInstantiated.Global
[DebuggerDisplay("Empty")]
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

    IEnumerable<string> Keys(bool? ascending = null);
    
    MapValue this[string key] { get; set; }
}
