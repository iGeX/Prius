using Raven.Client.Documents;
using Raven.Client.Documents.Operations.CompareExchange;
using Raven.TestDriver;
using Prius.Core.Maps;
using Prius.Data.RavenDB.Metadata;
using Xunit;

namespace Prius.Data.RavenDB.Tests.Metadata;

[Collection(nameof(MetadataManagementSessionTests))]
public sealed class MetadataManagementSessionTests : RavenTestDriver
{
    static MetadataManagementSessionTests()
    {
        try
        {
            ConfigureServer(new TestServerOptions { Licensing = { ThrowOnInvalidOrMissingLicense = false } });
        }
        catch (InvalidOperationException)
        {
        }
    }

    [Fact]
    public async Task ApplyVersionPlanAsync_SuccessfullyAppliesUnderLock_AndReleases()
    {
        using var store = GetDocumentStore();
        var applier = new MetadataMigrationApplier();
        var generator = new UniversalUpdateGenerator();
        var managerSession = new MetadataManagementSession(store, applier, generator);

        // Build a simple plan
        var plan = DictionaryMap.New;
        plan.DeepPut("VersionId".AsSpan(), "1.0.0");
        plan.DeepPut("BaseVersionId".AsSpan(), "0.0.0");
        plan.DeepPut("Status".AsSpan(), "Draft");

        var ops = DictionaryMap.New;
        var info = DictionaryMap.New.With("id", "Prius.Core").With("version", "1.0.0");
        ops["0"] = DictionaryMap.New.With("Action", "PutValue").With("Args", DictionaryMap.New.With("Path", "Packages/Prius.Core/1.0.0/Info").With("Value", info)).AsMapValue();
        
        var mHash = MetadataMigrationApplier.ComputeOperationsHash(ops);
        var cHash = MetadataMigrationApplier.ComputeChainHash(string.Empty, mHash);
        plan.DeepPut("Migrations/0".AsSpan(), DictionaryMap.New.With("Id", "Mig-1").With("ChainHash", cHash).With("Operations", ops));

        // Apply plan
        var result = await managerSession.ApplyVersionPlanAsync(plan, TestContext.Current.CancellationToken);
        
        Assert.True(result.Success);
        Assert.Equal("1.0.0", result.AppliedVersionId);
        Assert.False(string.IsNullOrEmpty(result.ComputedSnapshotHash));

        // Verify lock is released (compare exchange value should not exist)
        var lockVal = await store.Operations.SendAsync<CompareExchangeValue<string>>(
            new GetCompareExchangeValueOperation<string>("Locks/MetadataUpdate"), 
            token: TestContext.Current.CancellationToken);
        
        Assert.Null(lockVal);
    }

    [Fact]
    public async Task ApplyVersionPlanAsync_ReturnsFailure_WhenLockIsAlreadyHeld()
    {
        using var store = GetDocumentStore();
        var applier = new MetadataMigrationApplier();
        var generator = new UniversalUpdateGenerator();
        var managerSession = new MetadataManagementSession(store, applier, generator);

        // Pre-create the compare-exchange lock to simulate conflict
        var lockOp = new PutCompareExchangeValueOperation<string>("Locks/MetadataUpdate", "AnotherProcess", 0);
        var lockResult = await store.Operations.SendAsync<CompareExchangeResult<string>>(lockOp, token: TestContext.Current.CancellationToken);
        Assert.True(lockResult.Successful);

        // Build plan
        var plan = DictionaryMap.New;
        plan.DeepPut("VersionId".AsSpan(), "1.0.0");

        // Attempt apply
        var result = await managerSession.ApplyVersionPlanAsync(plan, TestContext.Current.CancellationToken);
        
        Assert.False(result.Success);
        Assert.Contains("lock", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);

        // Release the lock manually
        var deleteOp = new DeleteCompareExchangeValueOperation<string>("Locks/MetadataUpdate", lockResult.Index);
        await store.Operations.SendAsync<CompareExchangeResult<string>>(deleteOp, token: TestContext.Current.CancellationToken);

        // Build valid plan details
        var ops = DictionaryMap.New;
        var info = DictionaryMap.New.With("id", "Prius.Core").With("version", "1.0.0");
        ops["0"] = DictionaryMap.New.With("Action", "PutValue").With("Args", DictionaryMap.New.With("Path", "Packages/Prius.Core/1.0.0/Info").With("Value", info)).AsMapValue();
        
        var mHash = MetadataMigrationApplier.ComputeOperationsHash(ops);
        var cHash = MetadataMigrationApplier.ComputeChainHash(string.Empty, mHash);
        plan.DeepPut("BaseVersionId".AsSpan(), "0.0.0");
        plan.DeepPut("Status".AsSpan(), "Draft");
        plan.DeepPut("Migrations/0".AsSpan(), DictionaryMap.New.With("Id", "Mig-1").With("ChainHash", cHash).With("Operations", ops));

        // Retry apply - should succeed now
        var retryResult = await managerSession.ApplyVersionPlanAsync(plan, TestContext.Current.CancellationToken);
        Assert.True(retryResult.Success);
    }
}
