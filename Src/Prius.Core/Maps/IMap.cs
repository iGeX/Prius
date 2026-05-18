namespace Prius.Core.Maps;

public interface IMap : IEquatable<IMap>
{
    bool IsEmpty { get; }
    
    bool CanWrite { get; }

    IEnumerable<string> Keys(bool? ascending = null);
    
    bool ContainsKey(string key);
    
    MapValue this[string key] { get; set; }
}
