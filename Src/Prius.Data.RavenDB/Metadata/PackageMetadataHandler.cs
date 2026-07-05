using Prius.Core.Maps;

namespace Prius.Data.RavenDB.Metadata;

public sealed class PackageMetadataHandler : IMetadataHandler
{
    public ValueTask ProjectAsync(
        IMetadataProjectionContext context, 
        IMap oldSnapshot, 
        IMap newSnapshot, 
        CancellationToken ct)
    {
        var oldPkgs = oldSnapshot.DeepGet("Packages".AsSpan()).AsMap();
        var newPkgs = newSnapshot.DeepGet("Packages".AsSpan()).AsMap();

        // 1. Process deletions
        foreach (var pkgId in oldPkgs.Keys())
        {
            var oldVersions = oldPkgs[pkgId].AsMap();
            var newVersions = newPkgs.ContainsKey(pkgId) && !newPkgs[pkgId].IsEmpty 
                ? newPkgs[pkgId].AsMap() 
                : null;

            foreach (var version in oldVersions.Keys())
            {
                var isDeleted = newVersions is null || 
                                !newVersions.ContainsKey(version) || 
                                newVersions[version].IsEmpty;

                if (isDeleted)
                {
                    var docId = $"Packages/{pkgId}/{version}";
                    context.DeleteDocument(docId);
                }
            }
        }

        // 2. Process additions and updates
        foreach (var pkgId in newPkgs.Keys())
        {
            if (newPkgs[pkgId].IsEmpty)
                continue;

            var newVersions = newPkgs[pkgId].AsMap();
            var oldVersions = oldPkgs.ContainsKey(pkgId) && !oldPkgs[pkgId].IsEmpty 
                ? oldPkgs[pkgId].AsMap() 
                : null;

            foreach (var version in newVersions.Keys())
            {
                var newPkgData = newVersions[version];
                if (newPkgData.IsEmpty)
                    continue;

                var oldPkgData = oldVersions is not null && oldVersions.ContainsKey(version) && !oldVersions[version].IsEmpty
                    ? oldVersions[version].AsMap() 
                    : null;

                if (oldPkgData is null || !oldPkgData.Equals(newPkgData.AsMap()))
                    ProjectPackage(context, pkgId, version, newPkgData.AsMap());
            }
        }

        return ValueTask.CompletedTask;
    }

    private void ProjectPackage(
        IMetadataProjectionContext context, 
        string pkgId, 
        string version, 
        IMap pkgData)
    {
        var docId = $"Packages/{pkgId}/{version}";
        var clonedData = new DictionaryMap(pkgData.DeepCopy());
        
        var attachmentsToStore = new List<(string Hash, byte[] Bytes)>();
        
        var assetsNode = clonedData.DeepGet("Assets".AsSpan());
        if (assetsNode.IsMap)
            ExtractAttachmentsFromAssets(assetsNode.AsMap(), attachmentsToStore);

        // Save lightweight metadata document
        context.StoreDocument(docId, clonedData);

        // Store attachments
        foreach (var (hash, bytes) in attachmentsToStore)
        {
            var stream = new MemoryStream(bytes);
            context.StoreAttachment(docId, hash, stream);
        }
    }

    private void ExtractAttachmentsFromAssets(IMap assetsFolder, List<(string Hash, byte[] Bytes)> attachments)
    {
        foreach (var key in assetsFolder.Keys())
        {
            var nodeVal = assetsFolder[key];
            if (!nodeVal.IsMap)
                continue;

            var nodeMap = nodeVal.AsMap();
            var hashVal = nodeMap.DeepGet("Hash".AsSpan());
            var base64Val = nodeMap.DeepGet("ContentBase64".AsSpan());

            if (hashVal.IsString && base64Val.IsString)
            {
                var hash = hashVal.AsString();
                var base64 = base64Val.AsString();

                attachments.Add((hash, Convert.FromBase64String(base64)));
                nodeMap.DeepPut("ContentBase64".AsSpan(), new MapValue());
            }
            else
            {
                ExtractAttachmentsFromAssets(nodeMap, attachments);
            }
        }
    }
}
