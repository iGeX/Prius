using Prius.Core.Maps;
using Prius.Core.Packages;
using Prius.Engine;

if (args.Length == 0)
{
    Console.WriteLine("Usage: Prius.App.exe <Package/Version> ...");
    return;
}

var targets = DictionaryMap.New;
foreach (var arg in args)
{
    var path = (MapPath)arg;
    if (path.Tail.IsEmpty)
        continue;

    targets[path.Head] = path.Tail.ToString();
}

var repo = new DirectoryPackageRepository("./packages", new BinaryManager(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())));
var bootstrap = new Bootstrap(repo) 
{ 
    StartupTargets = targets 
};

Console.CancelKeyPress += (_, e) => 
{
    e.Cancel = true;
    Console.WriteLine("[CTRL+C] Shutting down...");
    bootstrap.Stasis().AsTask().GetAwaiter().GetResult();
    Environment.Exit(0);
};

try 
{
    await bootstrap.Activate();
    
    Console.WriteLine("[SYSTEM] Active. Waiting for signals...");
    await bootstrap.WaitAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"[FATAL] {ex.Message}");
}
