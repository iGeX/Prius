using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class RoutingTrie
{
    private readonly RoutingNode _root = new();

    internal RoutingNode Root => _root;

    public void AddRoute(string pathPattern, IElement element, IMap? staticEnv = null)
    {
        if (string.IsNullOrWhiteSpace(pathPattern)) 
            throw new ArgumentNullException(nameof(pathPattern));
        ArgumentNullException.ThrowIfNull(element);

        var path = (MapPath)pathPattern;
        var current = _root;

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

    public ResolveResult Resolve(MapPath absolutePath) => ResolveScoped(_root, absolutePath, null, null);

    internal ResolveResult ResolveScoped(RoutingNode startNode, MapPath path, IElement? initialFallback, IMap? initialFallbackEnv)
    {
        var current = startNode;
        IElement? fallbackElement = initialFallback;
        IMap? fallbackStaticEnv = initialFallbackEnv;
        var fallbackDepth = 0;
        var fallbackKey = string.Empty;
        var fallbackNode = startNode;

        var originalPath = path;
        var currentDepth = 0;
        var lastMatchedKey = string.Empty;

        if (path.IsEmpty)
        {
            if (current.TerminalElement != null)
                return new ResolveResult(current.TerminalElement, string.Empty, string.Empty, current.TerminalStaticEnv, current, MatchType.Terminal);
            
            return new ResolveResult(EmptyElement.Instance, string.Empty, string.Empty, null, current, MatchType.None);
        }

        while (!path.IsEmpty)
        {
            if (current.DeepWildcardElement != null && (current != startNode || initialFallback == null))
            {
                fallbackElement = current.DeepWildcardElement;
                fallbackStaticEnv = current.DeepWildcardStaticEnv;
                fallbackDepth = currentDepth;
                fallbackKey = lastMatchedKey;
                fallbackNode = current;
            }

            var segment = path.Head;
            var isLastSegment = path.Tail.IsEmpty;

            if (current.Children.TryGetValue(segment, out var nextNode))
            {
                current = nextNode;
                lastMatchedKey = segment;
                path = path.Tail;
                currentDepth++;
                
                if (current.TerminalElement != null)
                    return new ResolveResult(current.TerminalElement, path, lastMatchedKey, current.TerminalStaticEnv, current, MatchType.Terminal);
                
                continue;
            }

            if (isLastSegment && current.WildcardElement != null)
                return new ResolveResult(current.WildcardElement, string.Empty, segment, current.WildcardStaticEnv, current, MatchType.Wildcard);

            if (current.Children.TryGetValue("@wildcard", out var wildcardNode))
            {
                current = wildcardNode;
                lastMatchedKey = segment;
                path = path.Tail;
                currentDepth++;
                
                if (current.TerminalElement != null)
                    return new ResolveResult(current.TerminalElement, path, lastMatchedKey, current.TerminalStaticEnv, current, MatchType.Terminal);
                
                continue;
            }

            if (fallbackElement != null)
            {
                if (fallbackDepth == 0) 
                    return new ResolveResult(fallbackElement, originalPath.Tail, originalPath.Head, fallbackStaticEnv, fallbackNode, MatchType.DeepWildcard);
                
                return new ResolveResult(fallbackElement, SlicePath(originalPath, fallbackDepth), fallbackKey, fallbackStaticEnv, fallbackNode, MatchType.DeepWildcard);
            }

            return new ResolveResult(EmptyElement.Instance, string.Empty, segment, null, current, MatchType.None);
        }

        if (current.TerminalElement != null)
            return new ResolveResult(current.TerminalElement, string.Empty, lastMatchedKey, current.TerminalStaticEnv, current, MatchType.Terminal);

        if (current.DeepWildcardElement != null && (current != startNode || initialFallback == null))
            return new ResolveResult(current.DeepWildcardElement, string.Empty, lastMatchedKey, current.DeepWildcardStaticEnv, current, MatchType.DeepWildcard);

        if (fallbackElement != null)
        {
            if (fallbackDepth == 0) 
                return new ResolveResult(fallbackElement, originalPath.Tail, originalPath.Head, fallbackStaticEnv, fallbackNode, MatchType.DeepWildcard);
            
            return new ResolveResult(fallbackElement, SlicePath(originalPath, fallbackDepth), fallbackKey, fallbackStaticEnv, fallbackNode, MatchType.DeepWildcard);
        }

        return new ResolveResult(EmptyElement.Instance, string.Empty, lastMatchedKey, null, current, MatchType.None);
    }

    private static MapPath SlicePath(MapPath path, int segmentsToSkip)
    {
        var result = path;
        for (var i = 0; i < segmentsToSkip; i++)
            result = result.Tail;
            
        return result;
    }
}

public enum MatchType
{
    None,
    Terminal,
    Wildcard,
    DeepWildcard
}

public readonly ref struct ResolveResult
{
    public IElement Element { get; }
    public MapPath RemainingPath { get; }
    public IMap? StaticEnv { get; }
    internal RoutingNode MatchNode { get; }
    public MatchType MatchType { get; }

    internal ResolveResult(IElement element, MapPath remainingPath, string elementKey, IMap? staticEnv, RoutingNode node, MatchType matchType)
    {
        Element = element;
        RemainingPath = remainingPath;
        StaticEnv = staticEnv;
        MatchNode = node;
        MatchType = matchType;
    }
}
