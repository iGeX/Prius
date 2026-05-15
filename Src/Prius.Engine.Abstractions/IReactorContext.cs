using Prius.Core.Maps;

namespace Prius.Engine.Abstractions;

public interface IReactorContext
{
    string Key { get; }
    
    IMap Env { get; }

    void Put(MapPath path, MapValue value, IMap? envPatch = null);
    
    MapValue Get(MapPath path, IMap? envPatch = null);
    
    void Notify(IMap changedKeys);
}
