using Raven.Client.Documents.Indexes;
using Xunit;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Raven.Client.Documents.Session;
// ReSharper disable InconsistentNaming

namespace Prius.Data.RavenDB.Tests;

public class QueryDataIntentsProcessorTests : AbstractDataIntentsProcessorTests
{
    private class Users_ByAge : AbstractIndexCreationTask
    {
        public override IndexDefinition CreateIndexDefinition() =>
            new()
            {
                Maps = { "from user in docs.Users select new { user.Age }" },
                Name = "Users/ByAge"
            };
    }
    
    private class Users_ByNotes : AbstractIndexCreationTask
    {
        public override IndexDefinition CreateIndexDefinition() =>
            new()
            {
                Name = "Users/ByNotes",
                Maps = { "from user in docs.Users select new { Notes = user.Notes }" },
                Fields = new Dictionary<string, IndexFieldOptions>
                {
                    {
                        "Notes", new IndexFieldOptions 
                        { 
                            Indexing = FieldIndexing.Search,
                            Storage = FieldStorage.Yes,
                            Analyzer = "StandardAnalyzer"
                        }
                    }
                }
            };
    }

    private static async Task StoreUser(object user, string id, IAsyncDocumentSession session)
    {
        await session.StoreAsync(user, id, TestContext.Current.CancellationToken);
        session.Advanced.GetMetadataFor(user)["@collection"] = "Users";
    }

    [Fact]
    public async Task ShouldQueryByField()
    {
        using var store = GetDocumentStore();
        await new Users_ByAge().ExecuteAsync(store, token: TestContext.Current.CancellationToken);

        using (var session = store.OpenAsyncSession())
        {
            await StoreUser(new { Name = "John", Age = 30 }, "users/1", session);
            await StoreUser(new { Name = "Jane", Age = 25 }, "users/2", session);
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
            await StoreUser(new { Name = "John", Age = 30 }, "users/1", session);
            await StoreUser(new { Name = "Jane", Age = 25 }, "users/2", session);
            await StoreUser(new { Name = "Bob", Age = 35 }, "users/3", session);
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

    [Fact]
    public async Task ShouldQueryWithOrOperator()
    {
        using var store = GetDocumentStore();
        await new Users_ByAge().ExecuteAsync(store, token: TestContext.Current.CancellationToken);

        using (var session = store.OpenAsyncSession())
        {
            await StoreUser(new { Name = "John", Age = 30 }, "users/1", session);
            await StoreUser(new { Name = "Jane", Age = 25 }, "users/2", session);
            await StoreUser(new { Name = "Bob", Age = 35 }, "users/3", session);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Users/ByAge")),
            ("Where", DictionaryMap.New.With(("$or", DictionaryMap.New.With(
                ("Order", DictionaryMap.New.With(("0", new MapValue("cond1")), ("1", new MapValue("cond2"))).AsMapValue()),
                ("Data", DictionaryMap.New.With(
                    ("cond1", DictionaryMap.New.With(("Age", new MapValue(30L))).AsMapValue()),
                    ("cond2", DictionaryMap.New.With(("Age", new MapValue(35L))).AsMapValue())
                ).AsMapValue())
            ).AsMapValue())).AsMapValue())
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
            Assert.Equal(2L, items.Keys().Count());
            Assert.False(items.Get("users/1").IsEmpty);
            Assert.False(items.Get("users/3").IsEmpty);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldQueryWithPagination()
    {
        using var store = GetDocumentStore();
        await new Users_ByAge().ExecuteAsync(store, token: TestContext.Current.CancellationToken);

        using (var session = store.OpenAsyncSession())
        {
            for (var i = 1; i <= 5; i++)
                await StoreUser(new { Age = 20 + i }, $"users/{i}", session);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Users/ByAge")),
            ("Skip", new MapValue(2)),
            ("Take", new MapValue(2))
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
            Assert.Equal(2L, items.Keys().Count());
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldQueryWithFacets()
    {
        using var store = GetDocumentStore();
        await new Users_ByAge().ExecuteAsync(store, token: TestContext.Current.CancellationToken);

        using (var session = store.OpenAsyncSession())
        {
            await StoreUser(new { Age = 30 }, "users/1", session);
            await StoreUser(new { Age = 30 }, "users/2", session);
            await StoreUser(new { Age = 25 }, "users/3", session);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Users/ByAge")),
            ("Facets", DictionaryMap.New.With(
                ("Age", DictionaryMap.New.With(("Function", new MapValue("count")), ("Field", new MapValue("Age"))).AsMapValue())
            ).AsMapValue())
        );

        var provider = new MockDataIntentsProvider
        {
            Queries = [new QueryIntent(context, queryMap, "output/results", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/results"), "PutCalls should contain output/results");
            var results = context.PutCalls["output/results"].AsMap();
            
            Assert.True(results.Get("Items").AsMap().IsEmpty, "Items map should be empty for facet queries");
            Assert.True(results.Get("Order").AsMap().IsEmpty, "Order map should be empty for facet queries");
            
            var facets = results.Get("Facets").AsMap();
            if (facets.IsEmpty)
            {
                Assert.Fail("Facets map is empty. Full results: " + results.Serialize());
            }
            
            Assert.True(facets.ContainsKey("Age"), "Facets should contain 'Age' key");
            var ageFacet = facets.Get("Age").AsMap();
            var values = ageFacet.Get("Values").AsMap();
            
            Assert.Equal(2, values.Keys().Count());

            return Task.CompletedTask;
        });
    }
    
    [Fact]
    public async Task ShouldQueryMapReduceWithValidation()
    {
        // Arrange
        using var store = GetDocumentStore();
        
        var context = new MockReactorContext();
        
        var invalidQueryMap = DictionaryMap.New.With(
            ("From", new MapValue("Sales")),
            ("Reduce", DictionaryMap.New.With(
                ("Total", DictionaryMap.New.With(("$sum", new MapValue("Amount"))).AsMapValue())
            ).AsMapValue())
        );

        var provider = new MockDataIntentsProvider
        {
            Queries = [new QueryIntent(context, invalidQueryMap, "output/results", "failures/1", TestContext.Current.CancellationToken)]
        };

        // Act & Assert
        await ExecuteTest(store, provider, () =>
        {
            Assert.False(context.PutCalls.ContainsKey("output/results"));
            Assert.True(context.PutCalls.ContainsKey("failures/1"), "Should report architectural failure for invalid map-reduce");
            
            return Task.CompletedTask;
        });
    }
}
