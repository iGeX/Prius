using Raven.Client.Documents;
using Raven.TestDriver;
using Prius.Core.Maps;
using Prius.Data.RavenDB;
using Prius.Data.RavenDB.Metadata;
using Sparrow.Json;
using Xunit;

namespace Prius.Data.RavenDB.Tests.Metadata;

[Collection(nameof(MetadataMigrationApplierTests))]
public sealed class MetadataMigrationApplierTests : RavenTestDriver
{
    static MetadataMigrationApplierTests()
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
    public async Task ApplyVersionPlanAsync_SuccessfullyProjectsPackagesAndAttachments()
    {
        using var store = GetDocumentStore();
        var applier = new MetadataMigrationApplier();

        // 1. Build a draft VersionPlan
        var versionPlan = DictionaryMap.New;
        versionPlan.DeepPut("VersionId".AsSpan(), "1.0.0");
        versionPlan.DeepPut("BaseVersionId".AsSpan(), "0.0.0");
        versionPlan.DeepPut("Status".AsSpan(), "Draft");

        var operations = DictionaryMap.New;

        // Package metadata op
        var infoMap = DictionaryMap.New;
        infoMap.DeepPut("id".AsSpan(), "Prius.Core");
        infoMap.DeepPut("version".AsSpan(), "1.0.0");

        var op0 = DictionaryMap.New;
        op0.DeepPut("Action".AsSpan(), "PutValue");
        op0.DeepPut("Args/Path".AsSpan(), "Packages/Prius.Core/1.0.0/Info");
        op0.DeepPut("Args/Value".AsSpan(), infoMap);
        operations["0"] = op0.AsMapValue();

        // Package binary DLL op
        var binaryBase64 = Convert.ToBase64String("dll-bytes"u8.ToArray());
        var assetFile = DictionaryMap.New;
        assetFile.DeepPut("Hash".AsSpan(), "dll-hash-value");
        assetFile.DeepPut("ContentBase64".AsSpan(), binaryBase64);

        var op1 = DictionaryMap.New;
        op1.DeepPut("Action".AsSpan(), "PutValue");
        op1.DeepPut("Args/Path".AsSpan(), "Packages/Prius.Core/1.0.0/Assets/lib/net10.0/Prius.Core.dll");
        op1.DeepPut("Args/Value".AsSpan(), assetFile);
        operations["1"] = op1.AsMapValue();

        var migrationHash = MetadataMigrationApplier.ComputeOperationsHash(operations);
        var chainHash = MetadataMigrationApplier.ComputeChainHash(string.Empty, migrationHash);

        var migrationBlock = DictionaryMap.New;
        migrationBlock.DeepPut("Id".AsSpan(), "Migration-001");
        migrationBlock.DeepPut("ChainHash".AsSpan(), chainHash);
        migrationBlock.DeepPut("Operations".AsSpan(), operations);

        versionPlan.DeepPut("Migrations/0".AsSpan(), migrationBlock);

        // 2. Apply VersionPlan
        using (var session = store.OpenAsyncSession())
        {
            await applier.ApplyVersionPlanAsync(session, versionPlan, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // 3. Verify in database
        using (var session = store.OpenAsyncSession())
        {
            // Verify System/Snapshot info is correct
            var snapshotInfoDoc = await session.LoadAsync<BlittableJsonReaderObject>("System/SnapshotInfo", TestContext.Current.CancellationToken);
            Assert.NotNull(snapshotInfoDoc);
            var snapshotInfo = await snapshotInfoDoc.AsJsonReaderMap();
            Assert.Equal("Migration-001", snapshotInfo.DeepGet("LastAppliedMigrationId".AsSpan()).AsString());
            Assert.Equal(chainHash, snapshotInfo.DeepGet("CurrentChainHash".AsSpan()).AsString());

            // Verify System/Snapshot is stored
            var snapshotDoc = await session.LoadAsync<BlittableJsonReaderObject>("System/Snapshot", TestContext.Current.CancellationToken);
            Assert.NotNull(snapshotDoc);
            var snapshot = await snapshotDoc.AsJsonReaderMap();
            Assert.Equal("Prius.Core", snapshot.DeepGet("Packages/Prius.Core/1.0.0/Info/id".AsSpan()).AsString());

            // Verify package document exists
            var pkgDocObj = await session.LoadAsync<BlittableJsonReaderObject>("Packages/Prius.Core/1.0.0", TestContext.Current.CancellationToken);
            Assert.NotNull(pkgDocObj);
            var pkgDoc = await pkgDocObj.AsJsonReaderMap();
            Assert.Equal("Prius.Core", pkgDoc.DeepGet("Info/id".AsSpan()).AsString());
            Assert.Equal("dll-hash-value", pkgDoc.DeepGet("Assets/lib/net10.0/Prius.Core.dll/Hash".AsSpan()).AsString());
            
            // Verify ContentBase64 was stripped from metadata
            Assert.True(pkgDoc.DeepGet("Assets/lib/net10.0/Prius.Core.dll/ContentBase64".AsSpan()).IsEmpty);

            // Verify collection metadata
            var pkgMeta = session.Advanced.GetMetadataFor(pkgDocObj);
            Assert.Equal("Packages", pkgMeta["@collection"]);

            // Verify attachment
            using var attachment = await session.Advanced.Attachments.GetAsync("Packages/Prius.Core/1.0.0", "dll-hash-value", TestContext.Current.CancellationToken);
            Assert.NotNull(attachment);
            using var reader = new StreamReader(attachment.Stream);
            var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
            Assert.Equal("dll-bytes", content);
        }
    }

    [Fact]
    public async Task ApplyVersionPlanAsync_DeletesPackagesWhenRemovedFromSnapshot()
    {
        using var store = GetDocumentStore();
        var applier = new MetadataMigrationApplier();

        // 1. Initial Plan - Add Package
        var plan1 = DictionaryMap.New;
        plan1.DeepPut("VersionId".AsSpan(), "1.0.0");
        plan1.DeepPut("BaseVersionId".AsSpan(), "0.0.0");
        plan1.DeepPut("Status".AsSpan(), "Draft");

        var ops1 = DictionaryMap.New;
        var info1 = DictionaryMap.New.With("id", "Prius.Core").With("version", "1.0.0");
        ops1["0"] = DictionaryMap.New.With("Action", "PutValue").With("Args", DictionaryMap.New.With("Path", "Packages/Prius.Core/1.0.0/Info").With("Value", info1)).AsMapValue();
        
        var mHash1 = MetadataMigrationApplier.ComputeOperationsHash(ops1);
        var cHash1 = MetadataMigrationApplier.ComputeChainHash(string.Empty, mHash1);
        plan1.DeepPut("Migrations/0".AsSpan(), DictionaryMap.New.With("Id", "Mig-1").With("ChainHash", cHash1).With("Operations", ops1));

        using (var session = store.OpenAsyncSession())
        {
            await applier.ApplyVersionPlanAsync(session, plan1, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Verify package exists
        using (var session = store.OpenAsyncSession())
        {
            var doc = await session.LoadAsync<BlittableJsonReaderObject>("Packages/Prius.Core/1.0.0", TestContext.Current.CancellationToken);
            Assert.NotNull(doc);
        }

        // 2. Second Plan - Delete Package by setting it to Empty
        var plan2 = DictionaryMap.New;
        plan2.DeepPut("VersionId".AsSpan(), "2.0.0");
        plan2.DeepPut("BaseVersionId".AsSpan(), "1.0.0");
        plan2.DeepPut("Status".AsSpan(), "Draft");

        var ops2 = DictionaryMap.New;
        ops2["0"] = DictionaryMap.New.With("Action", "PutValue").With("Args", DictionaryMap.New.With("Path", "Packages/Prius.Core/1.0.0").With("Value", Empty.Instance)).AsMapValue();
        
        var mHash2 = MetadataMigrationApplier.ComputeOperationsHash(ops2);
        var cHash2 = MetadataMigrationApplier.ComputeChainHash(cHash1, mHash2);
        plan2.DeepPut("Migrations/0".AsSpan(), DictionaryMap.New.With("Id", "Mig-2").With("ChainHash", cHash2).With("Operations", ops2));

        using (var session = store.OpenAsyncSession())
        {
            await applier.ApplyVersionPlanAsync(session, plan2, TestContext.Current.CancellationToken);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Verify package was deleted
        using (var session = store.OpenAsyncSession())
        {
            var doc = await session.LoadAsync<BlittableJsonReaderObject>("Packages/Prius.Core/1.0.0", TestContext.Current.CancellationToken);
            Assert.Null(doc);
        }
    }
}
