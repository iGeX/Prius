using System;
using Prius.Core.Maps;
using Prius.Engine;

class Program
{
    static void Main()
    {
        var trie = new RoutingTrie();
        trie.AddRoute("gate/**", new GateElement());
        var bus = new VirtualBus(trie);

        var stateMap = DictionaryMap.New;
        stateMap["@state"] = new MapValue(JsonReaderMap.From("{ \"Existing\": true }"));
        bus["gate"] = new MapValue(stateMap);

        Console.WriteLine($"[1] Put gate/data: {bus.Put("gate/data", "from_map_state")}");
        
        Console.WriteLine($"[2] Put gate 0: {bus.Put("gate", 0)}");

        Console.WriteLine($"[3] Get gate/@state directly: {bus.Get("gate/@state").AsLong()}");
        Console.WriteLine($"[4] Put gate/data should fail: {bus.Put("gate/data", "should_fail")}");
    }
}