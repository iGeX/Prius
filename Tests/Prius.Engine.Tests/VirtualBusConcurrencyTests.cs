namespace Prius.Engine.Tests;

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
        const int Count = 100;
        
        var tasks = Enumerable.Range(0, Count).Select(i => Task.Run(() => 
        {
            bus.Put($"user/{i}", i);
        })).ToArray();

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
        })).ToArray();

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
}
