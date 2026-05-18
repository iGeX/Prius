namespace Prius.Core.Packages;

using System.Reflection;
using Maps;

public sealed class Bootstrap
{
    private readonly List<Assembly> _loadedAssemblies = [];
    
    private readonly TaskCompletionSource _killSignal = new();
    
    private readonly IPackageRepository _repository;

    private readonly IBootstrapRuntime _runtime;

    public IMap StartupTargets { get; init; } = DictionaryMap.New;

    public Bootstrap(IPackageRepository repository) : this(repository, new NativeBootstrapRuntime()) { }
    
    public Bootstrap(IPackageRepository repository, IBootstrapRuntime runtime)
    {
        _repository = repository;
        _runtime = runtime;

        _repository.OnStasisRequested += Stasis;
        _repository.OnBirthRequested += Birth;
        _repository.OnKillRequested += Kill;
    }

    public async ValueTask Birth()
    {
        try 
        {
            if (_loadedAssemblies.Count > 0)
                await Stasis();

            if (StartupTargets.IsEmpty)
                return;

            await _runtime.Prepare();

            var snapshot = await new PackageResolver(_repository).Resolve(_runtime.Tfm, StartupTargets);
            var order = snapshot["Order"].AsMap();
            var manifests = snapshot["Manifests"].AsMap();

            var runtimesPlan = DictionaryMap.New;
            var contentPlan = DictionaryMap.New;

            foreach (var index in order.Keys(true))
            {
                var pkgId = order[index].AsString();
                var manifest = manifests[pkgId].AsMap();
                var assets = manifest["Assets"].AsMap();

                Console.WriteLine($"[LOAD] {pkgId} ({manifest.Get("Info/version").AsString()})");
                
                await LoadLibs(assets);
                
                runtimesPlan.With(assets["runtimes"].AsMap());
                CollectContent(assets["contentFiles"].AsMap(), contentPlan);
            }

            await ExtractPlan(runtimesPlan, "runtimes");
            await ExtractPlan(contentPlan, string.Empty);

            await ExecuteEntry();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIRTH ERROR] {ex.Message}");
            throw;
        }
    }
    
    public async ValueTask Stasis()
    {
        if (_loadedAssemblies.Count == 0)
            return;

        _loadedAssemblies.Clear();
        await _runtime.Unload();
    }
    
    public Task WaitAsync() => _killSignal.Task;

    private async ValueTask Kill()
    {
        Console.WriteLine("[KILL] Closing application...");
        await Stasis();
        _killSignal.TrySetResult();
    }

    private async ValueTask LoadLibs(IMap assets)
    {
        var libs = assets["lib"].AsMap();
        var libMap = FrameworkConstants.GetCompatible(_runtime.Tfm)
            .Select(tfm => libs[tfm].AsMap())
            .FirstOrDefault(m => !m.IsEmpty) ?? DictionaryMap.New;

        await LoadAssembliesRecursive(libMap);
    }

    private async ValueTask LoadAssembliesRecursive(IMap map)
    {
        foreach (var key in map.Keys())
        {
            var val = map[key];
            if (!val.IsMap)
                continue;

            if (!key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                await LoadAssembliesRecursive(val.AsMap());
                continue;
            }

            var hash = val.AsMap()["hash"].AsString();
            if (string.IsNullOrEmpty(hash))
                continue;

            await using var stream = await _repository.OpenStream(hash);
            _loadedAssemblies.Add(await _runtime.LoadAssembly(stream));
        }
    }

    private void CollectContent(IMap contentFiles, IMap plan)
    {
        foreach (var tfmKey in contentFiles.Keys())
        {
            var tfmMap = contentFiles[tfmKey].AsMap();
            foreach (var specKey in tfmMap.Keys())
                plan.With(tfmMap[specKey].AsMap());
        }
    }

    private async ValueTask ExtractPlan(IMap plan, string subDir)
    {
        if (plan.IsEmpty)
            return;

        await ExtractAssetsRecursive(plan, subDir);
    }

    private async ValueTask ExtractAssetsRecursive(IMap map, string relativePath)
    {
        foreach (var key in map.Keys())
        {
            var val = map[key];
            if (!val.IsMap)
                continue;

            var currentPath = Path.Combine(relativePath, key);
            var hash = val.AsMap()["hash"].AsString();

            if (string.IsNullOrEmpty(hash))
            {
                await ExtractAssetsRecursive(val.AsMap(), currentPath);
                continue;
            }

            await using var stream = await _repository.OpenStream(hash);
            await _runtime.WriteAsset(currentPath, stream);
        }
    }

    private async ValueTask ExecuteEntry()
    {
        foreach (var assembly in _loadedAssemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.Name != "PriusEntry")
                    continue;

                var method = type.GetMethod("RunAsync", [typeof(IMap)]) ?? type.GetMethod("RunAsync", []);
                if (method == null)
                    continue;

                Console.WriteLine($"[START] {type.FullName}");
                var instance = Activator.CreateInstance(type);
                var args = method.GetParameters().Length > 0 ? [DictionaryMap.New] : Array.Empty<object>();
                
                if (method.Invoke(instance, args) is ValueTask vt)
                    await vt;
                
                return;
            }
        }
    }
}
