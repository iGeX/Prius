using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class GateElement : IElement
{
    public bool Put(IElementContext context, MapPath path, MapValue value)
    {
        if (path.IsEmpty)
        {
            if (value.IsMap)
            {
                if (!context.Get("$Active").AsBool()) 
                    return false;
                
                var map = value.AsMap();
                foreach (var key in map.Keys()) 
                    context.Put(key, map[key]);
                
                return true;
            }

            return context.Put("$Active", value);
        }

        var state = context.Get("$Active");
        if (!state.AsBool()) 
            return false;

        return context.Put(path, value);
    }

    public MapValue Get(IElementContext context, MapPath path)
    {
        var isActive = context.Get("$Active").AsBool();
        if (path.IsEmpty) 
            return isActive;

        return isActive ? context.Get(path) : new MapValue();
    }
}
