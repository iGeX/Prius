// ReSharper disable PossibleMultipleEnumeration
// ReSharper disable SuspiciousTypeConversion.Global

namespace Prius.Core.Maps;

internal sealed class AsyncMapAdapter(IMap map) : IAsyncMap
{
    private readonly IMap _map = map ?? throw new ArgumentNullException(nameof(map));
    
    private readonly IAsyncMap? _asyncMap = map as IAsyncMap;

    public ValueTask<bool> IsEmpty => _asyncMap?.IsEmpty ?? new ValueTask<bool>(_map.IsEmpty);
    
    public ValueTask<long> Size => _asyncMap?.Size ?? new ValueTask<long>(_map.Keys().Count());

    public IAsyncEnumerable<string> Keys(bool? ascending = null) => _asyncMap?.Keys(ascending) ?? GetKeys(_map, ascending);
    
    private static async IAsyncEnumerable<string> GetKeys(IMap map, bool? ascending)
    {
        foreach (var key in map.Keys(ascending)) 
            yield return key;
        await Task.CompletedTask;
    }
    
    public IAsyncEnumerable<MapValue> Values  => _asyncMap?.Values ?? GetValues(_map);
    
    private async IAsyncEnumerable<MapValue> GetValues(IMap map)
    {
        foreach (var value in map.Values)
        {
            yield return value;
        }
        await Task.CompletedTask;
    }
    
    public ValueTask<MapValue> Get(string key) => _asyncMap?.Get(key) ?? new ValueTask<MapValue>(_map.Get(key));

    public ValueTask<bool> Put(string key, MapValue value)
    {
        if(_asyncMap is not null)
            return _asyncMap.Put(key, value);
        
        _map.Put(key, value);
        return new ValueTask<bool>(true);
    }
    
    public ValueTask<IMap> GetAll(IEnumerable<string> keys) => _asyncMap?.GetAll(keys) ?? new ValueTask<IMap>(_map.GetAll(keys));


    public ValueTask<bool> PutAll(IEnumerable<string> keys, IMap source)
    {
        if(_asyncMap is not null)
            return _asyncMap.PutAll(keys, source);
        
        _map.PutAll(keys, source);
        return new ValueTask<bool>(true);
    }
}
