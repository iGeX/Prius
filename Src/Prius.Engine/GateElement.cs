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
                if (!context.Get("@Active").AsBool()) 
                    return false;
                
                var map = value.AsMap();
                foreach (var key in map.Keys()) 
                    context.Put(key, map[key]);
                
                return true;
            }

            if ("@Active".Equals(context.CallerSegment))
            {
                context.Put(string.Empty, value);
                return false;
            }

            context.Put("@Active", value);
            return true;
        }

        var state = context.Get("@Active");
        if (!state.AsBool()) 
            return false;

        context.Put(path, value);
        return true;
    }

    public MapValue Get(IElementContext context, MapPath path)
    {
        if (!path.IsEmpty) 
            return context.Get("@Active").AsBool() ? context.Get(path) : new MapValue();

        return "@Active".Equals(context.CallerSegment)
            ? context.Get(string.Empty)
            : context.Get("@Active").AsBool();
    }
}
