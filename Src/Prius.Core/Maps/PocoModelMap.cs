using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;

namespace Prius.Core.Maps;

[DebuggerDisplay("Count = {_props.Length}")]
[DebuggerTypeProxy(typeof(MapDebugView))]
public sealed class PocoModelMap : IMap
{
    private static readonly ConcurrentDictionary<Type, PocoAccessor[]> TypeCache = new();

    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly IPocoModel _model;
    
    [DebuggerBrowsable(DebuggerBrowsableState.Never)]
    private readonly PocoAccessor[] _props;

    public PocoModelMap(IPocoModel model)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        _props = TypeCache.GetOrAdd(model.GetType(), type => 
            type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => new PocoAccessor(p))
                .ToArray()
        );
    }

    public bool IsEmpty => _props.Length == 0;

    public bool CanWrite => true;
    
    public IEnumerable<string> Keys(bool? ascending = null)
    {
        var keys = _props.Select(p => p.Name);
        if (ascending.HasValue)
            keys = ascending.Value ? keys.OrderBy(k => k) : keys.OrderByDescending(k => k);
        return keys;
    }

    public bool ContainsKey(string key) => _props.Any(p => string.Equals(p.Name, key, StringComparison.Ordinal));

    public MapValue this[string key]
    {
        get
        {
            var prop = _props.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.Ordinal));
            return prop != null ? prop.GetValue(_model).AsMapValue() : Empty.Instance;
        }
        set
        {
            var prop = _props.FirstOrDefault(p => string.Equals(p.Name, key, StringComparison.Ordinal));
            if (prop == null || !prop.CanWrite) 
                return;
        
            value.Switch(
                _ => prop.SetValue(_model, null),
                map => prop.SetValue(_model, map),
                val => prop.SetValue(_model, val)
            );
        }
    }
    
    private class PocoAccessor(PropertyInfo prop)
    {
        public string Name => prop.Name;
        public bool CanWrite => prop.CanWrite;
        public object? GetValue(object target) => prop.GetValue(target);
        public void SetValue(object target, object? value) => prop.SetValue(target, value);
    }
}
