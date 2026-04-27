using Prius.Core.Maps;

namespace Prius.Core.Packages.Tests;

public sealed class MockPackageRepository : IPackageRepository
{
    private readonly Dictionary<string, IMap> _manifests = new();
    private readonly Dictionary<string, byte[]> _blobs = new();

    public void AddPackage(IMap manifest, Dictionary<string, byte[]>? blobs = null)
    {
        var id = manifest.DeepGet("Info/id").AsString();
        var ver = manifest.DeepGet("Info/version").AsString();
        _manifests[$"{id}_{ver}"] = manifest;

        if (blobs == null)
            return;
        
        foreach (var b in blobs) 
            _blobs[b.Key] = b.Value;
    }

    public ValueTask<IMap> GetPackages(CancellationToken ct) => throw new NotSupportedException();

    public ValueTask<IMap> GetVersions(string tfm, IMap ids, CancellationToken ct)
    {
        var res = DictionaryMap.New;
        foreach (var id in ids.Keys())
        {
            var vers = DictionaryMap.New;
            foreach (var key in _manifests.Keys.Where(k => k.StartsWith(id + "_")))
                vers.Put(key.Split('_')[1], true);
            res.Put(id, vers);
        }
        return new(res);
    }

    public ValueTask<IMap> GetManifests(string tfm, IMap packages, CancellationToken ct)
    {
        var res = DictionaryMap.New;
        foreach (var p in packages.Keys())
        {
            var key = $"{p}_{packages.Get(p).AsString()}";
            if (_manifests.TryGetValue(key, out var m)) res.Put(p, m);
        }
        return new(res);
    }

    public ValueTask<Stream> OpenStream(string hash, CancellationToken ct) => 
        new(new MemoryStream(_blobs[hash]));

    public event Func<ValueTask>? OnStasisRequested;
    
    public event Func<ValueTask>? OnBirthRequested;
    
    public event Func<ValueTask>? OnKillRequested;
}
