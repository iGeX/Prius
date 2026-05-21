using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Raven.Client.Documents.Session;
using Sparrow.Json;

namespace Prius.Data.RavenDB;

public sealed class RavenPackageRepository(
    IDocumentStoreHolder storeHolder, 
    IBinaryManager binaryManager, 
    ILogger<RavenPackageRepository>? logger = null) : IPackageRepository, IDisposable
{
    private readonly MemoryCache _manifestCache = new(new MemoryCacheOptions());

    public event Func<ValueTask>? OnTransitionToStasis;
    public event Func<ValueTask>? OnTransitionToActive;
    public event Func<ValueTask>? OnTransitionToTerminated;

    public async ValueTask<IMap> GetPackages(CancellationToken ct = default)
    {
        using var session = storeHolder.Store.OpenAsyncSession();
        
        var results = await session.Advanced.AsyncRawQuery<BlittableJsonReaderObject>(
            "from index 'Packages/Packages/ByIdAndVersion' select Id"
        ).ToListAsync(ct);
        
        var map = DictionaryMap.New;
        
        foreach (var doc in results)
        {
            if (doc.TryGet("Id", out string id) && !string.IsNullOrEmpty(id))
                map[id] = true;
        }
        
        logger?.LogInformation("GetPackages retrieved {Count} packages", map.Keys().Count());
        
        return map;
    }

    public async ValueTask<IMap> GetVersions(string tfm, IMap ids, CancellationToken ct = default)
    {
        logger?.LogInformation("GetVersions requested for TFM {Tfm} with {Count} IDs", tfm, ids.Keys().Count());
        
        var idList = ids.Keys().ToList();
        var response = DictionaryMap.New;
        
        if (idList.Count == 0)
            return response;

        var isAny = tfm.Equals("any", StringComparison.OrdinalIgnoreCase);
        using var session = storeHolder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        
        var rql = isAny
            ? "from index 'Packages/Packages/ByIdAndVersion' where Id in ($ids) select Id, Version"
            : "from index 'Packages/Packages/ByIdAndVersion' where Id in ($ids) and Tfms in ($tfms) select Id, Version, Tfms";

        var query = session.Advanced.AsyncRawQuery<BlittableJsonReaderObject>(rql)
            .WaitForNonStaleResults()
            .AddParameter("ids", idList);
        
        if (!isAny)
            query.AddParameter("tfms", FrameworkConstants.GetCompatible(tfm));

        var results = await query.ToListAsync(ct);

        if (isAny)
        {
            foreach (var doc in results)
            {
                if (!doc.TryGet("Id", out string id) || !doc.TryGet("Version", out string version))
                    continue;

                if (!response.ContainsKey(id))
                    response[id] = DictionaryMap.New.AsMapValue();

                response[id].AsMap()[version] = true;
            }
            return response;
        }

        var compatibleTfms = FrameworkConstants.GetCompatible(tfm);

        foreach (var targetPkgId in idList)
        {
            var versionsForBestTfm = DictionaryMap.New;
            
            foreach (var currentTfm in compatibleTfms)
            {
                var foundVersionsForCurrentTfm = false;

                foreach (var doc in results)
                {
                    if (!doc.TryGet("Id", out string id) || !string.Equals(id, targetPkgId, StringComparison.Ordinal))
                        continue;

                    if (!doc.TryGet("Version", out string version) || string.IsNullOrEmpty(version))
                        continue;

                    if (!doc.TryGet("Tfms", out BlittableJsonReaderArray tfmsArray))
                        continue;

                    var hasTfm = tfmsArray.Any(indexedTfm => string.Equals(indexedTfm?.ToString(), currentTfm, StringComparison.Ordinal));

                    if (!hasTfm)
                        continue;

                    versionsForBestTfm[version] = true;
                    foundVersionsForCurrentTfm = true;
                }

                if (!foundVersionsForCurrentTfm)
                    continue;
                
                response[targetPkgId] = new MapValue(versionsForBestTfm);
                break;
            }
        }
        
        return response;
    }

    public async ValueTask<IMap> GetManifests(string tfm, IMap packages, CancellationToken ct = default)
    {
        logger?.LogInformation("GetManifests requested for TFM {Tfm} with {Count} packages", tfm, packages.Keys().Count());
        
        var result = DictionaryMap.New;
        var toLoad = new List<string>();

        foreach (var key in packages.Keys())
        {
            var version = packages[key].ToString();
            var cacheKey = $"Manifest:{key}:{version}";
            
            if (_manifestCache.TryGetValue(cacheKey, out IMap? manifest) && manifest is not null)
            {
                result[key] = new MapValue(FilterManifestByTfm(manifest, tfm));
                continue;
            }
                
            toLoad.Add($"Packages/{key}/{version}");
        }

        if (toLoad.Count == 0)
            return result;

        using var session = storeHolder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var docs = await session.LoadAsync<BlittableJsonReaderObject>(toLoad, ct);
        
        foreach (var entry in docs)
        {
            if (entry.Value is null) 
                continue;
                
            var map = await entry.Value.AsJsonReaderMap();
            var parts = entry.Key.Split('/');

            if (parts.Length <= 2) 
                continue;
            
            _manifestCache.Set($"Manifest:{parts[1]}:{parts[2]}", new DictionaryMap(map.DeepCopy()), TimeSpan.FromMinutes(10));
            result[parts[1]] = new MapValue(FilterManifestByTfm(map, tfm));
        }
        
        return result;
    }

    public async ValueTask<Stream> OpenStream(string hash, CancellationToken ct = default)
    {
        var hashPath = $"Packages/{hash}";
        var accessor = binaryManager.Get(new MapPath(hashPath.AsSpan()));
        
        if (accessor.Exists)
            return accessor.OpenStream();

        if (logger?.IsEnabled(LogLevel.Debug) ?? false)
            logger.LogDebug("Cache miss for asset {Hash}, fetching from RavenDB", hash);

        using var session = storeHolder.Store.OpenAsyncSession();
        
        var assetMatches = await session.Advanced.AsyncRawQuery<BlittableJsonReaderObject>(
                "from index 'Packages/Assets/ByHash' where Hash = $hash select id()")
            .AddParameter("hash", hash).ToListAsync(ct);
        
        if (assetMatches.Count == 0)
            throw new FileNotFoundException($"Hash {hash} not found.");

        var firstMatch = assetMatches[0];
        if (!firstMatch.TryGet("id()", out string docId))
            throw new InvalidOperationException("Corrupted asset index entry: missing id().");

        var command = new Raven.Client.Documents.Commands.GetDocumentsCommand(
            storeHolder.Store.Conventions, 
            [docId], 
            includes: null, 
            metadataOnly: true
        );

        using var context = JsonOperationContext.ShortTermSingleUse();
        await storeHolder.Store.GetRequestExecutor().ExecuteAsync(command, context, null, ct);

        if (command.Result.Results is null || command.Result.Results.Length == 0)
            return binaryManager.Get(new MapPath(hashPath.AsSpan())).OpenStream();

        var doc = command.Result.Results;
        if (doc[0] is not BlittableJsonReaderObject jsonObj || 
                !jsonObj.TryGet("@metadata", out BlittableJsonReaderObject metadata) || 
                !metadata.TryGet("@attachments", out BlittableJsonReaderArray attachments))
            return binaryManager.Get(new MapPath(hashPath.AsSpan())).OpenStream();

        foreach (var obj in attachments)
        {
            if (obj is not BlittableJsonReaderObject attachmentObj) 
                continue;
                    
            if (!attachmentObj.TryGet("Name", out string attachmentHash)) 
                continue;

            var targetPath = $"Packages/{attachmentHash}";
            if (binaryManager.Get(targetPath).Exists)
                continue;

            using var attachment = await session.Advanced.Attachments.GetAsync(docId, attachmentHash, ct);
            if (attachment is null)
                continue;

            binaryManager.Store(targetPath, Empty.Instance, attachment.Stream);
        }

        return binaryManager.Get(new MapPath(hashPath.AsSpan())).OpenStream();
    }

    private static IMap FilterManifestByTfm(IMap fullManifest, string tfm)
    {
        var rootDependencies = fullManifest.Get(new MapPath("Dependencies".AsSpan()));
        if (!rootDependencies.IsMap)
            return fullManifest;

        var depsMap = rootDependencies.AsMap();
        var result = DictionaryMap.New;
        
        var info = fullManifest.Get(new MapPath("Info".AsSpan()));
        if (info.IsMap)
            result["Info"] = info;

        var isAny = tfm.Equals("any", StringComparison.OrdinalIgnoreCase);
        var tfmsToSearch = isAny ? depsMap.Keys() : FrameworkConstants.GetCompatible(tfm);

        foreach (var currentTfm in tfmsToSearch)
        {
            var targetDeps = depsMap.Get(new MapPath(currentTfm.AsSpan()));
            if (!targetDeps.IsMap)
                continue;
            
            result["Dependencies"] = DictionaryMap.New.With(currentTfm, targetDeps.AsMap()).AsMapValue();
            return result;
        }

        return result;
    }

    public void Dispose() => 
        _manifestCache.Dispose();
}
