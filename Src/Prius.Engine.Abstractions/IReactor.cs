namespace Prius.Engine.Abstractions;

using Core.Maps;

public interface IReactor
{
    void Put(IReactorContext context, MapPath path, MapValue value);
    
    MapValue Get(IReactorContext context, MapPath path);
    
    void Notify(IReactorContext context, IMap changedKeys);
}
