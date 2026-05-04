namespace Prius.Engine.Abstractions;

using Core.Maps;

public interface IReactor
{
    void Put(IReactorContext context, MapValue value);
    
    MapValue Get(IReactorContext context);
}
