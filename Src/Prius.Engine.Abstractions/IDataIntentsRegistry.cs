namespace Prius.Engine.Abstractions;

using System.IO;
using System.Threading.Tasks;
using System;
using System.Threading;
using Core.Maps;

public interface IDataIntentsRegistry
{
    CancellationTokenSource? Load(IElementContext context, string documentId, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? Query(IElementContext context, IMap queryMap, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? Store(IElementContext context, IMap document, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? Patch(IElementContext context, string documentId, string path, MapValue val, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? Delete(IElementContext context, string documentId, string? vector, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? Increment(IElementContext context, string documentId, string name, long delta, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? GetCounters(IElementContext context, string documentId, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? GetAttachmentsMetadata(IElementContext context, string documentId, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? StoreAttachment(IElementContext context, string documentId, string name, Stream stream, string contentType, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? GetAttachment(IElementContext context, string documentId, string name, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? DeleteAttachment(IElementContext context, string documentId, string name, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? ExecuteNative(IElementContext context, Func<object, NativeIntent, Task> nativeAction, MapPath successPath, MapPath failurePath);
    CancellationTokenSource? Subscription(IElementContext context, string topic, string dataPath, MapPath successPath, MapPath failurePath);
}

public record LoadIntent(IElementContext Context, string DocumentId, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record QueryIntent(IElementContext Context, IMap QueryMap, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record StoreIntent(IElementContext Context, IMap Document, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record PatchIntent(IElementContext Context, string DocumentId, string Path, MapValue Value, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record DeleteIntent(IElementContext Context, string DocumentId, string? ChangeVector, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record IncrementIntent(IElementContext Context, string DocumentId, string CounterName, long Delta, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record GetCountersIntent(IElementContext Context, string DocumentId, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record GetAttachmentsMetadataIntent(IElementContext Context, string DocumentId, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record StoreAttachmentIntent(IElementContext Context, string DocumentId, string Name, Stream Stream, string ContentType, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record GetAttachmentIntent(IElementContext Context, string DocumentId, string Name, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record DeleteAttachmentIntent(IElementContext Context, string DocumentId, string Name, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record NativeIntent(IElementContext Context, Func<object, NativeIntent, Task> Action, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
public record SubscriptionIntent(IElementContext Context, string TopicName, string SubscriptionPath, string SuccessPath, string FailurePath, CancellationToken Token) : IIntent;
