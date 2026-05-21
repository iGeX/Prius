using System.Reflection;

namespace Prius.Engine.Abstractions;

public interface IBootstrapRuntime : IAsyncDisposable
{
    string Tfm { get; }
    
    ValueTask Prepare();
    
    ValueTask<Assembly> LoadAssembly(Stream stream);
    
    ValueTask WriteAsset(string relativePath, Stream stream);
    
    ValueTask Unload();
}
