namespace Prius.Engine;

using Core.Maps;
using Abstractions;

public readonly struct ReactorContext : IReactorContext
{
    private readonly VirtualBus _bus;
    
    internal readonly string AbsolutePath;
    
    public string Key => new MapPath(AbsolutePath).Tail;

    public IMap Env {get;}
    
    internal ReactorContext(VirtualBus bus, string absolutePath, IMap env)
    {
        _bus = bus;
        AbsolutePath = absolutePath;
        Env = env;
    }

    public void Put(MapPath path, MapValue value, IMap? envPatch = null) => _bus.DispatchPut(this, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => _bus.DispatchGet(this, path, envPatch);

    public void Notify(IMap changedKeys) => _bus.DispatchNotify(this, changedKeys);
}
