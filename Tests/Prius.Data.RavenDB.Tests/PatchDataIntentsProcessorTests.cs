using Xunit;
using Prius.Engine.Abstractions;
using Newtonsoft.Json.Linq;

namespace Prius.Data.RavenDB.Tests;

public class PatchDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldPatchDocumentField()
    {
        const string DocId = "users/1";
        
        using var store = GetDocumentStore();
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John", Age = 30 }, DocId, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Patches = [new PatchIntent(context, DocId, "Age", 31L, "success/1", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            
            using var session = store.OpenAsyncSession();
            var doc = await session.LoadAsync<JObject>(DocId, TestContext.Current.CancellationToken);
            Assert.Equal(31L, doc!.Value<long>("Age"));
            Assert.Equal("John", doc["Name"]?.ToString());
        });
    }

    [Fact]
    public async Task ShouldPatchNestedDocumentField()
    {
        const string DocId = "docs/complex";
        using var store = GetDocumentStore();
        
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { 
                Name = "John", 
                Address = new { City = "Moscow", Zip = 123456 } 
            }, DocId, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Patches = [new PatchIntent(context, DocId, "Address/City", "Saint-Petersburg", "success/1", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            
            using var session = store.OpenAsyncSession();
            var doc = await session.LoadAsync<JObject>(DocId, TestContext.Current.CancellationToken);
            var address = doc!["Address"] as JObject;
            Assert.Equal("Saint-Petersburg", address!["City"]?.ToString());
        });
    }
}
