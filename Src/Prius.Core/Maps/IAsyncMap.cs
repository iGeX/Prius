namespace Prius.Core.Maps;

public interface IAsyncMap
{
    ValueTask<bool> IsEmpty { get; }
    
    ValueTask<long> Size { get; }

    IAsyncEnumerable<MapValue> Values { get; }

    IAsyncEnumerable<string> Keys(bool? ascending = null);

    ValueTask<MapValue> Get(string key);

    ValueTask<bool> Put(string key, MapValue value);

    ValueTask<IMap> GetAll(IEnumerable<string> keys);

    ValueTask<bool> PutAll(IEnumerable<string> keys, IMap source);
}
