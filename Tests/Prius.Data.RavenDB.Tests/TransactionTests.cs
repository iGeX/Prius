using Xunit;
using Prius.Engine.Abstractions;
using Prius.Core.Maps;
using Newtonsoft.Json.Linq;

namespace Prius.Data.RavenDB.Tests;

public class TransactionTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldCommitAllOrNothing_Success()
    {
        using var store = GetDocumentStore();
        
        var context = new MockElementContext();
        var doc1 = DictionaryMap.New
            .With("@metadata", DictionaryMap.New
                .With("@id", "users/1")
                .With("@collection", "Users"))
            .With("Name", "Alice");
            
        var doc2 = DictionaryMap.New
            .With("@metadata", DictionaryMap.New
                .With("@id", "users/2")
                .With("@collection", "Users"))
            .With("Name", "Bob");

        var provider = new MockDataIntentsProvider();
        provider.AddTransaction(context, [
            new StoreIntent(context, doc1, "success/1", "failures/1", TestContext.Current.CancellationToken),
            new StoreIntent(context, doc2, "success/2", "failures/2", TestContext.Current.CancellationToken)
        ]);

        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            Assert.True(context.PutCalls.ContainsKey("success/2"));
            
            using var session = store.OpenAsyncSession();
            var u1 = await session.LoadAsync<JObject>("users/1", TestContext.Current.CancellationToken);
            var u2 = await session.LoadAsync<JObject>("users/2", TestContext.Current.CancellationToken);
            
            Assert.NotNull(u1);
            Assert.Equal("Alice", u1["Name"]?.ToString());
            Assert.NotNull(u2);
            Assert.Equal("Bob", u2["Name"]?.ToString());
        });
    }

    [Fact]
    public async Task ShouldCommitAllOrNothing_RollbackOnFailure()
    {
        using var store = GetDocumentStore();
        
        var context = new MockElementContext();
        // Valid document
        var doc1 = DictionaryMap.New
            .With("@metadata", DictionaryMap.New
                .With("@id", "users/1")
                .With("@collection", "Users"))
            .With("Name", "Alice");
            
        // Invalid document (missing @collection) which will cause an exception in QueueStore
        var doc2 = DictionaryMap.New
            .With("@metadata", DictionaryMap.New
                .With("@id", "users/2"))
            .With("Name", "Bob");

        var provider = new MockDataIntentsProvider();
        provider.AddTransaction(context, [
            new StoreIntent(context, doc1, "success/1", "failures/1", TestContext.Current.CancellationToken),
            new StoreIntent(context, doc2, "success/2", "failures/2", TestContext.Current.CancellationToken)
        ]);

        await ExecuteTest(store, provider, async () =>
        {
            // The transaction should fail
            Assert.False(context.PutCalls.ContainsKey("success/1"));
            Assert.True(context.PutCalls.ContainsKey("failures/1"));
            Assert.True(context.PutCalls.ContainsKey("failures/2"));
            
            // Check that even the first document was not saved (rolled back)
            using var session = store.OpenAsyncSession();
            var u1 = await session.LoadAsync<JObject>("users/1", TestContext.Current.CancellationToken);
            Assert.Null(u1);
        });
    }

    [Fact]
    public async Task ShouldShareSession_ReadYourOwnWrites()
    {
        using var store = GetDocumentStore();
        
        var context = new MockElementContext();
        var doc = DictionaryMap.New
            .With("@metadata", DictionaryMap.New
                .With("@id", "users/1")
                .With("@collection", "Users"))
            .With("Name", "Alice");

        var provider = new MockDataIntentsProvider();
        provider.AddTransaction(context, [
            new StoreIntent(context, doc, "success/store", "failures/store", TestContext.Current.CancellationToken),
            // Load in the same transaction - should succeed and return the stored data
            new LoadIntent(context, "users/1", "success/load", "failures/load", TestContext.Current.CancellationToken)
        ]);

        await ExecuteTest(store, provider, async () =>
        {
            if (context.PutCalls.TryGetValue("failures/store", out var failStore))
            {
                throw new Exception($"Store failed: {failStore.AsMap()["Message"].AsString()}");
            }
            if (context.PutCalls.TryGetValue("failures/load", out var failLoad))
            {
                throw new Exception($"Load failed: {failLoad.AsMap()["Message"].AsString()}");
            }
            Assert.True(context.PutCalls.ContainsKey("success/store"));
            Assert.True(context.PutCalls.ContainsKey("success/load"));
            
            var loadedMap = context.PutCalls["success/load"].AsMap();
            Assert.Equal("Alice", loadedMap["Name"].AsString());
            
            // Double check in database
            using var session = store.OpenAsyncSession();
            var dbDoc = await session.LoadAsync<JObject>("users/1", TestContext.Current.CancellationToken);
            Assert.NotNull(dbDoc);
            Assert.Equal("Alice", dbDoc["Name"]?.ToString());
        });
    }
}
