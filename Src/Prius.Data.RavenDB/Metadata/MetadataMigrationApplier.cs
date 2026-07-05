using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Prius.Core.Maps;
using Prius.Data.RavenDB;
using Raven.Client.Documents.Session;
using Sparrow.Json;

namespace Prius.Data.RavenDB.Metadata;

public sealed class MetadataMigrationApplier
{
    private readonly ILogger<MetadataMigrationApplier>? _logger;
    private readonly List<IMetadataHandler> _handlers;
    
    private const string SnapshotId = "System/Snapshot";
    private const string SnapshotInfoId = "System/SnapshotInfo";
    
    private string _lastAppliedSnapshotHash = string.Empty;

    public MetadataMigrationApplier(
        IEnumerable<IMetadataHandler>? handlers = null,
        ILogger<MetadataMigrationApplier>? logger = null)
    {
        _logger = logger;
        _handlers = handlers?.ToList() ?? [ new PackageMetadataHandler() ];
    }

    public string GetLastAppliedSnapshotHash() => _lastAppliedSnapshotHash;

    public async ValueTask ApplyVersionPlanAsync(
        IAsyncDocumentSession session, 
        IMap versionPlan, 
        CancellationToken ct = default)
    {
        var versionId = versionPlan.DeepGet("VersionId".AsSpan()).AsString();
        _logger?.LogInformation("Starting to apply VersionPlan: {VersionId}", versionId);

        var migrationsNode = versionPlan.DeepGet("Migrations".AsSpan());
        if (!migrationsNode.IsMap)
            return;

        var migrationsMap = migrationsNode.AsMap();
        var migrationKeys = migrationsMap.Keys(ascending: true).ToList();

        // Load or initialize system snapshot
        var snapshotDoc = await session.LoadAsync<BlittableJsonReaderObject>(SnapshotId, ct);
        var snapshot = DictionaryMap.New;
        if (snapshotDoc is not null)
        {
            var map = await snapshotDoc.AsJsonReaderMap();
            foreach (var k in map.Keys())
                snapshot[k] = map[k];
        }

        // Load or initialize snapshot info
        var infoDoc = await session.LoadAsync<BlittableJsonReaderObject>(SnapshotInfoId, ct);
        var info = DictionaryMap.New;
        if (infoDoc is not null)
        {
            var map = await infoDoc.AsJsonReaderMap();
            foreach (var k in map.Keys())
                info[k] = map[k];
        }

        var lastAppliedId = info.DeepGet("LastAppliedMigrationId".AsSpan()).AsString();
        var currentChainHash = info.DeepGet("CurrentChainHash".AsSpan()).AsString() ?? string.Empty;

        var applying = false;
        if (string.IsNullOrEmpty(lastAppliedId))
            applying = true;
        else
        {
            var planHasLastApplied = migrationKeys.Any(key => 
                migrationsMap[key].AsMap().DeepGet("Id".AsSpan()).AsString() == lastAppliedId);
            if (!planHasLastApplied)
                applying = true;
        }

        foreach (var key in migrationKeys)
        {
            var migration = migrationsMap[key].AsMap();
            var migrationId = migration.DeepGet("Id".AsSpan()).AsString();
            var expectedChainHash = migration.DeepGet("ChainHash".AsSpan()).AsString();
            var expectedSnapshotHash = migration.DeepGet("SnapshotHash".AsSpan()).AsString();
            var expectedDerivedHash = migration.DeepGet("DerivedHash".AsSpan()).AsString();

            if (!applying)
            {
                if (migrationId == lastAppliedId)
                    applying = true;
                continue;
            }

            _logger?.LogInformation("Applying migration: {MigrationId}", migrationId);

            var operationsNode = migration.DeepGet("Operations".AsSpan());
            if (!operationsNode.IsMap)
                continue;

            var operationsMap = operationsNode.AsMap();

            // 1. Calculate and verify chain hash before executing mutations
            var migrationHash = ComputeOperationsHash(operationsMap);
            var computedChainHash = ComputeChainHash(currentChainHash, migrationHash);

            if (!string.IsNullOrEmpty(expectedChainHash) && !computedChainHash.Equals(expectedChainHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Cryptographic chain validation failed for migration '{migrationId}'. Expected chain hash: '{expectedChainHash}', computed: '{computedChainHash}'.");

            // 2. Clone snapshot before applying changes
            var oldSnapshot = new DictionaryMap(snapshot.DeepCopy());

            // 3. Apply operations to System Snapshot
            ApplyOperations(snapshot, operationsMap);

            // 4. Validate Snapshot Hash
            var computedSnapshotHash = MetadataProjectionContext.ComputeMapHash(snapshot);
            if (!string.IsNullOrEmpty(expectedSnapshotHash) && !computedSnapshotHash.Equals(expectedSnapshotHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Snapshot integrity validation failed for migration '{migrationId}'. Expected snapshot hash: '{expectedSnapshotHash}', computed: '{computedSnapshotHash}'.");

            // 5. Run Projection Handlers
            var contexts = new List<MetadataProjectionContext>();
            var allActions = new List<ProjectionAction>();

            foreach (var handler in _handlers)
            {
                var ns = GetHandlerNamespace(handler);
                var context = new MetadataProjectionContext(ns);
                
                await handler.ProjectAsync(context, oldSnapshot, snapshot, ct);
                
                contexts.Add(context);
                allActions.AddRange(context.Actions);
            }

            // 6. Validate Derived Hash
            var computedDerivedHash = CalculateCombinedDerivedHash(allActions);
            if (!string.IsNullOrEmpty(expectedDerivedHash) && !computedDerivedHash.Equals(expectedDerivedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Derived entities integrity validation failed for migration '{migrationId}'. Expected derived hash: '{expectedDerivedHash}', computed: '{computedDerivedHash}'.");

            // 7. Flush projections to RavenDB
            foreach (var context in contexts)
                await context.FlushAsync(session, ct);

            // 8. Update tracking variables
            currentChainHash = computedChainHash;
            lastAppliedId = migrationId;
            _lastAppliedSnapshotHash = computedSnapshotHash;
        }

        // Save updated snapshot and info to RavenDB session
        if (snapshotDoc is not null)
            session.Advanced.Evict(snapshotDoc);

        var snapshotDict = snapshot.ToDictionary();
        await session.StoreAsync(snapshotDict, SnapshotId, ct);
        session.Advanced.GetMetadataFor(snapshotDict)["@collection"] = "System";

        info.DeepPut("LastAppliedMigrationId".AsSpan(), lastAppliedId);
        info.DeepPut("CurrentChainHash".AsSpan(), currentChainHash);

        if (infoDoc is not null)
            session.Advanced.Evict(infoDoc);

        var infoDict = info.ToDictionary();
        await session.StoreAsync(infoDict, SnapshotInfoId, ct);
        session.Advanced.GetMetadataFor(infoDict)["@collection"] = "System";
    }

    private void ApplyOperations(IMap snapshot, IMap operations)
    {
        var keys = operations.Keys(ascending: true).ToList();
        foreach (var key in keys)
        {
            var op = operations[key].AsMap();
            var action = op.DeepGet("Action".AsSpan()).AsString();
            var args = op.DeepGet("Args".AsSpan()).AsMap();

            var path = args.DeepGet("Path".AsSpan()).AsString();

            if (action == "PutValue")
            {
                var value = args.DeepGet("Value".AsSpan());
                snapshot.DeepPut(new MapPath(path.AsSpan()), value);
            }
            else if (action == "IncrementValue")
            {
                var deltaVal = args.DeepGet("Delta".AsSpan());
                var mapPath = new MapPath(path.AsSpan());
                var currentVal = snapshot.DeepGet(mapPath);

                decimal currentNum = 0;
                if (currentVal.IsLong)
                    currentNum = currentVal.AsLong();
                else if (currentVal.IsDecimal)
                    currentNum = currentVal.AsDecimal();

                decimal delta = 0;
                if (deltaVal.IsLong)
                    delta = deltaVal.AsLong();
                else if (deltaVal.IsDecimal)
                    delta = deltaVal.AsDecimal();

                var sum = currentNum + delta;

                if (currentVal.IsLong && deltaVal.IsLong)
                    snapshot.DeepPut(mapPath, new MapValue((long)sum));
                else
                    snapshot.DeepPut(mapPath, new MapValue(sum));
            }
            else
                throw new NotSupportedException($"Operation action '{action}' is not supported.");
        }
    }

    private static string? GetHandlerNamespace(IMetadataHandler handler) =>
        handler switch
        {
            PackageMetadataHandler => "Packages/",
            _ => null
        };

    private static string CalculateCombinedDerivedHash(List<ProjectionAction> actions)
    {
        var sortedActions = actions.OrderBy(a => a).ToList();
        var sb = new StringBuilder();

        foreach (var action in sortedActions)
        {
            sb.Append((int)action.Type).Append(':')
              .Append(action.DocumentId).Append(':')
              .Append(action.KeyOrName).Append(':')
              .Append(action.ValueHashOrContent).Append(';');
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ComputeOperationsHash(IMap operations)
    {
        var sb = new StringBuilder();
        var keys = operations.Keys(ascending: true).ToList();
        
        foreach (var key in keys)
        {
            var op = operations[key].AsMap();
            sb.Append(op.DeepGet("Action".AsSpan()).AsString());
            sb.Append(op.DeepGet("Args/Path".AsSpan()).AsString());
            
            var val = op.DeepGet("Args/Value".AsSpan());
            if (!val.IsEmpty)
            {
                if (val.IsMap)
                    sb.Append(MetadataProjectionContext.ComputeMapHash(val.AsMap()));
                else
                    sb.Append(val.ToString());
            }

            var delta = op.DeepGet("Args/Delta".AsSpan());
            if (!delta.IsEmpty)
                sb.Append(delta.ToString());
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string ComputeChainHash(string prevHash, string currentHash)
    {
        var input = prevHash + currentHash;
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
