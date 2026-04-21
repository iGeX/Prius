using Prius.Core.Maps;

namespace Prius.Core.Packages.Tests;

public sealed class MockPackageRepository : IPackageRepository
{
    private readonly Dictionary<string, IMap> _manifests = new();
    private readonly Dictionary<string, byte[]> _blobs = new();
    private readonly Dictionary<string, HashSet<string>> _versions = new();

    public void AddPackage(IMap manifest, Dictionary<string, byte[]> packageBlobs)
    {
        var id = manifest.DeepGet("Info/id").AsValue<string>();
        var version = manifest.DeepGet("Info/version").AsValue<string>();
        var tfm = manifest.Get("Dependencies").AsMap().Keys().FirstOrDefault() ?? "any";

        _manifests[$"pkg/{tfm}/{id}/{version}".ToLowerInvariant()] = manifest;
        
        if (!_versions.ContainsKey(id)) _versions[id] = new HashSet<string>();
        _versions[id].Add(version);

        foreach (var blob in packageBlobs)
            _blobs[blob.Key] = blob.Value;
    }
    
    public async ValueTask<IMap> GetPackagesAsync(CancellationToken ct = default)
    {
        var result = DictionaryMap.New;
    
        foreach (var package in _versions.Keys) 
            result.Put(package, true);

        return await ValueTask.FromResult(result.AsReadOnly());
    }
    
    public ValueTask<IMap> GetVersionsAsync(string tfm, IMap ids, CancellationToken ct = default)
    {
        var result = DictionaryMap.New;
        foreach (var id in ids.Keys())
        {
            if (!_versions.TryGetValue(id, out var versions)) 
                continue;
            
            var vMap = DictionaryMap.New;
            foreach (var v in versions) 
                vMap.Put(v, true);
            result.Put(id, vMap);
        }
        return new ValueTask<IMap>(result);
    }

    public ValueTask<IMap> GetManifestsAsync(string tfm, IMap packages, CancellationToken ct = default)
    {
        var result = DictionaryMap.New;
        foreach (var pair in packages.Keys())
        {
            var version = packages.Get(pair).AsValue<string>();
            var key = $"pkg/{tfm}/{pair}/{version}".ToLowerInvariant();
            if (_manifests.TryGetValue(key, out var manifest))
                result.Put(pair, manifest);
        }
        return new ValueTask<IMap>(result);
    }

    public ValueTask<Stream> OpenStreamAsync(string hash, CancellationToken ct = default)
    {
        if (_blobs.TryGetValue(hash, out var data))
            return new ValueTask<Stream>(new MemoryStream(data));
        
        throw new FileNotFoundException($"Blob {hash} not found");
    }
}
