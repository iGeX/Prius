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
                            TermVector = FieldTermVector.WithPositionsAndOffsets
                        }
                    }
                }
            };
    }
    
    private class Orders_ByTotal : AbstractIndexCreationTask
    {
        public override IndexDefinition CreateIndexDefinition() =>
            new()
            {
                Name = "Orders/ByTotal",
                Maps = { "from order in docs.Orders select new { Total = order.Total }" }
            };
    }
    
    private class Sales_ByProduct : AbstractIndexCreationTask
    {
        public override IndexDefinition CreateIndexDefinition() =>
            new()
            {
                Name = "Sales/ByProduct",
                Maps = { "from sale in docs.Sales select new { ProductId = sale.ProductId, Amount = sale.Amount, Count = 1 }" },
                Reduce = "from result in results group result by result.ProductId into g select new { ProductId = g.Key, Amount = g.Sum(x => x.Amount), Count = g.Sum(x => x.Count) }"
            };
    }

    private class Stores_ByLocation : AbstractIndexCreationTask
    {
        public override IndexDefinition CreateIndexDefinition() =>
            new()
            {
                Name = "Stores/ByLocation",
                Maps = { "from store in docs.Stores select new { Coordinates = CreateSpatialField(store.Lat, store.Lng) }" }
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
            var items = results["Items"].AsMap();
            var order = results["Order"].AsMap();

            Assert.Equal(1L, items.Keys().Count());
            Assert.False(items["users/1"].IsEmpty);
            Assert.Equal("John", items["users/1"].AsMap()["Name"].AsString());
            Assert.Equal("users/1", order["0"].AsString());
            
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
            var order = results["Order"].AsMap();

            Assert.Equal("users/2", order["0"].AsString()); // Age 25
            Assert.Equal("users/1", order["1"].AsString()); // Age 30
            Assert.Equal("users/3", order["2"].AsString()); // Age 35
            
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
            Assert.True(results["Items"].AsMap().IsEmpty);
            Assert.True(results["Order"].AsMap().IsEmpty);
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
            var items = results["Items"].AsMap();
            Assert.Equal(2L, items.Keys().Count());
            Assert.False(items["users/1"].IsEmpty);
            Assert.False(items["users/3"].IsEmpty);
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
            var items = results["Items"].AsMap();
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
            
            Assert.True(results["Items"].AsMap().IsEmpty, "Items map should be empty for facet queries");
            Assert.True(results["Order"].AsMap().IsEmpty, "Order map should be empty for facet queries");
            
            var facets = results["Facets"].AsMap();
            if (facets.IsEmpty)
            {
                Assert.Fail("Facets map is empty. Full results: " + results.Serialize());
            }
            
            Assert.True(facets.ContainsKey("Age"), "Facets should contain 'Age' key");
            var ageFacet = facets["Age"].AsMap();
            var values = ageFacet["Values"].AsMap();
            
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
    
    [Fact]
    public async Task ShouldQueryWithHighlighting()
    {
        using var store = GetDocumentStore();
        await new Users_ByNotes().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        using (var session = store.OpenAsyncSession())
        {
            await StoreUser(new { Notes = "This is a very important RavenDB developer entry" }, "users/1", session);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Users/ByNotes")),
            ("Where", DictionaryMap.New.With(
                ("Notes", DictionaryMap.New.With(
                    ("$search", DictionaryMap.New.With(("$term", new MapValue("RavenDB"))).AsMapValue())
                ).AsMapValue())
            ).AsMapValue()),
            ("Highlight", DictionaryMap.New.With(("Field", new MapValue("Notes"))).AsMapValue())
        );

        var provider = new MockDataIntentsProvider
        {
            Queries = [new QueryIntent(context, queryMap, "output/results", "failures/1", TestContext.Current.CancellationToken)]
        };

        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("output/results"), "Should generate successful output");
            var results = context.PutCalls["output/results"].AsMap();
            var highlights = results["Highlights"].AsMap();

            Assert.False(highlights.IsEmpty, "Highlights map should contain keys for matched documents");
            Assert.True(highlights.ContainsKey("users/1"), "Highlights should have entry for users/1");

            var userHighlights = highlights["users/1"].AsMap();
            Assert.True(userHighlights.ContainsKey("Notes"), "Should contain highlights for 'Notes' field");
        
            return Task.CompletedTask;
        });
    }

    private static async Task StoreOrder(object order, string id, IAsyncDocumentSession session)
    {
        await session.StoreAsync(order, id, TestContext.Current.CancellationToken);
        session.Advanced.GetMetadataFor(order)["@collection"] = "Orders";
    }

    [Fact]
    public async Task ShouldQueryWithStandardIncludes()
    {
        // Arrange
        using var store = GetDocumentStore();
        
        await new Orders_ByTotal().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        using (var session = store.OpenAsyncSession())
        {
            var company = new { Name = "Microsoft" };
            await session.StoreAsync(company, "companies/1", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(company)["@collection"] = "Companies";
            
            var order = new { Total = 500M, CompanyId = "companies/1" };
            await StoreOrder(order, "orders/1", session);
            
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Orders/ByTotal")),
            ("Where", DictionaryMap.New.With(
                ("Total", DictionaryMap.New.With(("$eq", new MapValue(500M))).AsMapValue())
            ).AsMapValue()),
            ("Include", DictionaryMap.New.With(
                ("CompanyId", DictionaryMap.New.AsMapValue())
            ).AsMapValue())
        );

        var provider = new MockDataIntentsProvider
        {
            Queries = [new QueryIntent(context, queryMap, "output/results", "failures/1", TestContext.Current.CancellationToken)]
        };

        // Act
        await ExecuteTest(store, provider, () =>
        {
            // Assert
            if (context.PutCalls.ContainsKey("failures/1"))
            {
                var failureReason = context.PutCalls["failures/1"].Serialize();
                Assert.Fail("Query failed with exception: " + failureReason);
            }

            Assert.True(context.PutCalls.ContainsKey("output/results"), "Should generate successful output");
            var results = context.PutCalls["output/results"].AsMap();
            
            var items = results["Items"].AsMap();
            Assert.True(items.ContainsKey("orders/1"), "Should find the document in Items");
            
            var includes = results["Includes"].AsMap();
            Assert.False(includes.IsEmpty, "Includes map should not be empty");
            Assert.True(includes.ContainsKey("companies/1"), "Includes should contain the referenced company document");
            
            var companyDoc = includes["companies/1"].AsMap();
            Assert.Equal("Microsoft", companyDoc["Name"].AsString());

            return Task.CompletedTask;
        });
    }
    
    private static async Task StoreSale(object sale, string id, IAsyncDocumentSession session)
    {
        await session.StoreAsync(sale, id, TestContext.Current.CancellationToken);
        session.Advanced.GetMetadataFor(sale)["@collection"] = "Sales";
    }

    private static async Task StorePlace(object place, string id, IAsyncDocumentSession session)
    {
        await session.StoreAsync(place, id, TestContext.Current.CancellationToken);
        session.Advanced.GetMetadataFor(place)["@collection"] = "Stores";
    }

    [Fact]
    public async Task ShouldQueryMapReduceSuccessfulScenario()
    {
        // Arrange
        using var store = GetDocumentStore();
        await new Sales_ByProduct().ExecuteAsync(store, token: TestContext.Current.CancellationToken);

        using (var session = store.OpenAsyncSession())
        {
            await StoreSale(new { ProductId = "prod/1", Amount = 100L }, "sales/1", session);
            await StoreSale(new { ProductId = "prod/1", Amount = 150L }, "sales/2", session);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Sales/ByProduct")),
            ("GroupBy", DictionaryMap.New.With(("ProductId", DictionaryMap.New.AsMapValue())).AsMapValue())
        );

        var provider = new MockDataIntentsProvider
        {
            Queries = [new QueryIntent(context, queryMap, "output/results", "failures/1", TestContext.Current.CancellationToken)]
        };

        // Act
        await ExecuteTest(store, provider, () =>
        {
            if (context.PutCalls.ContainsKey("failures/1"))
            {
                var failureReason = context.PutCalls["failures/1"].Serialize();
                Assert.Fail("Map-Reduce query failed: " + failureReason);
            }

            Assert.True(context.PutCalls.ContainsKey("output/results"), "Should generate successful output");
            var results = context.PutCalls["output/results"].AsMap();
            
            var items = results["Items"].AsMap();
            Assert.False(items.IsEmpty, "Should return aggregated items");
            
            Assert.True(items.ContainsKey("prod/1"), "Processor should concatenate GroupBy values to form ID 'prod/1'");
            
            var aggregatedRow = items["prod/1"].AsMap();
            Assert.Equal(250L, aggregatedRow["Amount"].AsLong());
            Assert.Equal(2L, aggregatedRow["Count"].AsLong());

            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task ShouldQuerySpatialWithDistanceSorting()
    {
        // Arrange
        using var store = GetDocumentStore();
        await new Stores_ByLocation().ExecuteAsync(store, token: TestContext.Current.CancellationToken);

        using (var session = store.OpenAsyncSession())
        {
            await StorePlace(new { Lat = 55.7522M, Lng = 37.6156M, Name = "Moscow Store" }, "stores/1", session);
            await StorePlace(new { Lat = 59.9342M, Lng = 30.3351M, Name = "Spb Store" }, "stores/2", session);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var context = new MockReactorContext();
        var queryMap = DictionaryMap.New.With(
            ("From", new MapValue("Stores/ByLocation")),
            ("Spatial", DictionaryMap.New.With(
                ("Field", new MapValue("Coordinates")),
                ("$within", DictionaryMap.New.With(
                    ("Circle", DictionaryMap.New.With(
                        ("Latitude", new MapValue(55.7522M)),
                        ("Longitude", new MapValue(37.6156M)),
                        ("Radius", new MapValue(1000M))
                    ).AsMapValue())
                ).AsMapValue())
            ).AsMapValue()),
            ("OrderBy", DictionaryMap.New.With(
                ("Order", DictionaryMap.New.With(("0", new MapValue("$spatialDistance"))).AsMapValue()),
                ("Data", DictionaryMap.New.With(("$spatialDistance", new MapValue("Asc"))).AsMapValue())
            ).AsMapValue())
        );

        var provider = new MockDataIntentsProvider
        {
            Queries = [new QueryIntent(context, queryMap, "output/results", "failures/1", TestContext.Current.CancellationToken)]
        };

        // Act
        await ExecuteTest(store, provider, () =>
        {
            // Assert
            if (context.PutCalls.TryGetValue("failures/1", out var value))
            {
                var failureReason = value.Serialize();
                Assert.Fail("Spatial query failed: " + failureReason);
            }

            Assert.True(context.PutCalls.ContainsKey("output/results"));
            var results = context.PutCalls["output/results"].AsMap();
            var items = results["Items"].AsMap();
            
            Assert.Equal(2, items.Keys().Count());
            return Task.CompletedTask;
        });
    }
}
