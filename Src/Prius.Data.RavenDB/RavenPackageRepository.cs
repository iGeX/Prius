using Microsoft.Extensions.Caching.Memory;
using Prius.Core.Maps;
using Prius.Core.Packages;
using Prius.Engine.Abstractions;
using Raven.Client.Documents.Session;
using Sparrow.Json;

namespace Prius.Data.RavenDB;

public sealed class RavenPackageRepository(IDocumentStoreHolder storeHolder, IBinaryManager binaryManager) : IPackageRepository, IDisposable
{
    private readonly MemoryCache _manifestCache = new(new MemoryCacheOptions());

    public event Func<ValueTask>? OnStasisRequested;
    public event Func<ValueTask>? OnBirthRequested;
    public event Func<ValueTask>? OnKillRequested;

    public async ValueTask<IMap> GetPackages(CancellationToken ct = default)
    {
        using var session = storeHolder.Store.OpenAsyncSession();
        var results = await session.Advanced.AsyncDocumentQuery<dynamic>("Packages/Packages/ByIdAndVersion")
            .SelectFields<string>("Id")
            .Distinct()
            .ToListAsync(ct);
        
        var map = DictionaryMap.New;
        foreach (var parts in results.Select(id => id.Split('/')).Where(parts => parts.Length > 1)) 
            map[parts[1]] = true;
        return map;
    }

    public async ValueTask<IMap> GetVersions(string tfm, IMap ids, CancellationToken ct = default)
    {
        using var session = storeHolder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var query = session.Advanced.AsyncDocumentQuery<dynamic>("Packages/Packages/ByIdAndVersion");
        
        var idList = ids.Keys().ToList();
        
        var results = await query
            .WhereIn("Id", idList)
            .ToListAsync(ct);

        var response = DictionaryMap.New;
        foreach (var doc in results)
        {
            var id = (string)doc.Id;
            var version = (string)doc.Version;
            
            if (!response.ContainsKey(id))
                response[id] = DictionaryMap.New.AsMapValue();
            
            response[id].AsMap()[version] = true;
        }
        return response;
    }

    public async ValueTask<IMap> GetManifests(string tfm, IMap packages, CancellationToken ct = default)
    {
        var result = DictionaryMap.New;
        var toLoad = new List<string>();

        foreach (var key in packages.Keys())
        {
            var cacheKey = $"Manifest:{key}:{packages[key]}";
            if (_manifestCache.TryGetValue(cacheKey, out IMap? manifest))
                result[key] = new MapValue(manifest);
            else
                toLoad.Add($"Packages/{key}/{packages[key]}");
        }

        if (toLoad.Count <= 0) 
            return result;
        
        using var session = storeHolder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var docs = await session.LoadAsync<BlittableJsonReaderObject>(toLoad, ct);
        foreach (var entry in docs)
        {
            if (entry.Value == null) 
                continue;
            
            var map = (await entry.Value.AsJsonReaderMap());
            _manifestCache.Set(entry.Key, map, TimeSpan.FromMinutes(10));
                    
            var pkgId = entry.Key.Split('/')[1];
            result[pkgId] = new MapValue(map);
        }
        return result;
    }

    public async ValueTask<Stream> OpenStream(string hash, CancellationToken ct = default)
    {
        var hashPath = $"Packages/{hash}";
        var accessor = binaryManager.Get(new MapPath(hashPath.AsSpan()));
        
        if (accessor.Exists)
            return accessor.OpenStream();

        using (var session = storeHolder.Store.OpenAsyncSession())
        {
            var asset = await session.Advanced.AsyncDocumentQuery<dynamic>("Packages/Assets/ByHash")
                .WhereEquals("Hash", hash)
                .FirstOrDefaultAsync(ct);

            if (asset == null)
                throw new InvalidOperationException($"Asset with hash {hash} not found.");

            var allAssets = await session.Advanced.AsyncDocumentQuery<dynamic>("Packages/Assets/ByDocumentId")
                .WhereEquals("DocumentId", (string)asset.DocumentId)
                .ToListAsync(ct);

            foreach (var item in allAssets)
            {
                var assetHash = (string)item.Hash;
                
                using var attachment = await session.Advanced.Attachments.GetAsync((string)item.DocumentId, (string)item.AttachmentName, ct);
                
                var path = new MapPath($"Packages/{assetHash}".AsSpan());
                binaryManager.Store(path, Empty.Instance, attachment.Stream);
            }
        }

        return binaryManager.Get(new MapPath(hashPath.AsSpan())).OpenStream();
    }

    public void Dispose() => _manifestCache.Dispose();
}
