using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

internal sealed class RoutingNode
{
    public Dictionary<string, RoutingNode> Children { get; } = new(StringComparer.Ordinal);
    
    public IElement? TerminalElement { get; set; }
    
    public IMap? TerminalStaticEnv { get; set; }
    
    public IElement? WildcardElement { get; set; }
    
    public IMap? WildcardStaticEnv { get; set; }
    
    public IElement? DeepWildcardElement { get; set; }
    
    public IMap? DeepWildcardStaticEnv { get; set; }
}
