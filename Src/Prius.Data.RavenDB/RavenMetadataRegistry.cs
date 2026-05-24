using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using Raven.Client.Documents.Session;
using Sparrow.Json;

namespace Prius.Data.RavenDB;

public sealed class RavenMetadataRegistry(
    IDocumentStoreHolder storeHolder, 
    string metadataDocumentId = "System/Blueprint",
    ILogger<RavenMetadataRegistry>? logger = null) : IMetadataRegistry
{
    public event Func<ValueTask>? OnTransitionToStasis;
    public event Func<ValueTask>? OnTransitionToActive;
    public event Func<ValueTask>? OnTransitionToTerminated;

    public async ValueTask<IMap> GetBlueprint(CancellationToken ct = default)
    {
        using var session = storeHolder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        
        var doc = await session.LoadAsync<BlittableJsonReaderObject>(metadataDocumentId, ct);
        if (doc is null)
        {
            logger?.LogWarning("Blueprint document '{Id}' not found in RavenDB.", metadataDocumentId);
            return DictionaryMap.New;
        }

        var map = await doc.AsJsonReaderMap();
        return map;
    }
}