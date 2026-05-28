namespace Prius.Engine.Tests;

using Xunit;
using Core.Maps;

public sealed class VirtualBusMemoryTests
{
    [Fact]
    public void OverwritingScalarWithMap_ShouldSucceed()
    {
        var bus = new VirtualBus(new RoutingTrie());
        
        bus.Put("config/theme", "dark");
        Assert.Equal("dark", bus.Get("config/theme").AsString());
        
        bus.Put("config/theme/color", "#000");

        var theme = bus.Get("config/theme");
        Assert.True(theme.IsMap);
        Assert.Equal("#000", bus.Get("config/theme/color").AsString());
        
        Assert.True(theme.AsMap().ContainsKey("color"));
    }

    [Fact]
    public void DeepPathCreation_ShouldMaintainStructuralIntegrity()
    {
        var bus = new VirtualBus(new RoutingTrie());
        
        bus.Put("a/b/c/d", 123);

        Assert.Equal(123L, bus.Get("a/b/c/d").AsLong());
        Assert.True(bus.Get("a").IsMap);
        Assert.True(bus.Get("a/b").IsMap);
        Assert.True(bus.Get("a/b/c").IsMap);
    }

    [Fact]
    public void ParentNode_ShouldBeCorrect_InElementContext()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("root/sub", spy);
        var bus = new VirtualBus(trie);

        bus.Put("root/sub/data", 1);

        var ctx = (IBusContext)spy.LastContext!;
        Assert.NotNull(ctx.ParentNode);
        
        Assert.True(bus.Get("root").AsMap().DeepEquals(ctx.ParentNode));
    }
}
