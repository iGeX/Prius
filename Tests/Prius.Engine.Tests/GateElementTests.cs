using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Xunit;

namespace Prius.Engine.Tests;

public sealed class GateElementTests
{
    private IElementContext CreateBus(GateElement gate, string path)
    {
        var trie = new RoutingTrie();
        trie.AddRoute(path, gate);
        return new VirtualBus(trie);
    }

    [Fact]
    public void Gate_BaseState_WorksCorrectly()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate");
        
        Assert.False(bus.Get("gate").AsBool());
        
        bus.Put("gate", true);
        Assert.True(bus.Get("gate").AsBool());

        bus.Put("gate", false);
        Assert.False(bus.Get("gate").AsBool());
    }

    [Fact]
    public void Gate_FilterSubPaths_WhenClosed()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate");
        
        var putResult = bus.Put("gate/data", "hidden");
        Assert.False(putResult);
        Assert.True(bus.Get("gate/data").IsEmpty);
    }

    [Fact]
    public void Gate_AllowSubPaths_WhenOpen()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate");

        bus.Put("gate", true);
        
        var putResult = bus.Put("gate/data", "visible");
        Assert.False(putResult);
        Assert.Equal("visible", bus.Get("gate/data").AsString());
    }

    [Fact]
    public void Gate_UnfoldMap_WhenOpen()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate");

        bus.Put("gate", true);

        var data = JsonReaderMap.From("""
            {
                "A": 1,
                "B": { "Sub": 2 }
            }
            """);

        bus.Put("gate", new MapValue(data));

        Assert.Equal(1L, bus.Get("gate/A").AsLong());
        Assert.Equal(2L, bus.Get("gate/B/Sub").AsLong());
    }

    [Fact]
    public void Gate_StateTransitions_WorkCorrectly()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate");

        bus.Put("gate", 1);
        Assert.False(bus.Put("gate/data", "val1"));
        
        bus.Put("gate", 0);
        Assert.False(bus.Put("gate/data", "val2"));
        Assert.True(bus.Get("gate/data").IsEmpty);
        
        bus.Put("gate", 1);
        Assert.Equal("val1", bus.Get("gate/data").AsString());
        
        bus.Put("gate", 0.1m);
        Assert.True(bus.Put("gate/data", "val3"));
        Assert.Equal("val3", bus.Get("gate/data").AsString());
        
        bus.Put("gate", Empty.Instance);
        Assert.False(bus.Put("gate/data", "val4"));
    }

    [Fact]
    public void Gate_TruthyMapState_Works()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate");
        
        var stateMap = DictionaryMap.New;
        stateMap["$Active"] = new MapValue(JsonReaderMap.From("{ \"Existing\": true }"));
        bus["gate"] = new MapValue(stateMap);
        
        Assert.True(bus.Put("gate/data", "from_map_state"));
        Assert.Equal("from_map_state", bus.Get("gate/data").AsString());
        
        bus.Put("gate", 0);
        Assert.False(bus.Put("gate/data", "should_fail"));
    }
}
