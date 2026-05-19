using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class MockReactorContext : IReactorContext
{
    public string Key => "test";
    public readonly Dictionary<string, MapValue> PutCalls = new();
    public void Put(MapPath path, MapValue value, IMap? envPatch = null) => PutCalls[path.ToString()] = value;
    public MapValue Get(MapPath path, IMap? envPatch = null) => Empty.Instance;
    public void Notify(MapPath path, MapValue value) { }
    public bool IsEmpty => true;
    public bool CanWrite => false;
    public IEnumerable<string> Keys(bool? ascending = null) => [];
    public bool ContainsKey(string key) => false;
    public MapValue this[string key]
    {
        get => Empty.Instance;
        set {}
    }
}
