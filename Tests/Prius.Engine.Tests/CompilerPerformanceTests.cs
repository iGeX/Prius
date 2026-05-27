using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Prius.Core.Maps;
using Prius.Engine;
using Prius.Engine.Abstractions;
using Xunit;

namespace Prius.Engine.Tests;

public sealed class CompilerPerformanceTests
{
    private sealed class MockElement : IElement
    {
        public bool Put(IElementContext context, MapPath path, MapValue value) => true;
        public MapValue Get(IElementContext context, MapPath path) => new();
    }

    private Type? ResolveType(string name) => typeof(MockElement);

    [Fact]
    public void Compile_LargeBlueprint_IsFast()
    {
        var services = new ServiceCollection();
        services.AddTransient<MockElement>();
        var serviceProvider = services.BuildServiceProvider();

        var componentsCount = 100;
        var routesPerComponent = 10;
        var mountsCount = 1000;

        var componentsMap = DictionaryMap.New;
        for (int i = 0; i < componentsCount; i++)
        {
            var routes = DictionaryMap.New;
            for (int j = 0; j < routesPerComponent; j++)
            {
                routes.With($"route_{j}", DictionaryMap.New
                    .With("Element", "MockElement")
                    .With("Env", DictionaryMap.New.With("K", $"V_{i}_{j}")));
            }
            componentsMap.With($"Comp_{i}", DictionaryMap.New.With("Routes", routes));
        }

        var mountsMap = DictionaryMap.New;
        for (int i = 0; i < mountsCount; i++)
        {
            mountsMap.With($"/mount/point_{i}", DictionaryMap.New
                .With("Component", $"Comp_{i % componentsCount}")
                .With("Env", DictionaryMap.New.With("Instance", i.ToString())));
        }

        var blueprint = DictionaryMap.New
            .With("Components", componentsMap)
            .With("Mounts", mountsMap);

        var trie = new RoutingTrie();
        var compiler = new BlueprintCompiler(serviceProvider, ResolveType);

        var sw = Stopwatch.StartNew();
        compiler.Compile(trie, blueprint);
        sw.Stop();

        Assert.True(sw.ElapsedMilliseconds < 1000, $"Compilation took too long: {sw.ElapsedMilliseconds}ms");
        
        var result = trie.Resolve("/mount/point_500/route_5");
        Assert.Same(typeof(MockElement), result.Element.GetType());
        Assert.Equal("500", result.StaticEnv?["Instance"].AsString());
    }
}
