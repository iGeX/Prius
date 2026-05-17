using System.Text;

namespace Prius.Data.RavenDB;

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Commands.Batches;
using Raven.Client.Documents.Operations;
using Sparrow.Json;
using Engine.Abstractions;
using Core.Maps;

public sealed class DataIntentsProcessor(
    IDocumentStoreHolder holder,
    IDataIntentsProvider provider,
    ILogger<DataIntentsProcessor> logger)
{
    private const int MaxRetries = 3;

    public async Task StartAsync(CancellationToken ct) =>
        await Task.WhenAll(
            ProcessLoop(provider.PopLoad, HandleLoad, ct),
            ProcessLoop(provider.PopQuery, HandleQuery, ct),
            ProcessLoop(provider.PopStore, HandleStore, ct),
            ProcessLoop(provider.PopPatch, HandlePatch, ct),
            ProcessLoop(provider.PopDelete, HandleDelete, ct),
            ProcessLoop(provider.PopIncrement, HandleIncrement, ct),
            ProcessLoop(provider.PopGetCounters, HandleGetCounters, ct),
            ProcessLoop(provider.PopGetAttachmentsMetadata, HandleGetAttachmentsMetadata, ct),
            ProcessLoop(provider.PopStoreAttachment, HandleStoreAttachment, ct),
            ProcessLoop(provider.PopGetAttachment, HandleGetAttachment, ct),
            ProcessLoop(provider.PopDeleteAttachment, HandleDeleteAttachment, ct),
            ProcessLoop(provider.PopNative, HandleNative, ct),
            ProcessLoop(provider.PopSubscription, HandleSubscription, ct)
        );

    private Task ProcessLoop<T>(Func<CancellationToken, ValueTask<T>> popFunc, Func<T, Task> handler, CancellationToken ct) =>
        Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested)
            {
                var intent = await popFunc(ct);
                var retryCount = 0;

                while (true)
                {
                    try
                    {
                        await handler(intent);
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        if (logger.IsEnabled(LogLevel.Debug))
                            logger.LogDebug("Intent {IntentType} was cancelled", intent?.GetType().Name);
                        break;
                    }
                    catch (Exception ex) when (IsFatal(ex))
                    {
                        if (intent is not null)
                        {
                            logger.LogError(ex, "Fatal error processing intent {IntentInfo}", GetFullIntentInfo(intent));
                            RecordFailure(intent, ex);
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (retryCount >= MaxRetries)
                        {
                            if (intent is not null)
                            {
                                if (logger.IsEnabled(LogLevel.Debug))
                                {
                                    logger.LogDebug(ex, "Failed to process intent {IntentInfo} after {RetryCount} retries",
                                        GetFullIntentInfo(intent), MaxRetries);
                                }

                                RecordFailure(intent, ex);
                            }

                            break;
                        }

                        if (intent is not null && logger.IsEnabled(LogLevel.Trace))
                            logger.LogTrace(ex, "Retry {RetryCount} for intent {IntentInfo}", retryCount, GetFullIntentInfo(intent));

                        var delay = TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 100);
                        await Task.Delay(delay, ct);
                    }
                }
            }
        }, ct);

    private static bool IsFatal(Exception ex) => ex switch
    {
        ArgumentException => true,
        Raven.Client.Exceptions.RavenException ravenEx when 
            ravenEx.Message.Contains("Syntax error") || 
            ravenEx.Message.Contains("Could not find field") ||
            ravenEx.Message.Contains("Unauthorized") => true,
        _ => false
    };

    private static string GetFullIntentInfo(object intent) => intent switch
    {
        LoadIntent l => $"Load(Id={l.DocumentId}, Out={l.OutputPath})",
        QueryIntent q => $"Query(QueryMap={q.QueryMap.Serialize()})",
        StoreIntent s => $"Store(Document={s.Document.Serialize()})",
        PatchIntent p => $"Patch(Id={p.DocumentId}, Path={p.Path}, Val={p.Value})",
        DeleteIntent d => $"Delete(Id={d.DocumentId}, Vector={d.ChangeVector})",
        IncrementIntent i => $"Increment(Id={i.DocumentId}, Name={i.CounterName}, Delta={i.Delta})",
        GetCountersIntent gc => $"GetCounters(Id={gc.DocumentId}, Out={gc.OutputPath})",
        GetAttachmentsMetadataIntent gam => $"GetAttachmentsMetadata(Id={gam.DocumentId}, Out={gam.OutputPath})",
        StoreAttachmentIntent sa => $"StoreAttachment(Id={sa.DocumentId}, Name={sa.Name}, Type={sa.ContentType})",
        GetAttachmentIntent ga => $"GetAttachment(Id={ga.DocumentId}, Name={ga.Name}, Out={ga.OutputPath})",
        DeleteAttachmentIntent da => $"DeleteAttachment(Id={da.DocumentId}, Name={da.Name})",
        NativeIntent _ => "Native",
        SubscriptionIntent sub => $"Subscription(Topic={sub.TopicName}, Path={sub.SubscriptionPath})",
        _ => intent.GetType().Name
    };

    private static void RecordFailure(object intent, Exception ex)
    {
        if (intent is not IIntent i)
            return;

        var failureMap = DictionaryMap.New.With(
            ("Message", ex.Message), 
            ("Type", ex.GetType().Name));
        i.Context.Put(i.FailurePath, failureMap.AsMapValue());
    }
    
    private async Task HandleQuery(QueryIntent i)
    {
        var (rql, parameters) = RqlBuilder.Build(i.QueryMap);
        if (string.IsNullOrEmpty(rql))
            return;

        using var session = holder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var query = session.Advanced.AsyncRawQuery<BlittableJsonReaderObject>(rql);

        foreach (var pair in parameters)
            query.AddParameter(pair.Key, pair.Value);

        var results = await query.ToListAsync(i.Token);

        var items = DictionaryMap.New;
        var order = DictionaryMap.New;

        for (var idx = 0; idx < results.Count; idx++)
        {
            var doc = results[idx];
            if (!doc.TryGet("@metadata", out BlittableJsonReaderObject metadata) || !metadata.TryGet("@id", out string id)) 
                continue;
            
            var wrapper = new BlittableMemoryWrapper(doc);
            items.Put(id, new JsonReaderMap(wrapper.Memory).AsMapValue());
            order.Put(idx.ToIndexString(), new MapValue(id));
        }

        var result = DictionaryMap.New.With(
            ("Items", items.AsMapValue()),
            ("Includes", DictionaryMap.New.AsMapValue()), 
            ("Order", order.AsMapValue())
        );

        i.Context.Put(i.OutputPath, result.AsMapValue());
    }
    
    private async Task HandleLoad(LoadIntent i)
    {
        using var session = holder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var doc = await session.LoadAsync<BlittableJsonReaderObject>(i.DocumentId, i.Token);

        if (doc is null)
            return;

        i.Context.Put(i.OutputPath, new JsonReaderMap(new BlittableMemoryWrapper(doc).Memory).AsMapValue());
    }

    private async Task HandleDelete(DeleteIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();

        if (!string.IsNullOrEmpty(i.ChangeVector))
            session.Advanced.Defer(new DeleteCommandData(i.DocumentId, i.ChangeVector));
        else
            session.Delete(i.DocumentId);

        await session.SaveChangesAsync(i.Token);
    }

    private async Task HandleStore(StoreIntent i)
    {
        var id = i.Document.DeepGet("@metadata/@id");
        if (id.IsEmpty)
        {
            ReportFailure(i, "No @metadata/@id specified", "InvalidState");
            return;
        }
        
        if (i.Document.DeepGet("@metadata/@collection").IsEmpty)
        {
            ReportFailure(i, "No @metadata/@collection specified", "InvalidState");
            return;
        }
        
        logger.LogInformation("Attempting to store document with ID: {Id}", id.AsString());
        using var session = holder.Store.OpenAsyncSession();
        
        var command = new PutCommandData(id, null, i.Document.ToDynamicJson());
        session.Advanced.Defer(command);
        await session.SaveChangesAsync(i.Token);
        logger.LogInformation("Successfully saved document with ID: {Id}", id.AsString());
    }

    private static void ReportFailure(StoreIntent i, string message, string type)
    {
        var failureMap = DictionaryMap.New.With(
            ("Message", message), 
            ("Type", type));
        i.Context.Put(i.FailurePath, failureMap.AsMapValue());
    }

    private async Task HandlePatch(PatchIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        
        session.Advanced.Defer(new PatchCommandData(i.DocumentId, null, CreatePatchRequest(i.Path, i.Value)));
        await session.SaveChangesAsync(i.Token);
    }
    
    private static PatchRequest CreatePatchRequest(MapPath path, MapValue value)
    {
        var values = new Dictionary<string, object> 
        { 
            ["Val"] = value.Match<object>(
                onEmpty: _ => null!,
                onMap: map => map.ToDynamicJson(),
                onValue: val => val
            )
        };

        if (path.IsEmpty)
        {
            return new PatchRequest
            {
                Script = "Object.assign(this, args.Val);",
                Values = values
            };
        }

        var scriptBuilder = new StringBuilder();
        var currentPath = new StringBuilder("this");
        var remainingPath = path;

        while (!remainingPath.IsEmpty)
        {
            var segment = remainingPath.Head;
            remainingPath = remainingPath.Tail;

            var escapedSegment = segment.Replace("'", "\\'");
            currentPath.Append("['").Append(escapedSegment).Append("']");

            if (!remainingPath.IsEmpty)
            {
                scriptBuilder.Append(currentPath).Append(" = ").Append(currentPath).Append(" || {}; ");
                continue;
            }

            scriptBuilder.Append(currentPath).Append(" = args.Val;");
        }

        return new PatchRequest
        {
            Script = scriptBuilder.ToString(),
            Values = values
        };
    }
    
    private async Task HandleIncrement(IncrementIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        session.CountersFor(i.DocumentId).Increment(i.CounterName, i.Delta);
        await session.SaveChangesAsync(i.Token);
    }

    private async Task HandleGetCounters(GetCountersIntent i)
    {
        using var session = holder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var counters = await session.CountersFor(i.DocumentId).GetAllAsync();
        
        var result = DictionaryMap.New;
        foreach (var counter in counters)
            result.Put(counter.Key, counter.Value.AsMapValue());

        i.Context.Put(i.OutputPath, result.AsMapValue());
    }
    
    private async Task HandleGetAttachmentsMetadata(GetAttachmentsMetadataIntent i)
    {
        var command = new GetDocumentsCommand(holder.Store.Conventions, i.DocumentId, null, metadataOnly: true);

        using var context = JsonOperationContext.ShortTermSingleUse();
        await holder.Store.GetRequestExecutor().ExecuteAsync(command, context);
        
        if (command.Result.Results is null || 
                command.Result.Results.Length == 0 ||
                command.Result.Results[0] is not BlittableJsonReaderObject doc ||
                !doc.TryGet("@metadata", out BlittableJsonReaderObject metadata) || 
                !metadata.TryGet("@attachments", out BlittableJsonReaderArray attachments))
        {
            i.Context.Put(i.OutputPath, new MapValue());
            return;
        }

        var result = DictionaryMap.New;
        
        foreach (var obj in attachments)
        {
            if (obj is not BlittableJsonReaderObject attachmentObj) 
                continue;
            if (!attachmentObj.TryGet("Name", out string name)) 
                continue;

            result.Put(name, new JsonReaderMap(new BlittableMemoryWrapper(attachmentObj).Memory));
        }

        i.Context.Put(i.OutputPath, result.AsMapValue());
    }

    private async Task HandleStoreAttachment(StoreAttachmentIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        session.Advanced.Attachments.Store(i.DocumentId, i.Name, i.Stream, i.ContentType);
        await session.SaveChangesAsync(i.Token);
    }

    private async Task HandleGetAttachment(GetAttachmentIntent i)
    {
        //TODO
    }

    private async Task HandleDeleteAttachment(DeleteAttachmentIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        session.Advanced.Attachments.Delete(i.DocumentId, i.Name);
        await session.SaveChangesAsync(i.Token);
    }

    private async Task HandleNative(NativeIntent i) => await i.Action(holder.Store);
    
    private async Task HandleSubscription(SubscriptionIntent i)
    {
        //TODO
    }
}
