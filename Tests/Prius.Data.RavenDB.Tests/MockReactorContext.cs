using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class MockReactorContext : IReactorContext
{
    public string AbsolutePath { get; set; } = string.Empty;
    public string CallerSegment { get; set; } = string.Empty;
    public string Key { get; set; } = "mock";
    
    public Dictionary<string, MapValue> PutCalls { get; } = new();

    public bool IsEmpty => false;
    public bool CanWrite => false;
    
    public IEnumerable<string> Keys(bool? ascending = null) => PutCalls.Keys;

    public bool ContainsKey(string key) => PutCalls.ContainsKey(key);

    public MapValue this[string key]
    {
        get => PutCalls.GetValueOrDefault(key, Empty.Instance);
        set { }
    }

    public bool Put(MapPath path, MapValue value, IMap? envPatch = null)
    {
        PutCalls[path.ToString()] = value;
        return true;
    }

    public MapValue Get(MapPath path, IMap? envPatch = null) => Empty.Instance;

    public void PutAbsolute(MapPath absolutePath, MapValue value) => PutCalls[absolutePath.ToString()] = value;
}