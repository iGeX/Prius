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
        
        bus.Put("config/theme", "memory_dark");

        var theme = bus.Get("config/theme");
        Assert.Equal("dynamic_dark", theme.AsString());
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
}
