namespace Prius.Engine.Abstractions;

using System.IO;
using System.Threading.Tasks;
using System;
using System.Threading;
using Core.Maps;

public interface IDataIntentsRegistry
{
    CancellationTokenSource Load(IReactorContext context, string id, string output, string failure);
    CancellationTokenSource Query(IReactorContext context, IMap queryMap, string output, string failure);
    CancellationTokenSource Store(IReactorContext context, string id, IMap map, string? vector, string failure);
    CancellationTokenSource Patch(IReactorContext context, string id, string path, MapValue val, string failure);
    CancellationTokenSource Delete(IReactorContext context, string id, string? vector, string failure);
    CancellationTokenSource Increment(IReactorContext context, string id, string name, long delta, string failure);
    CancellationTokenSource GetCounters(IReactorContext context, string id, string output, string failure);
    CancellationTokenSource GetAttachmentsMetadata(IReactorContext context, string id, string output, string failure);
    CancellationTokenSource StoreAttachment(IReactorContext context, string id, string name, Stream stream, string contentType, string failure);
    CancellationTokenSource GetAttachment(IReactorContext context, string id, string name, string output, string failure);
    CancellationTokenSource DeleteAttachment(IReactorContext context, string id, string name, string failure);
    void ExecuteNative<T>(IReactorContext context, Func<T, Task> nativeAction) where T : class;
    CancellationTokenSource Subscription(IReactorContext context, string topic, string dataPath, string failure);
}

public record LoadIntent(IReactorContext Context, string DocumentId, string OutputPath, string FailurePath, CancellationToken Token) : IIntent;
public record QueryIntent(IReactorContext Context, IMap QueryMap, string OutputPath, string FailurePath, CancellationToken Token) : IIntent;
public record StoreIntent(IReactorContext Context, string DocumentId, IMap Map, string? ChangeVector, string FailurePath, CancellationToken Token) : IIntent;
public record PatchIntent(IReactorContext Context, string DocumentId, string Path, MapValue Value, string FailurePath, CancellationToken Token) : IIntent;
public record DeleteIntent(IReactorContext Context, string DocumentId, string? ChangeVector, string FailurePath, CancellationToken Token) : IIntent;
public record IncrementIntent(IReactorContext Context, string DocumentId, string CounterName, long Delta, string FailurePath, CancellationToken Token) : IIntent;
public record GetCountersIntent(IReactorContext Context, string DocumentId, string OutputPath, string FailurePath, CancellationToken Token) : IIntent;
public record GetAttachmentsMetadataIntent(IReactorContext Context, string DocumentId, string OutputPath, string FailurePath, CancellationToken Token) : IIntent;
public record StoreAttachmentIntent(IReactorContext Context, string DocumentId, string Name, Stream Stream, string ContentType, string FailurePath, CancellationToken Token) : IIntent;
public record GetAttachmentIntent(IReactorContext Context, string DocumentId, string Name, string OutputPath, string FailurePath, CancellationToken Token) : IIntent;
public record DeleteAttachmentIntent(IReactorContext Context, string DocumentId, string Name, string FailurePath, CancellationToken Token) : IIntent;
public record NativeIntent(IReactorContext Context, Delegate Action, CancellationToken Token) : IIntent { public string FailurePath => string.Empty; }
public record SubscriptionIntent(IReactorContext Context, string TopicName, string SubscriptionPath, string FailurePath, CancellationToken Token) : IIntent;
