using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Prius.Core.Maps;

namespace Prius.Core.Packages;

public static class PackageImporter
{
    /// <summary>
    /// Imports a NuGet package from a stream, mapping its manifest and all assets into a <see cref="IMap"/> structure.
    /// Assets are preserved in their original folder hierarchy (e.g., lib/net8.0/...) under the "Assets" root.
    /// </summary>
    /// <param name="nupkgStream">The stream containing the .nupkg (ZIP) file data.</param>
    /// <returns>A <see cref="DictionaryMap"/> containing the package identity, dependencies, and asset metadata with hashes.</returns>
    public static DictionaryMap Import(Stream nupkgStream)
    {
        using var archive = new ZipArchive(nupkgStream, ZipArchiveMode.Read);
        
        var nuspecEntry = archive.Entries.FirstOrDefault(e => 
            e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Nuspec not found.");

        using var reader = new StreamReader(nuspecEntry.Open(), Encoding.UTF8);
        var packageMap = NuspecMapper.ToMap(reader.ReadToEnd());

        foreach (var entry in archive.Entries)
        {
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
                continue;

            var parts = entry.FullName.Split('/');
            var assetPath = (MapPath)"Assets";
            
            foreach (var t in parts)
                assetPath += t;

            var fileInfo = DictionaryMap.New;
            fileInfo.Put("Size", entry.Length);
            
            using (var entryStream = entry.Open())
                fileInfo.Put("Hash", ComputeHash(entryStream));

            packageMap.DeepPut(assetPath, fileInfo);
        }

        return packageMap;
    }

    private static string ComputeHash(Stream stream)
    {
        using var sha256 = SHA256.Create();
        return Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();
    }
}
