using Raven.Client.Documents.Indexes;
using Xunit;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class QueryDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    private class Users_ByAge : AbstractIndexCreationTask
    {
        public override IndexDefinition CreateIndexDefinition()
        {
            return new IndexDefinition
            {
                Maps = { "from user in docs.Users select new { user.Age }" },
                Name = "Users/ByAge"
            };
        }
    }

    [Fact]
    public async Task ShouldQueryByField()
    {
        using var store = GetDocumentStore();
        await new Users_ByAge().ExecuteAsync(store, token: TestContext.Current.CancellationToken);

        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John", Age = 30 }, "users/1", TestContext.Current.CancellationToken);
            await session.StoreAsync(new { Name = "Jane", Age = 25 }, "users/2", TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Users/ByAge")),
            ("Where", DictionaryMap.New.With(("Age", new MapValue(30L))).AsMapValue())
        );

        var provider = new MockDataIntentsProvider
        {
            Queries = [new QueryIntent(context, queryMap, "output/results", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/results"));
            var results = context.PutCalls["output/results"].AsMap();
            var items = results.Get("Items").AsMap();
            var order = results.Get("Order").AsMap();

            Assert.Equal(1L, items.Keys().Count());
            Assert.False(items.Get("users/1").IsEmpty);
            Assert.Equal("John", items.Get("users/1").AsMap().Get("Name").AsString());
            Assert.Equal("users/1", order.Get("0").AsString());
            
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldQueryWithOrdering()
    {
        using var store = GetDocumentStore();
        await new Users_ByAge().ExecuteAsync(store, token: TestContext.Current.CancellationToken);

        using (var session = store.OpenAsyncSession())
        {
            await session.StoreAsync(new { Name = "John", Age = 30 }, "users/1", TestContext.Current.CancellationToken);
            await session.StoreAsync(new { Name = "Jane", Age = 25 }, "users/2", TestContext.Current.CancellationToken);
            await session.StoreAsync(new { Name = "Bob", Age = 35 }, "users/3", TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Users/ByAge")),
            ("OrderBy", DictionaryMap.New.With(
                ("Order", DictionaryMap.New.With(
                    ("0", new MapValue("Age"))
                ).AsMapValue()),
                ("Data", DictionaryMap.New.With(
                    ("Age", new MapValue("Asc"))
                ).AsMapValue())
            ).AsMapValue())
        );

        var provider = new MockDataIntentsProvider
        {
            Queries = [new QueryIntent(context, queryMap, "output/results", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/results"));
            var results = context.PutCalls["output/results"].AsMap();
            var order = results.Get("Order").AsMap();

            Assert.Equal("users/2", order.Get("0").AsString()); // Age 25
            Assert.Equal("users/1", order.Get("1").AsString()); // Age 30
            Assert.Equal("users/3", order.Get("2").AsString()); // Age 35
            
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldHandleEmptyResults()
    {
        using var store = GetDocumentStore();
        await new Users_ByAge().ExecuteAsync(store, token: TestContext.Current.CancellationToken);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Users/ByAge")),
            ("Where", DictionaryMap.New.With(("Age", new MapValue(99L))).AsMapValue())
        );

        var provider = new MockDataIntentsProvider
        {
            Queries = [new QueryIntent(context, queryMap, "output/results", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/results"));
            var results = context.PutCalls["output/results"].AsMap();
            Assert.True(results.Get("Items").AsMap().IsEmpty);
            Assert.True(results.Get("Order").AsMap().IsEmpty);
            return Task.CompletedTask;
        });
    }
}
