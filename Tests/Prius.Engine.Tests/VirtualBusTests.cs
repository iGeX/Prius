namespace Prius.Engine.Tests;

using Xunit;
using Core.Maps;
using Abstractions;

public sealed class VirtualBusTests
{
    [Fact]
    public void Put_ShouldResolveReactorWithRemainingPathAndKey()
    {
        var trie = new RoutingTrie();
        var spy = new SpyReactor();
        trie.AddRoute("orders/**", spy);
        var bus = new VirtualBus(trie);

        bus.Put("orders/123/items", new MapValue());

        Assert.Equal("123/items", spy.LastRemainingPath);
        Assert.Equal("orders", spy.LastContextKey);
    }

    [Fact]
    public void Notify_ShouldExecuteOnNextTickAfterPutFinishes()
    {
        var trie = new RoutingTrie();
        var parentSpy = new SpyReactor();
        var childSpy = new NotificationTriggerReactor("items/price");
        
        trie.AddRoute("orders", parentSpy);
        trie.AddRoute("orders/123", childSpy);
        var bus = new VirtualBus(trie);

        bus.Put("orders/123", new MapValue());

        Assert.True(childSpy.WasExecuted);
        Assert.True(parentSpy.WasNotifyExecuted);
        Assert.Equal("123/items/price", parentSpy.LastNotifyPath);
    }

    [Fact]
    public void Context_ShouldCascadinglyProbeHierarchicalEnvAndShadowValues()
    {
        var trie = new RoutingTrie();
        var spy = new SpyReactor();
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

        var intermediateReactor = new CascadeRouterReactor("items/1", level1Patch);
        var trieWithCascade = new RoutingTrie();
        trieWithCascade.AddRoute("orders/123", intermediateReactor);
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
        var spy = new MaliciousReactor();
        trie.AddRoute("secure/node", spy);
        var bus = new VirtualBus(trie);

        bus.Put("secure/node", new MapValue());

        Assert.False(spy.WasWriteSuccessful);
    }
    
    [Fact]
    public void Keys_ShouldReturnDeDuplicatedUnionFromAllParentLevels()
    {
        var trie = new RoutingTrie();
        var finalSpy = new SpyReactor();
        
        var level1Patch = JsonReaderMap.From("""{"currency": "EUR", "discount": "10%"}""");
        var level2Patch = JsonReaderMap.From("""{"tax": "20%"}""");

        var intermediate1 = new CascadeRouterReactor(deeperPath: "items", level1Patch);
        var intermediate2 = new CascadeRouterReactor(deeperPath: "1", level2Patch);
        
        trie.AddRoute("orders/123", intermediate1);
        trie.AddRoute("orders/123/items", intermediate2);
        trie.AddRoute("orders/123/items/1", finalSpy);

        var bus = new VirtualBus(trie);
        var rootEnv = JsonReaderMap.From("""{"currency": "USD", "theme": "dark"}""");
        
        bus.Put("orders/123", new MapValue(), rootEnv);

        var context = finalSpy.LastContext;
        Assert.NotNull(context);

        var allKeys = context.Keys().ToList();

        Assert.Equal(4, allKeys.Count);
        Assert.Contains("currency", allKeys);
        Assert.Contains("theme", allKeys);
        Assert.Contains("discount", allKeys);
        Assert.Contains("tax", allKeys);
    }
    
    [Fact]
    public void Put_ShouldReturnImmediatelyIfPathIsEmpty()
    {
        var trie = new RoutingTrie();
        var spy = new SpyReactor();
        trie.AddRoute("orders/**", spy);
        var bus = new VirtualBus(trie);

        bus.Put(string.Empty, new MapValue());

        Assert.Equal(string.Empty, spy.LastRemainingPath);
        Assert.True(string.IsNullOrEmpty(spy.LastContextKey));
    }

    [Fact]
    public void Routing_ShouldPreferExactMatchOverDeepWildcard()
    {
        var trie = new RoutingTrie();
        var exactSpy = new SpyReactor();
        var deepSpy = new SpyReactor();
        
        trie.AddRoute("orders/123/settings", exactSpy);
        trie.AddRoute("orders/**", deepSpy);
        var bus = new VirtualBus(trie);

        bus.Put("orders/123/settings", new MapValue());

        Assert.Equal(string.Empty, exactSpy.LastRemainingPath);
        Assert.Equal("settings", exactSpy.LastContextKey);
        Assert.Equal(string.Empty, deepSpy.LastRemainingPath);
    }
    
    [Fact]
    public void Routing_ShouldCorrectlyHandleEscapedSeparatorsInPaths()
    {
        var trie = new RoutingTrie();
        var spy = new SpyReactor();
        
        trie.AddRoute("finance/invoices//v1/lines/**", spy);
        var bus = new VirtualBus(trie);

        bus.Put("finance/invoices//v1/lines/5", new MapValue());

        Assert.Equal("5", spy.LastRemainingPath);
        Assert.Equal("lines", spy.LastContextKey); 
    }

    [Fact]
    public void Cache_ShouldInvalidateOrBypassWhenRouteIsDynamicallyAdded()
    {
        var trie = new RoutingTrie();
        var deepSpy = new SpyReactor();
        var exactSpy = new SpyReactor();
        
        trie.AddRoute("orders/**", deepSpy);
        var bus = new VirtualBus(trie);

        bus.Put("orders/123/settings", new MapValue());
        Assert.Equal("123/settings", deepSpy.LastRemainingPath);

        trie.AddRoute("orders/123/settings", exactSpy);
        
        bus.ClearCache();

        bus.Put("orders/123/settings", new MapValue());
        
        Assert.Equal(string.Empty, exactSpy.LastRemainingPath);
        Assert.Equal("settings", exactSpy.LastContextKey);
    }

    [Fact]
    public void Semaphores_ShouldEnsureStrictLinearExecutionAndPreventDeadlocks()
    {
        var trie = new RoutingTrie();
        var counterReactor = new ConcurrentCounterReactor();
        
        trie.AddRoute("orders/123", counterReactor);
        trie.AddRoute("orders/123/items", counterReactor);
        
        var bus = new VirtualBus(trie);
        const int Iterations = 100;

        Parallel.For(0, Iterations, _ => 
        {
            bus.Put("orders/123/items", new MapValue());
        });

        Assert.Equal(1, counterReactor.MaxObservedThreads);
        Assert.Equal(Iterations, counterReactor.NotifyCount);
    }
    
    [Fact]
    public void Notify_ShouldHandleMultipleSequentialNotificationsInCorrectOrder()
    {
        var trie = new RoutingTrie();
        var parentSpy = new SpyReactor();
        
        var child = new MultiNotificationReactor("path1", "path2");
        
        trie.AddRoute("orders", parentSpy);
        trie.AddRoute("orders/123", child);
        var bus = new VirtualBus(trie);

        bus.Put("orders/123", new MapValue());

        Assert.Equal(2, parentSpy.NotifyCount);
        Assert.Equal("123/path2", parentSpy.LastNotifyPath);
    }

    [Fact]
    public void Routing_ShouldPreserveEscapedTrailingDataInRemainingPath()
    {
        var trie = new RoutingTrie();
        var spy = new SpyReactor();
        
        trie.AddRoute("orders/123/url/**", spy);
        var bus = new VirtualBus(trie);

        bus.Put("orders/123/url/httpsExternal//://google.com", new MapValue());

        Assert.Equal("httpsExternal//://google.com", spy.LastRemainingPath);
        Assert.Equal("url", spy.LastContextKey);
    }

    [Fact]
    public async Task AsyncCallback_ShouldBeFactToletantIfReactorGraphChangesOnTheFly()
    {
        var trie = new RoutingTrie();
        var initialReactor = new SpyReactor();
        var hotReloadReactor = new SpyReactor();
        var callbackReactor = new CallbackForwarderReactor();
        
        trie.AddRoute("orders/123", initialReactor);
        trie.AddRoute("orders/123/success/ok", callbackReactor);
        var bus = new VirtualBus(trie);

        bus.Put("orders/123", new MapValue());
        var savedContext = initialReactor.LastContext;
        Assert.NotNull(savedContext);

        var broker = new AsyncCallbackStub((ReactorContext)savedContext, "success/ok");
        var brokerTask = broker.ExecuteAsync();

        trie.AddRoute("orders/123", hotReloadReactor);
        bus.ClearCache();

        await brokerTask;

        Assert.True(hotReloadReactor.WasNotifyExecuted);
        Assert.Equal("success/ok/changed", hotReloadReactor.LastNotifyPath);
        Assert.Equal("data_payload", hotReloadReactor.LastNotifyValue.AsValue<string>());
    }
}

public sealed class SpyReactor : IReactor
{
    public string LastRemainingPath { get; private set; } = string.Empty;
    public string LastContextKey { get; private set; } = string.Empty;
    public bool WasNotifyExecuted { get; private set; }
    public string LastNotifyPath { get; private set; } = string.Empty;
    public MapValue LastNotifyValue { get; private set; } = new();
    public int NotifyCount { get; private set; }
    public IReactorContext? LastContext { get; private set; }

    public void Put(IReactorContext context, MapPath path, MapValue value)
    {
        LastRemainingPath = path;
        LastContextKey = context.Key;
        LastContext = context;
    }

    public MapValue Get(IReactorContext context, MapPath path) => 
        new();

    public void Notify(IReactorContext context, MapPath path, MapValue value)
    {
        WasNotifyExecuted = true;
        LastNotifyPath = path;
        LastNotifyValue = value;
        NotifyCount++;
    }
}

public sealed class NotificationTriggerReactor(string pathToNotify) : IReactor
{
    public bool WasExecuted { get; private set; }

    public void Put(IReactorContext context, MapPath path, MapValue value)
    {
        WasExecuted = true;
        context.Notify(pathToNotify, new MapValue());
    }

    public MapValue Get(IReactorContext context, MapPath path) => 
        new();

    public void Notify(IReactorContext context, MapPath path, MapValue value)
    {
    }
}

public sealed class CascadeRouterReactor(string deeperPath, IMap patch) : IReactor
{
    public void Put(IReactorContext context, MapPath path, MapValue value) => 
        context.Put(deeperPath, value, patch);

    public MapValue Get(IReactorContext context, MapPath path) => 
        new();

    public void Notify(IReactorContext context, MapPath path, MapValue value)
    {
    }
}

public sealed class MaliciousReactor : IReactor
{
    public bool WasWriteSuccessful { get; private set; }

    public void Put(IReactorContext context, MapPath path, MapValue value)
    {
        context["secret_key"] = "hacked".AsMapValue();
        
        if (context.ContainsKey("secret_key"))
            WasWriteSuccessful = true;
    }

    public MapValue Get(IReactorContext context, MapPath path) => 
        new();

    public void Notify(IReactorContext context, MapPath path, MapValue value)
    {
    }
}

public sealed class MultiNotificationReactor(string path1, string path2) : IReactor
{
    public void Put(IReactorContext context, MapPath path, MapValue value)
    {
        context.Notify(path1, new MapValue());
        context.Notify(path2, new MapValue());
    }

    public MapValue Get(IReactorContext context, MapPath path) => 
        new();

    public void Notify(IReactorContext context, MapPath path, MapValue value)
    {
    }
}

public sealed class ConcurrentCounterReactor : IReactor
{
    private int _activeThreads;
    private int _maxObservedThreads;
    private int _notifyCount;

    public int MaxObservedThreads => _maxObservedThreads;
    public int NotifyCount => _notifyCount;

    public void Put(IReactorContext context, MapPath path, MapValue value)
    {
        var active = Interlocked.Increment(ref _activeThreads);
        
        if (active > _maxObservedThreads)
            _maxObservedThreads = active;

        Thread.SpinWait(100); 
        
        Interlocked.Decrement(ref _activeThreads);
        context.Notify("changed", new MapValue());
    }

    public MapValue Get(IReactorContext context, MapPath path) => 
        new();

    public void Notify(IReactorContext context, MapPath path, MapValue value) => 
        Interlocked.Increment(ref _notifyCount);
}

public sealed class CallbackForwarderReactor : IReactor
{
    public void Put(IReactorContext context, MapPath path, MapValue value) => 
        context.Notify("changed", "data_payload".AsMapValue());

    public MapValue Get(IReactorContext context, MapPath path) => 
        new();

    public void Notify(IReactorContext context, MapPath path, MapValue value)
    {
    }
}

public sealed class AsyncCallbackStub(ReactorContext context, string successPath)
{
    public async Task ExecuteAsync()
    {
        await Task.Delay(10);
        context.Put(successPath, new MapValue());
    }
}
