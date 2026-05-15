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

    public void Load(string id, string output, string failure) => _loads.Writer.TryWrite(new LoadIntent(id, output, failure));
    public void Query(IMap queryMap, string output, string failure) => _queries.Writer.TryWrite(new QueryIntent(queryMap, output, failure));
    public void Store(string id, IMap map, string? vector, string failure) => _stores.Writer.TryWrite(new StoreIntent(id, map, vector, failure));
    public void Patch(string id, string path, MapValue val, string failure) => _patches.Writer.TryWrite(new PatchIntent(id, path, val, failure));
    public void Delete(string id, string? vector, string failure) => _deletes.Writer.TryWrite(new DeleteIntent(id, vector, failure));
    public void Increment(string id, string name, long delta, string failure) => _increments.Writer.TryWrite(new IncrementIntent(id, name, delta, failure));
    public void GetCounters(string id, string output, string failure) => _getCounters.Writer.TryWrite(new GetCountersIntent(id, output, failure));
    public void GetAttachmentsMetadata(string id, string output, string failure) => _getAttachmentsMetadata.Writer.TryWrite(new GetAttachmentsMetadataIntent(id, output, failure));
    public void StoreAttachment(string id, string name, Stream stream, string contentType, string failure) => _storeAttachments.Writer.TryWrite(new StoreAttachmentIntent(id, name, stream, contentType, failure));
    public void GetAttachment(string id, string name, string output, string failure) => _getAttachments.Writer.TryWrite(new GetAttachmentIntent(id, name, output, failure));
    public void DeleteAttachment(string id, string name, string failure) => _deleteAttachments.Writer.TryWrite(new DeleteAttachmentIntent(id, name, failure));
    public void ExecuteNative<T>(Func<T, Task> nativeAction) where T : class => _natives.Writer.TryWrite(new NativeIntent(nativeAction));
    public void Subscription(string topic, string dataPath, string failure) => _subscriptions.Writer.TryWrite(new SubscriptionIntent(topic, dataPath, failure));
}
