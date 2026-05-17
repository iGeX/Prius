using Newtonsoft.Json.Linq;
using Xunit;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class StoreDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldStoreDocument()
    {
        const string DocId = "users/1";
        const string Collection = "users";
        
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(new MockReactorContext(), DictionaryMap.New
                .With("Name", "John")
                .With("Age", 30)
                .With("Address", DictionaryMap.New
                    .With("City", "Moscow")
                    .With("Zip", 123456))
                .With("@metadata", DictionaryMap.New
                    .With("@id", DocId)
                    .With("@collection", Collection)), "failures", TestContext.Current.CancellationToken)]
        };
        
        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, async () =>
        {
            using var session = store.OpenAsyncSession();
            var doc = await session.LoadAsync<JObject>(DocId, TestContext.Current.CancellationToken);
            Assert.NotNull(doc);
            
            Assert.Equal("John", doc["Name"]?.ToString());
            Assert.Equal(30L, doc.Value<long>("Age"));
            
            var address = doc["Address"] as JObject;
            Assert.NotNull(address);
            Assert.Equal("Moscow", address["City"]?.ToString());
            Assert.Equal(123456L, address.Value<long>("Zip"));
            
            var metadata = doc["@metadata"] as JObject;
            Assert.NotNull(metadata);
            Assert.Equal(DocId, metadata["@id"]?.ToString());
            Assert.Equal(Collection, metadata["@collection"]?.ToString());
        });
    }

    [Fact]
    public async Task ShouldRecordFailure_WhenNoIdSpecified()
    {
        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(context, DictionaryMap.New
                .With("Name", "John"), "failures/1", TestContext.Current.CancellationToken)]
        };

        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("failures/1"));
            Assert.Equal("No @metadata/@id specified", context.PutCalls["failures/1"].AsMap().Get("Message").AsString());
            return Task.CompletedTask;
        });
    }
    
    [Fact]
    public async Task ShouldHandleConcurrencyException_ByRecordingFailure()
    {
        const string DocId = "users/1";

        using var store = GetDocumentStore();
        
        // Pre-create to cause conflict
        using var session = store.OpenAsyncSession();
        
        var emptyObj = new { };
        await session.StoreAsync(emptyObj, DocId, TestContext.Current.CancellationToken);
        var metadata = session.Advanced.GetMetadataFor(emptyObj);
        metadata["@collection"] = "users";
        await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        metadata = session.Advanced.GetMetadataFor(emptyObj);
        var existingVector = metadata["@change-vector"]?.ToString() ?? string.Empty;
        
        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Stores = [
                new StoreIntent(context, DictionaryMap.New.With("@metadata", DictionaryMap.New.With("@id", DocId).With("@collection", "users").With("@change-vector", existingVector)), "failures/1", TestContext.Current.CancellationToken),
                new StoreIntent(context, DictionaryMap.New.With("@metadata", DictionaryMap.New.With("@id", DocId).With("@collection", "users").With("@change-vector", "Vector2")), "failures/2", TestContext.Current.CancellationToken)
            ]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.False(context.PutCalls.ContainsKey("failures/1"));
            Assert.True(context.PutCalls.ContainsKey("failures/2"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldHandleDateTimeFields()
    {
        var docId = "docs/1";
        var now = DateTime.UtcNow;
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(new MockReactorContext(), DictionaryMap.New
                .With("CreatedAt", now.ToString("O"))
                .With("@metadata", DictionaryMap.New
                    .With("@id", docId)
                    .With("@collection", "docs")), "failures", TestContext.Current.CancellationToken)]
        };

        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, async () =>
        {
            using var session = store.OpenAsyncSession();
            var doc = await session.LoadAsync<JObject>(docId, TestContext.Current.CancellationToken);
            Assert.Equal(now.ToString("O"), doc!["CreatedAt"]?.ToString());
        });
    }

    [Fact]
    public async Task ShouldHandleExpirationMetadata()
    {
        var docId = "docs/ttl";
        var expires = DateTime.UtcNow.AddMinutes(5);
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(new MockReactorContext(), DictionaryMap.New
                .With("@metadata", DictionaryMap.New
                    .With("@id", docId)
                    .With("@collection", "docs")
                    .With("@expires", expires.ToString("O"))), "failures", TestContext.Current.CancellationToken)]
        };

        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, async () =>
        {
            using var session = store.OpenAsyncSession();
            var metadata = session.Advanced.GetMetadataFor(await session.LoadAsync<dynamic>(docId));
            Assert.Equal(expires.ToString("O"), metadata.GetString("@expires"));
        });
    }
}
