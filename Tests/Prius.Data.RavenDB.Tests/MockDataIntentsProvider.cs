using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using Prius.Engine.Abstractions;

namespace Prius.Data.RavenDB.Tests;

public class MockDataIntentsProvider : IDataIntentsProvider
{
    private readonly Channel<DataTransaction> _transactions = Channel.CreateUnbounded<DataTransaction>();

    private int _pendingCount;
    public int PendingCount => _pendingCount;

    private void Accept<T>(IEnumerable<T> items) where T : IIntent
    {
        foreach (var i in items)
        {
            var txContext = i.Context as ISystemElementContext ?? new MockElementContext();
            _transactions.Writer.TryWrite(new DataTransaction(txContext, new IIntent[] { i }));
            Interlocked.Increment(ref _pendingCount);
        }
    }

    public IEnumerable<LoadIntent> Loads { set => Accept(value); }
    public IEnumerable<QueryIntent> Queries { set => Accept(value); }
    public IEnumerable<StoreIntent> Stores { set => Accept(value); }
    public IEnumerable<PatchIntent> Patches { set => Accept(value); }
    public IEnumerable<DeleteIntent> Deletes { set => Accept(value); }
    public IEnumerable<IncrementIntent> Increments { set => Accept(value); }
    public IEnumerable<GetCountersIntent> GetCounters { set => Accept(value); }
    public IEnumerable<GetAttachmentsMetadataIntent> GetAttachmentsMetadata { set => Accept(value); }
    public IEnumerable<StoreAttachmentIntent> StoreAttachments { set => Accept(value); }
    public IEnumerable<GetAttachmentIntent> GetAttachments { set => Accept(value); }
    public IEnumerable<DeleteAttachmentIntent> DeleteAttachments { set => Accept(value); }
    public IEnumerable<NativeIntent> Natives { set => Accept(value); }
    public IEnumerable<SubscriptionIntent> Subscriptions { set => Accept(value); }

    public async ValueTask<DataTransaction> PopTx(CancellationToken ct)
    {
        var tx = await _transactions.Reader.ReadAsync(ct);
        Interlocked.Decrement(ref _pendingCount);
        return tx;
    }
}
