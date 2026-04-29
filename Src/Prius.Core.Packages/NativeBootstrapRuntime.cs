namespace Prius.Core.Packages;

using System.Reflection;
using System.Runtime.Loader;
using System.Runtime.InteropServices;

public sealed class NativeBootstrapRuntime : IBootstrapRuntime
{
    private readonly string _workDir = Path.Combine(Path.GetTempPath(), "Prius", Guid.NewGuid().ToString("N"));
    private readonly string _rid = GetCurrentRid();
    private CustomLoadContext? _context;

    public string Tfm { get; } = $"net{Environment.Version.Major}.{Environment.Version.Minor}";

    public async ValueTask Prepare()
    {
        if (!Directory.Exists(_workDir))
            Directory.CreateDirectory(_workDir);

        Directory.SetCurrentDirectory(_workDir);
        _context = new CustomLoadContext(_workDir, _rid);
        await ValueTask.CompletedTask;
    }

    public ValueTask<Assembly> LoadAssembly(Stream stream)
    {
        try
        {
            return _context == null 
                ? throw new InvalidOperationException("Runtime not prepared.") 
                : ValueTask.FromResult(_context.LoadFromStream(stream));
        }
        catch (Exception exception)
        {
            return ValueTask.FromException<Assembly>(exception);
        }
    }

    public async ValueTask WriteAsset(string relativePath, Stream stream)
    {
        var fullPath = Path.Combine(_workDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        await using var dst = File.Create(fullPath);
        await stream.CopyToAsync(dst);
    }

    public async ValueTask Unload()
    {
        if (_context == null)
            return;

        Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);
        _context.Unload();
        _context = null;

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        _ = Task.Run(() => RetryDelete(_workDir, 5));
        await ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await Unload();

    private static string GetCurrentRid()
    {
        var os = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win" :
                 RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux" : "osx";

        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "x86"
        };

        return $"{os}-{arch}";
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
    }

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
}
