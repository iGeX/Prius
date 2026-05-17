using Raven.Client.Documents;
using Raven.TestDriver;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;
using System.Threading.Channels;

namespace Prius.Data.RavenDB.Tests;

public sealed class DataIntentsProcessorTests : RavenTestDriver
{
    static DataIntentsProcessorTests()
    {
        ConfigureServer(new TestServerOptions { Licensing = { ThrowOnInvalidOrMissingLicense = false } });
    }

    private class TestDocumentStoreHolder(IDocumentStore store) : IDocumentStoreHolder
    {
        public IDocumentStore Store => store;
    }

    private class MockDataIntentsProvider : IDataIntentsProvider
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

    private class MockReactorContext : IReactorContext
    {
        public string Key => "test";
        public IMap Env => EmptyMap.Instance;
        public readonly Dictionary<string, MapValue> PutCalls = new();
        public void Put(MapPath path, MapValue value, IMap? envPatch = null) => PutCalls[path.ToString()] = value;
        public MapValue Get(MapPath path, IMap? envPatch = null) => Empty.Instance;
        public void Notify(IMap changedKeys) { }
    }

    private static async Task WaitForCompletion(MockDataIntentsProvider provider, CancellationToken ct)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (provider.PendingCount > 0 && DateTime.UtcNow < timeout)
            await Task.Delay(100, ct);

        if (provider.PendingCount == 0)
            await Task.Delay(500, ct);
    }

    private async Task ExecuteTest(IDocumentStore store, MockDataIntentsProvider provider, Func<Task> assertion)
    {
        using var processorCt = new CancellationTokenSource();
        using var assertCt = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var holder = new TestDocumentStoreHolder(store);
        var processor = new DataIntentsProcessor(holder, provider, NullLogger<DataIntentsProcessor>.Instance);
        
        var task = processor.StartAsync(processorCt.Token);
        await WaitForCompletion(provider, assertCt.Token);
        
        await processorCt.CancelAsync();
        try { await task; } catch (OperationCanceledException) { }

        await assertion();
    }

    [Fact]
    public async Task HandleStore_ShouldStoreDocument()
    {
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(new MockReactorContext(), DictionaryMap.New
                .With("@metadata", DictionaryMap.New
                    .With("@id", "users/1")
                    .With("@collection", "users")), "failures", CancellationToken.None)]
        };
        
        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, async () =>
        {
            using var session = store.OpenAsyncSession();
            var doc = await session.LoadAsync<dynamic>("users/1", CancellationToken.None);
            Assert.NotNull(doc);
        });
    }

    [Fact]
    public async Task HandleStore_ShouldRecordFailure_WhenNoIdSpecified()
    {
        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(context, DictionaryMap.New
                .With("Name", "John"), "failures/1", CancellationToken.None)]
        };

        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("failures/1"));
            Assert.Equal("No @metadata/@id specified", context.PutCalls["failures/1"].AsMap().Get("Message").AsString());
            return Task.CompletedTask;
        });
    }
    
    [Fact]
    public async Task HandleStore_ShouldRecordFailure_WhenNoCollectionSpecified()
    {
        var context = new MockReactorContext();
        var provider = new MockDataIntentsProvider
        {
            Stores = [new StoreIntent(context, DictionaryMap.New
                .With("Name", "John")
                .With("@metadata", DictionaryMap.New
                    .With("@id", "users/1")), "failures/1", CancellationToken.None)]
        };

        using var store = GetDocumentStore();
        await ExecuteTest(store, provider, () =>
        {
            Assert.True(context.PutCalls.ContainsKey("failures/1"));
            Assert.Equal("No @metadata/@collection specified", context.PutCalls["failures/1"].AsMap().Get("Message").AsString());
            return Task.CompletedTask;
        });
    }
}
