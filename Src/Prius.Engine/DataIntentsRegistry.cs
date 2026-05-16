namespace Prius.Engine;

using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using Abstractions;
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

    public CancellationTokenSource Load(IReactorContext context, string documentId, MapPath outputPath, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _loads.Writer.TryWrite(new LoadIntent(context, documentId, outputPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource Query(IReactorContext context, IMap queryMap, MapPath outputPath, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _queries.Writer.TryWrite(new QueryIntent(context, queryMap, outputPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource Store(IReactorContext context, IMap map, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _stores.Writer.TryWrite(new StoreIntent(context, map, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource Patch(IReactorContext context, string documentId, string path, MapValue val, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _patches.Writer.TryWrite(new PatchIntent(context, documentId, path, val, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource Delete(IReactorContext context, string documentId, string? vector, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _deletes.Writer.TryWrite(new DeleteIntent(context, documentId, vector, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource Increment(IReactorContext context, string documentId, string name, long delta, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _increments.Writer.TryWrite(new IncrementIntent(context, documentId, name, delta, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource GetCounters(IReactorContext context, string documentId, MapPath outputPath, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _getCounters.Writer.TryWrite(new GetCountersIntent(context, documentId, outputPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource GetAttachmentsMetadata(IReactorContext context, string documentId, MapPath outputPath, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _getAttachmentsMetadata.Writer.TryWrite(new GetAttachmentsMetadataIntent(context, documentId, outputPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource StoreAttachment(IReactorContext context, string documentId, string name, Stream stream, string contentType, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _storeAttachments.Writer.TryWrite(new StoreAttachmentIntent(context, documentId, name, stream, contentType, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource GetAttachment(IReactorContext context, string documentId, string name, MapPath outputPath, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _getAttachments.Writer.TryWrite(new GetAttachmentIntent(context, documentId, name, outputPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource DeleteAttachment(IReactorContext context, string documentId, string name, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _deleteAttachments.Writer.TryWrite(new DeleteAttachmentIntent(context, documentId, name, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource ExecuteNative(IReactorContext context, Func<object, Task> nativeAction, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _natives.Writer.TryWrite(new NativeIntent(context, nativeAction, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource Subscription(IReactorContext context, string topic, string dataPath, MapPath failurePath)
    {
        var cts = new CancellationTokenSource();
        _subscriptions.Writer.TryWrite(new SubscriptionIntent(context, topic, dataPath, failurePath, cts.Token));
        return cts;
    }
}
