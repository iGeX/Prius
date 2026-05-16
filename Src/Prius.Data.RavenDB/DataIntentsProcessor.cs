namespace Prius.Data.RavenDB;

using System;
using System.Threading;
using System.Threading.Tasks;
using Prius.Engine.Abstractions;

using Microsoft.Extensions.Logging;

public class DataIntentsProcessor
{
    private readonly DocumentStoreHolder _holder;
    private readonly IDataIntentsProvider _provider;
    private readonly ILogger<DataIntentsProcessor> _logger;

    public DataIntentsProcessor(
        DocumentStoreHolder holder, 
        IDataIntentsProvider provider, 
        ILogger<DataIntentsProcessor> logger)
    {
        _holder = holder;
        _provider = provider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        await Task.WhenAll(
            ProcessLoop(_provider.PopLoad, HandleLoad, ct),
            ProcessLoop(_provider.PopQuery, HandleQuery, ct),
            ProcessLoop(_provider.PopStore, HandleStore, ct),
            ProcessLoop(_provider.PopPatch, HandlePatch, ct),
            ProcessLoop(_provider.PopDelete, HandleDelete, ct),
            ProcessLoop(_provider.PopIncrement, HandleIncrement, ct),
            ProcessLoop(_provider.PopGetCounters, HandleGetCounters, ct),
            ProcessLoop(_provider.PopGetAttachmentsMetadata, HandleGetAttachmentsMetadata, ct),
            ProcessLoop(_provider.PopStoreAttachment, HandleStoreAttachment, ct),
            ProcessLoop(_provider.PopGetAttachment, HandleGetAttachment, ct),
            ProcessLoop(_provider.PopDeleteAttachment, HandleDeleteAttachment, ct),
            ProcessLoop(_provider.PopNative, HandleNative, ct),
            ProcessLoop(_provider.PopSubscription, HandleSubscription, ct)
        );
    }

    private async Task ProcessLoop<T>(Func<CancellationToken, ValueTask<T>> popFunc, Func<T, Task> handler, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var intent = await popFunc(ct);
                await handler(intent);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in intent processing loop for {IntentType}", typeof(T).Name);
            }
        }
    }

    private async Task HandleLoad(LoadIntent i)
    {
        try
        {
            using var session = _holder.Store.OpenAsyncSession();
            var document = await session.LoadAsync<object>(i.DocumentId);
            _logger.LogInformation("Loaded document: {DocumentId}", i.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load document: {DocumentId}", i.DocumentId);
        }
    }
    
    private async Task HandleQuery(QueryIntent i)
    {
        try
        {
            var (rql, parameters) = RqlBuilder.Build(i.QueryMap);
            if (string.IsNullOrEmpty(rql))
                return;

            using var session = _holder.Store.OpenAsyncSession();
            var query = session.Advanced.AsyncRawQuery<object>(rql);
            
            foreach (var param in parameters)
                query.AddParameter(param.Key, param.Value);

            var results = await query.ToListAsync();
            _logger.LogInformation("Executed query against index {Index} returning {Count} results", i.QueryMap.Get("From"), results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute query");
        }
    }
    private async Task HandleStore(StoreIntent i) => await Task.CompletedTask;
    private async Task HandlePatch(PatchIntent i) => await Task.CompletedTask;
    private async Task HandleDelete(DeleteIntent i) => await Task.CompletedTask;
    private async Task HandleIncrement(IncrementIntent i) => await Task.CompletedTask;
    private async Task HandleGetCounters(GetCountersIntent i) => await Task.CompletedTask;
    private async Task HandleGetAttachmentsMetadata(GetAttachmentsMetadataIntent i) => await Task.CompletedTask;
    private async Task HandleStoreAttachment(StoreAttachmentIntent i) => await Task.CompletedTask;
    private async Task HandleGetAttachment(GetAttachmentIntent i) => await Task.CompletedTask;
    private async Task HandleDeleteAttachment(DeleteAttachmentIntent i) => await Task.CompletedTask;
    private async Task HandleNative(NativeIntent i) => await Task.CompletedTask;
    private async Task HandleSubscription(SubscriptionIntent i) => await Task.CompletedTask;
}
