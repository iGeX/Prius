namespace Prius.Core.Maps;

public static class MapTaskExtensions
{
    public static async Task<MapValue> Get(this Task<IMap> map, string key) => (await map).Get(key);

    public static async Task Put(this Task<IMap> map, string key, MapValue value) => (await map).Put(key, value);

    public static async Task<IMap> GetAll(this Task<IMap> map, IEnumerable<string> keys) => (await map).GetAll(keys);

    public static async Task PutAll(this Task<IMap> map, IEnumerable<string> keys, IMap subMap) => (await map).PutAll(keys, subMap);

    public static async Task<bool> IsValue(this Task<MapValue> mapValue) => (await mapValue).IsValue();
    
    public static Task PutEmpty(this Task<IMap> map, string key) => map.Put(key, Empty.Instance);

    public static async Task<object?> AsValue(this Task<MapValue> mapValue) => (await mapValue).AsValue();

    public static async Task<TValue> AsValue<TValue>(this Task<MapValue> mapValue) => (await mapValue).AsValue<TValue>();

    public static async Task<IMap> AsMap(this Task<MapValue> mapValue) => (await mapValue).AsMap();
    
    public static async Task<TResult> Match<TResult>(
        this Task<MapValue> mapValue,
        Func<Empty, Task<TResult>> onEmpty,
        Func<IMap, Task<TResult>> onMap,
        Func<object, Task<TResult>> onValue) =>
        await (await mapValue).Match(onEmpty, onMap, onValue);

    public static async Task Switch(
        this Task<MapValue> mapValue,
        Func<Empty, Task> onEmpty,
        Func<IMap, Task> onMap,
        Func<object, Task> onValue) =>
        await (await mapValue).Match(onEmpty, onMap, onValue);

    public static async Task Switch(
        this Task<MapValue> mapValue,
        Func<IMap, Task> onMap,
        Func<object, Task> onValue) =>
        await (await mapValue).Match(_ => Task.CompletedTask, onMap, onValue);

    public static async Task Switch(this Task<MapValue> mapValue, Func<IMap, Task> onMap) => 
        await (await mapValue).Match(_ => Task.CompletedTask, onMap, _ => Task.CompletedTask);

    public static async Task<Dictionary<string, object?>> DeepCopy(this Task<IMap> map) => (await map).DeepCopy();

    public static async Task<string> Serialize(this Task<IMap> map) => (await map).Serialize();
}
