namespace Prius.Engine;

using Core.Maps;
using Abstractions;

public sealed class RoutingTrie
{
    private readonly RoutingNode _root = new();

    public void AddRoute(string pathPattern, IElement element, IMap? staticEnv = null)
    {
        if (string.IsNullOrWhiteSpace(pathPattern)) 
            throw new ArgumentNullException(nameof(pathPattern));
        ArgumentNullException.ThrowIfNull(element);

        MapPath path = pathPattern;
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

    public ResolveResult Resolve(MapPath absolutePath)
    {
        var current = _root;
        IElement? fallbackElement = null;
        IMap? fallbackStaticEnv = null;
        
        var originalPath = absolutePath; 
        var currentDepth = 0;
        var fallbackDepth = 0;
        var lastMatchedKey = string.Empty;
        var fallbackKey = string.Empty;

        while (!absolutePath.IsEmpty)
        {
            if (current.DeepWildcardElement != null)
            {
                fallbackElement = current.DeepWildcardElement;
                fallbackStaticEnv = current.DeepWildcardStaticEnv;
                fallbackDepth = currentDepth;
                fallbackKey = lastMatchedKey;
            }

            var segment = absolutePath.Head;
            lastMatchedKey = segment;
            absolutePath = absolutePath.Tail;
            currentDepth++;

            if (current.Children.TryGetValue(segment, out var nextNode))
            {
                current = nextNode;
                continue;
            }

            if (current.Children.TryGetValue("@wildcard", out var wildcardNode))
            {
                current = wildcardNode;
                continue;
            }

            if (absolutePath.IsEmpty && current.WildcardElement != null)
                return new ResolveResult(current.WildcardElement, string.Empty, segment, current.WildcardStaticEnv);

            if (fallbackElement != null)
                return new ResolveResult(fallbackElement, SlicePath(originalPath, fallbackDepth), fallbackKey, fallbackStaticEnv);

            return new ResolveResult(EmptyElement.Instance, string.Empty, segment, null); 
        }

        if (current.TerminalElement != null)
            return new ResolveResult(current.TerminalElement, string.Empty, lastMatchedKey, current.TerminalStaticEnv);

        return fallbackElement != null 
            ? new ResolveResult(fallbackElement, SlicePath(originalPath, fallbackDepth), fallbackKey, fallbackStaticEnv) 
            : new ResolveResult(EmptyElement.Instance, string.Empty, lastMatchedKey, null);
    }

    private static MapPath SlicePath(MapPath path, int segmentsToSkip)
    {
        var result = path;
        for (var i = 0; i < segmentsToSkip; i++)
            result = result.Tail;
            
        return result;
    }
}

public readonly ref struct ResolveResult(IElement element, MapPath remainingPath, string elementKey, IMap? staticEnv)
{
    public IElement Element { get; } = element;
    
    public MapPath RemainingPath { get; } = remainingPath;
    
    public string ElementKey { get; } = elementKey;

    public IMap? StaticEnv { get; } = staticEnv;
}
