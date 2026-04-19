using System.IO.Compression;
using System.Xml.Linq;
using Prius.Core.Maps;

namespace Prius.Core.Packages;

public static class PackageExporter
{
    /// <summary>
    /// Exports a package from the repository to a .nupkg (ZIP) stream.
    /// </summary>
    public static async Task ExportAsync(
        IMap manifest, 
        IPackageRepository repository, 
        Stream outputStream, 
        CancellationToken ct = default)
    {
        await using var archive = new ZipArchive(outputStream, ZipArchiveMode.Create, true);
        
        var nuspecEntry = archive.CreateEntry($"{manifest.DeepGet("Info/id")}.nuspec");
        await using (var writer = new StreamWriter(await nuspecEntry.OpenAsync(ct)))
            await writer.WriteAsync(GenerateNuspec(manifest));
        
        await PackAssetsAsync(archive, manifest.Get("Assets").AsMap(), repository, string.Empty, ct);
    }

    private static string GenerateNuspec(IMap manifest)
    {
        var info = manifest.Get("Info").AsMap();
        var ns = XNamespace.Get("http://microsoft.com");
        var metadata = new XElement(ns + "metadata");

        foreach (var key in info.Keys())
        {
            if (info.Get(key).IsMap) 
                continue;
            metadata.Add(new XElement(ns + key, info.Get(key).AsValue<string>()));
        }

        var deps = manifest.Get("Dependencies").AsMap();
        if (deps.IsEmpty)
            return new XDocument(new XElement(ns + "package", metadata)).ToString();
        
        var depsElement = new XElement(ns + "dependencies");
        foreach (var tfm in deps.Keys())
        {
            var group = new XElement(ns + "group", new XAttribute("targetFramework", tfm));
            var groupMap = deps.Get(tfm).AsMap();
            foreach (var depId in groupMap.Keys())
            {
                var depInfo = groupMap.Get(depId).AsMap();
                var depElement = new XElement(ns + "dependency", new XAttribute("id", depId));
                foreach (var attr in depInfo.Keys())
                    depElement.Add(new XAttribute(attr, depInfo.Get(attr).AsValue<string>()));
                    
                group.Add(depElement);
            }
            depsElement.Add(group);
        }
        metadata.Add(depsElement);

        return new XDocument(new XElement(ns + "package", metadata)).ToString();
    }

    private static async Task PackAssetsAsync(ZipArchive archive, IMap assets, IPackageRepository repo, string currentPath, CancellationToken ct)
    {
        foreach (var key in assets.Keys())
        {
            var value = assets.Get(key);
            if (!value.IsMap) continue;

            var subMap = value.AsMap();
            var nextPath = string.IsNullOrEmpty(currentPath) ? key : $"{currentPath}/{key}";
            var hash = subMap.Get("hash").AsValue<string>();

            if (string.IsNullOrEmpty(hash))
            {
                await PackAssetsAsync(archive, subMap, repo, nextPath, ct);
                continue;
            }

            var entry = archive.CreateEntry(nextPath);
            await using var source = await repo.OpenStreamAsync(hash, ct);
            await using var target = await entry.OpenAsync(ct);
            await source.CopyToAsync(target, ct);
        }
    }
}
