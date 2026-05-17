using System.Threading.Channels;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class MockDataIntentsProvider : IDataIntentsProvider
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

    private int _pendingCount;
    public int PendingCount => _pendingCount;

    private void Accept<T>(Channel<T> channel, IEnumerable<T> items)
    {
        foreach (var i in items)
        {
            channel.Writer.TryWrite(i);
            Interlocked.Increment(ref _pendingCount);
        }
    }

    public IEnumerable<LoadIntent> Loads { set => Accept(_loads, value); }
    public IEnumerable<QueryIntent> Queries { set => Accept(_queries, value); }
    public IEnumerable<StoreIntent> Stores { set => Accept(_stores, value); }
    public IEnumerable<PatchIntent> Patches { set => Accept(_patches, value); }
    public IEnumerable<DeleteIntent> Deletes { set => Accept(_deletes, value); }
    public IEnumerable<IncrementIntent> Increments { set => Accept(_increments, value); }
    public IEnumerable<GetCountersIntent> GetCounters { set => Accept(_getCounters, value); }
    public IEnumerable<GetAttachmentsMetadataIntent> GetAttachmentsMetadata { set => Accept(_getAttachmentsMetadata, value); }
    public IEnumerable<StoreAttachmentIntent> StoreAttachments { set => Accept(_storeAttachments, value); }
    public IEnumerable<GetAttachmentIntent> GetAttachments { set => Accept(_getAttachments, value); }
    public IEnumerable<DeleteAttachmentIntent> DeleteAttachments { set => Accept(_deleteAttachments, value); }
    public IEnumerable<NativeIntent> Natives { set => Accept(_natives, value); }
    public IEnumerable<SubscriptionIntent> Subscriptions { set => Accept(_subscriptions, value); }

    private async ValueTask<T> Pop<T>(Channel<T> channel, CancellationToken ct)
    {
        var intent = await channel.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pendingCount);
        return intent;
    }

    public ValueTask<LoadIntent> PopLoad(CancellationToken ct) => Pop(_loads, ct);
    public ValueTask<QueryIntent> PopQuery(CancellationToken ct) => Pop(_queries, ct);
    public ValueTask<StoreIntent> PopStore(CancellationToken ct) => Pop(_stores, ct);
    public ValueTask<PatchIntent> PopPatch(CancellationToken ct) => Pop(_patches, ct);
    public ValueTask<DeleteIntent> PopDelete(CancellationToken ct) => Pop(_deletes, ct);
    public ValueTask<IncrementIntent> PopIncrement(CancellationToken ct) => Pop(_increments, ct);
    public ValueTask<GetCountersIntent> PopGetCounters(CancellationToken ct) => Pop(_getCounters, ct);
    public ValueTask<GetAttachmentsMetadataIntent> PopGetAttachmentsMetadata(CancellationToken ct) => Pop(_getAttachmentsMetadata, ct);
    public ValueTask<StoreAttachmentIntent> PopStoreAttachment(CancellationToken ct) => Pop(_storeAttachments, ct);
    public ValueTask<GetAttachmentIntent> PopGetAttachment(CancellationToken ct) => Pop(_getAttachments, ct);
    public ValueTask<DeleteAttachmentIntent> PopDeleteAttachment(CancellationToken ct) => Pop(_deleteAttachments, ct);
    public ValueTask<NativeIntent> PopNative(CancellationToken ct) => Pop(_natives, ct);
    public ValueTask<SubscriptionIntent> PopSubscription(CancellationToken ct) => Pop(_subscriptions, ct);
}
