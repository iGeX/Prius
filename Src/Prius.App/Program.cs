using Prius.App;
using Prius.Core.Maps;
using Prius.Core.Packages;

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

    targets.Put(path.Head, path.Tail.ToString());
}

var repo = new DirectoryPackageRepository("./packages");
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
    await bootstrap.Birth();
    
    Console.WriteLine("[SYSTEM] Active. Waiting for signals...");
    await bootstrap.WaitAsync();
}
catch (Exception ex)
{
    Console.WriteLine($"[FATAL] {ex.Message}");
}
