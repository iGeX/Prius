using Prius.Core.Maps;

namespace Prius.Engine.Abstractions;

public interface IReactorContext : IMap
{
    string Key { get; }
    
    void Put(MapPath path, MapValue value, IMap? envPatch = null);
    
    MapValue Get(MapPath path, IMap? envPatch = null);
    
    void Notify(MapPath path, MapValue value);
}
