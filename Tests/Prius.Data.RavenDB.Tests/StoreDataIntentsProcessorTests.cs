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

        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(context, DictionaryMap.New
                .With("Name", "John")
                .With("Age", 30)
                .With("Address", DictionaryMap.New
                    .With("City", "Moscow")
                    .With("Zip", 123456))
                .With("@metadata", DictionaryMap.New
                    .With("@id", DocId)
                    .With("@collection", Collection)), "success", "failures", TestContext.Current.CancellationToken)]
        };
        
        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success"));
            
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
                new StoreIntent(context, DictionaryMap.New.With("@metadata", DictionaryMap.New.With("@id", DocId).With("@collection", "users").With("@change-vector", existingVector)), "success/1","failures/1", TestContext.Current.CancellationToken),
                new StoreIntent(context, DictionaryMap.New.With("@metadata", DictionaryMap.New.With("@id", DocId).With("@collection", "users").With("@change-vector", "Vector2")), "success/2", "failures/2", TestContext.Current.CancellationToken)
            ]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            Assert.False(context.PutCalls.ContainsKey("failures/1"));
            Assert.True(context.PutCalls.ContainsKey("failures/2"));
            Assert.False(context.PutCalls.ContainsKey("success/2"));
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldHandleDateTimeFields()
    {
        const string DocId = "docs/1";
        
        var now = DateTime.UtcNow;
        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(context, DictionaryMap.New
                .With("CreatedAt", now.ToString("O"))
                .With("@metadata", DictionaryMap.New
                    .With("@id", DocId)
                    .With("@collection", "docs")), "success", "failures", TestContext.Current.CancellationToken)]
        };

        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success"));
            
            using var session = store.OpenAsyncSession();
            var doc = await session.LoadAsync<JObject>(DocId, TestContext.Current.CancellationToken);
            Assert.Equal(now.ToString("O"), doc!["CreatedAt"]?.ToString());
        });
    }

    [Fact]
    public async Task ShouldHandleExpirationMetadata()
    {
        const string DocId = "docs/ttl";
        
        var expires = DateTime.UtcNow.AddMinutes(5);
        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(context, DictionaryMap.New
                .With("@metadata", DictionaryMap.New
                    .With("@id", DocId)
                    .With("@collection", "docs")
                    .With("@expires", expires.ToString("O"))), "success", "failures", TestContext.Current.CancellationToken)]
        };

        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success"));
            
            using var session = store.OpenAsyncSession();
            var metadata = session.Advanced.GetMetadataFor(await session.LoadAsync<dynamic>(DocId));
            Assert.Equal(expires.ToString("O"), metadata.GetString("@expires"));
        });
    }
    
    [Fact]
    public async Task Should_Dispose_Stream_After_StoreAttachment()
    {
        const string DocId = "docs/with-binary";
        var context = new MockReactorContext();
        
        var spyStream = new SpyMemoryStream("binary content"u8.ToArray());
        var provider = new MockDataIntentsProvider
        {
            StoreAttachments = [new StoreAttachmentIntent(context, DocId, "file.bin", spyStream, "application/octet-stream", "success", "failures", TestContext.Current.CancellationToken)]
        };

        using var store = GetDocumentStore();
        
        using (var session = store.OpenAsyncSession())
        {
            var doc = new { };
            await session.StoreAsync(doc, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(doc)["@collection"] = "docs";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success"), "Intent should execute successfully and report to success path");
            Assert.True(spyStream.IsDisposed, "The processor must call Dispose on the incoming intent stream!");
            return Task.CompletedTask;
        });
    }

    private sealed class SpyMemoryStream(byte[] buffer) : MemoryStream(buffer)
    {
        public bool IsDisposed { get; private set; }
        
        protected override void Dispose(bool disposing)
        {
            IsDisposed = true;
            base.Dispose(disposing);
        }
    }
}
