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

    [Fact]
    public void Compile_DeepEnvMerging_WorksCorrectly()
    {
        var trie = new RoutingTrie();
        var compiler = new BlueprintCompiler(_serviceProvider, ResolveType);
        
        // Define 5 levels of components, each overriding or adding to Env
        var blueprint = JsonReaderMap.From("""
            {
                "Components": {
                    "L5": { "Routes": { "Leaf": { "Element": "MockElement", "Env": { "Base": "L5", "Deep": "L5" } } } },
                    "L4": { "Routes": { "Sub": { "Component": "L5", "Env": { "Deep": "L4", "V4": "4" } } } },
                    "L3": { "Routes": { "Sub": { "Component": "L4", "Env": { "V3": "3" } } } },
                    "L2": { "Routes": { "Sub": { "Component": "L3", "Env": { "Deep": "L2" } } } }
                },
                "Mounts": {
                    "/root": { "Component": "L2", "Env": { "Root": "True", "Deep": "Root" } }
                }
            }
            """);

        compiler.Compile(trie, blueprint);

        var result = trie.Resolve("/root/Sub/Sub/Sub/Leaf");
        Assert.Same(typeof(MockElement), result.Element.GetType());
        
        // Assertions
        Assert.Equal("L5", result.StaticEnv?["Base"].AsString());  // Preserved from bottom
        Assert.Equal("4", result.StaticEnv?["V4"].AsString());     // Preserved from L4
        Assert.Equal("3", result.StaticEnv?["V3"].AsString());     // Preserved from L3
        Assert.Equal("True", result.StaticEnv?["Root"].AsString());// From Mount
        Assert.Equal("Root", result.StaticEnv?["Deep"].AsString());// Mount overrides EVERYTHING below
    }

    [Fact]
    public void Compile_MissingComponent_HandlesGracefully()
    {
        var trie = new RoutingTrie();
        var compiler = new BlueprintCompiler(_serviceProvider, ResolveType);
        
        var blueprint = JsonReaderMap.From("""
            {
                "Components": {},
                "Mounts": {
                    "/valid": { "Element": "MockElement" },
                    "/invalid": { "Component": "NonExistent" }
                }
            }
            """);

        // Should not throw
        compiler.Compile(trie, blueprint);

        Assert.Same(typeof(MockElement), trie.Resolve("/valid").Element.GetType());
        Assert.Same(EmptyElement.Instance, trie.Resolve("/invalid").Element);
    }

    [Fact]
    public void Compile_MissingElementType_HandlesGracefully()
    {
        var trie = new RoutingTrie();
        var compiler = new BlueprintCompiler(_serviceProvider, ResolveType);
        
        var blueprint = JsonReaderMap.From("""
            {
                "Components": {},
                "Mounts": {
                    "/valid": { "Element": "MockElement" },
                    "/invalid": { "Element": "UnknownType" }
                }
            }
            """);

        // Should not throw
        compiler.Compile(trie, blueprint);

        Assert.Same(typeof(MockElement), trie.Resolve("/valid").Element.GetType());
        Assert.Same(EmptyElement.Instance, trie.Resolve("/invalid").Element);
    }
}