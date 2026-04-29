using Prius.Core.Packages;

namespace Prius.Blazor;

using System.Reflection;

public sealed class WebAssemblyBootstrapRuntime : IBootstrapRuntime
{
    public string Tfm => "browser-wasm";

    public async ValueTask Prepare() => await ValueTask.CompletedTask;

    public async ValueTask<Assembly> LoadAssembly(Stream stream)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return Assembly.Load(ms.ToArray());
    }

    public ValueTask WriteAsset(string relativePath, Stream stream) => ValueTask.CompletedTask;

    public ValueTask Unload() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => Unload();
}
