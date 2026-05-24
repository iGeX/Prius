using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class JsonMetadataRegistry(string filePath) : IMetadataRegistry
{
    public event Func<ValueTask>? OnTransitionToStasis;
    public event Func<ValueTask>? OnTransitionToActive;
    public event Func<ValueTask>? OnTransitionToTerminated;

    public async ValueTask<IMap> GetBlueprint(CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
            return DictionaryMap.New;

        var bytes = await File.ReadAllBytesAsync(filePath, ct);
        return new JsonReaderMap(bytes);
    }
}