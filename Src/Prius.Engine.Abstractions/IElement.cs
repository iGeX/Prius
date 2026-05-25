namespace Prius.Engine.Abstractions;

using Core.Maps;

public interface IElement
{
    bool Put(IElementContext context, MapPath path, MapValue value);
    
    MapValue Get(IElementContext context, MapPath path);
}
