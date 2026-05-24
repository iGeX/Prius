using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

internal sealed class RoutingNode
{
    // Точные совпадения сегментов (например, "Sys", "Orders", "Config")
    public Dictionary<string, RoutingNode> Children { get; } = new(StringComparer.Ordinal);
    
    // Реактор, если этот узел — точное окончание пути
    public IReactor? TerminalReactor { get; set; }
    public IMap? TerminalStaticEnv { get; set; }
    
    // Реактор, если на этом уровне сработал одинарный вайлдкард '*'
    public IReactor? WildcardReactor { get; set; }
    public IMap? WildcardStaticEnv { get; set; }
    
    // Реактор, если на этом уровне сработал глубокий вайлдкард '**' (перехватывает всё ниже)
    public IReactor? DeepWildcardReactor { get; set; }
    public IMap? DeepWildcardStaticEnv { get; set; }
}
