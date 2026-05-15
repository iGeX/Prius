namespace Prius.Engine.Abstractions;

using System.Threading;
using System.Threading.Tasks;

public interface IDataIntentsProvider
{
    ValueTask<LoadIntent> PopLoad(CancellationToken ct);
    ValueTask<QueryIntent> PopQuery(CancellationToken ct);
    ValueTask<StoreIntent> PopStore(CancellationToken ct);
    ValueTask<PatchIntent> PopPatch(CancellationToken ct);
    ValueTask<DeleteIntent> PopDelete(CancellationToken ct);
    ValueTask<IncrementIntent> PopIncrement(CancellationToken ct);
    ValueTask<GetCountersIntent> PopGetCounters(CancellationToken ct);
    ValueTask<GetAttachmentsMetadataIntent> PopGetAttachmentsMetadata(CancellationToken ct);
    ValueTask<StoreAttachmentIntent> PopStoreAttachment(CancellationToken ct);
    ValueTask<GetAttachmentIntent> PopGetAttachment(CancellationToken ct);
    ValueTask<DeleteAttachmentIntent> PopDeleteAttachment(CancellationToken ct);
    ValueTask<NativeIntent> PopNative(CancellationToken ct);
    ValueTask<SubscriptionIntent> PopSubscription(CancellationToken ct);
}
