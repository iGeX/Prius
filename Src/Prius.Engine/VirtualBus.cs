namespace Prius.Engine;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Core.Maps;
using Abstractions;

internal sealed class DeferredNotifyTask(ReactorContext childContext, string materializedPath, MapValue value)
{
    public ReactorContext ChildContext => childContext;
    public string MaterializedPath => materializedPath;
    public MapValue Value => value;
}

public sealed class VirtualBus
{
    private readonly ConcurrentDictionary<string, IReactor> _routeCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, IReactor>.AlternateLookup<ReadOnlySpan<char>> _cacheLookup;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _nodeLocks = new(StringComparer.Ordinal);
    
    private readonly RoutingTrie _routingTrie;
    private readonly ReactorContext _rootContext;
    
    private readonly ThreadLocal<Queue<DeferredNotifyTask>> _deferredTasks = new(() => new());
    private readonly ThreadLocal<bool> _isProcessingTick = new(() => false);

    public VirtualBus(RoutingTrie routingTrie)
    {
        _routingTrie = routingTrie ?? throw new ArgumentNullException(nameof(routingTrie));
        _cacheLookup = _routeCache.GetAlternateLookup<ReadOnlySpan<char>>();
        _rootContext = new ReactorContext(this, null, string.Empty, string.Empty, string.Empty, DictionaryMap.New);
    }

    public void Put(MapPath path, MapValue value, IMap? envPatch = null) => 
        DispatchPut(_rootContext, path, value, envPatch);

    public MapValue Get(MapPath path, IMap? envPatch = null) => 
        DispatchGet(_rootContext, path, envPatch);

    public void ClearCache() => 
        _routeCache.Clear();

    internal void DispatchPut(ReactorContext caller, MapPath relativePath, MapValue value, IMap? envPatch)
    {
        if (relativePath.IsEmpty)
            return;

        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new SemaphoreSlim(1, 1));
        nodeLock.Wait();

        try
        {
            var resolveResult = _routingTrie.Resolve(new MapPath(absolutePathString.AsSpan()));
            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Reactor);

            var subContext = new ReactorContext(this, caller, resolveResult.ReactorKey, absolutePathString, resolveResult.ReactorKey, envPatch);

            resolveResult.Reactor.Put(subContext, resolveResult.RemainingPath, value);
        }
        finally
        {
            nodeLock.Release();
        }
    }

    internal MapValue DispatchGet(ReactorContext caller, MapPath relativePath, IMap? envPatch)
    {
        if (relativePath.IsEmpty)
            return new MapValue();

        var absolutePathString = CombinePathsToString(caller.AbsolutePath, relativePath);
        var nodeLock = _nodeLocks.GetOrAdd(absolutePathString, _ => new SemaphoreSlim(1, 1));
        nodeLock.Wait();

        try
        {
            var resolveResult = _routingTrie.Resolve(new MapPath(absolutePathString.AsSpan()));
            CacheResolvedRoute(absolutePathString.AsSpan(), resolveResult.Reactor);

            var subContext = new ReactorContext(this, caller, resolveResult.ReactorKey, absolutePathString, resolveResult.ReactorKey, envPatch);

            return resolveResult.Reactor.Get(subContext, resolveResult.RemainingPath);
        }
        finally
        {
            nodeLock.Release();
        }
    }

    internal void DispatchNotify(ReactorContext caller, MapPath path, MapValue value)
    {
        if (path.IsEmpty || caller.Parent is null)
            return;

        _deferredTasks.Value!.Enqueue(new DeferredNotifyTask(caller, path.ToString(), value));

        if (!_isProcessingTick.Value)
            ProcessDeferredTicks();
    }

    private void ProcessDeferredTicks()
    {
        _isProcessingTick.Value = true;

        try
        {
            var queue = _deferredTasks.Value!;
            while (queue.Count > 0)
            {
                ExecuteNotifyTask(queue.Dequeue());
            }
        }
        finally
        {
            _isProcessingTick.Value = false;
        }
    }

    private void ExecuteNotifyTask(DeferredNotifyTask task)
    {
        var childContext = task.ChildContext;
        var parentContext = childContext.Parent;

        if (parentContext is null)
            return;

        var currentParentPathString = GetParentAbsolutePath(childContext.AbsolutePath);
        IReactor resolveReactor = EmptyReactor.Instance;
        var finalParentPathString = currentParentPathString;

        while (true)
        {
            var parentPath = new MapPath(currentParentPathString.AsSpan());
            var resolveResult = _routingTrie.Resolve(parentPath);

            if (resolveResult.Reactor is not EmptyReactor)
            {
                resolveReactor = resolveResult.Reactor;
                finalParentPathString = currentParentPathString;
                break;
            }

            // Если поднялись до корня и ничего не нашли — выходим
            if (string.IsNullOrEmpty(currentParentPathString))
                break;

            currentParentPathString = GetParentAbsolutePath(currentParentPathString);
        }

        if (resolveReactor is EmptyReactor)
            return;

        var nodeLock = _nodeLocks.GetOrAdd(finalParentPathString, _ => new SemaphoreSlim(1, 1));
        nodeLock.Wait();

        try
        {
            var childRelativePath = childContext.AbsolutePath[finalParentPathString.Length..]
                .TrimStart('/');

            var childPrefix = string.IsNullOrEmpty(childRelativePath)
                ? default
                : new MapPath(childRelativePath.AsSpan());

            var localPath = new MapPath(task.MaterializedPath.AsSpan());
            
            var transformedPath = childPrefix.IsEmpty 
                ? localPath 
                : new MapPath((childPrefix + localPath).AsSpan());

            resolveReactor.Notify(parentContext, transformedPath, task.Value);
        }
        finally
        {
            nodeLock.Release();
        }
    }

    private static string GetParentAbsolutePath(string absolutePath)
    {
        if (string.IsNullOrEmpty(absolutePath))
            return string.Empty;

        var lastSlashIndex = absolutePath.LastIndexOf('/');
        if (lastSlashIndex < 0)
            return string.Empty;

        return absolutePath[..lastSlashIndex];
    }

    private void CacheResolvedRoute(ReadOnlySpan<char> absolutePath, IReactor reactor)
    {
        if (_cacheLookup.TryGetValue(absolutePath, out _))
            return;

        _cacheLookup.TryAdd(absolutePath, reactor);
    }

    private static string CombinePathsToString(string baseAbsolutePath, MapPath relativePath) => 
        string.IsNullOrEmpty(baseAbsolutePath) 
            ? relativePath.ToString() 
            : new MapPath(baseAbsolutePath.AsSpan()) + relativePath;
}
