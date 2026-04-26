using Microsoft.Extensions.Caching.Memory;
using Prius.Core.Maps;

namespace Prius.Core.Packages.Registry;

public static class PackagesRegistryExtensions
{
    private const string NugetClientVersion = "7.3.1";
    
    public static IServiceCollection AddPackagesRegistry(this IServiceCollection services, Action<MemoryCacheOptions>? cacheSetup = null) => 
        services.AddMemoryCache(cacheSetup ?? (options => options.SizeLimit = 512 * 1024 * 1024));

    public static IEndpointRouteBuilder UsePackagesRegistry(this IEndpointRouteBuilder endpoints, string prefix = "/nuget")
    {
        var routePrefix = prefix.Trim('/');

        endpoints.MapGet($"{routePrefix}/index.json", (HttpContext context) =>
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}/{routePrefix}";
            var response = new ServiceIndexDto("3.0.0", [
                new ServiceResourceDto($"{baseUrl}/query", "SearchQueryService/3.4.0", "Search", NugetClientVersion),
                new ServiceResourceDto($"{baseUrl}/autocomplete", "SearchAutocompleteService/3.0.0", "Autocomplete", NugetClientVersion),
                new ServiceResourceDto($"{baseUrl}/metadata", "RegistrationsBaseUrl/3.6.0", "Metadata", NugetClientVersion),
                new ServiceResourceDto($"{baseUrl}/content", "PackageBaseAddress/3.0.0", "Download", NugetClientVersion)
            ]);

            return Results.Json(response, RegistryJsonContext.Default.ServiceIndexDto);
        });

        endpoints.MapGet($"{routePrefix}/content/{{id}}/{{version}}/{{file}}.nupkg", 
            async (string id, string version, IPackageRepository repo, IMemoryCache cache, ILogger<IPackageRepository> logger, CancellationToken ct) =>
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Nupkg download requested: {Id} {Version}", id, version);

            var cacheKey = $"nupkg_{id}_{version}".ToLowerInvariant();
            if (cache.TryGetValue(cacheKey, out byte[]? cachedBytes))
                return Results.File(cachedBytes!, "application/octet-stream", $"{id}.{version}.nupkg");

            var manifests = await repo.GetManifestsAsync("any", DictionaryMap.From((id, version)), ct);
            var manifest = manifests.Get(id).AsMap();
            
            if (manifest.IsEmpty)
                return Results.NotFound();

            using var ms = new MemoryStream();
            await PackageExporter.ExportAsync(manifest, repo, ms, ct);
            
            var bytes = ms.ToArray();
            cache.Set(cacheKey, bytes, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1), Size = bytes.Length });

            return Results.File(bytes, "application/octet-stream", $"{id}.{version}.nupkg");
        });

        endpoints.MapGet($"{routePrefix}/metadata/{{id}}/index.json", 
            async (string id, IPackageRepository repo, ILogger<IPackageRepository> logger, HttpContext context, CancellationToken ct) =>
        {
            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Metadata requested for: {Id}", id);

            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}/{routePrefix}";
            var versionsMap = await repo.GetVersionsAsync("any", DictionaryMap.From((id, true)), ct);
            var versions = versionsMap.Get(id).AsMap();
            
            if (versions.IsEmpty)
                return Results.NotFound();

            var leafs = new List<RegistrationLeafDto>();
            foreach (var v in versions.Keys())
            {
                var manifests = await repo.GetManifestsAsync("any", DictionaryMap.From((id, v)), ct);
                var manifest = manifests.Get(id).AsMap();
                
                var dependencyGroups = manifest.Get("Dependencies").AsMap().Keys().Select(tfm => 
                {
                    var tfmDeps = manifest.DeepGet("Dependencies/" + tfm).AsMap();
                    return new DependencyGroupDto(tfm, tfmDeps.Keys().Select(depId => 
                        new DependencyDto(depId, tfmDeps.Get(depId).AsMap().Get("version").AsValue<string>())
                    ).ToList());
                }).ToList();

                leafs.Add(new RegistrationLeafDto(
                    $"{baseUrl}/metadata/{id}/{v}.json",
                    $"{baseUrl}/content/{id}/{v}/{id}.{v}.nupkg",
                    new CatalogEntryDto(
                        $"{baseUrl}/metadata/{id}/{v}.json", 
                        id, v, 
                        manifest.DeepGet("Info/authors").AsValue<string>(), 
                        manifest.DeepGet("Info/description").AsValue<string>(), 
                        dependencyGroups)
                ));
            }

            var root = new RegistrationRootDto(leafs.Count, [
                new RegistrationPageDto($"{baseUrl}/metadata/{id}/index.json#page1", leafs.Count, leafs, versions.Keys().First(), versions.Keys().Last())
            ]);

            return Results.Json(root, RegistryJsonContext.Default.RegistrationRootDto);
        });

        endpoints.MapGet($"{routePrefix}/query", async (string? q, int? skip, int? take, IPackageRepository repo, HttpContext context, CancellationToken ct) =>
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}/{routePrefix}";
            var allPackagesMap = await repo.GetPackagesAsync(ct);
            
            var filtered = allPackagesMap.Keys(true)
                .Where(p => string.IsNullOrEmpty(q) || p.Contains(q, StringComparison.OrdinalIgnoreCase))
                .ToList();

            IEnumerable<string> paged = filtered;
            if (skip != null)
                paged = paged.Skip(skip.Value);
            paged = paged.Take(take ?? 20);

            var page = paged.ToList();
            var data = new List<object>();

            foreach (var id in page)
            {
                var versionsMap = await repo.GetVersionsAsync("any", DictionaryMap.From((id, true)), ct);
                var versions = versionsMap.Get(id).AsMap();
                var latestVersion = versions.Keys().LastOrDefault() ?? "0.0.0";
                
                var manifests = await repo.GetManifestsAsync("any", DictionaryMap.From((id, latestVersion)), ct);
                var manifest = manifests.Get(id).AsMap();

                data.Add(new
                {
                    id = $"{baseUrl}/metadata/{id}/index.json",
                    type = "Package",
                    version = latestVersion,
                    description = manifest.DeepGet("Info/description").AsValue<string>(),
                    authors = manifest.DeepGet("Info/authors").AsValue<string>(),
                    verified = true,
                    versions = versions.Keys().Select(v => new { 
                        version = v, 
                        id = $"{baseUrl}/metadata/{id}/{v}.json",
                        downloads = 0 
                    }).ToList()
                });
            }

            return Results.Ok(new { totalHits = filtered.Count, data });
        });
        
        endpoints.MapGet($"{routePrefix}/autocomplete", (string? q, int skip, int take) =>
        {
            try
            {
                return Task.FromResult(Results.Redirect($"{routePrefix}/query?q={q}&skip={skip}&take={take}"));
            }
            catch (Exception exception)
            {
                return Task.FromException<IResult>(exception);
            }
        });

        return endpoints;
    }
}
