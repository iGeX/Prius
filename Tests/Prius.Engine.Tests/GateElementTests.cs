using Prius.Core.Maps;
using Prius.Engine;
using Prius.Engine.Abstractions;
using Xunit;

namespace Prius.Engine.Tests;

public sealed class GateElementTests
{
    private VirtualBus CreateBus(GateElement gate, string path)
    {
        var trie = new RoutingTrie();
        trie.AddRoute(path, gate);
        return new VirtualBus(trie);
    }

    [Fact]
    public void Gate_BaseState_WorksCorrectly()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate/**");

        // Default should be false
        Assert.False(bus.Get("gate").AsBool());

        // Set to true
        bus.Put("gate", true);
        Assert.True(bus.Get("gate").AsBool());

        // Set back to false
        bus.Put("gate", false);
        Assert.False(bus.Get("gate").AsBool());
    }

    [Fact]
    public void Gate_FilterSubPaths_WhenClosed()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate/**");

        // Closed by default
        bool putResult = bus.Put("gate/data", "hidden");
        Assert.False(putResult);
        Assert.True(bus.Get("gate/data").IsEmpty);
    }

    [Fact]
    public void Gate_AllowSubPaths_WhenOpen()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate/**");

        bus.Put("gate", true);
        
        bool putResult = bus.Put("gate/data", "visible");
        Assert.True(putResult);
        Assert.Equal("visible", bus.Get("gate/data").AsString());
    }

    [Fact]
    public void Gate_UnfoldMap_WhenOpen()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate/**");

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
        var bus = CreateBus(gate, "gate/**");

        // 1. Open with 1
        bus.Put("gate", 1);
        Assert.True(bus.Put("gate/data", "val1"));

        // 2. Close with 0
        bus.Put("gate", 0);
        Assert.False(bus.Put("gate/data", "val2"));
        Assert.True(bus.Get("gate/data").IsEmpty); // Gate hides data when closed

        // 3. Re-open and check old value
        bus.Put("gate", 1);
        Assert.Equal("val1", bus.Get("gate/data").AsString()); // Old value preserved

        // 4. Open with non-zero decimal
        bus.Put("gate", 0.1m);
        Assert.True(bus.Put("gate/data", "val3"));
        Assert.Equal("val3", bus.Get("gate/data").AsString());

        // 5. Reset with Empty
        bus.Put("gate", Empty.Instance);
        Assert.False(bus.Put("gate/data", "val4"));
    }

    [Fact]
    public void Gate_TruthyMapState_Works()
    {
        var gate = new GateElement();
        var bus = CreateBus(gate, "gate/**");

        // Directly set a map to @Active (bypassing GateElement.Put unfolding logic to set STATE)
        var stateMap = DictionaryMap.New;
        stateMap["@Active"] = new MapValue(JsonReaderMap.From("{ \"Existing\": true }"));
        bus["gate"] = new MapValue(stateMap);

        // Gate should be open because Map is not empty
        Assert.True(bus.Put("gate/data", "from_map_state"));
        Assert.Equal("from_map_state", bus.Get("gate/data").AsString());

        // Now close it with 0 via GateElement.Put
        bus.Put("gate", 0);
        Assert.False(bus.Put("gate/data", "should_fail"));
    }
}