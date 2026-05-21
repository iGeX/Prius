using System.Reflection;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Prius.Engine.Packages;

namespace Prius.Engine;

public sealed class Bootstrap
{
    private readonly List<Assembly> _loadedAssemblies = [];
    
    private readonly TaskCompletionSource _killSignal = new();
    
    private readonly IPackageRepository _repository;

    private readonly IBootstrapRuntime _runtime;

    public IMap StartupTargets { get; init; } = DictionaryMap.New;
    
    public Bootstrap(IPackageRepository repository, IBootstrapRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(runtime);
        
        _repository = repository;
        _runtime = runtime;

        _repository.OnTransitionToStasis += Stasis;
        _repository.OnTransitionToActive += Activate;
        _repository.OnTransitionToTerminated += Terminate;
    }

    public async ValueTask Activate()
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
            Console.WriteLine($"[ACTIVE ERROR] {ex.Message}");
            throw;
        }
    }
    
    private async ValueTask ExecuteEntry()
    {
        if (_loadedAssemblies.Count == 0)
            return;
        
        //TODO
    }
    
    public async ValueTask Stasis()
    {
        if (_loadedAssemblies.Count == 0)
            return;

        _loadedAssemblies.Clear();
        await _runtime.Unload();
    }
    
    public Task WaitAsync() => _killSignal.Task;

    private async ValueTask Terminate()
    {
        Console.WriteLine("[DEAD] Closing application...");
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

            var hash = val["hash"].AsString();
            if (string.IsNullOrEmpty(hash))
                continue;

            await using var stream = await _repository.OpenStream(hash);
            var assembly = await _runtime.LoadAssembly(stream);
            _loadedAssemblies.Add(assembly);
        }
    }

    private static void CollectContent(IMap contentFiles, IMap plan)
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
            var hash = val["hash"].AsString();

            if (string.IsNullOrEmpty(hash))
            {
                await ExtractAssetsRecursive(val.AsMap(), currentPath);
                continue;
            }

            await using var stream = await _repository.OpenStream(hash);
            await _runtime.WriteAsset(currentPath, stream);
        }
    }
}
