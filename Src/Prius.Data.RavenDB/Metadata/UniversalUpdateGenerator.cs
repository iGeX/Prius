using System.Security.Cryptography;
using System.Text;
using Prius.Core.Maps;

namespace Prius.Data.RavenDB.Metadata;

public sealed class UniversalUpdateGenerator
{
    private readonly List<IMetadataHandler> _handlers;

    public UniversalUpdateGenerator(IEnumerable<IMetadataHandler>? handlers = null) => 
        _handlers = handlers?.ToList() ?? [ new PackageMetadataHandler() ];

    public IMap GenerateVersionPlan(
        IMap localSnapshot, 
        IMap updateScript, 
        string currentChainHash = "")
    {
        var scriptId = updateScript.DeepGet("ScriptId".AsSpan()).AsString();
        if (string.IsNullOrEmpty(scriptId))
            throw new ArgumentException("Script must contain a non-empty 'ScriptId'.");

        // 1. Evaluate Preconditions
        var preconditionsNode = updateScript.DeepGet("Preconditions".AsSpan());
        if (preconditionsNode.IsMap)
        {
            var preconditions = preconditionsNode.AsMap();
            foreach (var key in preconditions.Keys(ascending: true))
            {
                if (!EvaluatePrecondition(localSnapshot, preconditions[key].AsMap()))
                    throw new InvalidOperationException($"Universal script precondition '{key}' failed. Update cannot be generated.");
            }
        }

        // 2. Generate Operations based on Mutations list
        var generatedOps = DictionaryMap.New;
        var mutationsNode = updateScript.DeepGet("Mutations".AsSpan());
        var opIndex = 0;

        if (mutationsNode.IsMap)
        {
            var mutations = mutationsNode.AsMap();
            foreach (var key in mutations.Keys(ascending: true))
            {
                var mutation = mutations[key].AsMap();
                var type = mutation.DeepGet("Type".AsSpan()).AsString();

                switch (type)
                {
                    case "DeployPackage":
                        GenerateDeployPackageOps(mutation, generatedOps, ref opIndex);
                        break;
                    case "RegisterModule":
                        GenerateRegisterModuleOps(mutation, generatedOps, ref opIndex);
                        break;
                    default:
                        throw new NotSupportedException($"Mutation type '{type}' is not supported.");
                }
            }
        }

        // 3. Dry-run to calculate SnapshotHash and DerivedHash
        var clonedSnapshot = new DictionaryMap(localSnapshot.DeepCopy());
        ApplyDryRunOperations(clonedSnapshot, generatedOps);

        var snapshotHash = MetadataProjectionContext.ComputeMapHash(clonedSnapshot);

        var allActions = new List<ProjectionAction>();
        foreach (var handler in _handlers)
        {
            var ns = handler switch
            {
                PackageMetadataHandler => "Packages/",
                _ => null
            };
            var context = new MetadataProjectionContext(ns);
            handler.ProjectAsync(context, localSnapshot, clonedSnapshot, CancellationToken.None).AsTask().GetAwaiter().GetResult();
            allActions.AddRange(context.Actions);
        }

        var derivedHash = CalculateCombinedDerivedHash(allActions);
        var migrationHash = MetadataMigrationApplier.ComputeOperationsHash(generatedOps);
        var chainHash = MetadataMigrationApplier.ComputeChainHash(currentChainHash, migrationHash);

        // 4. Assemble the final VersionPlan
        var plan = DictionaryMap.New;
        plan.DeepPut("VersionId".AsSpan(), scriptId);
        plan.DeepPut("BaseVersionId".AsSpan(), "AutoDetectedBase");
        plan.DeepPut("Status".AsSpan(), "Draft");

        var migrationBlock = DictionaryMap.New;
        migrationBlock.DeepPut("Id".AsSpan(), $"Migration-{scriptId}");
        migrationBlock.DeepPut("ChainHash".AsSpan(), chainHash);
        migrationBlock.DeepPut("SnapshotHash".AsSpan(), snapshotHash);
        migrationBlock.DeepPut("DerivedHash".AsSpan(), derivedHash);
        migrationBlock.DeepPut("Operations".AsSpan(), generatedOps);

        plan.DeepPut("Migrations/0".AsSpan(), migrationBlock);

        return plan;
    }

    private bool EvaluatePrecondition(IMap snapshot, IMap precondition)
    {
        var check = precondition.DeepGet("Check".AsSpan()).AsString();
        var path = precondition.DeepGet("Path".AsSpan()).AsString();
        var expectedVal = precondition.DeepGet("Value".AsSpan());

        if (string.IsNullOrEmpty(check) || string.IsNullOrEmpty(path))
            return false;

        var cleanPath = path.Replace("System/Snapshot:", "");
        var actualVal = snapshot.DeepGet(new MapPath(cleanPath.AsSpan()));

        if (check == "PropertyEquals")
            return actualVal.Equals(expectedVal);

        return false;
    }

    private void GenerateDeployPackageOps(IMap mutation, IMap generatedOps, ref int opIndex)
    {
        var pkgId = mutation.DeepGet("PackageId".AsSpan()).AsString();
        var version = mutation.DeepGet("Version".AsSpan()).AsString();
        var assetsNode = mutation.DeepGet("Assets".AsSpan());

        if (string.IsNullOrEmpty(pkgId) || string.IsNullOrEmpty(version))
            throw new ArgumentException("DeployPackage mutation must specify 'PackageId' and 'Version'.");

        var rootPath = $"Packages/{pkgId}/{version}";

        // Write Info sub-map
        var infoMap = DictionaryMap.New;
        infoMap.DeepPut("id".AsSpan(), pkgId);
        infoMap.DeepPut("version".AsSpan(), version);

        var opInfo = DictionaryMap.New;
        opInfo.DeepPut("Action".AsSpan(), "PutValue");
        opInfo.DeepPut("Args/Path".AsSpan(), $"{rootPath}/Info");
        opInfo.DeepPut("Args/Value".AsSpan(), infoMap);
        generatedOps[opIndex++.ToString()] = opInfo.AsMapValue();

        if (assetsNode.IsMap)
            GenerateAssetOpsRecursive(assetsNode.AsMap(), rootPath + "/Assets", generatedOps, ref opIndex);
    }

    private void GenerateAssetOpsRecursive(IMap folder, string currentPath, IMap generatedOps, ref int opIndex)
    {
        foreach (var key in folder.Keys())
        {
            var val = folder[key];
            if (val.IsMap)
            {
                GenerateAssetOpsRecursive(val.AsMap(), $"{currentPath}/{key}", generatedOps, ref opIndex);
            }
            else if (val.IsString)
            {
                var base64 = val.AsString();
                var rawBytes = Convert.FromBase64String(base64);
                var hashBytes = SHA256.HashData(rawBytes);
                var hash = Convert.ToHexString(hashBytes).ToLowerInvariant();

                var assetNode = DictionaryMap.New;
                assetNode.DeepPut("Hash".AsSpan(), hash);
                assetNode.DeepPut("ContentBase64".AsSpan(), base64);

                var opAsset = DictionaryMap.New;
                opAsset.DeepPut("Action".AsSpan(), "PutValue");
                opAsset.DeepPut("Args/Path".AsSpan(), $"{currentPath}/{key}");
                opAsset.DeepPut("Args/Value".AsSpan(), assetNode);
                generatedOps[opIndex++.ToString()] = opAsset.AsMapValue();
            }
        }
    }

    private void GenerateRegisterModuleOps(IMap mutation, IMap generatedOps, ref int opIndex)
    {
        var archetype = mutation.DeepGet("Archetype".AsSpan()).AsString();
        var module = mutation.DeepGet("Module".AsSpan()).AsString();

        if (string.IsNullOrEmpty(archetype) || string.IsNullOrEmpty(module))
            throw new ArgumentException("RegisterModule mutation must specify 'Archetype' and 'Module'.");

        var path = $"Blueprint/Archetypes/{archetype}/Modules/{module}";

        var op = DictionaryMap.New;
        op.DeepPut("Action".AsSpan(), "PutValue");
        op.DeepPut("Args/Path".AsSpan(), path);
        op.DeepPut("Args/Value".AsSpan(), true);
        generatedOps[opIndex++.ToString()] = op.AsMapValue();
    }

    private void ApplyDryRunOperations(IMap snapshot, IMap operations)
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
                var val = args.DeepGet("Value".AsSpan());
                snapshot.DeepPut(new MapPath(path.AsSpan()), val);
            }
        }
    }

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
}
