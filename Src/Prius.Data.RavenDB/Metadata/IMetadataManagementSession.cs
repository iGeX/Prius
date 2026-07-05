using Prius.Core.Maps;

namespace Prius.Data.RavenDB.Metadata;

public interface IMetadataManagementSession
{
    ValueTask<MetadataApplyResult> ApplyVersionPlanAsync(
        IMap versionPlan, 
        CancellationToken ct = default);

    ValueTask<MetadataApplyResult> ApplyUniversalScriptAsync(
        IMap universalScript, 
        CancellationToken ct = default);
}

public record MetadataApplyResult(
    bool Success, 
    string AppliedVersionId, 
    string ComputedSnapshotHash, 
    string ErrorMessage = ""
);
