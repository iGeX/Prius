using System.Collections.Concurrent;
using System.IO.Compression;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Prius.Engine.Nuspec;

namespace Prius.Packages.Directory.Server;

public sealed class DirectoryPackageRepository : IPackageRepository, IDisposable
{
    private readonly string _rootPath;
    private readonly IBinaryManager _binaryManager;
    private readonly ILogger<DirectoryPackageRepository>? _logger;
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, ConcurrentDictionary<string, IMap>>> _manifests = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, List<(string Tfm, string Pkg, string Ver)>> _fileTracker = new(StringComparer.OrdinalIgnoreCase);
    private readonly FileSystemWatcher _watcher;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool _isInitialized;

    public DirectoryPackageRepository(string rootPath, IBinaryManager binaryManager, ILogger<DirectoryPackageRepository>? logger = null)
    {
        _rootPath = Path.GetFullPath(rootPath);
        _binaryManager = binaryManager;
        _logger = logger;
        _watcher = new FileSystemWatcher(_rootPath, "*.nupkg") 
        { 
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size 
        };

        _watcher.Created += async (_, e) =>
        {
            await Task.Delay(100); 
            await IndexFileAsync(e.FullPath);
        };
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
                result[pkgName] = true;
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
            var tfmsToSearch = isAny ? _manifests.Keys : compatibleTfms;

            foreach (var currentTfm in tfmsToSearch)
            {
                if (!_manifests.TryGetValue(currentTfm, out var tfmStore) || !tfmStore.TryGetValue(pkgName, out var pkgStore))
                    continue;
                
                foreach (var version in pkgStore.Keys)
                    versions[version] = true;
            }
            result[pkgName] = new MapValue(versions);
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
            var version = packages[pkgName].AsValue<string>();
            var tfmsToSearch = isAny ? _manifests.Keys : compatibleTfms;

            foreach (var currentTfm in tfmsToSearch)
            {
                if (!_manifests.TryGetValue(currentTfm, out var tfmStore) ||
                    !tfmStore.TryGetValue(pkgName, out var pkgStore) ||
                    !pkgStore.TryGetValue(version, out var manifest))
                    continue;
                
                result[pkgName] = new MapValue(manifest);
                break;
            }
        }
        return result;
    }

    public async ValueTask<Stream> OpenStream(string hash, CancellationToken ct = default)
    {
        await EnsureInitializedAsync(ct);
        
        var path = new MapPath($"Packages/{hash}");
        var accessor = _binaryManager.Get(path);
        
        if (!accessor.Exists)
            throw new FileNotFoundException($"Hash {hash} not found.");

        return accessor.OpenStream();
    }

#pragma warning disable CS0067
    public event Func<ValueTask>? OnTransitionToStasis;
    
    public event Func<ValueTask>? OnTransitionToActive;
    
    public event Func<ValueTask>? OnTransitionToTerminated;
#pragma warning restore CS0067
    
    private async Task IndexFileAsync(string path)
    {
        try 
        {
            await using var stream = File.OpenRead(path);
            var map = PackageImporter.Import(stream);
        
            var pkg = map.Get("Info/id").AsString();
            var ver = map.Get("Info/version").AsString();
        
            var tracked = new List<(string Tfm, string Pkg, string Ver)>();
            var foundTfms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var tfm in map["Dependencies"].AsMap().Keys())
                foundTfms.Add(tfm);
            foreach (var tfm in map["Assets"]["lib"].AsMap().Keys())
                foundTfms.Add(tfm);

            if (foundTfms.Count == 0)
                foundTfms.Add("any");

            foreach (var tfm in foundTfms)
            {
                var tfmStore = _manifests.GetOrAdd(tfm, _ => new(StringComparer.OrdinalIgnoreCase));
                var pkgStore = tfmStore.GetOrAdd(pkg, _ => new(StringComparer.OrdinalIgnoreCase));
            
                pkgStore[ver] = map;
                tracked.Add((tfm, pkg, ver));
            }

            _fileTracker[path] = tracked;
            
            using var packageStream = File.OpenRead(path);
            using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
            IndexBlobs(map["Assets"].AsMap(), archive);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[INDEX ERROR] {path}: {ex.Message}");
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

        if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
            _logger?.LogDebug("Removed package data for file: {Path}", path);
    }

    private void IndexBlobs(IMap assets, ZipArchive archive)
    {
        foreach (var key in assets.Keys())
        {
            var value = assets[key];
            if (!value.IsMap) 
                continue;

            var subMap = value.AsMap();
            var hash = subMap["hash"].AsValue<string>();

            if (string.IsNullOrEmpty(hash)) 
                IndexBlobs(subMap, archive);
            else
            {
                var entryPath = assets.Keys().First(k => assets[k].IsMap && assets[k].AsMap()["hash"].AsString() == hash);
                var entry = archive.GetEntry(entryPath);
                if (entry != null)
                {
                    using var entryStream = entry.Open();
                    var ms = new MemoryStream();
                    entryStream.CopyTo(ms);
                    ms.Position = 0;
                    
                    var metadata = DictionaryMap.New.With("Hash", hash).AsMapValue();
                    _binaryManager.Store(new MapPath($"Packages/{hash}"), metadata, ms);
                }
            }
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

            foreach (var file in System.IO.Directory.GetFiles(_rootPath, "*.nupkg"))
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
