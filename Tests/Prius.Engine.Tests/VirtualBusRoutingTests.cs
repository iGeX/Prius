namespace Prius.Engine.Tests;

using Xunit;
using Core.Maps;
using Abstractions;

public sealed class VirtualBusRoutingTests
{
    [Fact]
    public void FallbackToDeepWildcard_WhenPathFailsMidWay()
    {
        var trie = new RoutingTrie();
        var deepSpy = new SpyElement();
        
        trie.AddRoute("api/users/**", deepSpy);
        
        var bus = new VirtualBus(trie);

        bus.Put("api/users/profile/avatar", "image_data");

        Assert.True(deepSpy.WasExecuted);
        Assert.Equal("profile/avatar", deepSpy.LastRemainingPath);
        Assert.Equal("api/users", ((ISystemElementContext)deepSpy.LastContext!).AbsolutePath);
    }

    [Fact]
    public void AnomalousPaths_DoubleSlashes_ShouldBeTreatedAsEscapedSlash()
    {
        var bus = new VirtualBus(new RoutingTrie());
        
        var path = new MapPath("a//b");
        Assert.Equal(4, path.Length);
        Assert.Equal("a/b", path.Head);
        Assert.True(path.Tail.IsEmpty);
        
        bus.Put("a//b", "value");
        Assert.Equal("value", bus.Get("a//b").AsString());
        
        Assert.True(bus.Get("a").IsEmpty);
    }

    [Fact]
    public void UpdateTrie_ShouldClearCacheAndApplyNewRoutes()
    {
        var trie1 = new RoutingTrie();
        var spy1 = new SpyElement();
        trie1.AddRoute("test", spy1);
        
        var bus = new VirtualBus(trie1);
        bus.Put("test", 1);
        Assert.True(spy1.WasExecuted);
        
        var trie2 = new RoutingTrie();
        var spy2 = new SpyElement();
        trie2.AddRoute("test", spy2);
        
        bus.UpdateTrie(trie2);
        
        spy1.Reset();
        bus.Put("test", 2);
        
        Assert.False(spy1.WasExecuted);
        Assert.True(spy2.WasExecuted);
    }

    [Fact]
    public void DeepWildcard_Fallback_WithPartialMatch()
    {
        var trie = new RoutingTrie();
        var rootDeepSpy = new SpyElement();
        var subDeepSpy = new SpyElement();
        
        trie.AddRoute("**", rootDeepSpy);
        trie.AddRoute("api/**", subDeepSpy);
        
        var bus = new VirtualBus(trie);
        
        bus.Put("api/any/thing", 1);
        Assert.False(rootDeepSpy.WasExecuted);
        Assert.True(subDeepSpy.WasExecuted);
        Assert.Equal("any/thing", subDeepSpy.LastRemainingPath);

        subDeepSpy.Reset();
        
        bus.Put("other/path", 2);
        Assert.True(rootDeepSpy.WasExecuted);
        Assert.Equal("other/path", rootDeepSpy.LastRemainingPath);
    }
}
