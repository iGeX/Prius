using Microsoft.Extensions.Caching.Memory;
using Prius.Core.Maps;

namespace Prius.Core.Packages.Registry;

public static class PackagesRegistryExtensions
{
    public static IServiceCollection AddPackagesRegistry(this IServiceCollection services, Action<MemoryCacheOptions>? cacheSetup = null) => 
        services.AddMemoryCache(cacheSetup ?? (options => options.SizeLimit = 512 * 1024 * 1024));

    public static IEndpointRouteBuilder UsePackagesRegistry(this IEndpointRouteBuilder endpoints, string prefix = "/nuget")
    {
        var routePrefix = prefix.Trim('/');

        // 1. Service Index
        endpoints.MapGet($"{routePrefix}/index.json", (HttpContext context) =>
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}/{routePrefix}";
            var index = new ServiceIndexDto("3.0.0", [
                new($"{baseUrl}/query", "SearchQueryService", "Search"),
                new($"{baseUrl}/metadata", "RegistrationsBaseUrl", "Metadata"),
                new($"{baseUrl}/content", "PackageBaseAddress/3.0.0", "Download")
            ]);
            return Results.Json(index, RegistryJsonContext.Default.ServiceIndexDto);
        });

        // 2. Package Content (Download .nupkg)
        // Используем двойные скобки для экранирования параметров маршрута внутри интерполированной строки
        endpoints.MapGet($"{routePrefix}/content/{{id}}/{{version}}/{{file}}.nupkg", 
            async (string id, string version, IPackageRepository repo, IMemoryCache cache, CancellationToken ct) =>
        {
            var cacheKey = $"nupkg_{id}_{version}".ToLowerInvariant();
            if (cache.TryGetValue(cacheKey, out byte[]? cachedBytes))
                return Results.File(cachedBytes!, "application/octet-stream", $"{id}.{version}.nupkg");

            // Используем новый DictionaryMap.From с кортежем
            var manifests = await repo.GetManifestsAsync("any", DictionaryMap.From((id, version)), ct);
            var manifest = manifests.Get(id).AsMap();
            if (manifest.IsEmpty) return Results.NotFound();

            using var ms = new MemoryStream();
            await PackageExporter.ExportAsync(manifest, repo, ms, ct);
            var bytes = ms.ToArray();

            cache.Set(cacheKey, bytes, new MemoryCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(1), Size = bytes.Length });
            return Results.File(bytes, "application/octet-stream", $"{id}.{version}.nupkg");
        });

        // 3. Registrations (Metadata)
        endpoints.MapGet($"{routePrefix}/metadata/{{id}}/index.json", async (string id, IPackageRepository repo, HttpContext context, CancellationToken ct) =>
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}/{routePrefix}";
            
            var versionsMap = await repo.GetVersionsAsync("any", DictionaryMap.From((id, true)), ct);
            var versions = versionsMap.Get(id).AsMap();
            if (versions.IsEmpty) return Results.NotFound();

            var leafs = versions.Keys().Select(v => new RegistrationLeafDto(
                $"{baseUrl}/metadata/{id}/{v}.json",
                $"{baseUrl}/content/{id}/{v}/{id}.{v}.nupkg",
                new CatalogEntryDto($"{baseUrl}/metadata/{id}/{v}.json", id, v, "Prius", "Package", [])
            )).ToList();

            var root = new RegistrationRootDto(1, [
                new RegistrationPageDto($"{baseUrl}/metadata/{id}/index.json#page1", leafs.Count, leafs, versions.Keys().First(), versions.Keys().Last())
            ]);

            return Results.Json(root, RegistryJsonContext.Default.RegistrationRootDto);
        });

        return endpoints;
    }
}
