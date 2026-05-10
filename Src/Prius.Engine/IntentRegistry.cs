namespace Prius.Engine;

using System.Collections.Generic;
using Core.Maps;

internal sealed class IntentRegistry
{
    private readonly List<LoadIntent> _loads = [];
    private readonly List<QueryIntent> _queries = [];
    private readonly List<StoreIntent> _stores = [];
    private readonly List<PatchIntent> _patches = [];
    private readonly List<DeleteIntent> _deletes = [];
    private readonly List<LiveIntent> _lives = [];

    public IReadOnlyList<LoadIntent> Loads => _loads;
    public IReadOnlyList<QueryIntent> Queries => _queries;
    public IReadOnlyList<StoreIntent> Stores => _stores;
    public IReadOnlyList<PatchIntent> Patches => _patches;
    public IReadOnlyList<DeleteIntent> Deletes => _deletes;
    public IReadOnlyList<LiveIntent> Lives => _lives;

    public bool HasIntents => _loads.Count > 0 || _queries.Count > 0 || _stores.Count > 0 || _patches.Count > 0 || _deletes.Count > 0 || _lives.Count > 0;
    
    public bool HasReadIntents => _loads.Count > 0 || _queries.Count > 0;
    
    public bool HasWriteIntents => _stores.Count > 0 || _patches.Count > 0 || _deletes.Count > 0;

    public void RegisterLoad(string id, string output, string failure, ReactorContext context) => 
        _loads.Add(new LoadIntent(id, output, failure, context));

    public void RegisterQuery(IMap queryMap, string output, string failure, ReactorContext context) => 
        _queries.Add(new QueryIntent(queryMap, output, failure, context));

    public void RegisterStore(string id, IMap map, string? vector, string failure, ReactorContext context) => 
        _stores.Add(new StoreIntent(id, map, vector, failure, context));

    public void RegisterPatch(string id, string path, MapValue val, string? vector, string failure, ReactorContext context) => 
        _patches.Add(new PatchIntent(id, path, val, vector, failure, context));

    public void RegisterDelete(string id, string? vector, string failure, ReactorContext context) => 
        _deletes.Add(new DeleteIntent(id, vector, failure, context));

    public void RegisterLive(string topic, string livePath, string failure, ReactorContext context) => 
        _lives.Add(new LiveIntent(topic, livePath, failure, context));
}

internal record LoadIntent(string DocumentId, string OutputPath, string FailurePath, ReactorContext Context)
{
    public object? LazyResult { get; set; }
}

internal record QueryIntent(IMap QueryMap, string OutputPath, string FailurePath, ReactorContext Context)
{
    public object? LazyResult { get; set; }
}

internal record StoreIntent(string DocumentId, IMap Map, string? ChangeVector, string FailurePath, ReactorContext Context);

internal record PatchIntent(string DocumentId, string Path, MapValue Value, string? ChangeVector, string FailurePath, ReactorContext Context);

internal record DeleteIntent(string DocumentId, string? ChangeVector, string FailurePath, ReactorContext Context);

internal record LiveIntent(string TopicName, string LivePath, string FailurePath, ReactorContext Context);
