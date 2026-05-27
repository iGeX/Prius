using Prius.Core.Maps;

namespace Prius.Engine.Abstractions;

public interface IElementContext : IMap
{
    string AbsolutePath { get; }
    
    string CallerSegment { get; }
    
    bool Put(MapPath path, MapValue value, IMap? envPatch = null);
    
    MapValue Get(MapPath path, IMap? envPatch = null);
    
    void PutAbsolute(MapPath absolutePath, MapValue value);
}
