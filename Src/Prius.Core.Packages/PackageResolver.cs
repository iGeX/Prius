using Prius.Core.Maps;

namespace Prius.Core.Packages;

public sealed class PackageResolver(IPackageRepository repository)
{
    /// <summary>
    /// Resolves the dependency graph and builds a self-contained deployment snapshot.
    /// Uses TFM-chaining to find the most compatible dependencies and ensures a deterministic loading order.
    /// </summary>
    /// <param name="tfm">The target framework (e.g., "net10.0") used to filter dependencies and assets.</param>
    /// <param name="targets">
    /// A map of root package IDs to their requested versions.
    /// Layout: { "Prius.Web": "1.0.0", "Prius.Data": "2.1.0" }
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A snapshot map containing everything needed for the bootstrap process:
    /// {
    ///   "Order": { "0": "Newtonsoft.Json", "1": "Prius.Web" },
    ///   "Manifests": {
    ///     "Newtonsoft.Json": { "Info": {...}, "Assets": {...}, "Dependencies": {...} },
    ///     "Prius.Web": { ... }
    ///   }
    /// }
    /// </returns>
    public async ValueTask<IMap> Resolve(string tfm, IMap targets, CancellationToken ct = default)
    {
        var resolvedManifests = DictionaryMap.New;
        var queue = targets;

        while (!queue.IsEmpty)
        {
            ct.ThrowIfCancellationRequested();
            var currentManifests = await repository.GetManifests(tfm, queue, ct);
            var nextQueue = DictionaryMap.New;

            foreach (var id in queue.Keys())
            {
                var manifestValue = currentManifests.Get(id);
                if (manifestValue.IsEmpty) continue;

                var manifest = manifestValue.AsMap();
                var version = queue.Get(id).AsString();
                
                var existing = resolvedManifests.Get(id).AsMap();
                if (!existing.IsEmpty)
                {
                    var existingVer = existing.DeepGet("Info/version").AsString();
                    if (string.CompareOrdinal(existingVer, version) >= 0) continue;
                }

                resolvedManifests.Put(id, manifest);
                
                var deps = GetCompatibleDeps(manifest, tfm);
                foreach (var depId in deps.Keys())
                    nextQueue.Put(depId, deps.Get(depId).AsMap().Get("version").AsString());
            }
            queue = nextQueue;
        }
        
        var order = DictionaryMap.New;
        var visited = DictionaryMap.New;
        
        foreach (var id in resolvedManifests.Keys())
            Sort(id, tfm, resolvedManifests, order, visited);

        return DictionaryMap.New
            .With("Order", order)
            .With("Manifests", resolvedManifests);
    }

    private static void Sort(string id, string tfm, IMap manifests, IMap order, IMap visited)
    {
        if (visited.Get(id).AsBool()) return;
        visited.Put(id, true);

        var manifest = manifests.Get(id).AsMap();
        var deps = GetCompatibleDeps(manifest, tfm);

        foreach (var depId in deps.Keys())
        {
            if (!manifests.Get(depId).IsEmpty)
                Sort(depId, tfm, manifests, order, visited);
        }

        order.Put(order.Values.Count().ToIndexString(), id);
    }

    private static IMap GetCompatibleDeps(IMap manifest, string tfm)
    {
        var allDeps = manifest.Get("Dependencies").AsMap();
        foreach (var compatibleTfm in FrameworkConstants.GetCompatible(tfm))
        {
            var branch = allDeps.Get(compatibleTfm).AsMap();
            if (!branch.IsEmpty) 
                return branch;
        }
        return EmptyMap.Instance;
    }
}
