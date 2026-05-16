namespace Prius.Engine;

using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using Prius.Engine.Abstractions;
using Core.Maps;
using System.IO;
using System;

public class DataIntentsRegistry : IDataIntentsRegistry, IDataIntentsProvider
{
    private readonly Channel<LoadIntent> _loads = Channel.CreateUnbounded<LoadIntent>();
    private readonly Channel<QueryIntent> _queries = Channel.CreateUnbounded<QueryIntent>();
    private readonly Channel<StoreIntent> _stores = Channel.CreateUnbounded<StoreIntent>();
    private readonly Channel<PatchIntent> _patches = Channel.CreateUnbounded<PatchIntent>();
    private readonly Channel<DeleteIntent> _deletes = Channel.CreateUnbounded<DeleteIntent>();
    private readonly Channel<IncrementIntent> _increments = Channel.CreateUnbounded<IncrementIntent>();
    private readonly Channel<GetCountersIntent> _getCounters = Channel.CreateUnbounded<GetCountersIntent>();
    private readonly Channel<GetAttachmentsMetadataIntent> _getAttachmentsMetadata = Channel.CreateUnbounded<GetAttachmentsMetadataIntent>();
    private readonly Channel<StoreAttachmentIntent> _storeAttachments = Channel.CreateUnbounded<StoreAttachmentIntent>();
    private readonly Channel<GetAttachmentIntent> _getAttachments = Channel.CreateUnbounded<GetAttachmentIntent>();
    private readonly Channel<DeleteAttachmentIntent> _deleteAttachments = Channel.CreateUnbounded<DeleteAttachmentIntent>();
    private readonly Channel<NativeIntent> _natives = Channel.CreateUnbounded<NativeIntent>();
    private readonly Channel<SubscriptionIntent> _subscriptions = Channel.CreateUnbounded<SubscriptionIntent>();

    public async ValueTask<LoadIntent> PopLoad(CancellationToken ct) => await _loads.Reader.ReadAsync(ct);
    public async ValueTask<QueryIntent> PopQuery(CancellationToken ct) => await _queries.Reader.ReadAsync(ct);
    public async ValueTask<StoreIntent> PopStore(CancellationToken ct) => await _stores.Reader.ReadAsync(ct);
    public async ValueTask<PatchIntent> PopPatch(CancellationToken ct) => await _patches.Reader.ReadAsync(ct);
    public async ValueTask<DeleteIntent> PopDelete(CancellationToken ct) => await _deletes.Reader.ReadAsync(ct);
    public async ValueTask<IncrementIntent> PopIncrement(CancellationToken ct) => await _increments.Reader.ReadAsync(ct);
    public async ValueTask<GetCountersIntent> PopGetCounters(CancellationToken ct) => await _getCounters.Reader.ReadAsync(ct);
    public async ValueTask<GetAttachmentsMetadataIntent> PopGetAttachmentsMetadata(CancellationToken ct) => await _getAttachmentsMetadata.Reader.ReadAsync(ct);
    public async ValueTask<StoreAttachmentIntent> PopStoreAttachment(CancellationToken ct) => await _storeAttachments.Reader.ReadAsync(ct);
    public async ValueTask<GetAttachmentIntent> PopGetAttachment(CancellationToken ct) => await _getAttachments.Reader.ReadAsync(ct);
    public async ValueTask<DeleteAttachmentIntent> PopDeleteAttachment(CancellationToken ct) => await _deleteAttachments.Reader.ReadAsync(ct);
    public async ValueTask<NativeIntent> PopNative(CancellationToken ct) => await _natives.Reader.ReadAsync(ct);
    public async ValueTask<SubscriptionIntent> PopSubscription(CancellationToken ct) => await _subscriptions.Reader.ReadAsync(ct);

    public CancellationTokenSource Load(IReactorContext context, string id, string output, string failure)
    {
        var cts = new CancellationTokenSource();
        _loads.Writer.TryWrite(new LoadIntent(context, id, output, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource Query(IReactorContext context, IMap queryMap, string output, string failure)
    {
        var cts = new CancellationTokenSource();
        _queries.Writer.TryWrite(new QueryIntent(context, queryMap, output, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource Store(IReactorContext context, string id, IMap map, string? vector, string failure)
    {
        var cts = new CancellationTokenSource();
        _stores.Writer.TryWrite(new StoreIntent(context, id, map, vector, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource Patch(IReactorContext context, string id, string path, MapValue val, string failure)
    {
        var cts = new CancellationTokenSource();
        _patches.Writer.TryWrite(new PatchIntent(context, id, path, val, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource Delete(IReactorContext context, string id, string? vector, string failure)
    {
        var cts = new CancellationTokenSource();
        _deletes.Writer.TryWrite(new DeleteIntent(context, id, vector, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource Increment(IReactorContext context, string id, string name, long delta, string failure)
    {
        var cts = new CancellationTokenSource();
        _increments.Writer.TryWrite(new IncrementIntent(context, id, name, delta, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource GetCounters(IReactorContext context, string id, string output, string failure)
    {
        var cts = new CancellationTokenSource();
        _getCounters.Writer.TryWrite(new GetCountersIntent(context, id, output, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource GetAttachmentsMetadata(IReactorContext context, string id, string output, string failure)
    {
        var cts = new CancellationTokenSource();
        _getAttachmentsMetadata.Writer.TryWrite(new GetAttachmentsMetadataIntent(context, id, output, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource StoreAttachment(IReactorContext context, string id, string name, Stream stream, string contentType, string failure)
    {
        var cts = new CancellationTokenSource();
        _storeAttachments.Writer.TryWrite(new StoreAttachmentIntent(context, id, name, stream, contentType, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource GetAttachment(IReactorContext context, string id, string name, string output, string failure)
    {
        var cts = new CancellationTokenSource();
        _getAttachments.Writer.TryWrite(new GetAttachmentIntent(context, id, name, output, failure, cts.Token));
        return cts;
    }

    public CancellationTokenSource DeleteAttachment(IReactorContext context, string id, string name, string failure)
    {
        var cts = new CancellationTokenSource();
        _deleteAttachments.Writer.TryWrite(new DeleteAttachmentIntent(context, id, name, failure, cts.Token));
        return cts;
    }

    public void ExecuteNative<T>(IReactorContext context, Func<T, Task> nativeAction) where T : class
    {
        var cts = new CancellationTokenSource();
        _natives.Writer.TryWrite(new NativeIntent(context, nativeAction, cts.Token));
    }

    public CancellationTokenSource Subscription(IReactorContext context, string topic, string dataPath, string failure)
    {
        var cts = new CancellationTokenSource();
        _subscriptions.Writer.TryWrite(new SubscriptionIntent(context, topic, dataPath, failure, cts.Token));
        return cts;
    }
}
