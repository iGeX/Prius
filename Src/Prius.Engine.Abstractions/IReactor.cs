namespace Prius.Engine.Abstractions;

using Core.Maps;

public interface IReactor
{
    bool Put(IReactorContext context, MapPath path, MapValue value);
    
    MapValue Get(IReactorContext context, MapPath path);
}
