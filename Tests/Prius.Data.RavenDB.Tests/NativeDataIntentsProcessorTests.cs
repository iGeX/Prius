using Xunit;
using Prius.Engine.Abstractions;
using Raven.Client.Documents.Session;
using Newtonsoft.Json.Linq;

namespace Prius.Data.RavenDB.Tests;

public class NativeDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldExecuteNativeIntentInTransaction()
    {
        using var store = GetDocumentStore();
        
        var context = new MockElementContext();
        var provider = new MockDataIntentsProvider
        {
            Natives = [new NativeIntent(context, async (sessionObj, nativeIntent) =>
            {
                var session = (IAsyncDocumentSession)sessionObj;
                await session.StoreAsync(new { Name = "Alice", Age = 25 }, "users/alice", nativeIntent.Token);
            }, "success/1", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            
            using var session = store.OpenAsyncSession();
            var doc = await session.LoadAsync<JObject>("users/alice", TestContext.Current.CancellationToken);
            Assert.NotNull(doc);
            Assert.Equal("Alice", doc["Name"]?.ToString());
            Assert.Equal(25L, doc.Value<long>("Age"));
        });
    }
}
