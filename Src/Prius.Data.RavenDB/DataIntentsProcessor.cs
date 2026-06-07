using System.Text;
using Raven.Client.Exceptions;
using Raven.Client.Documents.Subscriptions;

namespace Prius.Data.RavenDB;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents.Session;
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
        await Task.Factory.StartNew(async () => 
        {
            while (!ct.IsCancellationRequested)
            {
                var tx = await provider.PopTx(ct);
                await ProcessTransaction(tx, ct);
            }
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task ProcessTransaction(DataTransaction tx, CancellationToken ct)
    {
        using var session = holder.Store.OpenAsyncSession();
        session.Advanced.UseOptimisticConcurrency = true;

        var queuedWrites = new List<IIntent>();
        var streamsToDispose = new List<Stream>();
        var hasFailure = false;
        Exception? failureException = null;

        // Phase 1: Execute intents on the shared session
        foreach (var intent in tx.Intents)
        {
            if (ct.IsCancellationRequested)
                break;

            if (logger.IsEnabled(LogLevel.Debug))
                logger.LogDebug("Processing intent: {IntentInfo}", GetFullIntentInfo(intent));

            try
            {
                if (IsWrite(intent))
                {
                    await QueueWrite(session, intent, streamsToDispose);
                    queuedWrites.Add(intent);
                }
                else
                    await HandleReadOrImmediate(session, intent);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Transaction step failed for intent: {IntentInfo}", GetFullIntentInfo(intent));
                hasFailure = true;
                failureException = ex;
                
                // If it's a read/immediate, we should report its failure
                if (!IsWrite(intent)) 
                    ReportFailure(intent, ex);

                break; // Abort the transaction immediately
            }
        }

        // Phase 2: Save changes if no failure occurred
        if (!hasFailure && queuedWrites.Count > 0)
        {
            var retryCount = 0;
            while (true)
            {
                try
                {
                    await session.SaveChangesAsync(ct);
                    
                    // Success! Report success for all queued writes
                    foreach (var intent in queuedWrites)
                    {
                        ReportSuccess(intent, true);
                    }
                    break;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex) when (IsFatal(ex))
                {
                    logger.LogError(ex, "Fatal error committing transaction");
                    hasFailure = true;
                    failureException = ex;
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Transient error committing transaction, retrying...");
                    retryCount++;
                    if (retryCount >= MaxRetries)
                    {
                        hasFailure = true;
                        failureException = ex;
                        break;
                    }

                    var delay = TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 100);
                    await Task.Delay(delay, ct);
                }
            }
        }

        // Dispose streams after SaveChangesAsync completes
        foreach (var stream in streamsToDispose)
        {
            try
            {
                await stream.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to dispose stream for StoreAttachmentIntent");
            }
        }

        // If transaction failed, report failure for all write intents in the transaction
        if (hasFailure && failureException != null)
        {
            foreach (var intent in tx.Intents)
            {
                if (IsWrite(intent))
                {
                    ReportFailure(intent, failureException);
                }
            }
        }
    }

    private static bool IsWrite(IIntent intent) => intent switch
    {
        StoreIntent => true,
        PatchIntent => true,
        DeleteIntent => true,
        IncrementIntent => true,
        StoreAttachmentIntent => true,
        DeleteAttachmentIntent => true,
        NativeIntent => true,
        _ => false
    };

    private async Task QueueWrite(IAsyncDocumentSession session, IIntent intent, List<Stream> streamsToDispose)
    {
        switch (intent)
        {
            case StoreIntent store:
                await QueueStore(session, store);
                break;
            case PatchIntent patch:
                await QueuePatch(session, patch);
                break;
            case DeleteIntent delete:
                await QueueDelete(session, delete);
                break;
            case IncrementIntent increment:
                await QueueIncrement(session, increment);
                break;
            case StoreAttachmentIntent storeAttachment:
                await QueueStoreAttachment(session, storeAttachment, streamsToDispose);
                break;
            case DeleteAttachmentIntent deleteAttachment:
                await QueueDeleteAttachment(session, deleteAttachment);
                break;
            case NativeIntent native:
                await HandleNative(session, native);
                break;
            default:
                throw new ArgumentException($"Unknown write intent type: {intent.GetType().Name}");
        }
    }

    private async Task HandleReadOrImmediate(IAsyncDocumentSession session, IIntent intent)
    {
        switch (intent)
        {
            case LoadIntent load:
                await HandleLoad(session, load);
                break;
            case QueryIntent query:
                await HandleQuery(session, query);
                break;
            case GetCountersIntent getCounters:
                await HandleGetCounters(session, getCounters);
                break;
            case GetAttachmentsMetadataIntent getAttachmentsMetadata:
                await HandleGetAttachmentsMetadata(session, getAttachmentsMetadata);
                break;
            case GetAttachmentIntent getAttachment:
                await HandleGetAttachment(session, getAttachment);
                break;
            case SubscriptionIntent subscription:
                await HandleSubscription(subscription);
                break;
            default:
                throw new ArgumentException($"Unknown read/immediate intent type: {intent.GetType().Name}");
        }
    }

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
    
    private static void ReportSuccess(IIntent i, MapValue value)
    {
        var sysCtx = (ISystemElementContext)i.Context;
        var absolutePath = new MapPath(sysCtx.AbsolutePath.AsSpan()) + new MapPath(i.SuccessPath.AsSpan());
        sysCtx.PutAbsolute(absolutePath, value);
    }
    
    private static void ReportFailure(IIntent i, string message, string type)
    {
        var failureMap = DictionaryMap.New.With(
        [
            ("Message", message), 
            ("Type", type)
        ]);
        
        var sysCtx = (ISystemElementContext)i.Context;
        var absolutePath = new MapPath(sysCtx.AbsolutePath.AsSpan()) + new MapPath(i.FailurePath.AsSpan());
        sysCtx.PutAbsolute(absolutePath, failureMap.AsMapValue());
    }

    private static void ReportFailure(IIntent i, Exception ex) => ReportFailure(i, ex.Message, ex.GetType().Name);
    
    private async Task HandleQuery(IAsyncDocumentSession session, QueryIntent i)
    {
        var (rql, parameters) = RqlBuilder.Build(i.QueryMap);
        if (string.IsNullOrEmpty(rql))
            return;
        
        var query = session.Advanced.AsyncRawQuery<BlittableJsonReaderObject>(rql);
        foreach (var pair in parameters)
            query.AddParameter(pair.Key, pair.Value);

        var hasGroupBy = !i.QueryMap["GroupBy"].IsEmpty;
        var hasFacets = !i.QueryMap["Facets"].IsEmpty;

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
                [
                    ("Range", new MapValue(item.Range)),
                    ("Count", new MapValue(item.Count))
                ]);
                valuesList[idx.ToIndexString()] = itemMap.AsMapValue();
            }

            var facetData = DictionaryMap.New.With(
            [
                ("Name", new MapValue(pair.Key)),
                ("Values", valuesList.AsMapValue())
            ]);

            facetsMap[pair.Key] = facetData.AsMapValue();
        }

        return DictionaryMap.New.With(
        [
            ("Items", DictionaryMap.New.AsMapValue()), 
            ("Includes", DictionaryMap.New.AsMapValue()), 
            ("Order", DictionaryMap.New.AsMapValue()),
            ("Facets", facetsMap.AsMapValue()) 
        ]);
    }

    private static async Task<IMap> ExecuteStandardQuery(IAsyncRawDocumentQuery<BlittableJsonReaderObject> query, CancellationToken token, IMap queryMap)
    {
        var queryResult = await ((AsyncDocumentQuery<BlittableJsonReaderObject>) query).GetQueryResultAsync(token);
        
        var results = queryResult.Results;
        var items = DictionaryMap.New;
        var order = DictionaryMap.New;
        var highlightsMap = DictionaryMap.New;
        var includesMap = DictionaryMap.New;
        
        var groupByMap = queryMap["GroupBy"].AsMap();
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
            
            items[id] = (await json.AsJsonReaderMap()).AsMapValue();
            order[idx.ToIndexString()] = new MapValue(id);
        }
        
        if (queryResult.Includes != null)
        {
            foreach (var propertyName in queryResult.Includes.GetPropertyNames())
            {
                if (queryResult.Includes.TryGet(propertyName, out BlittableJsonReaderObject linkedDoc)) 
                    includesMap[propertyName] = (await linkedDoc.AsJsonReaderMap()).AsMapValue();
            }
        }
        
        if (!queryMap["Highlight"].IsEmpty && queryResult.Highlightings != null)
        {
            var originalField = queryMap["Highlight"]["Field"].AsString();
            if (queryResult.Highlightings.TryGetValue(originalField, out var docsWithHighlights))
            {
                foreach (var pair in docsWithHighlights)
                {
                    var docId = pair.Key;
                    var fragments = pair.Value;
                    var fragmentsList = DictionaryMap.New;
                    for (var fIdx = 0; fIdx < fragments.Length; fIdx++)
                        fragmentsList[fIdx.ToIndexString()] = new MapValue(fragments[fIdx]);
                    highlightsMap[docId] = DictionaryMap.New.With(originalField, fragmentsList.AsMapValue()).AsMapValue();
                }
            }
        }

        return DictionaryMap.New.With(
        [
            ("Items", items.AsMapValue()),
            ("Includes", includesMap.AsMapValue()), 
            ("Order", order.AsMapValue()),
            ("Highlights", highlightsMap.AsMapValue())
        ]);
    }

    private async Task HandleLoad(IAsyncDocumentSession session, LoadIntent i)
    {
        var doc = await session.LoadAsync<BlittableJsonReaderObject>(i.DocumentId, i.Token);

        if (doc is null)
        {
            ReportFailure(i, $"Document not found {i.DocumentId}", "NotFound");
            return;
        }

        var map = await doc.AsJsonReaderMap();
        ReportSuccess(i, map.AsMapValue());
    }

    private Task QueueDelete(IAsyncDocumentSession session, DeleteIntent i)
    {
        if (!string.IsNullOrEmpty(i.ChangeVector))
            session.Advanced.Defer(new DeleteCommandData(i.DocumentId, i.ChangeVector));
        else
            session.Delete(i.DocumentId);
        return Task.CompletedTask;
    }

    private async Task QueueStore(IAsyncDocumentSession session, StoreIntent i)
    {
        if (i.Document.DeepGet("@metadata/@collection").IsEmpty)
        {
            throw new InvalidOperationException("No @metadata/@collection specified");
        }
        
        if (logger.IsEnabled(LogLevel.Debug))
            logger.LogDebug("Queuing store document: {Id}", i.Document.Serialize());
            
        var id = i.Document.DeepGet("@metadata/@id").AsString();
        var changeVector = i.Document.DeepGet("@metadata/@change-vector").AsString();
        
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(i.Document.Serialize()));
        var blittable = await session.Advanced.Context.ReadForMemoryAsync(stream, id, i.Token);

        if (!string.IsNullOrEmpty(changeVector))
        {
            await session.StoreAsync(blittable, changeVector, id);
        }
        else
        {
            await session.StoreAsync(blittable, id);
        }
    }

    private Task QueuePatch(IAsyncDocumentSession session, PatchIntent i)
    {
        var (script, values) = CreatePatchRequest(i.Path, i.Value);
        session.Advanced.Defer(new PatchCommandData(i.DocumentId, null, new PatchRequest { Script = script, Values = values }));
        return Task.CompletedTask;
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
    
    private Task QueueIncrement(IAsyncDocumentSession session, IncrementIntent i)
    {
        session.CountersFor(i.DocumentId).Increment(i.CounterName, i.Delta);
        return Task.CompletedTask;
    }

    private async Task HandleGetCounters(IAsyncDocumentSession session, GetCountersIntent i)
    {
        var counters = await session.CountersFor(i.DocumentId).GetAllAsync();
        
        var result = DictionaryMap.New;
        foreach (var counter in counters)
            result[counter.Key] = counter.Value.AsMapValue();

        ReportSuccess(i, result.AsMapValue());
    }
    
    private async Task HandleGetAttachmentsMetadata(IAsyncDocumentSession session, GetAttachmentsMetadataIntent i)
    {
        var doc = await session.LoadAsync<BlittableJsonReaderObject>(i.DocumentId, i.Token);
        
        if (doc is null || 
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

            result[name] = (await attachmentObj.AsJsonReaderMap()).AsMapValue();
        }

        ReportSuccess(i, result.AsMapValue());
    }

    private Task QueueStoreAttachment(IAsyncDocumentSession session, StoreAttachmentIntent i, List<Stream> streamsToDispose)
    {
        session.Advanced.Attachments.Store(i.DocumentId, i.Name, i.Stream, i.ContentType);
        streamsToDispose.Add(i.Stream);
        return Task.CompletedTask;
    }

    private async Task HandleGetAttachment(IAsyncDocumentSession session, GetAttachmentIntent i)
    {
        using var attachmentResult = await session.Advanced.Attachments.GetAsync(i.DocumentId, i.Name, i.Token);
        if (attachmentResult == null)
        {
            ReportFailure(i, $"Attachment '{i.Name}' not found for document '{i.DocumentId}'", "NotFound");
            return;
        }
        
        var binaryPathStr = $"Attachments/{i.DocumentId}/{i.Name}";
        var targetBinaryPath = new MapPath(binaryPathStr);

        var metadataMap = DictionaryMap.New.With(
        [
            ("ContentType", new MapValue(attachmentResult.Details.ContentType)),
            ("Size", new MapValue(attachmentResult.Details.Size)),
            ("Hash", new MapValue(attachmentResult.Details.Hash))
        ]);
        
        binaryManager.Store(targetBinaryPath, metadataMap.AsMapValue(), attachmentResult.Stream);
        ReportSuccess(i, DictionaryMap.New.With(binaryPathStr, metadataMap.AsMapValue()).AsMapValue());
    }

    private Task QueueDeleteAttachment(IAsyncDocumentSession session, DeleteAttachmentIntent i)
    {
        session.Advanced.Attachments.Delete(i.DocumentId, i.Name);
        return Task.CompletedTask;
    }

    private async Task HandleNative(IAsyncDocumentSession session, NativeIntent i) => await i.Action(session, i);
    
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
                        var sysCtx = (ISystemElementContext)i.Context;
                        foreach (var item in batch.Items)
                        {
                            var map = await item.Result.AsJsonReaderMap();
                            var relativePath = new MapPath(i.SubscriptionPath.AsSpan()) + new MapPath(item.Id.AsSpan());
                            var absolutePath = new MapPath(sysCtx.AbsolutePath.AsSpan()) + relativePath;
                            sysCtx.PutAbsolute(absolutePath, map.AsMapValue());
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
