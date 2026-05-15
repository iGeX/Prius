namespace Prius.Data.RavenDB;

using System;
using System.Threading;
using System.Threading.Tasks;
using Prius.Engine.Abstractions;

public class DataIntentsProcessor
{
    private readonly DocumentStoreHolder _holder;
    private readonly IDataIntentsProvider _provider;

    public DataIntentsProcessor(DocumentStoreHolder holder, IDataIntentsProvider provider)
    {
        _holder = holder;
        _provider = provider;
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
            catch (OperationCanceledException) { break; }
            catch (Exception ex) { /* Log error */ }
        }
    }

    private async Task HandleLoad(LoadIntent i) => await Task.CompletedTask;
    private async Task HandleQuery(QueryIntent i) => await Task.CompletedTask;
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
