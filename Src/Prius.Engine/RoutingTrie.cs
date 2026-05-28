using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

internal sealed class RoutingTrie
{
    public RoutingNode Root { get; } = new();
    
    public void AddRoute(string pathPattern, IElement element, IMap? staticEnv = null)
    {
        if (string.IsNullOrWhiteSpace(pathPattern)) 
            throw new ArgumentNullException(nameof(pathPattern));
        ArgumentNullException.ThrowIfNull(element);

        var path = (MapPath)pathPattern;
        var current = Root;

        while (!path.IsEmpty)
        {
            var segment = path.Head;
            path = path.Tail;

            switch (segment)
            {
                case "**":
                    current.DeepWildcardElement = element;
                    current.DeepWildcardStaticEnv = staticEnv;
                    return;
                case "*" when path.IsEmpty:
                    current.WildcardElement = element;
                    current.WildcardStaticEnv = staticEnv;
                    return;
                case "*":
                    segment = "@wildcard";
                    break;
            }

            if (!current.Children.TryGetValue(segment, out var nextNode))
            {
                nextNode = new RoutingNode();
                current.Children.Add(segment, nextNode);
            }
            current = nextNode;
        }

        current.TerminalElement = element;
        current.TerminalStaticEnv = staticEnv;
    }

    public ResolveResult Resolve(MapPath absolutePath) => ResolveScoped(Root, absolutePath, null, null);

    public static ResolveResult ResolveScoped(RoutingNode startNode, MapPath path, IElement? initialFallback, IMap? initialFallbackEnv)
    {
        var current = startNode;
        var fallbackElement = initialFallback;
        var fallbackStaticEnv = initialFallbackEnv;
        var fallbackDepth = 0;
        var fallbackNode = startNode;

        var originalPath = path;
        var currentDepth = 0;

        if (path.IsEmpty)
        {
            if (current.TerminalElement != null)
                return new ResolveResult(current.TerminalElement, string.Empty, current.TerminalStaticEnv, current, MatchType.Terminal);
            
            return new ResolveResult(EmptyElement.Instance, string.Empty, null, current, MatchType.None);
        }

        while (!path.IsEmpty)
        {
            if (current.DeepWildcardElement != null && (current != startNode || initialFallback == null))
            {
                fallbackElement = current.DeepWildcardElement;
                fallbackStaticEnv = current.DeepWildcardStaticEnv;
                fallbackDepth = currentDepth;
                fallbackNode = current;
            }

            var segment = path.Head;
            var isLastSegment = path.Tail.IsEmpty;

            if (current.Children.TryGetValue(segment, out var nextNode))
            {
                current = nextNode;
                path = path.Tail;
                currentDepth++;
                
                if (current.TerminalElement != null)
                    return new ResolveResult(current.TerminalElement, path, current.TerminalStaticEnv, current, MatchType.Terminal);
                
                continue;
            }

            if (isLastSegment && current.WildcardElement != null)
                return new ResolveResult(current.WildcardElement, string.Empty, current.WildcardStaticEnv, current, MatchType.Wildcard);

            if (current.Children.TryGetValue("@wildcard", out var wildcardNode))
            {
                current = wildcardNode;
                path = path.Tail;
                currentDepth++;
                
                if (current.TerminalElement != null)
                    return new ResolveResult(current.TerminalElement, path, current.TerminalStaticEnv, current, MatchType.Terminal);
                
                continue;
            }

            if (fallbackElement != null)
                return new ResolveResult(fallbackElement, SlicePath(originalPath, fallbackDepth), fallbackStaticEnv, fallbackNode, MatchType.DeepWildcard);

            return new ResolveResult(EmptyElement.Instance, string.Empty, null, current, MatchType.None);
        }

        if (current.TerminalElement != null)
            return new ResolveResult(current.TerminalElement, string.Empty, current.TerminalStaticEnv, current, MatchType.Terminal);

        if (current.DeepWildcardElement != null && (current != startNode || initialFallback == null))
            return new ResolveResult(current.DeepWildcardElement, string.Empty, current.DeepWildcardStaticEnv, current, MatchType.DeepWildcard);

        return fallbackElement != null 
            ? new ResolveResult(fallbackElement, SlicePath(originalPath, fallbackDepth), fallbackStaticEnv, fallbackNode, MatchType.DeepWildcard) 
            : new ResolveResult(EmptyElement.Instance, string.Empty, null, current, MatchType.None);
    }

    private static MapPath SlicePath(MapPath path, int segmentsToSkip)
    {
        var result = path;
        for (var i = 0; i < segmentsToSkip; i++)
            result = result.Tail;
            
        return result;
    }
}

internal enum MatchType
{
    None,
    Terminal,
    Wildcard,
    DeepWildcard
}

internal readonly ref struct ResolveResult(IElement element, MapPath remainingPath, IMap? staticEnv, RoutingNode node, MatchType matchType)
{
    public IElement Element { get; } = element;

    public MapPath RemainingPath { get; } = remainingPath;

    public IMap? StaticEnv { get; } = staticEnv;

    public RoutingNode MatchNode { get; } = node;

    public MatchType MatchType { get; } = matchType;
}
