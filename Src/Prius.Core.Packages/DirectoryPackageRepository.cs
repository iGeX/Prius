using System.Collections.Concurrent;
using System.IO.Compression;
using Microsoft.Extensions.Logging;
using Prius.Core.Maps;

namespace Prius.Core.Packages;

public sealed class DirectoryPackageRepository : IPackageRepository, IDisposable
{
    private readonly string _rootPath;
    private readonly ILogger<DirectoryPackageRepository>? _logger;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, IMap>>> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, (string ZipPath, string EntryName)> _blobMap = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<(string Tfm, string Pkg, string Ver)>> _fileTracker = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher _watcher;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isInitialized;

    public DirectoryPackageRepository(string rootPath, ILogger<DirectoryPackageRepository>? logger = null)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _logger = logger;
        _watcher = new FileSystemWatcher(_rootPath, "*.nupkg") 
        { 
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size 
        };

        _watcher.Created += async (_, e) => await IndexFileAsync(e.FullPath);
        _watcher.Deleted += (_, e) => RemoveFile(e.FullPath);
        _watcher.Changed += async (_, e) => 
        { 
            RemoveFile(e.FullPath); 
            await IndexFileAsync(e.FullPath); 
        };
        _watcher.Renamed += async (_, e) => 
        { 
            RemoveFile(e.OldFullPath); 
            await IndexFileAsync(e.FullPath); 
        };

        _watcher.EnableRaisingEvents = true;
    }

    public async ValueTask<IMap> GetPackages(CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        
        var result = DictionaryMap.New;
        foreach (var tfmStore in _manifests.Values)
        {
            foreach (var pkgName in tfmStore.Keys)
                result.Put(pkgName, true);
        }

        return result;
    }

    public async ValueTask<IMap> GetVersions(string tfm, IMap ids, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var result = DictionaryMap.New;
        var isAny = tfm.Equals("any", StringComparison.OrdinalIgnoreCase);
        var compatibleTfms = FrameworkConstants.GetCompatible(tfm);

        foreach (var pkgName in ids.Keys())
        {
            var versions = DictionaryMap.New;
        
            // Если просят any — берем все TFM из кэша
            var tfmsToSearch = isAny ? _manifests.Keys : compatibleTfms;

            foreach (var currentTfm in tfmsToSearch)
            {
                if (_manifests.TryGetValue(currentTfm, out var tfmStore) && tfmStore.TryGetValue(pkgName, out var pkgStore))
                {
                    foreach (var version in pkgStore.Keys)
                        versions.Put(version, true);
                }
            }
            result.Put(pkgName, versions);
        }
        return result;
    }

    public async ValueTask<IMap> GetManifests(string tfm, IMap packages, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        var result = DictionaryMap.New;
        var isAny = tfm.Equals("any", StringComparison.OrdinalIgnoreCase);
        var compatibleTfms = FrameworkConstants.GetCompatible(tfm);

        foreach (var pkgName in packages.Keys())
        {
            var version = packages.Get(pkgName).AsValue<string>();
            var tfmsToSearch = isAny ? _manifests.Keys : compatibleTfms;

            foreach (var currentTfm in tfmsToSearch)
            {
                if (_manifests.TryGetValue(currentTfm, out var tfmStore) && 
                    tfmStore.TryGetValue(pkgName, out var pkgStore) && 
                    pkgStore.TryGetValue(version, out var manifest))
                {
                    result.Put(pkgName, manifest);
                    // Если мы в режиме any, нам достаточно найти любой первый попавшийся манифест этой версии
                    break; 
                }
            }
        }
        return result;
    }

    public async ValueTask<Stream> OpenStream(string hash, CancellationToken ct = default)
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

    private async Task IndexFileAsync(string path)
    {
        try 
        {
            await Task.Delay(100); 
            await using var stream = File.OpenRead(path);
            var map = PackageImporter.Import(stream);
            
            var pkg = map.DeepGet("Info/id").AsValue<string>();
            var ver = map.DeepGet("Info/version").AsValue<string>();
            var tracked = new List<(string, string, string)>();

            var depMap = map.Get("Dependencies").AsMap();
            var tfms = depMap.Keys().ToList();

            if (tfms.Count == 0)
                tfms.Add("any");

            foreach (var tfm in tfms)
            {
                var tfmStore = _manifests.GetOrAdd(tfm, _ => new(StringComparer.OrdinalIgnoreCase));
                var pkgStore = tfmStore.GetOrAdd(pkg, _ => new(StringComparer.OrdinalIgnoreCase));
                pkgStore[ver] = map;
                tracked.Add((tfm, pkg, ver));
            }

            _fileTracker[path] = tracked;
            IndexBlobs(map.Get("Assets").AsMap(), path, string.Empty);
            
            _logger?.LogDebug("Indexed package: {Package} {Version} from {Path}", pkg, ver, path);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to index package file: {Path}", path);
        }
    }

    private void RemoveFile(string path)
    {
        if (_fileTracker.TryRemove(path, out var items))
        {
            foreach (var (tfm, pkg, ver) in items)
            {
                if (!_manifests.TryGetValue(tfm, out var tfmStore) || !tfmStore.TryGetValue(pkg, out var pkgStore)) 
                    continue;
                pkgStore.TryRemove(ver, out _);
                if (pkgStore.IsEmpty)
                    tfmStore.TryRemove(pkg, out _);
            }
        }

        var toRemove = _blobMap.Where(kv => kv.Value.ZipPath.Equals(path, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList();
        foreach (var hash in toRemove) 
            _blobMap.TryRemove(hash, out _);

        if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
            _logger?.LogDebug("Removed package data for file: {Path}", path);
    }

    private void IndexBlobs(IMap assets, string zipPath, string currentPath)
    {
        foreach (var key in assets.Keys())
        {
            var value = assets.Get(key);
            if (!value.IsMap) 
                continue;

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
        if (_isInitialized)
            return;

        await _lock.WaitAsync(ct);
        try
        {
            if (_isInitialized)
                return;

            _logger?.LogInformation("Initializing DirectoryPackageRepository at {Path}", _rootPath);

            foreach (var file in Directory.GetFiles(_rootPath, "*.nupkg"))
                await IndexFileAsync(file);

            _isInitialized = true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose() 
    { 
        _watcher.Dispose(); 
        _lock.Dispose(); 
    }
}
