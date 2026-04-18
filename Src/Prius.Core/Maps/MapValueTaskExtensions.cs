namespace Prius.Core.Maps;

public static class MapValueTaskExtensions
{
    public static async ValueTask<MapValue> Get(this ValueTask<IMap> map, string key) => (await map).Get(key);

    public static async ValueTask Put(this ValueTask<IMap> map, string key, MapValue value) => (await map).Put(key, value);

    public static async ValueTask<IMap> GetAll(this ValueTask<IMap> map, IEnumerable<string> keys) => (await map).GetAll(keys);

    public static async ValueTask PutAll(this ValueTask<IMap> map, IEnumerable<string> keys, IMap subMap) => (await map).PutAll(keys, subMap);

    public static async ValueTask<bool> IsValue(this ValueTask<MapValue> mapValue) => (await mapValue).IsValue();
    
    public static ValueTask PutEmpty(this ValueTask<IMap> map, string key) => map.Put(key, Empty.Instance);

    public static async ValueTask<object?> AsValue(this ValueTask<MapValue> mapValue) => (await mapValue).AsValue();

    public static async ValueTask<TValue> AsValue<TValue>(this ValueTask<MapValue> mapValue) => (await mapValue).AsValue<TValue>();

    public static async ValueTask<IMap> AsMap(this ValueTask<MapValue> mapValue) => (await mapValue).AsMap();
    
    public static async ValueTask<TResult> Match<TResult>(
        this ValueTask<MapValue> mapValue,
        Func<Empty, ValueTask<TResult>> onEmpty,
        Func<IMap, ValueTask<TResult>> onMap,
        Func<object, ValueTask<TResult>> onValue) =>
        await (await mapValue).Match(onEmpty, onMap, onValue);

    public static async ValueTask Switch(
        this ValueTask<MapValue> mapValue,
        Func<Empty, ValueTask> onEmpty,
        Func<IMap, ValueTask> onMap,
        Func<object, ValueTask> onValue) =>
        await (await mapValue).Match(onEmpty, onMap, onValue);

    public static async ValueTask Switch(
        this ValueTask<MapValue> mapValue,
        Func<IMap, ValueTask> onMap,
        Func<object, ValueTask> onValue) =>
        await (await mapValue).Match(_ => default, onMap, onValue);

    public static async ValueTask Switch(this ValueTask<MapValue> mapValue, Func<IMap, ValueTask> onMap) => 
        await (await mapValue).Match(_ => default, onMap, _ => default);

    public static async ValueTask<Dictionary<string, object?>> DeepCopy(this ValueTask<IMap> map) => (await map).DeepCopy();

    public static async ValueTask<string> Serialize(this ValueTask<IMap> map) => (await map).Serialize();
}
