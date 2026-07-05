using Microsoft.Extensions.Logging;
using Prius.Core.Maps;
using Prius.Data.RavenDB;
using Raven.Client.Documents;
using Raven.Client.Documents.Operations.CompareExchange;
using Sparrow.Json;

namespace Prius.Data.RavenDB.Metadata;

public sealed class MetadataManagementSession : IMetadataManagementSession
{
    private readonly IDocumentStore _store;
    private readonly MetadataMigrationApplier _applier;
    private readonly UniversalUpdateGenerator _generator;
    private readonly ILogger<MetadataManagementSession>? _logger;

    private const string LockKey = "Locks/MetadataUpdate";
    private const string SnapshotId = "System/Snapshot";
    private const string SnapshotInfoId = "System/SnapshotInfo";

    public MetadataManagementSession(
        IDocumentStore store,
        MetadataMigrationApplier applier,
        UniversalUpdateGenerator generator,
        ILogger<MetadataManagementSession>? logger = null)
    {
        _store = store;
        _applier = applier;
        _generator = generator;
        _logger = logger;
    }

    public async ValueTask<MetadataApplyResult> ApplyVersionPlanAsync(
        IMap versionPlan, 
        CancellationToken ct = default)
    {
        var versionId = versionPlan.DeepGet("VersionId".AsSpan()).AsString();
        _logger?.LogInformation("Attempting to apply version plan '{VersionId}'", versionId);

        var lockAcquired = await TryAcquireClusterLockAsync(versionId, ct);
        if (!lockAcquired)
            return new MetadataApplyResult(
                false, 
                versionId, 
                string.Empty, 
                "Failed to acquire cluster-wide metadata update lock. Another update might be in progress.");

        try
        {
            using var session = _store.OpenAsyncSession();
            
            await _applier.ApplyVersionPlanAsync(session, versionPlan, ct);
            
            var finalHash = _applier.GetLastAppliedSnapshotHash();
            await session.SaveChangesAsync(ct);
            
            _logger?.LogInformation("Version plan '{VersionId}' successfully committed to cluster.", versionId);
            return new MetadataApplyResult(true, versionId, finalHash);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to apply version plan '{VersionId}'", versionId);
            return new MetadataApplyResult(false, versionId, string.Empty, ex.Message);
        }
        finally
        {
            await ReleaseClusterLockAsync(versionId, ct);
        }
    }

    public async ValueTask<MetadataApplyResult> ApplyUniversalScriptAsync(
        IMap universalScript, 
        CancellationToken ct = default)
    {
        var scriptId = universalScript.DeepGet("ScriptId".AsSpan()).AsString();
        _logger?.LogInformation("Processing universal script '{ScriptId}'", scriptId);

        var lockAcquired = await TryAcquireClusterLockAsync(scriptId, ct);
        if (!lockAcquired)
            return new MetadataApplyResult(
                false, 
                scriptId, 
                string.Empty, 
                "Failed to acquire cluster-wide metadata update lock.");

        try
        {
            string currentChainHash;
            DictionaryMap snapshotDoc;

            using (var checkSession = _store.OpenAsyncSession())
            {
                var infoDoc = await checkSession.LoadAsync<BlittableJsonReaderObject>(SnapshotInfoId, ct);
                currentChainHash = infoDoc is not null 
                    ? (await infoDoc.AsJsonReaderMap()).DeepGet("CurrentChainHash".AsSpan()).AsString() 
                    : string.Empty;
                
                var snapshotBlittable = await checkSession.LoadAsync<BlittableJsonReaderObject>(SnapshotId, ct);
                snapshotDoc = DictionaryMap.New;
                if (snapshotBlittable is not null)
                {
                    var map = await snapshotBlittable.AsJsonReaderMap();
                    foreach (var k in map.Keys())
                        snapshotDoc[k] = map[k];
                }
            }

            var localPlan = _generator.GenerateVersionPlan(snapshotDoc, universalScript, currentChainHash);

            using var session = _store.OpenAsyncSession();
            
            await _applier.ApplyVersionPlanAsync(session, localPlan, ct);
            
            var finalHash = _applier.GetLastAppliedSnapshotHash();
            await session.SaveChangesAsync(ct);

            var planId = localPlan.DeepGet("VersionId".AsSpan()).AsString();
            return new MetadataApplyResult(true, planId, finalHash);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to execute universal script '{ScriptId}'", scriptId);
            return new MetadataApplyResult(false, scriptId, string.Empty, ex.Message);
        }
        finally
        {
            await ReleaseClusterLockAsync(scriptId, ct);
        }
    }

    private async Task<bool> TryAcquireClusterLockAsync(string owner, CancellationToken ct)
    {
        var operation = new PutCompareExchangeValueOperation<string>(LockKey, owner, 0);
        var result = await _store.Operations.SendAsync<CompareExchangeResult<string>>(operation, token: ct);
        return result.Successful;
    }

    private async Task ReleaseClusterLockAsync(string owner, CancellationToken ct)
    {
        var getOperation = new GetCompareExchangeValueOperation<string>(LockKey);
        var result = await _store.Operations.SendAsync<CompareExchangeValue<string>>(getOperation, token: ct);
        
        if (result is null)
            return;

        if (result.Value == owner)
        {
            var deleteOperation = new DeleteCompareExchangeValueOperation<string>(LockKey, result.Index);
            await _store.Operations.SendAsync<CompareExchangeResult<string>>(deleteOperation, token: ct);
        }
    }
}
