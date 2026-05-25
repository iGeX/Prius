using Xunit;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class LoadDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    [Fact]
    public async Task ShouldLoadExistingDocument()
    {
        const string DocId = "users/1";
        
        using var store = GetDocumentStore();
        
        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John", Age = 30 }, DocId, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var context = new MockElementContext();
        var provider = new MockDataIntentsProvider
        {
            Loads = [new LoadIntent(context, DocId, "output/user", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/user"));
          
            var loadedMap = context.PutCalls["output/user"].AsMap();
            Assert.Equal("John", loadedMap["Name"].AsString());
            Assert.Equal(30L, loadedMap["Age"].AsLong());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldLoadComplexDocument()
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

        var context = new MockElementContext();
        var provider = new MockDataIntentsProvider
        {
            Loads = [new LoadIntent(context, DocId, "output/user", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/user"));
            var loadedMap = context.PutCalls["output/user"].AsMap();
            Assert.Equal("John", loadedMap["Name"].AsString());
            Assert.Equal("Moscow", loadedMap["Address"]["City"].AsString());
            Assert.Equal(123456L, loadedMap["Address"]["Zip"].AsLong());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldPutFailure_WhenDocumentMissing()
    {
        const string DocId = "nonexistent/1";
        
        var context = new MockElementContext();
        var provider = new MockDataIntentsProvider
        {
            Loads = [new LoadIntent(context, DocId, "output/user", "failures/1", TestContext.Current.CancellationToken)]
        };

        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, () =>
        {
            Assert.False(context.PutCalls.ContainsKey("output/user"));
            Assert.True(context.PutCalls.ContainsKey("failures/1"));
            Assert.Contains("Document not found", context.PutCalls["failures/1"]["Message"].AsString());
            return Task.CompletedTask;
        });
    }
}
