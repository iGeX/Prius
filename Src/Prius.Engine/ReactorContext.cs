namespace Prius.Engine;

using Core.Maps;
using Abstractions;

public sealed class ReactorContext(VirtualBus bus, string absolutePath, string key, IMap env) : IReactorContext
{
    internal string AbsolutePath => absolutePath;

    public string Key => key;

    public IMap Env => env;

    public void Put(MapPath path, MapValue value, IMap? envPatch = null) => bus.DispatchPut(this, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => bus.DispatchGet(this, path, envPatch);

    public void Notify(IMap changedKeys) => bus.DispatchNotify(this, changedKeys);
}
