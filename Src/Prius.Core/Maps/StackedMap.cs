using System.Diagnostics;

namespace Prius.Core.Maps;

[DebuggerTypeProxy(typeof(MapDebugView))]
public sealed class StackedMap(IEnumerable<IMap> maps) : IMap
{
    public IEnumerable<IMap> Maps { get; } = maps ?? throw new ArgumentNullException(nameof(maps));

    public bool IsEmpty => !Keys().Any();
    
    public bool CanWrite => true;
    
    public IEnumerable<string> Keys(bool? ascending = null)
    {
        var enm = Maps.SelectMany(m => m.Keys()).Distinct();
        if (ascending != null)
            enm = ascending.Value ? enm.OrderBy(k => k) : enm.OrderByDescending(k => k);
        return enm;
    }

    public bool ContainsKey(string key) => Maps.Any(m => m.ContainsKey(key));

    public MapValue this[string key]
    {
        get
        {
            if (string.IsNullOrEmpty(key)) 
                return Empty.Instance;
        
            foreach (var map in Maps.Reverse())
            {
                var result = map[key];
                if (!result.IsEmpty)
                    return result;
            }

            return Empty.Instance;
        }
        set
        {
            foreach (var map in Maps.Reverse())
                map[key] = value;
        }
    }
}
