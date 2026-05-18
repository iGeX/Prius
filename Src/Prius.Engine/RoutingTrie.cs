namespace Prius.Engine;

using Core.Maps;
using Abstractions;

public sealed class RoutingTrie
{
    private readonly RoutingNode _root = new();

    public void AddRoute(string pathPattern, IReactor reactor)
    {
        if (string.IsNullOrWhiteSpace(pathPattern)) throw new ArgumentNullException(nameof(pathPattern));
        if (reactor == null) throw new ArgumentNullException(nameof(reactor));

        MapPath path = pathPattern;
        var current = _root;

        while (!path.IsEmpty)
        {
            var segment = path.Head;
            path = path.Tail;

            if (segment == "**")
            {
                current.DeepWildcardReactor = reactor;
                return;
            }
            
            if (segment == "*")
            {
                if (path.IsEmpty)
                {
                    current.WildcardReactor = reactor;
                    return;
                }
                segment = "@wildcard"; 
            }

            if (!current.Children.TryGetValue(segment, out var nextNode))
            {
                nextNode = new RoutingNode();
                current.Children.Add(segment, nextNode);
            }
            current = nextNode;
        }

        current.TerminalReactor = reactor;
    }

    public ResolveResult Resolve(MapPath absolutePath)
    {
        var current = _root;
        IReactor? fallbackReactor = null;
        
        var originalPath = absolutePath; 
        var currentDepth = 0;
        var fallbackDepth = 0;
        var lastMatchedKey = string.Empty;
        var fallbackKey = string.Empty;

        while (!absolutePath.IsEmpty)
        {
            if (current.DeepWildcardReactor != null)
            {
                fallbackReactor = current.DeepWildcardReactor;
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

            if (fallbackReactor != null)
                return new ResolveResult(fallbackReactor, SlicePath(originalPath, fallbackDepth), fallbackKey);

            if (absolutePath.IsEmpty && current.WildcardReactor != null)
                return new ResolveResult(current.WildcardReactor, string.Empty, segment);

            return new ResolveResult(EmptyReactor.Instance, string.Empty, segment); 
        }

        if (current.TerminalReactor != null)
            return new ResolveResult(current.TerminalReactor, string.Empty, lastMatchedKey);

        if (fallbackReactor != null)
            return new ResolveResult(fallbackReactor, SlicePath(originalPath, fallbackDepth), fallbackKey);

        return new ResolveResult(EmptyReactor.Instance, string.Empty, lastMatchedKey);
    }

    private static MapPath SlicePath(MapPath path, int segmentsToSkip)
    {
        var result = path;
        for (var i = 0; i < segmentsToSkip; i++)
            result = result.Tail;
            
        return result;
    }
}

public readonly ref struct ResolveResult(IReactor reactor, MapPath remainingPath, string reactorKey)
{
    public IReactor Reactor { get; } = reactor;
    public MapPath RemainingPath { get; } = remainingPath;
    public string ReactorKey { get; } = reactorKey;
}
