using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Prius.Engine.Nuspec;

namespace Prius.Engine;

public sealed class Bootstrap
{
    private readonly List<Assembly> _loadedAssemblies = [];
    private readonly TaskCompletionSource _killSignal = new();
    private readonly IPackageRepository _repository;
    private readonly IBootstrapRuntime _runtime;
    private readonly IMetadataRegistry _metadataRegistry;
    private readonly VirtualBus _bus;
    private readonly SemaphoreSlim _gate = new(1, 1);
    
    private IServiceProvider? _serviceProvider;
    private readonly List<IPriusModule> _modules = [];

    private TimeSpan _activeTimeout = TimeSpan.FromSeconds(30);

    public IMap StartupTargets { get; init; } = DictionaryMap.New;
    public TimeSpan ModuleTimeout { get; init; } = TimeSpan.FromSeconds(30);
    
    public Bootstrap(IPackageRepository repository, IBootstrapRuntime runtime, IMetadataRegistry metadataRegistry)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _metadataRegistry = metadataRegistry ?? throw new ArgumentNullException(nameof(metadataRegistry));
        _bus = new VirtualBus(new RoutingTrie());

        _repository.OnTransitionToStasis += Stasis;
        _repository.OnTransitionToActive += Activate;
        _repository.OnTransitionToTerminated += Terminate;
        
        _metadataRegistry.OnTransitionToStasis += Stasis;
        _metadataRegistry.OnTransitionToActive += Refresh;
        _metadataRegistry.OnTransitionToTerminated += Terminate;
    }

    public async ValueTask Activate()
    {
        await _gate.WaitAsync();
        try
        {
            if (_loadedAssemblies.Count > 0) 
                await DoStasis();

            if (StartupTargets.IsEmpty) 
                return;

            await _runtime.Prepare();

            var snapshot = await new PackageResolver(_repository).Resolve(_runtime.Tfm, StartupTargets);
            var order = snapshot["Order"].AsMap();
            var manifests = snapshot["Manifests"].AsMap();

            var runtimesPlan = DictionaryMap.New;
            var contentPlan = DictionaryMap.New;

            var services = new ServiceCollection();
            
            foreach (var index in order.Keys(true))
            {
                var pkgId = order[index].AsString();
                var manifest = manifests[pkgId].AsMap();
                var assets = manifest["Assets"].AsMap();

                Console.WriteLine($"[LOAD] {pkgId} ({manifest.DeepGet("Info/version").AsString()})");
                
                await LoadLibs(assets, services);
                
                runtimesPlan.With(assets["runtimes"].AsMap());
                CollectContent(assets["contentFiles"].AsMap(), contentPlan);
            }

            await ExtractPlan(runtimesPlan, "runtimes");
            await ExtractPlan(contentPlan, string.Empty);

            var config = new ConfigurationBuilder()
                .Add(new BusConfigurationSource(_bus))
                .Build();

            _activeTimeout = ModuleTimeout;
            var timeoutStr = config["Bootstrap:ModuleTimeout"];
            if (!string.IsNullOrEmpty(timeoutStr))
            {
                if (TimeSpan.TryParse(timeoutStr, out var ts))
                    _activeTimeout = ts;
                else if (double.TryParse(timeoutStr, out var secs))
                    _activeTimeout = TimeSpan.FromSeconds(secs);
            }

            services.AddSingleton<IConfiguration>(config);
            services.AddLogging(builder => builder.AddConsole());
            
            var registry = new DataIntentsRegistry();
            registry.ExitStasis();
            services.AddSingleton<IDataIntentsRegistry>(registry);
            services.AddSingleton<IDataIntentsProvider>(registry);
            services.AddSingleton<IElementContext>(_bus);
            services.AddSingleton<IBinaryManager>(new BinaryManager());

            foreach (var module in _modules) 
                module.ConfigureServices(services, config);

            _serviceProvider = services.BuildServiceProvider();

            _bus.UpdateTrie(await BuildRoutingTrie(CancellationToken.None));

            foreach (var module in _modules) 
                await ExecuteWithTimeout(ct => module.Activate(_serviceProvider, config, ct), _activeTimeout, "Activate");

            await ExecuteEntry();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BOOT ERROR] {ex.Message}");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask Refresh()
    {
        await _gate.WaitAsync();
        try
        {
            if (_serviceProvider == null)
            {
                _gate.Release();
                await Activate();
                return;
            }

            Console.WriteLine("[SYSTEM] Refreshing blueprint...");
            _bus.UpdateTrie(await BuildRoutingTrie(CancellationToken.None));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[REFRESH ERROR] {ex.Message}");
        }
        finally
        {
            if (_gate.CurrentCount == 0)
                _gate.Release();
        }
    }
    
    private async ValueTask<RoutingTrie> BuildRoutingTrie(CancellationToken ct)
    {
        var trie = new RoutingTrie();
        trie.AddRoute("/Configuration/**", new ConfigurationElement(new BusConfigurationProvider(_bus)));

        new BlueprintCompiler(_serviceProvider!, ResolveType).Compile(trie, await _metadataRegistry.GetBlueprint(ct));

        return trie;
    }

    private Type? ResolveType(string typeName)
    {
        foreach (var assembly in _loadedAssemblies)
        {
            var type = assembly.GetType(typeName);
            if (type != null) 
                return type;
        }
        return Type.GetType(typeName);
    }
    
    private async ValueTask LoadLibs(IMap assets, IServiceCollection services)
    {
        var libs = assets["lib"].AsMap();
        var libMap = FrameworkConstants.GetCompatible(_runtime.Tfm)
            .Select(tfm => libs[tfm].AsMap())
            .FirstOrDefault(m => !m.IsEmpty) ?? DictionaryMap.New;

        await LoadAssembliesRecursive(libMap, services);
    }

    private async ValueTask LoadAssembliesRecursive(IMap map, IServiceCollection services)
    {
        foreach (var key in map.Keys())
        {
            var val = map[key];
            if (!val.IsMap) 
                continue;

            if (!key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                await LoadAssembliesRecursive(val.AsMap(), services);
                continue;
            }

            var hash = val["hash"].AsString();
            if (string.IsNullOrEmpty(hash)) 
                continue;

            await using var stream = await _repository.OpenStream(hash);
            var assembly = await _runtime.LoadAssembly(stream);
            
            foreach (var type in assembly.GetExportedTypes())
            {
                if (typeof(IPriusModule).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false })
                {
                    var module = (IPriusModule)Activator.CreateInstance(type)!;
                    _modules.Add(module);
                    services.AddSingleton(module);
                }
                
                if (typeof(IElement).IsAssignableFrom(type) && type is { IsInterface: false, IsAbstract: false }) 
                    services.AddSingleton(typeof(IElement), type);
            }

            _loadedAssemblies.Add(assembly);
        }
    }

    private static async ValueTask ExecuteWithTimeout(Func<CancellationToken, ValueTask> action, TimeSpan timeout, string name)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            var task = action(cts.Token);
            if (task.IsCompleted)
            {
                await task;
                return;
            }

            var delayTask = Task.Delay(timeout, cts.Token);
            var completedTask = await Task.WhenAny(task.AsTask(), delayTask);
            if (completedTask == delayTask)
                throw new TimeoutException($"Module {name} timed out after {timeout.TotalSeconds}s");

            await task;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException($"Module {name} timed out after {timeout.TotalSeconds}s");
        }
    }
    
    private static ValueTask ExecuteEntry() => ValueTask.CompletedTask;
    
    public async ValueTask Stasis()
    {
        await _gate.WaitAsync();
        try
        {
            await DoStasis();
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask DoStasis()
    {
        if (_loadedAssemblies.Count == 0) 
            return;

        if (_serviceProvider?.GetService<IDataIntentsRegistry>() is DataIntentsRegistry registry) 
            registry.EnterStasis();

        foreach (var module in _modules)
        {
            try
            {
                await ExecuteWithTimeout(module.Stasis, _activeTimeout, "Stasis");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SHUTDOWN ERROR] Module {module.GetType().Name} failed during stasis: {ex.Message}");
            }
        }

        _modules.Clear();
        _loadedAssemblies.Clear();
        
        (_serviceProvider as IDisposable)?.Dispose();
        _serviceProvider = null;
        
        await _runtime.Unload();
    }
    
    public Task WaitAsync() => _killSignal.Task;

    private async ValueTask Terminate()
    {
        await _gate.WaitAsync();
        try
        {
            Console.WriteLine("[DEAD] Closing application...");
            await DoStasis();
            _killSignal.TrySetResult();
        }
        finally
        {
            _gate.Release();
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

            var hash = val["hash"].AsString();
            if (string.IsNullOrEmpty(hash))
            {
                await ExtractAssetsRecursive(val.AsMap(), Path.Combine(relativePath, key));
                continue;
            }

            await using var stream = await _repository.OpenStream(hash);
            await _runtime.WriteAsset(Path.Combine(relativePath, key), stream);
        }
    }
}
