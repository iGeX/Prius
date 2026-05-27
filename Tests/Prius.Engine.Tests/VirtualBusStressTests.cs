using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Xunit;

namespace Prius.Engine.Tests;

public sealed class VirtualBusStressTests
{
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

    [Fact]
    public async Task UpdateTrie_UnderLoad_IsAtomicAndStable()
    {
        var bus = new VirtualBus(new RoutingTrie());
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        
        const int WorkerCount = 20;
        var workers = new Task[WorkerCount];
        var totalRequests = 0L;
        
        for (var i = 0; i < WorkerCount; i++)
        {
            workers[i] = Task.Run(() => 
            {
                while (!cts.IsCancellationRequested)
                {
                    bus.Put("/api/test", true);
                    Interlocked.Increment(ref totalRequests);
                }
            }, TestContext.Current.CancellationToken);
        }

        var swapCount = 0;
        var swapper = Task.Run(async () => 
        {
            while (!cts.IsCancellationRequested)
            {
                var trie = new RoutingTrie();
                trie.AddRoute("/api/test", new AtomicSpyElement());
                bus.UpdateTrie(trie);
                swapCount++;
                await Task.Delay(10, cts.Token);
            }
        }, TestContext.Current.CancellationToken);

        await Task.WhenAll(workers.Concat([swapper]));

        Assert.True(totalRequests > 1000, $"Total requests should be high, was {totalRequests}");
        Assert.True(swapCount >= 5, $"Total swaps should be significant, was {swapCount}");
        
        var finalTrie = new RoutingTrie();
        var finalSpy = new AtomicSpyElement();
        finalTrie.AddRoute("/final", finalSpy);
        bus.UpdateTrie(finalTrie);
        
        bus.Put("/final", "done");
        Assert.Equal(1, finalSpy.CallCount);
    }
}
