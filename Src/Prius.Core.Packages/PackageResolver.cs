using NuGet.Versioning;
using Prius.Core.Maps;

namespace Prius.Core.Packages;

public sealed class PackageResolver(IPackageRepository repository)
{
    /// <summary>
    /// Resolves a full dependency graph starting from the root package.
    /// Returns a map containing resolved versions and a deterministic loading order.
    /// </summary>
    /// <param name="rootPackage">The identifier of the starting package.</param>
    /// <param name="rootVersion">The version or version range of the starting package.</param>
    /// <param name="tfm">The Target Framework Moniker (e.g., "net8.0") to resolve compatible paths.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A read-only map with the following structure:
    /// - /Versions: { "PackageName": "Version" }
    /// - /Order: { "0": "PackageName_Root", "1": "PackageName_Level1", ... }
    /// </returns>
    public async ValueTask<IMap> ResolveAsync(string rootPackage, string rootVersion, string tfm, CancellationToken ct = default)
    {
        var resolvedVersions = DictionaryMap.New;
        var orderMap = DictionaryMap.New;
        var pendingRequests = DictionaryMap.New;
        
        var orderIndex = 0;
        pendingRequests.Put(rootPackage, rootVersion);

        while (!pendingRequests.IsEmpty)
        {
            var versionRequest = DictionaryMap.New;
            foreach (var package in pendingRequests.Keys())
                versionRequest.Put(package, true);

            var versionsMap = await repository.GetVersionsAsync(tfm, versionRequest, ct);
            
            var manifestRequest = DictionaryMap.New;
            
            foreach (var package in pendingRequests.Keys())
            {
                var range = pendingRequests.Get(package).AsValue<string>();
                var available = versionsMap.Get(package).AsMap();
                
                var best = SelectBestVersion(available, range);
                if (string.IsNullOrEmpty(best))
                    throw new InvalidOperationException($"Could not resolve version for {package} within range {range}");

                manifestRequest.Put(package, best);
                
                if (resolvedVersions.Get(package).IsEmpty)
                {
                    resolvedVersions.Put(package, best);
                    orderMap.Put(orderIndex++.ToString(), package);
                }
            }

            var manifests = await repository.GetManifestsAsync(tfm, manifestRequest, ct);
            pendingRequests = DictionaryMap.New; 
            
            foreach (var package in manifests.Keys())
            {
                var manifest = manifests.Get(package).AsMap();
                var dependencies = manifest.DeepGet("Dependencies/" + tfm).AsMap();
                
                foreach (var depPackage in dependencies.Keys())
                {
                    var depInfo = dependencies.Get(depPackage).AsMap();
                    
                    if (depInfo.Get("PrivateAssets").AsValue<string>().Equals("All", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (resolvedVersions.Get(depPackage).IsEmpty)
                        pendingRequests.Put(depPackage, depInfo.Get("Version").AsValue<string>());
                }
            }
        }

        var result = DictionaryMap.New;
        result.Put("Versions", resolvedVersions);
        result.Put("Order", orderMap);

        return result.AsReadOnly();
    }

    private static string SelectBestVersion(IMap availableVersions, string range)
    {
        if (availableVersions.IsEmpty || !VersionRange.TryParse(range, out var versionRange)) 
            return string.Empty;

        NuGetVersion? bestVersion = null;
        foreach (var versionStr in availableVersions.Keys())
        {
            if (!NuGetVersion.TryParse(versionStr, out var current) || !versionRange.Satisfies(current)) 
                continue;
            
            if (bestVersion == null || current > bestVersion)
                bestVersion = current;
        }

        return bestVersion?.ToNormalizedString() ?? string.Empty;
    }
}
