using System.Reflection;
using System.Runtime.InteropServices;

namespace Prius.App;

using System.Runtime.Loader;
using Core.Maps;
using Core.Packages;

public sealed class Bootstrap
{
    private sealed class CustomLoadContext(string workDir, string rid) 
        : AssemblyLoadContext("Prius.Body", isCollectible: true)
    {
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var files = Directory.GetFiles(workDir, $"{unmanagedDllName}*", SearchOption.AllDirectories);
            var found = files.FirstOrDefault(f => 
                f.Contains(rid, StringComparison.OrdinalIgnoreCase) && 
                (f.EndsWith(".dll") || f.EndsWith(".so") || f.EndsWith(".dylib")));

            return found != null ? NativeLibrary.Load(found) : IntPtr.Zero;
        }
    }
    
    private AssemblyLoadContext? _bodyContext;
    private string? _currentWorkDir;
    private readonly IPackageRepository _repository;
    private readonly string _currentTfm;
    private readonly TaskCompletionSource _killSignal = new();
    private readonly List<Assembly> _loadedAssemblies = [];

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

            if (StartupTargets.IsEmpty)
                return;

            Console.WriteLine($"[BIRTH] Initializing world for {_currentTfm}...");

            _currentWorkDir = Path.Combine(Path.GetTempPath(), "Prius", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_currentWorkDir);
            Directory.SetCurrentDirectory(_currentWorkDir);

            var snapshot = await new PackageResolver(_repository).Resolve(_currentTfm, StartupTargets);
            var order = snapshot.Get("Order").AsMap();
            var manifests = snapshot.Get("Manifests").AsMap();

            _bodyContext = new CustomLoadContext(_currentWorkDir, GetCurrentRid());

            foreach (var index in order.Keys(true))
            {
                var pkgId = order.Get(index).AsString();
                var manifest = manifests.Get(pkgId).AsMap();
                
                Console.WriteLine($"[LOAD] {pkgId} ({manifest.DeepGet("Info/version").AsString()})");
                await ExtractAndLoad(manifest);
            }

            await ExecuteEntry();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BIRTH ERROR] {ex.Message}");
            throw;
        }
    }

    private async ValueTask ExtractAndLoad(IMap manifest)
    {
        var assets = manifest.Get("Assets").AsMap();
        
        var libMap = FrameworkConstants.GetCompatible(_currentTfm)
            .Select(tfm => assets.DeepGet((MapPath)$"lib/{tfm}").AsMap())
            .FirstOrDefault(branch => !branch.IsEmpty) ?? EmptyMap.Instance;

        if (!libMap.IsEmpty)
            await LoadAssembliesRecursive(libMap, string.Empty);
        
        var runtimesMap = assets.Get("runtimes").AsMap();
        if (!runtimesMap.IsEmpty)
            await ExtractAssets(runtimesMap, Path.Combine(_currentWorkDir!, "runtimes"));
        
        var contentFiles = assets.Get("contentFiles").AsMap();
        foreach (var codeTfm in contentFiles.Keys())
        {
            var tfmMap = contentFiles.Get(codeTfm).AsMap();
            foreach (var specificTfm in tfmMap.Keys())
                await ExtractAssets(tfmMap.Get(specificTfm).AsMap(), _currentWorkDir!);
        }
    }

    private async ValueTask ExtractAssets(IMap map, string targetDir)
    {
        if (!Directory.Exists(targetDir))
            Directory.CreateDirectory(targetDir);

        foreach (var key in map.Keys())
        {
            var val = map.Get(key);
            var fullPath = Path.Combine(targetDir, key);

            if (!val.IsMap)
                continue;
            
            var subMap = val.AsMap();
            var hash = subMap.Get("hash").AsString();

            if (string.IsNullOrEmpty(hash))
            {
                await ExtractAssets(subMap, fullPath);
                continue;
            }
            
            await using var src = await _repository.OpenStream(hash);
            await using var dst = File.Create(fullPath);
            await src.CopyToAsync(dst);
        }
    }

    private async ValueTask LoadAssembliesRecursive(IMap map, string subPath)
    {
        foreach (var key in map.Keys())
        {
            var val = map.Get(key);
            if (!val.IsMap)
                continue;
            
            var nextPath = Path.Combine(subPath, key);
            if (!key.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                await LoadAssembliesRecursive(val.AsMap(), nextPath);
                continue;
            }
            
            var hash = val.AsMap().Get("hash").AsString();
            if (string.IsNullOrEmpty(hash))
                continue;

            await using var stream = await _repository.OpenStream(hash);
            _loadedAssemblies.Add(_bodyContext!.LoadFromStream(stream));
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

                var method = type.GetMethod("RunAsync", []);
                if (method == null)
                    continue;

                Console.WriteLine($"[START] {type.FullName}");
                var instance = Activator.CreateInstance(type);
                await (ValueTask)method.Invoke(instance, null)!;
                return;
            }
        }
        
        Console.WriteLine("[WARN] No entry point found.");
    }

    public ValueTask Stasis()
    {
        if (_bodyContext == null)
            return ValueTask.CompletedTask;
        
        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

        var dirToDelete = _currentWorkDir;
        Console.WriteLine("[STASIS] Unloading world...");

        _bodyContext.Unload();
        _bodyContext = null;
        _loadedAssemblies.Clear();
        
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        if (dirToDelete != null)
            _ = Task.Run(() => RetryDelete(dirToDelete, 5));
        
        return ValueTask.CompletedTask;
    }

    private static async Task RetryDelete(string path, int retries)
    {
        for (var i = 0; i < retries; i++)
        {
            try 
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
                
                return;
            }
            catch 
            {
                await Task.Delay(1000);
            }
        }
        
        Console.WriteLine($"[CLEANUP ERROR] Could not delete temp directory after {retries} attempts: {path}");
    }

    private async ValueTask Kill()
    {
        Console.WriteLine("[KILL] Closing application...");
        await Stasis();
        _killSignal.TrySetResult();
    }
    
    private static string GetCurrentRid()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
            RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" :
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx" : "any";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.X86 => "x86",
            Architecture.Arm64 => "arm64",
            Architecture.Arm => "arm",
            _ => "any"
        };

        return $"{os}-{arch}";
    }
}
