using System.Text;
using Prius.Core.Maps;
using Prius.Data.RavenDB.Metadata;
using Xunit;

namespace Prius.Data.RavenDB.Tests.Metadata;

public sealed class UniversalUpdateGeneratorTests
{
    [Fact]
    public void GenerateVersionPlan_GeneratesValidVersionPlanWithHashes_WhenPreconditionsPass()
    {
        var generator = new UniversalUpdateGenerator();

        // 1. Initial local snapshot state
        var snapshot = DictionaryMap.New;
        snapshot.DeepPut("Blueprint/Archetypes/GatewayNode/BillingStatus".AsSpan(), "Active");

        // 2. High-level update script
        var script = DictionaryMap.New;
        script.DeepPut("ScriptId".AsSpan(), "VendorUpdate-1.2");
        
        var precondition = DictionaryMap.New;
        precondition.DeepPut("Check".AsSpan(), "PropertyEquals");
        precondition.DeepPut("Path".AsSpan(), "System/Snapshot:Blueprint/Archetypes/GatewayNode/BillingStatus");
        precondition.DeepPut("Value".AsSpan(), "Active");
        script.DeepPut("Preconditions/0".AsSpan(), precondition);

        var m1 = DictionaryMap.New;
        m1.DeepPut("Type".AsSpan(), "DeployPackage");
        m1.DeepPut("PackageId".AsSpan(), "Prius.Security");
        m1.DeepPut("Version".AsSpan(), "1.2.0");
        m1.DeepPut("Assets/lib/net10.0/Prius.Security.dll".AsSpan(), Convert.ToBase64String("security-bytes"u8.ToArray()));
        script.DeepPut("Mutations/0".AsSpan(), m1);

        var m2 = DictionaryMap.New;
        m2.DeepPut("Type".AsSpan(), "RegisterModule");
        m2.DeepPut("Archetype".AsSpan(), "GatewayNode");
        m2.DeepPut("Module".AsSpan(), "Prius.Security");
        script.DeepPut("Mutations/1".AsSpan(), m2);

        // 3. Compile plan
        var plan = generator.GenerateVersionPlan(snapshot, script, "prev-chain-hash");

        // 4. Verify plan structure and hashes
        Assert.NotNull(plan);
        Assert.Equal("VendorUpdate-1.2", plan.DeepGet("VersionId".AsSpan()).AsString());
        Assert.Equal("Draft", plan.DeepGet("Status".AsSpan()).AsString());

        var migration = plan.DeepGet("Migrations/0".AsSpan()).AsMap();
        Assert.Equal("Migration-VendorUpdate-1.2", migration.DeepGet("Id".AsSpan()).AsString());

        var chainHash = migration.DeepGet("ChainHash".AsSpan()).AsString();
        var snapshotHash = migration.DeepGet("SnapshotHash".AsSpan()).AsString();
        var derivedHash = migration.DeepGet("DerivedHash".AsSpan()).AsString();

        Assert.False(string.IsNullOrEmpty(chainHash));
        Assert.False(string.IsNullOrEmpty(snapshotHash));
        Assert.False(string.IsNullOrEmpty(derivedHash));

        // Verify operations count and contents
        var ops = migration.DeepGet("Operations".AsSpan()).AsMap();
        Assert.Equal(3, ops.Keys().Count()); // Info, DLL, and Module registration

        Assert.Equal("PutValue", ops.DeepGet("0/Action".AsSpan()).AsString());
        Assert.Equal("Packages/Prius.Security/1.2.0/Info", ops.DeepGet("0/Args/Path".AsSpan()).AsString());

        Assert.Equal("PutValue", ops.DeepGet("1/Action".AsSpan()).AsString());
        Assert.Equal("Packages/Prius.Security/1.2.0/Assets/lib/net10.0/Prius.Security.dll", ops.DeepGet("1/Args/Path".AsSpan()).AsString());
        Assert.Equal("security-bytes", Encoding.UTF8.GetString(Convert.FromBase64String(ops.DeepGet("1/Args/Value/ContentBase64".AsSpan()).AsString())));

        Assert.Equal("PutValue", ops.DeepGet("2/Action".AsSpan()).AsString());
        Assert.Equal("Blueprint/Archetypes/GatewayNode/Modules/Prius.Security", ops.DeepGet("2/Args/Path".AsSpan()).AsString());
        Assert.True(ops.DeepGet("2/Args/Value".AsSpan()).AsBool());
    }

    [Fact]
    public void GenerateVersionPlan_ThrowsException_WhenPreconditionFails()
    {
        var generator = new UniversalUpdateGenerator();

        var snapshot = DictionaryMap.New;
        snapshot.DeepPut("Blueprint/Archetypes/GatewayNode/BillingStatus".AsSpan(), "Disabled"); // Mismatch

        var script = DictionaryMap.New;
        script.DeepPut("ScriptId".AsSpan(), "VendorUpdate-1.2");

        var precondition = DictionaryMap.New;
        precondition.DeepPut("Check".AsSpan(), "PropertyEquals");
        precondition.DeepPut("Path".AsSpan(), "System/Snapshot:Blueprint/Archetypes/GatewayNode/BillingStatus");
        precondition.DeepPut("Value".AsSpan(), "Active");
        script.DeepPut("Preconditions/0".AsSpan(), precondition);

        var m1 = DictionaryMap.New.With("Type", "RegisterModule").With("Archetype", "GatewayNode").With("Module", "Prius.Security");
        script.DeepPut("Mutations/0".AsSpan(), m1);

        Assert.Throws<InvalidOperationException>(() => 
        {
            generator.GenerateVersionPlan(snapshot, script, "prev-chain-hash");
        });
    }
}
