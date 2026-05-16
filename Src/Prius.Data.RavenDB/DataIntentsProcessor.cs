using Raven.Client.Documents.Commands;
using Raven.Client.Documents.Commands.Batches;
using Raven.Client.Documents.Operations;
using Sparrow.Json.Parsing;

namespace Prius.Data.RavenDB;

using System;
using System.Threading;
using System.Threading.Tasks;
using Engine.Abstractions;
using Core.Maps;
using Raven.Client.Documents.Session;
using Sparrow.Json;
using Microsoft.Extensions.Logging;

public sealed class DataIntentsProcessor(
    DocumentStoreHolder holder,
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

    private async Task ProcessLoop<T>(Func<CancellationToken, ValueTask<T>> popFunc, Func<T, Task> handler, CancellationToken ct)
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
    }

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
        QueryIntent q => $"Query(Map={q.QueryMap.Serialize()})",
        StoreIntent s => $"Store(Id={s.DocumentId}, Vector={s.ChangeVector}, Map={s.Map.Serialize()})",
        PatchIntent p => $"Patch(Id={p.DocumentId}, Path={p.Path}, Val={p.Value})",
        DeleteIntent d => $"Delete(Id={d.DocumentId}, Vector={d.ChangeVector})",
        IncrementIntent i => $"Increment(Id={i.DocumentId}, Name={i.CounterName}, Delta={i.Delta})",
        GetCountersIntent gc => $"GetCounters(Id={gc.DocumentId}, Out={gc.OutputPath})",
        GetAttachmentsMetadataIntent gam => $"GetAttachmentsMetadata(Id={gam.DocumentId}, Out={gam.OutputPath})",
        StoreAttachmentIntent sa => $"StoreAttachment(Id={sa.DocumentId}, Name={sa.Name}, Type={sa.ContentType})",
        GetAttachmentIntent ga => $"GetAttachment(Id={ga.DocumentId}, Name={ga.Name}, Out={ga.OutputPath})",
        DeleteAttachmentIntent da => $"DeleteAttachment(Id={da.DocumentId}, Name={da.Name})",
        NativeIntent n => $"Native(Action={n.Action.Method.Name})",
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
        using var stream = new MemoryStream();
        i.Map.Serialize(stream);
        stream.Seek(0, SeekOrigin.Begin);
     
        using var session = holder.Store.OpenAsyncSession();
        using var blittableJson = await session.Advanced.Context.ReadForMemoryAsync(stream, i.DocumentId, i.Token);
        
        blittableJson.Modifications = new DynamicJsonValue
        {
            ["@metadata"] = new DynamicJsonValue
            {
                ["@collection"] = i"Users" // Укажите вашу коллекцию
            }
        };
        
        {
            // 2. Обязательно добавляем метаданные (без имени коллекции RavenDB не поймет, куда класть документ)
            

            // Применяем модификации, чтобы получить финальный объект
            using (var finalBlittable = session.Advanced.Context.ReadObject(blittableJson, docId))
            {
                // 3. Передаем в PutCommandData и деферим в сессию
                var putCommand = new PutCommandData(docId, changeVector: null, finalBlittable);
            
                session.Advanced.Defer(putCommand);
            }
        }
        var command = new PutCommandData(i.DocumentId, i.ChangeVector, 
            await holder.Store.GetRequestExecutor().Serializer.CreateReader(json, null));
        
        session.Advanced.Defer(command);
        await session.SaveChangesAsync(i.Token);
    }

    private async Task HandlePatch(PatchIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        //TODO: Сформировать скрипт который делает патч данных по адресу Path значением или мапой из Value, Path надо поменять на MapPath
        var patch = new PatchRequest { Script = i.Path };
        
        session.Advanced.Defer(new PatchCommandData(i.DocumentId, null, patch));
        await session.SaveChangesAsync(i.Token);
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
        var command = new GetDocumentsCommand(
            new[] { i.DocumentId }, 
            includes: null, 
            metadataOnly: true);

        await holder.Store.GetRequestExecutor().ExecuteAsync(command, holder.Store.Context);
        
        var doc = command.Result.Results[0];
        if (doc is null || !doc.TryGet("@attachments", out BlittableJsonReaderObject attachments))
            return;

        var result = DictionaryMap.New;
        foreach (var property in attachments.GetProperties())
        {
            if (property.Value is BlittableJsonReaderObject attachment)
                result.Put(property.Name, DictionaryMap.New.With(("Type", (MapValue)attachment.GetByString("ContentType")), ("Size", (MapValue)attachment.GetByString("Size"))).AsMapValue());
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
        using var session = holder.Store.OpenAsyncSession();
        using var attachment = await session.Advanced.Attachments.GetAsync(i.DocumentId, i.Name);
        
        // В рамках Prius нам нужно вернуть поток в контекст интента или обработать его
        // Предполагаем, что i.Context поддерживает прямую передачу стрима или мы сохраняем его локально
        i.Context.Put(i.OutputPath, new MapValue(attachment.Stream));
    }

    private async Task HandleDeleteAttachment(DeleteAttachmentIntent i)
    {
        using var session = holder.Store.OpenAsyncSession();
        session.Advanced.Attachments.Delete(i.DocumentId, i.Name);
        await session.SaveChangesAsync(i.Token);
    }

    private async Task HandleNative(NativeIntent i) => await i.Action(holder.Store);
    
    private async Task HandleSubscription(SubscriptionIntent i) => await Task.CompletedTask;
}
