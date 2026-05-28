namespace Prius.Engine.Tests;

using Xunit;
using Core.Maps;
using Abstractions;

public sealed class VirtualBusGetTests
{
    [Fact]
    public void Get_ShouldBeInterceptedByElement()
    {
        var trie = new RoutingTrie();
        var dynamicElement = new DynamicConfigElement();
        trie.AddRoute("config/*", dynamicElement);
        var bus = new VirtualBus(trie);

        // Put some data in memory to see if it's IGNORED when element intercepts
        bus.Put("config/theme", "memory_dark");

        var theme = bus.Get("config/theme");
        Assert.Equal("dynamic_dark", theme.AsString());
    }

    [Fact]
    public void Get_ShouldFallbackToMemory_WhenElementReturnsEmpty()
    {
        // 1. Start with empty trie and populate memory
        var bus = new VirtualBus(new RoutingTrie());
        bus.Put("data/exists", "memory_value");
        bus.Put("data/missing", "memory_value");

        // 2. Update trie with element that intercepts ONLY via Get
        var trie = new RoutingTrie();
        var partialElement = new PartialInterceptElement();
        trie.AddRoute("**", partialElement); 
        bus.UpdateTrie(trie);

        // Element intercepts "data/exists" but returns Empty for others
        Assert.Equal("intercepted", bus.Get("data/exists").AsString());
        Assert.Equal("memory_value", bus.Get("data/missing").AsString());
    }

    private sealed class DynamicConfigElement : IElement
    {
        public bool Put(IElementContext context, MapPath path, MapValue value) => true;

        public MapValue Get(IElementContext context, MapPath path)
        {
            if (context.CallerSegment == "theme") return "dynamic_dark";
            return new MapValue();
        }
    }

    private sealed class PartialInterceptElement : IElement
    {
        public bool Put(IElementContext context, MapPath path, MapValue value) => true;

        public MapValue Get(IElementContext context, MapPath path)
        {
            if (path == "data/exists") return "intercepted";
            return new MapValue(); 
        }
    }
}
