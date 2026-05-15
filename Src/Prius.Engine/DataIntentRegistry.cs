namespace Prius.Engine;

using System.Collections.Generic;
using System.IO;
using Core.Maps;
using Abstractions;

internal sealed class DataIntentRegistry : IDataIntentRegistry
{
    private readonly List<LoadIntent> _loads = [];
    private readonly List<QueryIntent> _queries = [];
    private readonly List<StoreIntent> _stores = [];
    private readonly List<PatchIntent> _patches = [];
    private readonly List<DeleteIntent> _deletes = [];
    private readonly List<IncrementIntent> _increments = [];
    private readonly List<GetCountersIntent> _getCounters = [];
    private readonly List<GetAttachmentsMetadataIntent> _getAttachmentsMeta = [];
    private readonly List<StoreAttachmentIntent> _storeAttachments = [];
    private readonly List<GetAttachmentIntent> _getAttachments = [];
    private readonly List<DeleteAttachmentIntent> _deleteAttachments = [];
    private readonly List<NativeIntent> _natives = [];
    private readonly List<SubscriptionIntent> _lives = [];

    public IReadOnlyList<LoadIntent> Loads => _loads;
    public IReadOnlyList<QueryIntent> Queries => _queries;
    public IReadOnlyList<StoreIntent> Stores => _stores;
    public IReadOnlyList<PatchIntent> Patches => _patches;
    public IReadOnlyList<DeleteIntent> Deletes => _deletes;
    public IReadOnlyList<IncrementIntent> Increments => _increments;
    public IReadOnlyList<GetCountersIntent> Counters => _getCounters;
    public IReadOnlyList<GetAttachmentsMetadataIntent> AttachmentsMetadata => _getAttachmentsMeta;
    public IReadOnlyList<StoreAttachmentIntent> StoreAttachments => _storeAttachments;
    public IReadOnlyList<GetAttachmentIntent> Attachments => _getAttachments;
    public IReadOnlyList<DeleteAttachmentIntent> DeleteAttachments => _deleteAttachments;
    public IReadOnlyList<NativeIntent> Natives => _natives;
    public IReadOnlyList<SubscriptionIntent> Subscriptions => _lives;

    public void Load(string id, string output, string failure) => _loads.Add(new LoadIntent(id, output, failure));
    public void Query(IMap queryMap, string output, string failure) => _queries.Add(new QueryIntent(queryMap, output, failure));
    public void Store(string id, IMap map, string? vector, string failure) => _stores.Add(new StoreIntent(id, map, vector, failure));
    public void Patch(string id, string path, MapValue val, string failure) => _patches.Add(new PatchIntent(id, path, val, failure));
    public void Delete(string id, string? vector, string failure) => _deletes.Add(new DeleteIntent(id, vector, failure));
    public void Increment(string id, string name, long delta, string failure) => _increments.Add(new IncrementIntent(id, name, delta, failure));
    public void GetCounters(string id, string output, string failure) => _getCounters.Add(new GetCountersIntent(id, output, failure));
    public void GetAttachmentsMetadata(string id, string output, string failure) => _getAttachmentsMeta.Add(new GetAttachmentsMetadataIntent(id, output, failure));
    public void StoreAttachment(string id, string name, Stream stream, string contentType, string failure) => _storeAttachments.Add(new StoreAttachmentIntent(id, name, stream, contentType, failure));
    public void GetAttachment(string id, string name, string output, string failure) => _getAttachments.Add(new GetAttachmentIntent(id, name, output, failure));
    public void DeleteAttachment(string id, string name, string failure) => _deleteAttachments.Add(new DeleteAttachmentIntent(id, name, failure));
    public void ExecuteNative<T>(Func<T, Task> nativeAction) where T : class => _natives.Add(new NativeIntent(nativeAction));
    public void Subscription(string topic, string dataPath, string failure) => _lives.Add(new SubscriptionIntent(topic, dataPath, failure));
}
