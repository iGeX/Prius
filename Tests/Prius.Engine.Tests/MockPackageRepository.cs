using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine.Tests;

public sealed class MockPackageRepository : IPackageRepository
{
    private readonly Dictionary<string, IMap> _manifests = new();
    private readonly IBinaryManager _binaryManager = new BinaryManager();

    public void AddPackage(IMap manifest, Dictionary<string, byte[]>? blobs = null)
    {
        var id = manifest.Get("Info/id").AsString();
        var ver = manifest.Get("Info/version").AsString();
        _manifests[$"{id}_{ver}"] = manifest;

        if (blobs == null)
            return;
        
        foreach (var b in blobs)
        {
            var path = new MapPath($"Packages/{b.Key}");
            _binaryManager.Store(path, Empty.Instance, new MemoryStream(b.Value));
        }
    }

    public ValueTask<IMap> GetPackages(CancellationToken ct) => throw new NotSupportedException();

    public ValueTask<IMap> GetVersions(string tfm, IMap ids, CancellationToken ct)
    {
        var res = DictionaryMap.New;
        foreach (var id in ids.Keys())
        {
            var vers = DictionaryMap.New;
            foreach (var key in _manifests.Keys.Where(k => k.StartsWith(id + "_")))
                vers[key.Split('_')[1]] = true;
            res[id] = new MapValue(vers);
        }
        return new(res);
    }

    public ValueTask<IMap> GetManifests(string tfm, IMap packages, CancellationToken ct)
    {
        var res = DictionaryMap.New;
        foreach (var p in packages.Keys())
        {
            var key = $"{p}_{packages[p].AsString()}";
            if (_manifests.TryGetValue(key, out var m)) 
                res[p] = new MapValue(m);
        }
        return new ValueTask<IMap>(res);
    }

    public ValueTask<Stream> OpenStream(string hash, CancellationToken ct) => 
        new(_binaryManager.Get(new MapPath($"Packages/{hash}")).OpenStream());

#pragma warning disable CS0067
    public event Func<ValueTask>? OnTransitionToStasis;
    
    public event Func<ValueTask>? OnTransitionToActive;
    
    public event Func<ValueTask>? OnTransitionToTerminated;
#pragma warning restore CS0067
}
