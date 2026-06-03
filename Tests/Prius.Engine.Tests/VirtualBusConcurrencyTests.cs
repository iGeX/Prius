namespace Prius.Engine.Tests;

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Core.Maps;
using Abstractions;

public sealed class VirtualBusConcurrencyTests
{
    [Fact]
    public async Task ParallelPut_DifferentNodes_ShouldSucceed()
    {
        var bus = new VirtualBus(new RoutingTrie());
        const int Count = 100;
        
        var tasks = Enumerable.Range(0, Count).Select(i => Task.Run(() => 
        {
            bus.Put($"user/{i}", i);
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll(tasks);

        for (var i = 0; i < Count; i++)
            Assert.Equal(i, bus.Get($"user/{i}").AsLong());
    }

    [Fact]
    public async Task ConcurrentPut_SameNode_DifferentKeys_ShouldSucceed()
    {
        var bus = new VirtualBus(new RoutingTrie());
        const int Count = 100;
        
        var tasks = Enumerable.Range(0, Count).Select(i => Task.Run(() => 
        {
            bus.Put($"config/key_{i}", i);
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll(tasks);

        var config = bus.Get("config");
        Assert.True(config.IsMap);
        Assert.Equal(Count, config.AsMap().Keys().Count());
    }

    [Fact]
    public void Reentrancy_ShouldNotDeadlock()
    {
        var trie = new RoutingTrie();
        var reentrantElement = new ReentrantElement();
        trie.AddRoute("reentrant", reentrantElement);
        var bus = new VirtualBus(trie);
        
        var result = bus.Put("reentrant", "start");
        
        Assert.True(result);
        Assert.Equal("done", bus.Get("reentrant/status").AsString());
        Assert.Equal("data", bus.Get("reentrant/data").AsString());
    }

    [Fact]
    public async Task ConcurrentDeepPathCreation_ShouldNotCorruptMemory()
    {
        var bus = new VirtualBus(new RoutingTrie());
        const int ThreadCount = 20;
        const int Iterations = 100;
        
        var tasks = Enumerable.Range(0, ThreadCount).Select(t => Task.Run(() => 
        {
            for (var i = 0; i < Iterations; i++)
                bus.Put($"root/thread_{t}/iter_{i}/data", i);
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll(tasks);

        for (var t = 0; t < ThreadCount; t++)
        {
            for (var i = 0; i < Iterations; i++)
                Assert.Equal(i, bus.Get($"root/thread_{t}/iter_{i}/data").AsInt());
        }
    }

    [Fact]
    public async Task ConcurrentUpdateTrie_And_Operations_ShouldBeStable()
    {
        var bus = new VirtualBus(new RoutingTrie());
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        
        var readWriteTask = Task.Run(() => 
        {
            var counter = 0;
            while (!cts.IsCancellationRequested)
            {
                var path = $"data/{counter % 100}";
                bus.Put(path, counter);
                counter++;
            }
        }, TestContext.Current.CancellationToken);

        var trieUpdateTask = Task.Run(async () => 
        {
            while (!cts.IsCancellationRequested)
            {
                var trie = new RoutingTrie();
                trie.AddRoute("data/*", new AtomicSpyElement());
                bus.UpdateTrie(trie);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }, TestContext.Current.CancellationToken);

        try { await Task.WhenAll(readWriteTask, trieUpdateTask); }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task ConcurrentScalarToMapTransition_ShouldBeAtomic()
    {
        var bus = new VirtualBus(new RoutingTrie());
        const int Iterations = 1000;
        const string Path = "collision/node";

        var task1 = Task.Run(() => 
        {
            for (var i = 0; i < Iterations; i++)
                bus.Put(Path, i);
        }, TestContext.Current.CancellationToken);

        var task2 = Task.Run(() => 
        {
            for (var i = 0; i < Iterations; i++)
                bus.Put($"{Path}/sub", "value");
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(task1, task2);
        
        var result = bus.Get(Path);
        if (result.IsMap)
            Assert.Equal("value", bus.Get($"{Path}/sub").AsString());
        else
            Assert.True(result.AsLong() >= 0);
    }

    [Fact]
    public async Task ConcurrentPutAbsolute_ShouldNotDeadlock()
    {
        var trie = new RoutingTrie();
        var spy = new AtomicSpyElement();
        trie.AddRoute("sink/**", spy);
        var bus = new VirtualBus(trie);

        const int Count = 500;
        var tasks = Enumerable.Range(0, Count).Select(i => Task.Run(() => 
        {
            bus.PutAbsolute($"sink/event_{i}", i);
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll(tasks);
        
        var retries = 50;
        while (spy.CallCount < Count && retries-- > 0)
            await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.Equal(Count, spy.CallCount);
    }

    [Fact]
    public async Task ReadWriteContention_HeavyLoad()
    {
        var bus = new VirtualBus(new RoutingTrie());
        const int Writers = 5;
        const int Readers = 15;
        const int Iterations = 1000;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));

        var writerTasks = Enumerable.Range(0, Writers).Select(w => Task.Run(() => 
        {
            for (var i = 0; i < Iterations && !cts.IsCancellationRequested; i++)
                bus.Put($"branch_{w}/key_{i % 10}", i);
        }, TestContext.Current.CancellationToken)).ToArray();

        var readerTasks = Enumerable.Range(0, Readers).Select(r => Task.Run(() => 
        {
            while (!cts.IsCancellationRequested)
            {
                for (var w = 0; w < Writers; w++)
                    _ = bus.Get($"branch_{w}/key_{r % 10}");
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        try { await Task.WhenAll(writerTasks.Concat(readerTasks)); }
        catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task RecursiveConcurrentPut_ShouldNotDeadlock()
    {
        var trie = new RoutingTrie();
        var rec = new ConcurrentRecursiveElement();
        trie.AddRoute("rec/**", rec);
        var bus = new VirtualBus(trie);

        const int ThreadCount = 10;
        const int Iterations = 50;

        var tasks = Enumerable.Range(0, ThreadCount).Select(t => Task.Run(() =>
        {
            for (var i = 0; i < Iterations; i++)
                bus.Put($"rec/t{t}/i{i}", "trigger");
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll(tasks);
        
        for (var t = 0; t < ThreadCount; t++)
        {
            for (var i = 0; i < Iterations; i++)
                Assert.Equal("done", bus.Get($"rec/t{t}/i{i}/sub").AsString());
        }
    }

    [Fact]
    public async Task ConcurrentRoutingResolution_WithWildcards_ShouldBeStable()
    {
        var trie = new RoutingTrie();
        var spy = new AtomicSpyElement();
        trie.AddRoute("api/v1/*", spy);
        trie.AddRoute("api/**", spy);
        trie.AddRoute("web/pages/**", spy);
        trie.AddRoute("web/pages/home", spy);
        var bus = new VirtualBus(trie);

        const int ThreadCount = 20;
        const int Iterations = 500;

        var tasks = Enumerable.Range(0, ThreadCount).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < Iterations; i++)
            {
                bus.Get("api/v1/users");
                bus.Get("api/v2/products/123/details");
                bus.Get("web/pages/home");
                bus.Get("web/pages/about/company");
            }
        }, TestContext.Current.CancellationToken)).ToArray();

        await Task.WhenAll(tasks);
    }

    private sealed class ReentrantElement : IElement
    {
        public bool Put(IElementContext context, MapPath path, MapValue value)
        {
            context.Put("status", "done");
            var data = context.Get("data");
            if (data.IsEmpty)
                 context.Put("data", "data");
            return true;
        }

        public MapValue Get(IElementContext context, MapPath path) => context.Get(path);
    }

    private sealed class ConcurrentRecursiveElement : IElement
    {
        public bool Put(IElementContext context, MapPath path, MapValue value)
        {
            if (value.AsString() == "trigger")
            {
                context.Put(path + "sub", "done");
                return true;
            }
            
            if (context is IBusContext busContext)
            {
                busContext.Node.DeepPut(path, value);
                return true;
            }
            
            return false;
        }

        public MapValue Get(IElementContext context, MapPath path) 
        {
            if (path.IsEmpty) return context.Get(string.Empty);
            
            if (context is IBusContext busContext)
                return busContext.Node.DeepGet(path);
            
            return context.Get(path);
        }
    }

    private sealed class AtomicSpyElement : IElement
    {
        public int CallCount;
        public bool Put(IElementContext context, MapPath path, MapValue value)
        {
            Interlocked.Increment(ref CallCount);
            return true;
        }
        public MapValue Get(IElementContext context, MapPath path) => new();
    }
}
