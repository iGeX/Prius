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
    private bool _inStasis;
    private readonly Lock _sync = new();
    
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

    public void EnterStasis()
    {
        lock (_sync) _inStasis = true;
    }

    public void ExitStasis()
    {
        lock (_sync) _inStasis = false;
    }

    private CancellationTokenSource? TryRegister()
    {
        lock (_sync)
        {
            if (_inStasis) return null;
            return new CancellationTokenSource();
        }
    }

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

    public CancellationTokenSource? Load(IReactorContext context, string documentId, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _loads.Writer.TryWrite(new LoadIntent(context, documentId, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? Query(IReactorContext context, IMap queryMap, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _queries.Writer.TryWrite(new QueryIntent(context, queryMap, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? Store(IReactorContext context, IMap map, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _stores.Writer.TryWrite(new StoreIntent(context, map, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? Patch(IReactorContext context, string documentId, string path, MapValue val, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _patches.Writer.TryWrite(new PatchIntent(context, documentId, path, val, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? Delete(IReactorContext context, string documentId, string? vector, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _deletes.Writer.TryWrite(new DeleteIntent(context, documentId, vector, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? Increment(IReactorContext context, string documentId, string name, long delta, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _increments.Writer.TryWrite(new IncrementIntent(context, documentId, name, delta, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? GetCounters(IReactorContext context, string documentId, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _getCounters.Writer.TryWrite(new GetCountersIntent(context, documentId, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? GetAttachmentsMetadata(IReactorContext context, string documentId, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _getAttachmentsMetadata.Writer.TryWrite(new GetAttachmentsMetadataIntent(context, documentId, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? StoreAttachment(IReactorContext context, string documentId, string name, Stream stream, string contentType, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _storeAttachments.Writer.TryWrite(new StoreAttachmentIntent(context, documentId, name, stream, contentType, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? GetAttachment(IReactorContext context, string documentId, string name, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _getAttachments.Writer.TryWrite(new GetAttachmentIntent(context, documentId, name, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? DeleteAttachment(IReactorContext context, string documentId, string name, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _deleteAttachments.Writer.TryWrite(new DeleteAttachmentIntent(context, documentId, name, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? ExecuteNative(IReactorContext context, Func<object, NativeIntent, Task> nativeAction, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _natives.Writer.TryWrite(new NativeIntent(context, nativeAction, successPath, failurePath, cts.Token));
        return cts;
    }

    public CancellationTokenSource? Subscription(IReactorContext context, string topic, string dataPath, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        _subscriptions.Writer.TryWrite(new SubscriptionIntent(context, topic, dataPath, successPath, failurePath, cts.Token));
        return cts;
    }
}
