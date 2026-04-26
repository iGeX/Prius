using Microsoft.Extensions.Caching.Memory;
using NuGet.Versioning;
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
                new($"{baseUrl}/query", "SearchQueryService", "Search", NugetClientVersion),
                new($"{baseUrl}/query", "SearchQueryService/3.0.0", "Search", NugetClientVersion),
                new($"{baseUrl}/query", "SearchQueryService/3.4.0", "Search", NugetClientVersion),
                new($"{baseUrl}/autocomplete", "SearchAutocompleteService", "Autocomplete", NugetClientVersion),
                new($"{baseUrl}/metadata/", "RegistrationsBaseUrl", "Metadata", NugetClientVersion),
                new($"{baseUrl}/metadata/", "RegistrationsBaseUrl/3.0.0-rc", "Metadata", NugetClientVersion),
                new($"{baseUrl}/metadata/", "RegistrationsBaseUrl/3.4.0", "Metadata", NugetClientVersion),
                new($"{baseUrl}/metadata/", "RegistrationsBaseUrl/3.6.0", "Metadata", NugetClientVersion),
                new($"{baseUrl}/content/", "PackageBaseAddress/3.0.0", "Download", NugetClientVersion)
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

            var searchResults = new List<SearchResultDto>();

            foreach (var id in paged)
            {
                var versionsMap = await repo.GetVersionsAsync("any", DictionaryMap.From((id, true)), ct);
                var sortedVersions = versionsMap.Get(id).AsMap().Keys()
                    .Select(NuGetVersion.Parse)
                    .OrderBy(v => v)
                    .ToList();

                var latestVersion = sortedVersions.LastOrDefault()?.ToNormalizedString() ?? "0.0.0";
                
                var manifests = await repo.GetManifestsAsync("any", DictionaryMap.From((id, latestVersion)), ct);
                var manifest = manifests.Get(id).AsMap();

                searchResults.Add(new SearchResultDto(
                    RegistrationId: $"{baseUrl}/metadata/{id.ToLowerInvariant()}/index.json",
                    Type: "Package",
                    Id: id,
                    Version: latestVersion,
                    Description: manifest.DeepGet("Info/description").AsValue<string>(),
                    Authors: manifest.DeepGet("Info/authors").AsValue<string>(),
                    ProjectUrl: manifest.DeepGet("Info/projectUrl").AsValue<string>(),
                    RegistrationUrl: $"{baseUrl}/metadata/{id.ToLowerInvariant()}/index.json",
                    Versions: sortedVersions.Select(v => new SearchResultVersionDto(
                        $"{baseUrl}/metadata/{id.ToLowerInvariant()}/{v.ToNormalizedString()}.json",
                        v.ToNormalizedString()
                    )).ToList()
                ));
            }

            return Results.Json(new SearchResponseDto(filtered.Count, searchResults), RegistryJsonContext.Default.SearchResponseDto);
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
