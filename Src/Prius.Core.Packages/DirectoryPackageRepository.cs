using System.Collections.Concurrent;
using System.IO.Compression;
using Prius.Core.Maps;

namespace Prius.Core.Packages;

public sealed class DirectoryPackageRepository : IPackageRepository, IDisposable
{
    private readonly string _rootPath;
    private readonly ConcurrentDictionary<string, IMap> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string ZipPath, string EntryName)> _blobMap = new(StringComparer.OrdinalIgnoreCase);
    
    private readonly ConcurrentDictionary<string, List<string>> _fileToManifestKeys = new(StringComparer.OrdinalIgnoreCase);
    
    private readonly FileSystemWatcher _watcher;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isInitialized;

    public DirectoryPackageRepository(string rootPath)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _watcher = new FileSystemWatcher(_rootPath, "*.nupkg")
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };

        _watcher.Created += async (_, e) => await IndexFileAsync(e.FullPath);
        _watcher.Deleted += (_, e) => RemoveFile(e.FullPath);
        _watcher.Changed += async (_, e) => { RemoveFile(e.FullPath); await IndexFileAsync(e.FullPath); };
        _watcher.Renamed += async (_, e) => { RemoveFile(e.OldFullPath); await IndexFileAsync(e.FullPath); };

        _watcher.EnableRaisingEvents = true;
    }

    public async ValueTask<IMap> GetPackagesAsync(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var result = DictionaryMap.New;
        foreach (var key in _manifests.Keys)
            result.Put(key.Split('/')[2], true);
        return result;
    }

    public async ValueTask<IMap> GetVersionsAsync(string tfm, IMap ids, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var result = DictionaryMap.New;
        foreach (var package in ids.Keys())
        {
            var versions = DictionaryMap.New;
            foreach (var key in _manifests.Keys)
            {
                var parts = key.Split('/');
                if (parts[1].Equals(tfm, StringComparison.OrdinalIgnoreCase) && parts[2].Equals(package, StringComparison.OrdinalIgnoreCase))
                    versions.Put(parts[3], true);
            }
            result.Put(package, versions);
        }
        return result;
    }

    public async ValueTask<IMap> GetManifestsAsync(string tfm, IMap packages, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var result = DictionaryMap.New;
        foreach (var package in packages.Keys())
        {
            var version = packages.Get(package).AsValue<string>();
            var key = $"{tfm}/{package}/{version}".ToLowerInvariant();
            if (_manifests.TryGetValue(key, out var manifest))
                result.Put(package, manifest);
        }
        return result;
    }

    public async ValueTask<Stream> OpenStreamAsync(string hash, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        if (!_blobMap.TryGetValue(hash, out var loc))
            throw new FileNotFoundException($"Hash {hash} not found.");

        await using var zip = new ZipArchive(File.OpenRead(loc.ZipPath), ZipArchiveMode.Read);
        var ms = new MemoryStream();
        await using (var entryStream = await zip.GetEntry(loc.EntryName)!.OpenAsync(ct))
            await entryStream.CopyToAsync(ms, ct);
        
        ms.Position = 0;
        return ms;
    }

    private async ValueTask<FileStream> TryOpenStream(string path)
    {
        for (var i = 0; i < 3; i++)
        {
            try
            {
                return File.OpenRead(path);
            }
            catch
            {
                await Task.Delay(100);
            }
        }
        
        throw new FileNotFoundException($"Path {path} not found.");
    }

    private async Task IndexFileAsync(string path)
    {
        await using var stream = await TryOpenStream(path);
        
        var map = PackageImporter.Import(stream);
        var pkg = map.DeepGet("Info/id").AsValue<string>();
        var ver = map.DeepGet("Info/version").AsValue<string>();
        var keys = new List<string>();

        foreach (var tfm in map.Get("Dependencies").AsMap().Keys())
        {
            var key = $"{tfm}/{pkg}/{ver}".ToLowerInvariant();
            _manifests[key] = map;
            keys.Add(key);
        }

        _fileToManifestKeys[path] = keys;
        IndexBlobs(map.Get("Assets").AsMap(), path, string.Empty);
    }

    private void RemoveFile(string path)
    {
        if (_fileToManifestKeys.TryRemove(path, out var keys))
            foreach (var key in keys) _manifests.TryRemove(key, out _);
        
        var toRemove = _blobMap.Where(kv => kv.Value.ZipPath.Equals(path, StringComparison.OrdinalIgnoreCase))
                               .Select(kv => kv.Key).ToList();
        foreach (var hash in toRemove) _blobMap.TryRemove(hash, out _);
    }

    private void IndexBlobs(IMap assets, string zipPath, string currentPath)
    {
        foreach (var key in assets.Keys())
        {
            var value = assets.Get(key);
            if (!value.IsMap) continue;

            var subMap = value.AsMap();
            var entryName = string.IsNullOrEmpty(currentPath) ? key : $"{currentPath}/{key}";
            var hash = subMap.Get("hash").AsValue<string>();

            if (string.IsNullOrEmpty(hash))
                IndexBlobs(subMap, zipPath, entryName);
            else
                _blobMap[hash] = (zipPath, entryName);
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken ct)
    {
        if (_isInitialized) return;
        await _lock.WaitAsync(ct);
        try
        {
            if (_isInitialized) return;
            foreach (var file in Directory.GetFiles(_rootPath, "*.nupkg"))
                await IndexFileAsync(file);
            _isInitialized = true;
        }
        finally { _lock.Release(); }
    }

    public void Dispose() { _watcher.Dispose(); _lock.Dispose(); }
}
