namespace Prius.Engine;

using Core.Maps;
using Abstractions;

public readonly struct ReactorContext : IReactorContext
{
    internal readonly VirtualBus Bus;
    
    internal readonly string AbsolutePath;
    
    internal readonly IRavenBroker RavenBroker;

    public string Key => new MapPath(AbsolutePath.AsSpan()).Head;
    
    public IMap Env { get; }
    
    public IRavenBroker Raven => RavenBroker;

    internal ReactorContext(VirtualBus bus, string absolutePath, IMap env, IRavenBroker ravenBroker)
    {
        Bus = bus;
        AbsolutePath = absolutePath;
        Env = env;
        RavenBroker = ravenBroker;
    }

    public void Put(MapPath path, MapValue value, IMap? envPatch = null) => Bus.DispatchPut(AbsolutePath + path, value, Env, envPatch);

    public MapValue Get(MapPath path) => Bus.DispatchGet(AbsolutePath + path, Env);

    public void Notify(IMap changedKeys) => Bus.DispatchNotify(AbsolutePath, changedKeys);
}
