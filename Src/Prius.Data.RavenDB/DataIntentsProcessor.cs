using Prius.Core.Maps;

namespace Prius.Data.RavenDB;

using System;
using System.Threading;
using System.Threading.Tasks;
using Engine.Abstractions;

using Microsoft.Extensions.Logging;

public sealed class DataIntentsProcessor
{
    private const int MaxRetries = 3;
    
    private readonly DocumentStoreHolder _holder;
    private readonly IDataIntentsProvider _provider;
    private readonly ILogger<DataIntentsProcessor> _logger;

    public DataIntentsProcessor(
        DocumentStoreHolder holder, 
        IDataIntentsProvider provider, 
        ILogger<DataIntentsProcessor> logger)
    {
        _holder = holder;
        _provider = provider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct) =>
        await Task.WhenAll(
            ProcessLoop(_provider.PopLoad, HandleLoad, ct),
            ProcessLoop(_provider.PopQuery, HandleQuery, ct),
            ProcessLoop(_provider.PopStore, HandleStore, ct),
            ProcessLoop(_provider.PopPatch, HandlePatch, ct),
            ProcessLoop(_provider.PopDelete, HandleDelete, ct),
            ProcessLoop(_provider.PopIncrement, HandleIncrement, ct),
            ProcessLoop(_provider.PopGetCounters, HandleGetCounters, ct),
            ProcessLoop(_provider.PopGetAttachmentsMetadata, HandleGetAttachmentsMetadata, ct),
            ProcessLoop(_provider.PopStoreAttachment, HandleStoreAttachment, ct),
            ProcessLoop(_provider.PopGetAttachment, HandleGetAttachment, ct),
            ProcessLoop(_provider.PopDeleteAttachment, HandleDeleteAttachment, ct),
            ProcessLoop(_provider.PopNative, HandleNative, ct),
            ProcessLoop(_provider.PopSubscription, HandleSubscription, ct)
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
                    _logger.LogInformation("Intent {IntentType} was cancelled", intent?.GetType().Name);
                    break;
                }
                catch (Exception ex)
                {
                    retryCount++;
                    if (retryCount >= MaxRetries)
                    {
                        if (intent is not null)
                        {
                            _logger.LogError(ex, "Failed to process intent {IntentInfo} after {RetryCount} retries", 
                                GetFullIntentInfo(intent), MaxRetries);
                            RecordFailure(intent, ex);
                        }

                        break;
                    }

                    var delay = TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 100);
                    await Task.Delay(delay, ct);
                }
            }
        }
    }

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

    private async Task HandleLoad(LoadIntent i)
    {
        try
        {
            using var session = _holder.Store.OpenAsyncSession();
            var document = await session.LoadAsync<object>(i.DocumentId);
            _logger.LogInformation("Loaded document: {DocumentId}", i.DocumentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load document: {DocumentId}", i.DocumentId);
            throw;
        }
    }
    
    private async Task HandleQuery(QueryIntent i)
    {
        try
        {
            var (rql, parameters) = RqlBuilder.Build(i.QueryMap);
            if (string.IsNullOrEmpty(rql))
                return;

            using var session = _holder.Store.OpenAsyncSession();
            var query = session.Advanced.AsyncRawQuery<object>(rql);
            
            foreach (var param in parameters)
                query.AddParameter(param.Key, param.Value);

            var results = await query.ToListAsync(i.Token);
            _logger.LogInformation("Executed query against index {Index} returning {Count} results", i.QueryMap.Get("From"), results.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute query");
            throw;
        }
    }

    private async Task HandleStore(StoreIntent i) => await Task.CompletedTask;
    private async Task HandlePatch(PatchIntent i) => await Task.CompletedTask;
    private async Task HandleDelete(DeleteIntent i) => await Task.CompletedTask;
    private async Task HandleIncrement(IncrementIntent i) => await Task.CompletedTask;
    private async Task HandleGetCounters(GetCountersIntent i) => await Task.CompletedTask;
    private async Task HandleGetAttachmentsMetadata(GetAttachmentsMetadataIntent i) => await Task.CompletedTask;
    private async Task HandleStoreAttachment(StoreAttachmentIntent i) => await Task.CompletedTask;
    private async Task HandleGetAttachment(GetAttachmentIntent i) => await Task.CompletedTask;
    private async Task HandleDeleteAttachment(DeleteAttachmentIntent i) => await Task.CompletedTask;
    private async Task HandleNative(NativeIntent i) => await Task.CompletedTask;
    private async Task HandleSubscription(SubscriptionIntent i) => await Task.CompletedTask;
}
