using Prius.Core.Maps;

namespace Prius.Core.Packages;

/// <summary>
/// Resolves a dependency graph and builds a flat loading order.
/// </summary>
public sealed class PackageResolver(IPackageRepository repository)
{
    /// <summary>
    /// Resolves dependencies for a set of target packages and returns a flat order map.
    /// </summary>
    /// <param name="tfm">The target framework (e.g., "net10.0").</param>
    /// <param name="targets">
    /// A map of package IDs to versions. 
    /// Layout: { "Package.Id": "1.0.0" }
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A flat order map for sequential loading.
    /// Layout: 
    /// { 
    ///   "0": { "Id": "Dep.A", "Version": "1.0.0", "Dependencies": { ... } },
    ///   "1": { "Id": "Root.B", "Version": "1.1.0", "Dependencies": { ... } } 
    /// }
    /// </returns>
    public async ValueTask<IMap> Resolve(string tfm, IMap targets, CancellationToken ct = default)
    {
        IMap resolved = DictionaryMap.New;
        var queue = targets;

        while (!queue.IsEmpty)
        {
            ct.ThrowIfCancellationRequested();

            var manifests = await repository.GetManifests(tfm, queue, ct);
            var nextQueue = DictionaryMap.New;

            foreach (var id in queue.Keys())
            {
                var manifestValue = manifests.Get(id);
                if (manifestValue.IsEmpty) 
                    continue;

                var manifest = manifestValue.AsMap();
                var version = queue.Get(id).AsString();

                var existing = resolved.Get(id).AsMap();
                if (!existing.IsEmpty && string.CompareOrdinal(existing.Get("Version").AsString(), version) >= 0)
                    continue;

                var packageDeps = manifest.Get("Dependencies").AsMap();
                resolved.Put(id, DictionaryMap.New
                    .With("Version", version)
                    .With("Dependencies", packageDeps));

                var tfmDeps = packageDeps.Get(tfm).AsMap();
                if (tfmDeps.IsEmpty && string.CompareOrdinal(tfm, FrameworkConstants.Any) != 0)
                    tfmDeps = packageDeps.Get(FrameworkConstants.Any).AsMap();

                foreach (var depId in tfmDeps.Keys())
                    nextQueue.Put(depId, tfmDeps.Get(depId).AsString());
            }

            queue = nextQueue;
        }

        return BuildOrderMap(tfm, resolved);
    }

    private IMap BuildOrderMap(string tfm, IMap resolved)
    {
        IMap order = DictionaryMap.New;
        IMap visited = DictionaryMap.New;
        var index = 0;

        foreach (var id in resolved.Keys())
            (order, visited, index) = Sort(id, tfm, resolved, order, visited, index);

        return order;
    }

    private (IMap order, IMap visited, int index) Sort(string id, string tfm, IMap resolved, IMap order, IMap visited, int index)
    {
        if (visited.Get(id).AsBool()) 
            return (order, visited, index);

        var data = resolved.Get(id).AsMap();
        var packageDeps = data.Get("Dependencies").AsMap();
        
        var deps = packageDeps.Get(tfm).AsMap();
        if (deps.IsEmpty && string.CompareOrdinal(tfm, FrameworkConstants.Any) != 0)
            deps = packageDeps.Get(FrameworkConstants.Any).AsMap();

        visited.Put(id, true);

        foreach (var depId in deps.Keys())
            (order, visited, index) = Sort(depId, tfm, resolved, order, visited, index);

        order.Put(index.ToIndexString(), data.With("Id", id));
        return (order, visited, index + 1);
    }
}
