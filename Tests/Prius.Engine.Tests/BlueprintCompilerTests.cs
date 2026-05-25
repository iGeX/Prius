using Microsoft.Extensions.DependencyInjection;
using Prius.Core.Maps;
using Prius.Engine;
using Prius.Engine.Abstractions;
using Xunit;

namespace Prius.Engine.Tests;

public sealed class BlueprintCompilerTests
{
    private sealed class MockElement : IElement
    {
        public bool Put(IElementContext context, MapPath path, MapValue value) => true;
        public MapValue Get(IElementContext context, MapPath path) => new();
    }

    private readonly IServiceProvider _serviceProvider;

    public BlueprintCompilerTests()
    {
        var services = new ServiceCollection();
        services.AddTransient<MockElement>();
        _serviceProvider = services.BuildServiceProvider();
    }

    private Type? ResolveType(string name) => name == "MockElement" ? typeof(MockElement) : null;

    [Fact]
    public void Compile_SimpleElement_MountsCorrectly()
    {
        var trie = new RoutingTrie();
        var compiler = new BlueprintCompiler(_serviceProvider, ResolveType);
        var blueprint = JsonReaderMap.From("""
            {
                "Components": {},
                "Mounts": {
                    "/test": {
                        "Element": "MockElement",
                        "Env": { "K1": "V1" }
                    }
                }
            }
            """);

        compiler.Compile(trie, blueprint);

        var result = trie.Resolve("/test");
        Assert.Same(typeof(MockElement), result.Element.GetType());
        Assert.Equal("V1", result.StaticEnv?["K1"].AsString());
    }

    [Fact]
    public void Compile_RecursiveComponent_ExpandsAndMergesEnv()
    {
        var trie = new RoutingTrie();
        var compiler = new BlueprintCompiler(_serviceProvider, ResolveType);
        
        var blueprint = JsonReaderMap.From("""
            {
                "Components": {
                    "CompA": {
                        "Routes": {
                            "Sub": {
                                "Element": "MockElement",
                                "Env": { "Level": "Comp" }
                            }
                        }
                    }
                },
                "Mounts": {
                    "/api": {
                        "Component": "CompA",
                        "Env": { "Level": "Mount" }
                    }
                }
            }
            """);

        compiler.Compile(trie, blueprint);

        var result = trie.Resolve("/api/Sub");
        Assert.Same(typeof(MockElement), result.Element.GetType());
        Assert.Equal("Mount", result.StaticEnv?["Level"].AsString());
    }

    [Fact]
    public void Compile_DeepNesting_WorksCorrectly()
    {
        var trie = new RoutingTrie();
        var compiler = new BlueprintCompiler(_serviceProvider, ResolveType);

        var blueprint = JsonReaderMap.From("""
            {
                "Components": {
                    "Inner": {
                        "Routes": {
                            "Leaf": { "Element": "MockElement" }
                        }
                    },
                    "Outer": {
                        "Routes": {
                            "Mid": { "Component": "Inner" }
                        }
                    }
                },
                "Mounts": {
                    "/root": { "Component": "Outer" }
                }
            }
            """);

        compiler.Compile(trie, blueprint);

        var result = trie.Resolve("/root/Mid/Leaf");
        Assert.Same(typeof(MockElement), result.Element.GetType());
    }

    [Fact]
    public void Compile_CircularDependency_LogsErrorAndStops()
    {
        var trie = new RoutingTrie();
        var compiler = new BlueprintCompiler(_serviceProvider, ResolveType);

        var blueprint = JsonReaderMap.From("""
            {
                "Components": {
                    "A": { "Routes": { "toB": { "Component": "B" } } },
                    "B": { "Routes": { "toA": { "Component": "A" } } }
                },
                "Mounts": {
                    "/test": { "Component": "A" }
                }
            }
            """);

        compiler.Compile(trie, blueprint);
        
        var result = trie.Resolve("/test/toB/toA/toB");
        Assert.Same(EmptyElement.Instance, result.Element);
    }
}