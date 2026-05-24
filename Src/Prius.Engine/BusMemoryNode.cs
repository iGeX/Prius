namespace Prius.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using Prius.Core.Maps;

internal sealed class BusMemoryNode : IMap
{
    public MapValue Value { get; set; } = Empty.Instance;
    public Dictionary<string, BusMemoryNode>? Children { get; set; }

    public bool IsEmpty => (Children == null || Children.Count == 0) && Value.IsEmpty;
    
    public bool CanWrite => true;

    public IEnumerable<string> Keys(bool? ascending = null)
    {
        if (Children == null) return Array.Empty<string>();
        var keys = Children.Keys;
        if (ascending == true) return keys.OrderBy(k => k);
        if (ascending == false) return keys.OrderByDescending(k => k);
        return keys;
    }

    public bool ContainsKey(string key) => Children != null && Children.ContainsKey(key);

    public MapValue this[string key]
    {
        get
        {
            if (Children != null && Children.TryGetValue(key, out var node))
            {
                if (node.Children is { Count: > 0 }) return new MapValue(node);
                return node.Value.IsEmpty && node.Children != null ? new MapValue(node) : node.Value;
            }
            return Empty.Instance;
        }
        set
        {
            Children ??= new Dictionary<string, BusMemoryNode>(StringComparer.Ordinal);
            if (!Children.TryGetValue(key, out var node))
            {
                node = new BusMemoryNode();
                Children[key] = node;
            }
            
            if (value.IsMap)
            {
                var map = value.AsMap();
                node.Children = new Dictionary<string, BusMemoryNode>(StringComparer.Ordinal);
                node.Value = Empty.Instance;
                foreach (var k in map.Keys()) node[k] = map[k];
            }
            else
                node.Value = value;
        }
    }
}
