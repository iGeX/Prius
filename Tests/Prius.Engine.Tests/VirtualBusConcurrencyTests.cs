namespace Prius.Engine.Tests;

using System;
using System.Linq;
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
        const int count = 100;
        
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(() => 
        {
            bus.Put($"user/{i}", i);
        })).ToArray();

        await Task.WhenAll(tasks);

        for (int i = 0; i < count; i++)
        {
            Assert.Equal((long)i, bus.Get($"user/{i}").AsLong());
        }
    }

    [Fact]
    public async Task ConcurrentPut_SameNode_DifferentKeys_ShouldSucceed()
    {
        var bus = new VirtualBus(new RoutingTrie());
        const int count = 100;
        
        var tasks = Enumerable.Range(0, count).Select(i => Task.Run(() => 
        {
            bus.Put($"config/key_{i}", i);
        })).ToArray();

        await Task.WhenAll(tasks);

        var config = bus.Get("config");
        Assert.True(config.IsMap);
        Assert.Equal(count, config.AsMap().Keys().Count());
    }

    [Fact]
    public void Reentrancy_ShouldNotDeadlock()
    {
        var trie = new RoutingTrie();
        var reentrantElement = new ReentrantElement();
        trie.AddRoute("reentrant", reentrantElement);
        var bus = new VirtualBus(trie);

        // Put triggers reentrant calls within the same lock (since it's same path prefix or exactly same path)
        // Note: DispatchPut uses CombinePathsToString(caller.AbsolutePath, relativePath)
        // If "reentrant" element puts to "reentrant/sub", it's a different absolute path string, 
        // thus a different lock object in _nodeLocks?
        // Wait, "reentrant/sub" is a DIFFERENT path than "reentrant".
        
        var result = bus.Put("reentrant", "start");
        
        Assert.True(result);
        Assert.Equal("done", bus.Get("reentrant/status").AsString());
        Assert.Equal("data", bus.Get("reentrant/data").AsString());
    }

    private sealed class ReentrantElement : IElement
    {
        public bool Put(IElementContext context, MapPath path, MapValue value)
        {
            // Path is empty because we are at "reentrant"
            context.Put("status", "done");
            var data = context.Get("data"); // Reading what we might have put or will put
            if (data.IsEmpty)
            {
                 context.Put("data", "data");
            }
            return true;
        }

        public MapValue Get(IElementContext context, MapPath path)
        {
             return context.Get(path);
        }
    }
}
