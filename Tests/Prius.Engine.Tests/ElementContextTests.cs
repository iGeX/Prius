namespace Prius.Engine.Tests;

using System.Linq;
using Xunit;
using Core.Maps;

public sealed class ElementContextTests
{
    [Fact]
    public void Keys_ShouldMergeAllLayers()
    {
        var parentEnv = DictionaryMap.New.With("K1", "P1").With("K2", "P2");
        var staticEnv = DictionaryMap.New.With("K2", "S2").With("K3", "S3");
        var envPatch = DictionaryMap.New.With("K3", "D3").With("K4", "D4");

        var trie = new RoutingTrie();
        var spy = new SpyElement();
        
        trie.AddRoute("root", new CascadeRouterElement("child", envPatch), parentEnv);
        trie.AddRoute("root/child", spy, staticEnv);
        
        var bus = new VirtualBus(trie);
        bus.Put("root", "trigger");

        var ctx = spy.LastContext;
        Assert.NotNull(ctx);
        
        var keys = ctx.Keys().ToList();
        
        Assert.Contains("K1", keys);
        Assert.Contains("K2", keys);
        Assert.Contains("K3", keys);
        Assert.Contains("K4", keys);
        Assert.Equal(4, keys.Count);

        Assert.Equal("P1", ctx["K1"].AsString());
        Assert.Equal("S2", ctx["K2"].AsString());
        Assert.Equal("D3", ctx["K3"].AsString());
        Assert.Equal("D4", ctx["K4"].AsString());
    }

    [Fact]
    public void ContainsKey_ShouldSearchHierarchy()
    {
        var parentEnv = DictionaryMap.New.With("ParentKey", 1);
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("a", new CascadeRouterElement("b", null), parentEnv);
        trie.AddRoute("a/b", spy);
        
        var bus = new VirtualBus(trie);
        bus.Put("a", 1);

        var ctx = spy.LastContext;
        Assert.NotNull(ctx);
        Assert.True(ctx.ContainsKey("ParentKey"));
        Assert.False(ctx.ContainsKey("NonExistent"));
    }

    [Fact]
    public void IsEmpty_ShouldCheckHierarchy()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("a", spy);
        var bus = new VirtualBus(trie);

        bus.Put("a", 1);
        Assert.True(spy.LastContext!.IsEmpty);

        var trie2 = new RoutingTrie();
        trie2.AddRoute("a", spy, DictionaryMap.New.With("K", 1));
        bus.UpdateTrie(trie2);
        bus.Put("a", 1);
        Assert.False(spy.LastContext!.IsEmpty);
    }
}
