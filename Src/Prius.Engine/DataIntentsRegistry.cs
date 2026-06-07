namespace Prius.Engine;

using System.Threading.Channels;
using System.Threading;
using System.Threading.Tasks;
using Abstractions;
using Core.Maps;
using System.IO;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

public class DataIntentsRegistry : IDataIntentsRegistry, IDataIntentsProvider
{
    private bool _inStasis;
    private readonly Lock _sync = new();
    
    private readonly Channel<DataTransaction> _transactions = Channel.CreateUnbounded<DataTransaction>();
    private readonly ConcurrentDictionary<ISystemElementContext, List<IIntent>> _pendingTransactions = new();

    public void EnterStasis()
    {
        lock (_sync) _inStasis = true;
    }

    public void ExitStasis()
    {
        lock (_sync) _inStasis = false;
    }

    private CancellationTokenSource? TryRegister()
    {
        lock (_sync)
        {
            if (_inStasis) return null;
            return new CancellationTokenSource();
        }
    }

    public async ValueTask<DataTransaction> PopTx(CancellationToken ct) => 
        await _transactions.Reader.ReadAsync(ct);

    private ISystemElementContext GetTxRoot(ISystemElementContext context)
    {
        var current = context;
        while (current.Parent is ISystemElementContext parent && parent.Parent is not null)
        {
            current = parent;
        }
        return current;
    }

    private void AddIntent(ISystemElementContext context, IIntent intent)
    {
        var txRoot = GetTxRoot(context);
        
        List<IIntent>? intentsList;
        if (!_pendingTransactions.TryGetValue(txRoot, out intentsList))
        {
            var newList = new List<IIntent>();
            lock (txRoot)
            {
                if (!_pendingTransactions.TryGetValue(txRoot, out intentsList))
                {
                    txRoot.OnCompleted += HandleTxCompleted;
                    txRoot.OnFailed += HandleTxFailed;
                    _pendingTransactions[txRoot] = newList;
                    intentsList = newList;
                }
            }
        }

        lock (intentsList)
        {
            intentsList.Add(intent);
        }
    }

    private void HandleTxCompleted(ISystemElementContext root)
    {
        root.OnCompleted -= HandleTxCompleted;
        root.OnFailed -= HandleTxFailed;

        if (_pendingTransactions.TryRemove(root, out var intents))
        {
            lock (intents)
            {
                if (intents.Count > 0)
                {
                    _transactions.Writer.TryWrite(new DataTransaction(root, intents));
                }
            }
        }
    }

    private void HandleTxFailed(ISystemElementContext root, Exception ex)
    {
        root.OnCompleted -= HandleTxCompleted;
        root.OnFailed -= HandleTxFailed;

        _pendingTransactions.TryRemove(root, out _);
    }

    public CancellationTokenSource? Load(IElementContext context, string documentId, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new LoadIntent(context, documentId, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? Query(IElementContext context, IMap queryMap, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new QueryIntent(context, queryMap, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? Store(IElementContext context, IMap map, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new StoreIntent(context, map, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? Patch(IElementContext context, string documentId, string path, MapValue val, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new PatchIntent(context, documentId, path, val, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? Delete(IElementContext context, string documentId, string? vector, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new DeleteIntent(context, documentId, vector, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? Increment(IElementContext context, string documentId, string name, long delta, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new IncrementIntent(context, documentId, name, delta, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? GetCounters(IElementContext context, string documentId, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new GetCountersIntent(context, documentId, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? GetAttachmentsMetadata(IElementContext context, string documentId, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new GetAttachmentsMetadataIntent(context, documentId, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? StoreAttachment(IElementContext context, string documentId, string name, Stream stream, string contentType, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new StoreAttachmentIntent(context, documentId, name, stream, contentType, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? GetAttachment(IElementContext context, string documentId, string name, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new GetAttachmentIntent(context, documentId, name, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? DeleteAttachment(IElementContext context, string documentId, string name, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new DeleteAttachmentIntent(context, documentId, name, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? ExecuteNative(IElementContext context, Func<object, NativeIntent, Task> nativeAction, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new NativeIntent(context, nativeAction, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }

    public CancellationTokenSource? Subscription(IElementContext context, string topic, string dataPath, MapPath successPath, MapPath failurePath)
    {
        var cts = TryRegister();
        if (cts == null) return null;
        var intent = new SubscriptionIntent(context, topic, dataPath, successPath.ToString(), failurePath.ToString(), cts.Token);
        AddIntent((ISystemElementContext)context, intent);
        return cts;
    }
}
