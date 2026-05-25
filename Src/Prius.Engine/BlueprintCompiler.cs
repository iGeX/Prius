using Microsoft.Extensions.DependencyInjection;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class BlueprintCompiler(IServiceProvider serviceProvider, Func<string, Type?> typeResolver)
{
    public void Compile(RoutingTrie trie, IMap blueprint)
    {
        var components = blueprint["Components"].AsMap();
        var mounts = blueprint["Mounts"].AsMap();

        foreach (var path in mounts.Keys())
            Expand(trie, components, path, mounts[path].AsMap(), DictionaryMap.New, []);
    }

    private void Expand(RoutingTrie trie, IMap components, string path, IMap node, IMap parentEnv, HashSet<string> stack)
    {
        var currentEnv = MergeEnv(parentEnv, node["Env"].AsMap());

        if (node.ContainsKey("Element"))
        {
            var typeName = node["Element"].AsString();
            var type = typeResolver(typeName);
            
            if (type == null || !typeof(IElement).IsAssignableFrom(type))
            {
                Console.WriteLine($"[WARNING] Element type '{typeName}' not found or invalid for route '{path}'");
                return;
            }

            trie.AddRoute(path, (IElement)ActivatorUtilities.CreateInstance(serviceProvider, type), currentEnv);
            return;
        }

        if (node.ContainsKey("Component"))
        {
            var componentName = node["Component"].AsString();

            if (stack.Contains(componentName))
            {
                Console.WriteLine($"[ERROR] Circular component dependency detected: {string.Join(" -> ", stack)} -> {componentName}");
                return;
            }

            var component = components[componentName].AsMap();
            if (component.IsEmpty)
            {
                Console.WriteLine($"[WARNING] Component '{componentName}' not found.");
                return;
            }

            var nextStack = new HashSet<string>(stack, StringComparer.Ordinal) { componentName };
            var routes = component["Routes"].AsMap();
            foreach (var subPath in routes.Keys())
                Expand(trie, components, CombinePaths(path, subPath), routes[subPath].AsMap(), currentEnv, nextStack);
        }
    }

    private static IMap MergeEnv(IMap parent, IMap local)
    {
        if (local.IsEmpty) 
            return parent;
        if (parent.IsEmpty) 
            return local;

        // Higher-level (parent/mount) values should override lower-level (local/component) values.
        return DictionaryMap.New.With(local).With(parent);
    }

    private static string CombinePaths(string baseBatch, string subPath)
    {
        if (string.IsNullOrEmpty(baseBatch) || baseBatch == "/") 
            return subPath.StartsWith('/') ? subPath : "/" + subPath;
        
        var normalizedBase = baseBatch.TrimEnd('/');
        var normalizedSub = subPath.TrimStart('/');
        
        return $"{normalizedBase}/{normalizedSub}";
    }
}