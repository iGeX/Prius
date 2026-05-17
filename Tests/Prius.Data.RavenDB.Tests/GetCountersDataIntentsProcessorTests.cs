using Xunit;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class GetCountersDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldGetCounters()
    {
        const string DocId = "users/1";
        
        using var store = GetDocumentStore();
        
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John" }, DocId, TestContext.Current.CancellationToken);
            session.CountersFor(DocId).Increment("Visits", 5);
            session.CountersFor(DocId).Increment("Likes", 10);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            GetCounters = [new GetCountersIntent(context, DocId, "output/counters", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/counters"));
            var counters = context.PutCalls["output/counters"].AsMap();
            Assert.Equal(5L, counters.Get("Visits").AsLong());
            Assert.Equal(10L, counters.Get("Likes").AsLong());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldReturnEmpty_WhenNoCountersFound()
    {
        const string DocId = "users/1";
        
        using var store = GetDocumentStore();
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John" }, DocId, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            GetCounters = [new GetCountersIntent(context, DocId, "output/counters", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/counters"));
            Assert.True(context.PutCalls["output/counters"].AsMap().IsEmpty);
            return Task.CompletedTask;
        });
    }
}
