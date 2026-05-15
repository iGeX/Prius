namespace Prius.Engine.Abstractions;

using System.Collections.Generic;
using System.IO;
using Core.Maps;

public interface IDataIntentRegistry
{
    IReadOnlyList<LoadIntent> Loads {get;}
    IReadOnlyList<QueryIntent> Queries {get;}
    IReadOnlyList<StoreIntent> Stores {get;}
    IReadOnlyList<PatchIntent> Patches {get;}
    IReadOnlyList<DeleteIntent> Deletes {get;}
    IReadOnlyList<IncrementIntent> Increments {get;}
    IReadOnlyList<GetCountersIntent> Counters {get;}
    IReadOnlyList<GetAttachmentsMetadataIntent> AttachmentsMetadata {get;}
    IReadOnlyList<StoreAttachmentIntent> StoreAttachments {get;}
    IReadOnlyList<GetAttachmentIntent> Attachments {get;}
    IReadOnlyList<DeleteAttachmentIntent> DeleteAttachments {get;}
    IReadOnlyList<NativeIntent> Natives {get;}
    IReadOnlyList<SubscriptionIntent> Subscriptions {get;}

    void Load(string id, string output, string failure);
    void Query(IMap queryMap, string output, string failure);
    void Store(string id, IMap map, string? vector, string failure);
    void Patch(string id, string path, MapValue val, string failure);
    void Delete(string id, string? vector, string failure);
    void Increment(string id, string name, long delta, string failure);
    void GetCounters(string id, string output, string failure);
    void GetAttachmentsMetadata(string id, string output, string failure);
    void StoreAttachment(string id, string name, Stream stream, string contentType, string failure);
    void GetAttachment(string id, string name, string output, string failure);
    void DeleteAttachment(string id, string name, string failure);
    void ExecuteNative<T>(Func<T, Task> nativeAction) where T : class;
    void Subscription(string topic, string dataPath, string failure);
}

public record LoadIntent(string DocumentId, string OutputPath, string FailurePath) { public object? LazyResult { get; set; } }
public record QueryIntent(IMap QueryMap, string OutputPath, string FailurePath) { public object? LazyResult { get; set; } }
public record StoreIntent(string DocumentId, IMap Map, string? ChangeVector, string FailurePath);
public record PatchIntent(string DocumentId, string Path, MapValue Value, string FailurePath);
public record DeleteIntent(string DocumentId, string? ChangeVector, string FailurePath);
public record IncrementIntent(string DocumentId, string CounterName, long Delta, string FailurePath);
public record GetCountersIntent(string DocumentId, string OutputPath, string FailurePath);
public record GetAttachmentsMetadataIntent(string DocumentId, string OutputPath, string FailurePath);
public record StoreAttachmentIntent(string DocumentId, string Name, Stream Stream, string ContentType, string FailurePath);
public record GetAttachmentIntent(string DocumentId, string Name, string OutputPath, string FailurePath);
public record DeleteAttachmentIntent(string DocumentId, string Name, string FailurePath);
public record NativeIntent(Delegate Action);
public record SubscriptionIntent(string TopicName, string SubscriptionPath, string FailurePath);
