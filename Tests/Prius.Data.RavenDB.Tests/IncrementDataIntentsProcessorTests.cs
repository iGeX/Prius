using Xunit;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class IncrementDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldIncrementCounter()
    {
        const string DocId = "users/1";
        const string CounterName = "Visits";
        
        using var store = GetDocumentStore();
        
        // Ensure doc exists
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John" }, DocId, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockElementContext();
        var provider = new MockDataIntentsProvider
        {
            Increments = [new IncrementIntent(context, DocId, CounterName, 5, "success/1", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            
            using var session = store.OpenAsyncSession();
            var counters = await session.CountersFor(DocId).GetAllAsync();
            Assert.Equal(5L, counters[CounterName]);
        });
    }

    [Fact]
    public async Task ShouldHandleMultipleIncrements()
    {
        const string DocId = "users/1";
        const string CounterName = "Visits";
        
        using var store = GetDocumentStore();
        
        // Ensure doc exists
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John" }, DocId, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        
        var context = new MockElementContext();
        var provider = new MockDataIntentsProvider
        {
            Increments = [
                new IncrementIntent(context, DocId, CounterName, 5, "success/1", "failures/1", TestContext.Current.CancellationToken),
                new IncrementIntent(context, DocId, CounterName, 3, "success/2", "failures/2", TestContext.Current.CancellationToken)
            ]
        };

        await ExecuteTest(store, provider, async () =>
        {
            using var session = store.OpenAsyncSession();
            var counters = await session.CountersFor(DocId).GetAllAsync();
            Assert.Equal(8L, counters[CounterName]);
        });
    }
}
