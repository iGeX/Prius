using Sparrow.Json;

namespace Prius.Data.RavenDB;

using Core.Maps;
using Sparrow.Json.Parsing;
using System.Collections.Generic;

public static class RavenJsonExtensions
{
    private readonly struct StackItem(DynamicJsonValue targetJson, IMap map)
    {
        public readonly DynamicJsonValue TargetJson = targetJson;
        public readonly IEnumerator<string> KeysEnumerator = map.Keys().GetEnumerator();
        public readonly IMap Map = map;
    }

    public static async ValueTask<JsonReaderMap> AsJsonReaderMap(this BlittableJsonReaderObject doc)
    {
        var ms = new MemoryStream(doc.Count);
        await doc.WriteJsonToAsync(ms);
        return new JsonReaderMap(ms.GetBuffer());
    }

    public static DynamicJsonValue AsDynamicJson(this IMap? map)
    {
        var result = new DynamicJsonValue();
        if (map is null) return result;

        var stack = new Stack<StackItem>();
        stack.Push(new StackItem(result, map));

        while (stack.Count > 0)
        {
            var current = stack.Peek();

            if (!current.KeysEnumerator.MoveNext())
            {
                current.KeysEnumerator.Dispose();
                stack.Pop();
                continue;
            }

            var key = current.KeysEnumerator.Current;
            var value = current.Map[key];

            if (value.IsEmpty) continue;

            if (value.IsMap)
            {
                var subMap = new DynamicJsonValue();
                current.TargetJson[key] = subMap;
                stack.Push(new StackItem(subMap, value.AsMap()));
                continue;
            }

            current.TargetJson[key] = value.AsValue();
        }

        return result;
    }

    public static Dictionary<string, object?> ToDictionary(this IMap map)
    {
        var dict = new Dictionary<string, object?>();
        foreach (var key in map.Keys())
        {
            var val = map[key];
            val.Switch(
                onEmpty: _ => dict[key] = null,
                onMap: m => dict[key] = ToDictionary(m),
                onString: s => dict[key] = s,
                onLong: l => dict[key] = l,
                onBool: b => dict[key] = b,
                onDecimal: d => dict[key] = d,
                onDateTimeOffset: dto => dict[key] = dto
            );
        }
        return dict;
    }
}
