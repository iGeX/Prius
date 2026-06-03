namespace Prius.Engine.Tests;

using System.Diagnostics;
using System.Threading.Tasks;
using Xunit;
using Core.Maps;
using Abstractions;

public sealed class VirtualBusFineGrainedLockingTests
{
    [Fact]
    public async Task FineGrainedLocking_DifferentBranches_ShouldRunInParallel()
    {
        var trie = new RoutingTrie();
        var blockingElement = new BlockingElement();
        trie.AddRoute("branch1/**", blockingElement);
        var bus = new VirtualBus(trie);
        
        bus.Put("branch1/init", true);
        bus.Put("branch2/init", true);

        var task1 = Task.Run(() => bus.Put("branch1/sub", "trigger"), TestContext.Current.CancellationToken);
        
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var sw = Stopwatch.StartNew();
        bus.Put("branch2/sub", "data");
        sw.Stop();

        await task1;
        
        Assert.True(sw.ElapsedMilliseconds < 500, $"Branch 2 was blocked for {sw.ElapsedMilliseconds}ms. Fine-grained locking might not be working.");
    }

    private sealed class BlockingElement : IElement
    {
        public bool Put(IElementContext context, MapPath path, MapValue value)
        {
            if (value.AsString() != "trigger")
                return true;
            
            Task.Delay(1000).Wait();
            return true;
        }

        public MapValue Get(IElementContext context, MapPath path) => new();
    }
}
