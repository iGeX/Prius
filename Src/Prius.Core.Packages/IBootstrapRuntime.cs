namespace Prius.Core.Packages;

using System.Reflection;

public interface IBootstrapRuntime : IAsyncDisposable
{
    string Tfm { get; }
    
    ValueTask Prepare();
    
    ValueTask<Assembly> LoadAssembly(Stream stream);
    
    ValueTask WriteAsset(string relativePath, Stream stream);
    
    ValueTask Unload();
}
