using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Xunit;

namespace Prius.Engine.Tests;

public sealed class RoutingTrieTests
{
    private sealed class SpyElement(string id) : IElement
    {
        public string Id { get; } = id;
        public bool Put(IElementContext context, MapPath path, MapValue value) => true;
        public MapValue Get(IElementContext context, MapPath path) => new();
    }

    [Fact]
    public void Resolve_PrefersExactMatchOverWildcard()
    {
        var trie = new RoutingTrie();
        trie.AddRoute("/a/b", new SpyElement("exact"));
        trie.AddRoute("/a/*", new SpyElement("wildcard"));

        var result = trie.Resolve("/a/b");
        Assert.Equal("exact", ((SpyElement)result.Element).Id);
    }

    [Fact]
    public void Resolve_PrefersWildcardOverDeepWildcard()
    {
        var trie = new RoutingTrie();
        trie.AddRoute("/a/*", new SpyElement("wildcard"));
        trie.AddRoute("/a/**", new SpyElement("deep"));

        var result = trie.Resolve("/a/any");
        Assert.Equal("wildcard", ((SpyElement)result.Element).Id);

        var resultDeep = trie.Resolve("/a/any/more");
        Assert.Equal("deep", ((SpyElement)resultDeep.Element).Id);
    }

    [Fact]
    public void Resolve_DeepWildcard_MatchesMultipleLevels()
    {
        var trie = new RoutingTrie();
        trie.AddRoute("/api/**", new SpyElement("api"));

        var result = trie.Resolve("/api/v1/users/get");
        Assert.Equal("api", ((SpyElement)result.Element).Id);
        Assert.Equal("v1/users/get", result.RemainingPath.ToString());
    }

    [Fact]
    public void Resolve_MidPathWildcard_Works()
    {
        var trie = new RoutingTrie();
        trie.AddRoute("/a/*/c", new SpyElement("mid"));

        var result = trie.Resolve("/a/any/c");
        Assert.Equal("mid", ((SpyElement)result.Element).Id);
        
        var resultNone = trie.Resolve("/a/any/d");
        Assert.Same(EmptyElement.Instance, resultNone.Element);
    }

    [Fact]
    public void Resolve_MostSpecificDeepWildcardWins()
    {
        var trie = new RoutingTrie();
        trie.AddRoute("/**", new SpyElement("root"));
        trie.AddRoute("/a/**", new SpyElement("a"));

        var result = trie.Resolve("/a/b/c");
        Assert.Equal("a", ((SpyElement)result.Element).Id);
        
        var resultRoot = trie.Resolve("/b/c");
        Assert.Equal("root", ((SpyElement)resultRoot.Element).Id);
    }

    [Fact]
    public void Resolve_EmptyPath_ReturnsTerminalOrEmpty()
    {
        var trie = new RoutingTrie();
        trie.AddRoute("/", new SpyElement("home"));

        var result = trie.Resolve("/");
        Assert.Equal("home", ((SpyElement)result.Element).Id);
        
        var resultNotFound = trie.Resolve("/other");
        Assert.Same(EmptyElement.Instance, resultNotFound.Element);
    }
}
