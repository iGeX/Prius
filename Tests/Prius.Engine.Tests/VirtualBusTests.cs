namespace Prius.Engine.Tests;

using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Core.Maps;
using Abstractions;

public sealed class VirtualBusTests
{
    // --- 1. Работа с корнем и памятью (Root & Memory Fallthrough) ---

    [Fact]
    public void Root_Get_ShouldReturnFullMemoryMap()
    {
        var bus = VirtualBusFactory.Create(new RoutingTrie());
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
        var bus = VirtualBusFactory.Create(new RoutingTrie());
        bus.Put("existing/data", "old_value");

        var newMap = JsonReaderMap.From("""
        {
            "existing": { "new_prop": "added" },
            "new_section": { "flag": true }
        }
        """);

        bus.Put("", new MapValue(newMap));

        // It should merge, preserving old_value
        Assert.Equal("old_value", bus.Get("existing/data").AsString());
        Assert.Equal("added", bus.Get("existing/new_prop").AsString());
        Assert.True(bus.Get("new_section/flag").AsBool());
    }

    [Fact]
    public void Root_Put_Scalar_ShouldBeIgnored()
    {
        var bus = VirtualBusFactory.Create(new RoutingTrie());
        bus.Put("test", 123);
        
        bus.Put("", 456); // Trying to overwrite root with scalar

        Assert.Equal(123L, bus.Get("test").AsLong()); // Should not have crashed, and data should be intact
    }

    [Fact]
    public void ImplicitMemoryWrite_NoReactor_ShouldAutoCreatePath()
    {
        var bus = VirtualBusFactory.Create(new RoutingTrie());
        var result = bus.Put("deep/nested/path", 123);
        
        Assert.False(result); // Returns false indicating it fell through to memory
        Assert.Equal(123L, bus.Get("deep/nested/path").AsLong());
    }

    [Fact]
    public void MemoryFallthrough_ByWildcard_ShouldWriteToMemory()
    {
        var trie = new RoutingTrie();
        var gate = new GateElement();
        trie.AddRoute("gate/**", gate);
        var bus = VirtualBusFactory.Create(trie);

        // Put an empty path inside the gate to trigger internal state write
        bus.Put("gate", 1); 
        
        // Ensure the value fell through to memory at gate/@Active
        Assert.Equal(1L, bus.Get("gate/@Active").AsLong());
    }

    // --- 2. Относительность систем координат (Sandboxing) ---

    [Fact]
    public void RelativeCoordinates_ElementShouldOperateInItsOwnSandbox()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("api/v1/users", spy);
        var bus = VirtualBusFactory.Create(trie);

        // Write from outside
        bus.Put("api/v1/users/profile", "data");

        // Spy caught it. Check the relative path it saw
        Assert.Equal("profile", spy.LastRemainingPath.ToString());
        Assert.Equal("api/v1/users", spy.LastContext!.AbsolutePath);

        // If the element tries to write relatively via its context, it goes to the correct absolute path
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

        var bus = VirtualBusFactory.Create(trie);

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

    // --- 3. Окружение и Приоритеты (Env Merging) ---

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
        
        var bus = VirtualBusFactory.Create(trie);

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
        var bus = VirtualBusFactory.Create(new RoutingTrie());
        var value = bus.Get("some/non/existent/path", null);
        Assert.True(value.IsEmpty);
    }

    // --- 4. Защита от дедлоков и рекурсий (Limits & Locks) ---

    [Fact]
    public void MaxDispatchDepth_ShouldPreventInfiniteRecursion()
    {
        var trie = new RoutingTrie();
        var recursiveElement = new RecursiveElement();
        trie.AddRoute("loop/**", recursiveElement);
        var bus = VirtualBusFactory.Create(trie);

        var exception = Record.Exception(() => bus.Put("loop/start", 1));
        
        Assert.NotNull(exception);
        Assert.IsType<InvalidOperationException>(exception);
        Assert.Equal("Maximum dispatch depth exceeded.", exception.Message);
        
        // Because of the root call, it will hit MaxDispatchDepth at exactly MaxDispatchDepth iterations.
        Assert.Equal(128, recursiveElement.CallCount); 
    }

    // --- 5. Асинхронные выбросы (PutAbsolute) ---

    [Fact]
    public async Task PutAbsolute_ShouldExecuteTopDownInBackground()
    {
        var trie = new RoutingTrie();
        var spy = new SpyElement();
        trie.AddRoute("events/occurred", spy);
        var bus = VirtualBusFactory.Create(trie);

        var eventElement = new AsyncEventElement();
        trie.AddRoute("trigger", eventElement);
        
        bus.Put("trigger", new MapValue());
        
        // Wait briefly for the ThreadPool execution
        await Task.Delay(50);

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

    public MapValue Get(IElementContext context, MapPath path) => new();
}

public sealed class RecursiveElement : IElement
{
    public int CallCount;
    public bool Put(IElementContext context, MapPath path, MapValue value)
    {
        CallCount++;
        return context.Put("next", value); // Calls deeper into itself infinitely
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
    public bool Put(IElementContext context, MapPath path, MapValue value) =>
        context.Put(deeperPath, value, patch);

    public MapValue Get(IElementContext context, MapPath path) => new();
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

    public MapValue Get(IElementContext context, MapPath path) => new();
}
