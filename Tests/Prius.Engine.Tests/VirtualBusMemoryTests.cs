namespace Prius.Engine.Tests;

using Xunit;
using Core.Maps;
using Abstractions;

public sealed class VirtualBusMemoryTests
{
    [Fact]
    public void OverwritingScalarWithMap_ShouldSucceed()
    {
        var bus = new VirtualBus(new RoutingTrie());
        
        // 1. Put scalar
        bus.Put("config/theme", "dark");
        Assert.Equal("dark", bus.Get("config/theme").AsString());

        // 2. Put something DEEPER under the scalar
        bus.Put("config/theme/color", "#000");

        // 3. Verify 'theme' is now a map, and 'color' is inside
        var theme = bus.Get("config/theme");
        Assert.True(theme.IsMap);
        Assert.Equal("#000", bus.Get("config/theme/color").AsString());
        
        // Original scalar value is lost (expected in Prius when node becomes a map)
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
        
        // Parent node of 'root/sub' should be the 'root' map
        // Use DeepEquals because DictionaryMap wraps IDictionary on every Get
        Assert.True(bus.Get("root").AsMap().DeepEquals(ctx.ParentNode));
    }
}
