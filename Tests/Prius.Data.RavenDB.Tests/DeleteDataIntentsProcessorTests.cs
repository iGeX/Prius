using Xunit;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Newtonsoft.Json.Linq;

namespace Prius.Data.RavenDB.Tests;

public class DeleteDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldDeleteDocument()
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
            Deletes = [new DeleteIntent(context, DocId, null, "success/1", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            
            using var session = store.OpenAsyncSession();
            var doc = await session.LoadAsync<JObject>(DocId, TestContext.Current.CancellationToken);
            Assert.Null(doc);
        });
    }

    [Fact]
    public async Task ShouldDeleteDocument_WithChangeVector()
    {
        const string DocId = "users/1";
        
        using var store = GetDocumentStore();
        string changeVector;
        using (var session = store.OpenAsyncSession())
        {
            var doc = new { Name = "John" };
            await session.StoreAsync(doc, DocId, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
            var metadata = session.Advanced.GetMetadataFor(doc);
            changeVector = metadata["@change-vector"]?.ToString() ?? string.Empty;
        }

        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Deletes = [new DeleteIntent(context, DocId, changeVector, "success/1", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            
            using var session = store.OpenAsyncSession();
            var doc = await session.LoadAsync<JObject>(DocId, TestContext.Current.CancellationToken);
            Assert.Null(doc);
        });
    }

    [Fact]
    public async Task ShouldRecordFailure_WhenChangeVectorIsInvalid()
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
            Deletes = [new DeleteIntent(context, DocId, "invalid-vector", "success/1", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.False(context.PutCalls.ContainsKey("success/1"));
            Assert.True(context.PutCalls.ContainsKey("failures/1"));
            Assert.Equal("ConcurrencyException", context.PutCalls["failures/1"]["Type"].AsString());
            return Task.CompletedTask;
        });
    }
}
