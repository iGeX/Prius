namespace Prius.Engine.Tests;

using System;
using System.Threading.Tasks;
using Xunit;
using Core.Maps;
using Abstractions;

public sealed class VirtualBusTests
{
    [Fact]
    public void Root_Get_ShouldReturnFullMemoryMap()
    {
        var bus = new VirtualBus(new RoutingTrie());
        bus.Put("users/1/name", "Alice");
        bus.Put("config/theme", "dark");

        var rootMap = bus.Get("");
        Assert.True(rootMap.IsMap);
        Assert.Equal("Alice", rootMap["users"]["1"]["name"].AsString());
        Assert.Equal("dark", rootMap["config"]["theme"].AsString());
    }

    [Fact]
    public void Root_Put_Map_ShouldMergeWithRootMemory()
    {
        var bus = new VirtualBus(new RoutingTrie());
        bus.Put("existing/data", "old_value");

        var newMap = JsonReaderMap.From("""
        {
            "existing": { "new_prop": "added" },
            "new_section": { "flag": true }
        }
        """);

        bus.Put("", new MapValue(newMap));

        Assert.Equal("old_value", bus.Get("existing/data").AsString());
        Assert.Equal("added", bus.Get("existing/new_prop").AsString());
        Assert.True(bus.Get("new_section/flag").AsBool());
    }

    [Fact]
    public void Root_Put_Scalar_ShouldBeIgnored()
    {
        var bus = new VirtualBus(new RoutingTrie());
        bus.Put("test", 123);
        
        bus.Put("", 456);

        Assert.Equal(123L, bus.Get("test").AsLong());
    }

    [Fact]
    public void ImplicitMemoryWrite_NoReactor_ShouldAutoCreatePath()
    {
        var bus = new VirtualBus(new RoutingTrie());
        var result = bus.Put("deep/nested/path", 123);
        
        Assert.False(result);
        Assert.Equal(123L, bus.Get("deep/nested/path").AsLong());
    }

    [Fact]
    public void RelativeCoordinates_ElementShouldOperateInItsOwnSandbox()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("api/v1/users", spy);
        var bus = new VirtualBus(trie);

        bus.Put("api/v1/users/profile", "data");

        Assert.Equal("profile", spy.LastRemainingPath);
        Assert.Equal("api/v1/users", spy.LastContext!.AbsolutePath);
        
        spy.LastContext.Put("status", "active");
        Assert.Equal("active", bus.Get("api/v1/users/status").AsString());
    }

    [Fact]
    public void RouteResolution_Priority_ExactMatch_Overrides_Wildcards()
    {
        var trie = new RoutingTrie();
        var exactSpy = new SpyElement();
        var wildcardSpy = new SpyElement();
        var deepSpy = new SpyElement();

        trie.AddRoute("api/users", exactSpy);
        trie.AddRoute("api/*", wildcardSpy);
        trie.AddRoute("api/**", deepSpy);

        var bus = new VirtualBus(trie);

        bus.Put("api/users", 1);
        Assert.True(exactSpy.WasExecuted);
        Assert.False(wildcardSpy.WasExecuted);
        Assert.False(deepSpy.WasExecuted);

        exactSpy.Reset();

        bus.Put("api/settings", 2);
        Assert.False(exactSpy.WasExecuted);
        Assert.True(wildcardSpy.WasExecuted);
        Assert.False(deepSpy.WasExecuted);

        wildcardSpy.Reset();

        bus.Put("api/settings/advanced", 3);
        Assert.False(exactSpy.WasExecuted);
        Assert.False(wildcardSpy.WasExecuted);
        Assert.True(deepSpy.WasExecuted);
    }
    
    [Fact]
    public void Environment_Shadowing_DynamicOverStaticOverParent()
    {
        var parentEnv = DictionaryMap.New.With("K1", "Parent1").With("K2", "Parent2").With("K3", "Parent3");
        var staticEnv = DictionaryMap.New.With("K2", "Static2").With("K3", "Static3");
        var dynamicPatch = DictionaryMap.New.With("K3", "Dynamic3");

        var trie = new RoutingTrie();
        var spy = new SpyElement();
        
        trie.AddRoute("root", new CascadeRouterElement("child", dynamicPatch), parentEnv);
        trie.AddRoute("root/child", spy, staticEnv);
        
        var bus = new VirtualBus(trie);

        bus.Put("root", "trigger");

        var ctx = spy.LastContext;
        Assert.NotNull(ctx);
        Assert.Equal("Parent1", ctx["K1"].AsString());
        Assert.Equal("Static2", ctx["K2"].AsString());
        Assert.Equal("Dynamic3", ctx["K3"].AsString());
    }

    [Fact]
    public void Environment_ZeroAllocation_Get_ReturnsEmpty()
    {
        var bus = new VirtualBus(new RoutingTrie());
        var value = bus.Get("some/non/existent/path");
        Assert.True(value.IsEmpty);
    }

    [Fact]
    public void MaxDispatchDepth_ShouldPreventInfiniteRecursion()
    {
        var trie = new RoutingTrie();
        var recursiveElement = new RecursiveElement();
        trie.AddRoute("loop/**", recursiveElement);
        var bus = new VirtualBus(trie);

        var exception = Record.Exception(() => bus.Put("loop/start", 1));
        
        Assert.NotNull(exception);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Maximum dispatch depth exceeded.", exception.Message);
        
        Assert.Equal(128, recursiveElement.CallCount); 
    }

    [Fact]
    public async Task PutAbsolute_ShouldExecuteTopDownInBackground()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("events/occurred", spy);
        var bus = new VirtualBus(trie);

        var eventElement = new AsyncEventElement();
        trie.AddRoute("trigger", eventElement);
        
        bus.Put("trigger", new MapValue());
        
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(spy.WasExecuted);
    }
}

public sealed class SpyElement : IElement
{
    public string LastRemainingPath { get; private set; } = string.Empty;
    public IElementContext? LastContext { get; private set; }
    public bool WasExecuted { get; private set; }

    public void Reset() => WasExecuted = false;

    public bool Put(IElementContext context, MapPath path, MapValue value)
    {
        LastRemainingPath = path.ToString();
        LastContext = context;
        WasExecuted = true;
        return true;
    }

    public MapValue Get(IElementContext context, MapPath path) => context.Get(path);
}

public sealed class RecursiveElement : IElement
{
    public int CallCount;
    public bool Put(IElementContext context, MapPath path, MapValue value)
    {
        CallCount++;
        return context.Put("next", value);
    }

    public MapValue Get(IElementContext context, MapPath path) => new();
}

public sealed class AsyncEventElement : IElement
{
    public bool Put(IElementContext context, MapPath path, MapValue value)
    {
        context.PutAbsolute("events/occurred", new MapValue());
        return true;
    }

    public MapValue Get(IElementContext context, MapPath path) => new();
}

public sealed class CascadeRouterElement(string deeperPath, IMap? patch) : IElement
{
    public bool Put(IElementContext context, MapPath path, MapValue value) => context.Put(deeperPath, value, patch);

    public MapValue Get(IElementContext context, MapPath path) => new();
}
