using System;
using Prius.Core.Maps;

namespace Prius.Engine.Abstractions;

/// <summary>
/// Public interface for application Element developers.
/// Focuses strictly on relative mapping and local scoping.
/// </summary>
public interface IElementContext : IMap
{
    bool Put(MapPath path, MapValue value, IMap? envPatch = null);
    
    MapValue Get(MapPath path, IMap? envPatch = null);
}

/// <summary>
/// Public interface for system infrastructure (e.g., Intent Processors).
/// Provides access to absolute path information and out-of-band execution.
/// </summary>
public interface ISystemElementContext : IElementContext
{
    string AbsolutePath { get; }
    
    string CallerSegment { get; }
    
    IElementContext? Parent { get; }
    
    void PutAbsolute(MapPath absolutePath, MapValue value);
    
    event Action<ISystemElementContext> OnCompleted;
    
    event Action<ISystemElementContext, Exception> OnFailed;
}
