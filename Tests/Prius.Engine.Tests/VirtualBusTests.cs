namespace Prius.Engine.Tests;

using System.Threading;
using Xunit;
using Core.Maps;
using Abstractions;

public sealed class VirtualBusTests
{
    [Fact]
    public void Put_ShouldResolveElementWithRemainingPathAndKey()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("orders/**", spy);
        var bus = new VirtualBus(trie);

        bus.Put("orders/123/items", new MapValue());

        Assert.Equal("123/items", spy.LastRemainingPath);
        Assert.Equal("orders", spy.LastContextKey);
        Assert.Equal("orders", spy.LastContext?.AbsolutePath);
    }

    [Fact]
    public void Context_ShouldCascadinglyProbeHierarchicalEnvAndShadowValues()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("orders/123/items/1", spy);

        var rootEnv = JsonReaderMap.From("""
            {
                "currency": "USD",
                "theme": "dark"
            }
            """);

        var level1Patch = JsonReaderMap.From("""
            {
                "currency": "EUR",
                "discount": "10%"
            }
            """);

        var intermediateElement = new CascadeRouterElement("items/1", level1Patch);
        var trieWithCascade = new RoutingTrie();
        trieWithCascade.AddRoute("orders/123", intermediateElement);
        trieWithCascade.AddRoute("orders/123/items/1", spy);

        var cascadeBus = new VirtualBus(trieWithCascade);
        cascadeBus.Put("orders/123", new MapValue(), rootEnv);

        var context = spy.LastContext;
        Assert.NotNull(context);

        Assert.Equal("EUR", context["currency"].AsValue<string>());
        Assert.Equal("dark", context["theme"].AsValue<string>());
        Assert.Equal("10%", context["discount"].AsValue<string>());
        Assert.True(context["non_existent"].IsEmpty);
    }

    [Fact]
    public void Context_ShouldSilentlyIgnoreWriteAttempts()
    {
        var trie = new RoutingTrie();
        var spy = new MaliciousElement();
        trie.AddRoute("secure/node", spy);
        var bus = new VirtualBus(trie);

        bus.Put("secure/node", new MapValue());

        Assert.False(spy.WasWriteSuccessful);
    }

    [Fact]
    public void Put_ShouldReturnFalseWhenHittingBackingMap()
    {
        var trie = new RoutingTrie();
        var bus = new VirtualBus(trie);

        var result = bus.Put("data/value", "test_value".AsMapValue());

        Assert.False(result); // Hit backing map
        Assert.Equal("test_value", bus.Get("data/value").AsString());
    }
    
    [Fact]
    public void Put_ShouldReturnTrueWhenInterceptedByElement()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("data/**", spy);
        var bus = new VirtualBus(trie);

        var result = bus.Put("data/value", "test_value".AsMapValue());

        Assert.True(result); // Hit element
    }

    [Fact]
    public void PutAbsolute_ShouldExecuteTopDownInBackground()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("events/occurred", spy);
        var bus = new VirtualBus(trie);

        var eventElement = new AsyncEventElement();
        trie.AddRoute("trigger", eventElement);
        
        bus.UpdateTrie(trie);

        bus.Put("trigger", new MapValue());
        
        Thread.Sleep(500);

        Assert.True(spy.WasExecuted);
        Assert.Equal("occurred", spy.LastContextKey);
    }
}

public sealed class SpyElement : IElement
{
    public string LastRemainingPath { get; private set; } = string.Empty;
    public string LastContextKey { get; private set; } = string.Empty;
    public IElementContext? LastContext { get; private set; }
    public bool WasExecuted { get; private set; }

    public bool Put(IElementContext context, MapPath path, MapValue value)
    {
        LastRemainingPath = path;
        LastContextKey = context.Key;
        LastContext = context;
        WasExecuted = true;
        return true;
    }

    public MapValue Get(IElementContext context, MapPath path) =>
        new();
}

public sealed class AsyncEventElement : IElement
{
    public bool Put(IElementContext context, MapPath path, MapValue value)
    {
        context.PutAbsolute("events/occurred", new MapValue());
        return true;
    }

    public MapValue Get(IElementContext context, MapPath path) =>
        new();
}

public sealed class CascadeRouterElement(string deeperPath, IMap patch) : IElement
{
    public bool Put(IElementContext context, MapPath path, MapValue value) =>
        context.Put(deeperPath, value, patch);

    public MapValue Get(IElementContext context, MapPath path) =>
        new();
}

public sealed class MaliciousElement : IElement
{
    public bool WasWriteSuccessful { get; private set; }

    public bool Put(IElementContext context, MapPath path, MapValue value)
    {
        context["secret_key"] = "hacked".AsMapValue();

        if (context.ContainsKey("secret_key"))
            WasWriteSuccessful = true;
            
        return true;
    }

    public MapValue Get(IElementContext context, MapPath path) =>
        new();
}
