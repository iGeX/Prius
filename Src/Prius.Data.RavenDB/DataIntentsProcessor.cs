using System.Text;
using Raven.Client.Exceptions;
using Raven.Client.Documents.Subscriptions;

namespace Prius.Data.RavenDB;

using System;
using System.Collections.Generic;
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
    IBinaryManager binaryManager,
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

    private async Task ProcessLoop<T>(Func<CancellationToken, ValueTask<T>> popFunc, Func<T, Task> handler, CancellationToken ct) =>
        await Task.Factory.StartNew(async () => 
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
                        if (intent is IIntent ii)
                        {
                            logger.LogError(ex, "Fatal error processing intent {IntentInfo}", GetFullIntentInfo(intent));
                            ReportFailure(ii, ex);
                        }
                        break;
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (retryCount >= MaxRetries)
                        {
                            if (intent is IIntent ii)
                            {
                                if (logger.IsEnabled(LogLevel.Debug))
                                {
                                    logger.LogDebug(ex, "Failed to process intent {IntentInfo} after {RetryCount} retries",
                                        GetFullIntentInfo(intent), MaxRetries);
                                }

                                ReportFailure(ii, ex);
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
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

    private static bool IsFatal(Exception ex) => ex switch
    {
        ArgumentException => true,
        InvalidOperationException => true,
        ConcurrencyException => true,
        RavenException ravenEx when 
            ravenEx.Message.Contains("Syntax error") || 
            ravenEx.Message.Contains("Could not find field") ||
            ravenEx.Message.Contains("Unauthorized") => true,
        _ => false
    };

    private static string GetFullIntentInfo(object intent) => intent switch
    {
        LoadIntent l => $"Load(Id={l.DocumentId}, Out={l.SuccessPath})",
        QueryIntent q => $"Query(QueryMap={q.QueryMap.Serialize()})",
        StoreIntent s => $"Store(Document={s.Document.Serialize()})",
        PatchIntent p => $"Patch(Id={p.DocumentId}, Path={p.Path}, Val={p.Value})",
        DeleteIntent d => $"Delete(Id={d.DocumentId}, Vector={d.ChangeVector})",
        IncrementIntent i => $"Increment(Id={i.DocumentId}, Name={i.CounterName}, Delta={i.Delta})",
        GetCountersIntent gc => $"GetCounters(Id={gc.DocumentId}, Out={gc.SuccessPath})",
        GetAttachmentsMetadataIntent gam => $"GetAttachmentsMetadata(Id={gam.DocumentId}, Out={gam.SuccessPath})",
        StoreAttachmentIntent sa => $"StoreAttachment(Id={sa.DocumentId}, Name={sa.Name}, Type={sa.ContentType})",
        GetAttachmentIntent ga => $"GetAttachment(Id={ga.DocumentId}, Name={ga.Name}, Out={ga.SuccessPath})",
        DeleteAttachmentIntent da => $"DeleteAttachment(Id={da.DocumentId}, Name={da.Name})",
        NativeIntent _ => "Native",
        SubscriptionIntent sub => $"Subscription(Topic={sub.TopicName}, Path={sub.SubscriptionPath})",
        _ => intent.GetType().Name
    };
    
    private static void ReportSuccess(IIntent i, MapValue value) => i.Context.Put(i.SuccessPath, value);
    
    private static void ReportFailure(IIntent i, string message, string type)
    {
        var failureMap = DictionaryMap.New.With(
            ("Message", message), 
            ("Type", type));
        i.Context.Put(i.FailurePath, failureMap.AsMapValue());
    }

    private static void ReportFailure(IIntent i, Exception ex) => ReportFailure(i, ex.Message, ex.GetType().Name);
    
    private async Task HandleQuery(QueryIntent i)
    {
        var (rql, parameters) = RqlBuilder.Build(i.QueryMap);
        if (string.IsNullOrEmpty(rql))
            return;

        using var session = holder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        
        var query = session.Advanced.AsyncRawQuery<BlittableJsonReaderObject>(rql);
        foreach (var pair in parameters)
            query.AddParameter(pair.Key, pair.Value);

        var hasGroupBy = !i.QueryMap.Get("GroupBy").IsEmpty;
        var hasFacets = !i.QueryMap.Get("Facets").IsEmpty;

        var result = (hasFacets && !hasGroupBy)
            ? await ExecuteFacetQuery(query, i.Token)
            : await ExecuteStandardQuery(query, i.Token, i.QueryMap);

        ReportSuccess(i, result.AsMapValue());
    }

    private static async Task<IMap> ExecuteFacetQuery(IAsyncRawDocumentQuery<BlittableJsonReaderObject> query, CancellationToken token)
    {
        var facetResults = await query.ExecuteAggregationAsync(token);
        var facetsMap = DictionaryMap.New;

        foreach (var pair in facetResults)
        {
            var valuesList = DictionaryMap.New;
            for (var idx = 0; idx < pair.Value.Values.Count; idx++)
            {
                var item = pair.Value.Values[idx];
                var itemMap = DictionaryMap.New.With(
                    ("Range", new MapValue(item.Range)),
                    ("Count", new MapValue(item.Count))
                );
                valuesList.Put(idx.ToIndexString(), itemMap.AsMapValue());
            }

            var facetData = DictionaryMap.New.With(
                ("Name", new MapValue(pair.Key)),
                ("Values", valuesList.AsMapValue())
            );

            facetsMap.Put(pair.Key, facetData.AsMapValue());
        }

        return DictionaryMap.New.With(
            ("Items", DictionaryMap.New.AsMapValue()), 
            ("Includes", DictionaryMap.New.AsMapValue()), 
            ("Order", DictionaryMap.New.AsMapValue()),
            ("Facets", facetsMap.AsMapValue()) 
        );
    }

    private static async Task<IMap> ExecuteStandardQuery(IAsyncRawDocumentQuery<BlittableJsonReaderObject> query, CancellationToken token, IMap queryMap)
    {
        var queryResult = await ((AsyncDocumentQuery<BlittableJsonReaderObject>) query).GetQueryResultAsync(token);
        
        var results = queryResult.Results;
        var items = DictionaryMap.New;
        var order = DictionaryMap.New;
        var highlightsMap = DictionaryMap.New;
        var includesMap = DictionaryMap.New;
        
        var groupByMap = queryMap.Get("GroupBy").AsMap();
        var hasGroupBy = !groupByMap.IsEmpty;

        for (var idx = 0; idx < results.Length; idx++)
        {
            var doc = results[idx];
            if (doc is not BlittableJsonReaderObject json) 
                continue;
                
            string? id = null;
            
            if (json.TryGet("@metadata", out BlittableJsonReaderObject metadata) && metadata.TryGet("@id", out string metadataId))
                id = metadataId;
            else if (hasGroupBy)
            {
                var idBuilder = new StringBuilder();
                foreach (var groupKey in groupByMap.Keys(true))
                {
                    if (json.TryGet(groupKey, out object val) && val != null)
                    {
                        if (idBuilder.Length > 0) 
                            idBuilder.Append('/');
                        idBuilder.Append(val);
                    }
                }
                id = idBuilder.ToString();
            }

            if (string.IsNullOrEmpty(id))
                continue;
            
            items.Put(id, (await json.AsJsonReaderMap()).AsMapValue());
            order.Put(idx.ToIndexString(), new MapValue(id));
        }
        
        if (queryResult.Includes != null)
        {
            foreach (var propertyName in queryResult.Includes.GetPropertyNames())
            {
                if (queryResult.Includes.TryGet(propertyName, out BlittableJsonReaderObject linkedDoc)) 
                    includesMap.Put(propertyName, (await linkedDoc.AsJsonReaderMap()).AsMapValue());
            }
        }
        
        if (!queryMap.Get("Highlight").IsEmpty && queryResult.Highlightings != null)
        {
            var originalField = queryMap.Get("Highlight").AsMap().Get("Field").AsString();
            if (queryResult.Highlightings.TryGetValue(originalField, out var docsWithHighlights))
            {
                foreach (var pair in docsWithHighlights)
                {
                    var docId = pair.Key;
                    var fragments = pair.Value;
                    var fragmentsList = DictionaryMap.New;
                    for (var fIdx = 0; fIdx < fragments.Length; fIdx++)
                        fragmentsList.Put(fIdx.ToIndexString(), new MapValue(fragments[fIdx]));
                    highlightsMap.Put(docId, DictionaryMap.New.With(originalField, fragmentsList.AsMapValue()).AsMapValue());
                }
            }
        }

        return DictionaryMap.New.With(
            ("Items", items.AsMapValue()),
            ("Includes", includesMap.AsMapValue()), 
            ("Order", order.AsMapValue()),
            ("Highlights", highlightsMap.AsMapValue())
        );
    }

    private async Task HandleLoad(LoadIntent i)
    {
        using var session = holder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var doc = await session.LoadAsync<BlittableJsonReaderObject>(i.DocumentId, i.Token);

        if (doc is null)
        {
            ReportFailure(i, $"Document not found {i.DocumentId}", "NotFound");
            return;
        }

        var map = await doc.AsJsonReaderMap();
        ReportSuccess(i, map.AsMapValue());
    }

    private async Task HandleDelete(DeleteIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        session.Advanced.UseOptimisticConcurrency = true;

        if (!string.IsNullOrEmpty(i.ChangeVector))
            session.Advanced.Defer(new DeleteCommandData(i.DocumentId, i.ChangeVector));
        else
            session.Delete(i.DocumentId);

        await session.SaveChangesAsync(i.Token);
        ReportSuccess(i, true);
    }

    private async Task HandleStore(StoreIntent i)
    {
        if (i.Document.DeepGet("@metadata/@collection").IsEmpty)
        {
            ReportFailure(i, "No @metadata/@collection specified", "InvalidState");
            return;
        }
        
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Attempting to store document: {Id}", i.Document.Serialize());
            
        using var session = holder.Store.OpenAsyncSession();
        session.Advanced.UseOptimisticConcurrency = true;
        
        var changeVector = i.Document.DeepGet("@metadata/@change-vector").AsString();
        var command = new PutCommandData(i.Document.DeepGet("@metadata/@id").AsString(), changeVector, i.Document.AsDynamicJson());
        session.Advanced.Defer(command);
        
        await session.SaveChangesAsync(i.Token);
        
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Successfully saved document: {Id}", i.Document.Serialize());
        
        ReportSuccess(i, true);
    }

    private async Task HandlePatch(PatchIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        
        var (script, values) = CreatePatchRequest(i.Path, i.Value);
        session.Advanced.Defer(new PatchCommandData(i.DocumentId, null, new PatchRequest { Script = script, Values = values }));
        await session.SaveChangesAsync(i.Token);
        ReportSuccess(i, true);
    }

    private static (string Script, Dictionary<string, object> Values) CreatePatchRequest(MapPath path, MapValue value)
    {
        var values = new Dictionary<string, object> 
        { 
            ["Val"] = value.Match<object>(
                onEmpty: _ => null!,
                onMap: map => map.AsDynamicJson(),
                onValue: val => val
            )
        };

        if (path.IsEmpty)
            return ("Object.assign(this, args.Val);", values);

        var scriptBuilder = new StringBuilder();
        var currentPath = new StringBuilder("this");
        var remainingPath = path;
        var pIdx = 0;

        while (!remainingPath.IsEmpty)
        {
            var segment = remainingPath.Head;
            remainingPath = remainingPath.Tail;

            var pName = $"p_{pIdx++}";
            values[pName] = segment;

            currentPath.Append("[args.").Append(pName).Append("]");

            if (!remainingPath.IsEmpty)
            {
                scriptBuilder.Append(currentPath).Append(" = ").Append(currentPath).Append(" || {}; ");
                continue;
            }

            scriptBuilder.Append(currentPath).Append(" = args.Val;");
        }

        return (scriptBuilder.ToString(), values);
    }
    
    private async Task HandleIncrement(IncrementIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        session.CountersFor(i.DocumentId).Increment(i.CounterName, i.Delta);
        await session.SaveChangesAsync(i.Token);
        ReportSuccess(i, true);
    }

    private async Task HandleGetCounters(GetCountersIntent i)
    {
        using var session = holder.Store.OpenAsyncSession(new SessionOptions { NoTracking = true });
        var counters = await session.CountersFor(i.DocumentId).GetAllAsync();
        
        var result = DictionaryMap.New;
        foreach (var counter in counters)
            result.Put(counter.Key, counter.Value.AsMapValue());

        ReportSuccess(i, result.AsMapValue());
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
            ReportSuccess(i, new MapValue());
            return;
        }

        var result = DictionaryMap.New;
        foreach (var obj in attachments)
        {
            if (obj is not BlittableJsonReaderObject attachmentObj) 
                continue;
            if (!attachmentObj.TryGet("Name", out string name)) 
                continue;

            result.Put(name, (await attachmentObj.AsJsonReaderMap()).AsMapValue());
        }

        ReportSuccess(i, result.AsMapValue());
    }

    private async Task HandleStoreAttachment(StoreAttachmentIntent i)
    {
        await using (i.Stream)
        {
            using var session = holder.Store.OpenAsyncSession();
            session.Advanced.Attachments.Store(i.DocumentId, i.Name, i.Stream, i.ContentType);
            await session.SaveChangesAsync(i.Token);
            ReportSuccess(i, true);
        }
    }

    private async Task HandleGetAttachment(GetAttachmentIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        
        using var attachmentResult = await session.Advanced.Attachments.GetAsync(i.DocumentId, i.Name, i.Token);
        if (attachmentResult == null)
        {
            ReportFailure(i, $"Attachment '{i.Name}' not found for document '{i.DocumentId}'", "NotFound");
            return;
        }

        var metadataMap = DictionaryMap.New.With(
            ("ContentType", new MapValue(attachmentResult.Details.ContentType)),
            ("Size", new MapValue(attachmentResult.Details.Size)),
            ("Hash", new MapValue(attachmentResult.Details.Hash))
        );

        var targetBinaryPath = new MapPath(i.SuccessPath);
        binaryManager.Store(targetBinaryPath, metadataMap.AsMapValue(), attachmentResult.Stream);
            
        ReportSuccess(i, true);
    }

    private async Task HandleDeleteAttachment(DeleteAttachmentIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        session.Advanced.Attachments.Delete(i.DocumentId, i.Name);
        await session.SaveChangesAsync(i.Token);
        ReportSuccess(i, true);
    }

    private async Task HandleNative(NativeIntent i) => await i.Action(holder.Store, i);
    
    private Task HandleSubscription(SubscriptionIntent i)
    {
        try
        {
            var options = new SubscriptionWorkerOptions(i.TopicName)
            {
                Strategy = SubscriptionOpeningStrategy.WaitForFree
            };
            
            _ = Task.Factory.StartNew(async () =>
            {
                try
                {
                    await using var worker = holder.Store.Subscriptions.GetSubscriptionWorker<BlittableJsonReaderObject>(options);
                    await worker.Run(async batch =>
                    {
                        foreach (var item in batch.Items)
                        {
                            var map = await item.Result.AsJsonReaderMap();
                            i.Context.Put($"{i.SubscriptionPath}/{item.Id}", map.AsMapValue());
                        }
                    }, i.Token);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Subscription worker failed for topic {Topic}", i.TopicName);
                }
            }, i.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            ReportSuccess(i, true);
            return Task.CompletedTask;
        }
        catch (Exception exception)
        {
            return Task.FromException(exception);
        }
    }
}
