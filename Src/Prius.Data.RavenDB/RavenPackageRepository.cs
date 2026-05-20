using Prius.Core.Maps;
using Prius.Core.Packages;
using Prius.Engine.Abstractions;
using Raven.Client.Documents.Session;
using Sparrow.Json;

namespace Prius.Data.RavenDB;

public sealed class RavenPackageRepository : IPackageRepository, IDisposable
{
    private readonly IDocumentStoreHolder _storeHolder;
    private readonly IBinaryManager _binaryManager;

    public event Func<ValueTask>? OnStasisRequested;
    public event Func<ValueTask>? OnBirthRequested;
    public event Func<ValueTask>? OnKillRequested;

    public RavenPackageRepository(IDocumentStoreHolder storeHolder, IBinaryManager binaryManager)
    {
        _storeHolder = storeHolder;
        _binaryManager = binaryManager;
    }

    public async ValueTask<IMap> GetPackages(CancellationToken ct = default)
    {
        using var session = _storeHolder.Store.OpenAsyncSession();
        var results = await session.Advanced.AsyncDocumentQuery<dynamic>("Packages/Packages/ByIdAndVersion")
            .SelectFields<string>("Id")
            .Distinct()
            .ToListAsync(ct);
        
        var map = DictionaryMap.New;
        foreach (var id in results)
        {
            var parts = id.Split('/');
            if (parts.Length > 1)
                map[parts[1]] = true;
        }
        return map;
    }

    public async ValueTask<IMap> GetVersions(string tfm, IMap ids, CancellationToken ct = default)
    {
        using var session = _storeHolder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var query = session.Advanced.AsyncDocumentQuery<dynamic>("Packages/Packages/ByIdAndVersion");
        
        var idList = new List<string>();
        foreach (var key in ids.Keys())
            idList.Add(key);
            
        var results = await query
            .WhereIn("Id", idList)
            .ToListAsync(ct);

        var response = DictionaryMap.New;
        foreach (var doc in results)
        {
            // RavenDB result as dynamic expected to have Id and Version fields
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
        using var session = _storeHolder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var keys = new List<string>();
        foreach (var key in packages.Keys())
            keys.Add($"Packages/{key}/{packages[key]}");
            
        var docs = await session.LoadAsync<BlittableJsonReaderObject>(keys, ct);
        var map = DictionaryMap.New;
        
        foreach (var entry in docs)
        {
            if (entry.Value != null)
                map[entry.Key] = (await entry.Value.AsJsonReaderMap()).AsMapValue();
        }
        return map;
    }

    public async ValueTask<Stream> OpenStream(string hash, CancellationToken ct = default)
    {
        var accessor = _binaryManager.Get(new MapPath(hash.AsSpan()));
        
        if (accessor.Exists)
            return accessor.OpenStream();

        string documentId = "";
        string attachmentName = "";
        using (var session = _storeHolder.Store.OpenAsyncSession())
        {
            var result = await session.Advanced.AsyncDocumentQuery<dynamic>("Packages/Assets/ByHash")
                .WhereEquals("Hash", hash)
                .FirstOrDefaultAsync(ct);

            if (result == null)
                throw new InvalidOperationException($"Asset with hash {hash} not found in repository.");

            documentId = (string)result.DocumentId;
            attachmentName = (string)result.AttachmentName;

            using var attachment = await session.Advanced.Attachments.GetAsync(documentId, attachmentName, ct);
            _binaryManager.Store(new MapPath(hash.AsSpan()), Empty.Instance, attachment.Stream);
        }

        return _binaryManager.Get(new MapPath(hash.AsSpan())).OpenStream();
    }

    public void Dispose()
    {
    }
}
