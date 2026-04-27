namespace Prius.App;

using System.Runtime.Loader;
using System.Runtime.InteropServices;
using Core.Maps;
using Core.Packages;

public sealed class Bootstrap
{
    private AssemblyLoadContext? _bodyContext;
    private readonly IPackageRepository _repository;
    private readonly string _currentTfm;
    private readonly TaskCompletionSource _killSignal = new();

    //TODO: Remove
    public IMap StartupTargets { get; init; } = EmptyMap.Instance;

    public Bootstrap(IPackageRepository repository)
    {
        _repository = repository;
        
        var version = Environment.Version;
        _currentTfm = $"net{version.Major}.{version.Minor}";

        _repository.OnStasisRequested += Stasis;
        _repository.OnBirthRequested += Birth;
        _repository.OnKillRequested += Kill;
        
        Console.WriteLine($"[BOOT] Target Framework: {_currentTfm}");
    }

    public Task WaitAsync() => _killSignal.Task;

    public async ValueTask Birth()
    {
        try 
        {
            if (_bodyContext != null)
                await Stasis();

            var targets = StartupTargets;
            if (targets.IsEmpty)
            {
                Console.WriteLine("[WARN] No targets for Birth.");
                return;
            }

            Console.WriteLine($"[BIRTH] Initializing world for {_currentTfm}...");

            var snapshot = await new PackageResolver(_repository).Resolve(_currentTfm, targets);
            var order = snapshot.Get("Order").AsMap();
            
            if (order.IsEmpty)
            {
                Console.WriteLine("[WARN] Birth failed: empty dependency graph.");
                return;
            }

            _bodyContext = new AssemblyLoadContext("Prius.Body", isCollectible: true);
            var manifests = snapshot.Get("Manifests").AsMap();

            foreach (var index in order.Keys(true))
            {
                var pkgId = order.Get(index).AsString();
                var manifest = manifests.Get(pkgId).AsMap();
                var version = manifest.DeepGet("Info/version").AsString();
                
                Console.WriteLine($"[LOAD] {pkgId} ({version})");
                await LoadPackage(manifest);
            }

            await ExecuteEntry();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIRTH ERROR] {ex.Message}");
        }
    }

    private async ValueTask LoadPackage(IMap manifest)
    {
        var libMap = manifest.DeepGet((MapPath)$"lib/{_currentTfm}").AsMap();
        if (libMap.IsEmpty)
            libMap = manifest.DeepGet((MapPath)"lib/any").AsMap();

        if (libMap.IsEmpty)
            return;

        foreach (var fileName in libMap.Keys())
        {
            if (!fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            var hash = libMap.Get(fileName).AsMap().Get("hash").AsString();
            await using var stream = await _repository.OpenStream(hash);
            
            _bodyContext!.LoadFromStream(stream);
        }
    }

    private async ValueTask ExecuteEntry()
    {
        foreach (var assembly in _bodyContext!.Assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.Name != "PriusEntry")
                    continue;

                var method = type.GetMethod("RunAsync", [typeof(IMap)]);
                if (method == null)
                    continue;

                Console.WriteLine($"[START] Calling {type.FullName}...");
                var instance = Activator.CreateInstance(type);
                await (ValueTask)method.Invoke(instance, [DictionaryMap.New])!;
                return;
            }
        }
        
        Console.WriteLine("[WARN] No entry point found.");
    }

    public async ValueTask Stasis()
    {
        if (_bodyContext == null)
            return;

        Console.WriteLine("[STASIS] Unloading world...");

        _bodyContext.Unload();
        _bodyContext = null;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.CompletedTask;
    }

    private async ValueTask Kill()
    {
        Console.WriteLine("[KILL] Killing process...");
        
        await Stasis();
        _killSignal.TrySetResult();
    }
}
