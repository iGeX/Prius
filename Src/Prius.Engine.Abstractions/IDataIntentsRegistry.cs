namespace Prius.Engine.Abstractions;

using System.IO;
using System.Threading.Tasks;
using System;
using System.Threading;
using Core.Maps;

public interface IDataIntentsRegistry
{
    CancellationTokenSource Load(IReactorContext context, string documentId, MapPath successPath, MapPath failurePath);
    CancellationTokenSource Query(IReactorContext context, IMap queryMap, MapPath successPath, MapPath failurePath);
    CancellationTokenSource Store(IReactorContext context, IMap document, MapPath successPath, MapPath failurePath);
    CancellationTokenSource Patch(IReactorContext context, string documentId, string path, MapValue val, MapPath successPath, MapPath failurePath);
    CancellationTokenSource Delete(IReactorContext context, string documentId, string? vector, MapPath successPath, MapPath failurePath);
    CancellationTokenSource Increment(IReactorContext context, string documentId, string name, long delta, MapPath successPath, MapPath failurePath);
    CancellationTokenSource GetCounters(IReactorContext context, string documentId, MapPath successPath, MapPath failurePath);
    CancellationTokenSource GetAttachmentsMetadata(IReactorContext context, string documentId, MapPath successPath, MapPath failurePath);
    CancellationTokenSource StoreAttachment(IReactorContext context, string documentId, string name, Stream stream, string contentType, MapPath successPath, MapPath failurePath);
    CancellationTokenSource GetAttachment(IReactorContext context, string documentId, string name, MapPath successPath, MapPath failurePath);
    CancellationTokenSource DeleteAttachment(IReactorContext context, string documentId, string name, MapPath successPath, MapPath failurePath);
    CancellationTokenSource ExecuteNative(IReactorContext context, Func<object, NativeIntent, Task> nativeAction, MapPath successPath, MapPath failurePath);
    CancellationTokenSource Subscription(IReactorContext context, string topic, string dataPath, MapPath successPath, MapPath failurePath);
}

public record LoadIntent(IReactorContext Context, string DocumentId, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record QueryIntent(IReactorContext Context, IMap QueryMap, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record StoreIntent(IReactorContext Context, IMap Document, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record PatchIntent(IReactorContext Context, string DocumentId, string Path, MapValue Value, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record DeleteIntent(IReactorContext Context, string DocumentId, string? ChangeVector, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record IncrementIntent(IReactorContext Context, string DocumentId, string CounterName, long Delta, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record GetCountersIntent(IReactorContext Context, string DocumentId, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record GetAttachmentsMetadataIntent(IReactorContext Context, string DocumentId, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record StoreAttachmentIntent(IReactorContext Context, string DocumentId, string Name, Stream Stream, string ContentType, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record GetAttachmentIntent(IReactorContext Context, string DocumentId, string Name, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record DeleteAttachmentIntent(IReactorContext Context, string DocumentId, string Name, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record NativeIntent(IReactorContext Context, Func<object, NativeIntent, Task> Action, string SuccessPath, string FailurePath, CancellationToken Token);
public record SubscriptionIntent(IReactorContext Context, string TopicName, string SubscriptionPath, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
