using System.IO.Compression;
using System.Text;
using Prius.Core.Maps;
using Xunit;

namespace Prius.Core.Packages.Tests;

public sealed class PackageFullCycleTests
{
    [Fact]
    public async Task Full_Cycle_From_Import_To_Export_Should_Work()
    {
        const string OriginalContent = "important binary meat";
        
        using var sourceStream = new MemoryStream();
        await using (var archive = new ZipArchive(sourceStream, ZipArchiveMode.Create, true))
        {
            var nuspec = archive.CreateEntry("Prius.Test.nuspec");
            await using (var writer = new StreamWriter(await nuspec.OpenAsync(TestContext.Current.CancellationToken)))
            {
                await writer.WriteAsync("""
                                        <package xmlns="http://microsoft.com">
                                           <metadata>
                                               <id>Prius.Test</id>
                                               <version>1.0.0</version>
                                               <authors>GeX</authors>
                                            </metadata>
                                        </package>
                                        """);
            }

            var dll = archive.CreateEntry("lib/net10.0/Prius.Test.dll");
            await using (var writer = new StreamWriter(await dll.OpenAsync(TestContext.Current.CancellationToken)))
                await writer.WriteAsync(OriginalContent);
        }
        
        sourceStream.Position = 0;
        var importedMap = PackageImporter.Import(sourceStream);
        
        var repo = new MockPackageRepository();
        var blobs = new Dictionary<string, byte[]> { 
            [importedMap.DeepGet("Assets/lib/net10.0/Prius.Test.dll/hash").AsValue<string>()] = Encoding.UTF8.GetBytes(OriginalContent) 
        };
        repo.AddPackage(importedMap, blobs);
        
        var resolver = new PackageResolver(repo);
        var resolvedGraph = await resolver.ResolveAsync("Prius.Test", "1.0.0", "net10.0", TestContext.Current.CancellationToken);
        
        Assert.Equal("1.0.0", resolvedGraph.DeepGet("Versions/Prius.Test").AsValue<string>());
        
        using var outputStream = new MemoryStream();
        var manifest = await repo.GetManifestsAsync("any", resolvedGraph.Get("Versions").AsMap(), TestContext.Current.CancellationToken);
        
        await PackageExporter.ExportAsync(manifest.Get("Prius.Test").AsMap(), repo, outputStream, TestContext.Current.CancellationToken);
        
        outputStream.Position = 0;
        await using var finalArchive = new ZipArchive(outputStream, ZipArchiveMode.Read);
        
        Assert.NotNull(finalArchive.GetEntry("Prius.Test.nuspec"));
        var dllEntry = finalArchive.GetEntry("lib/net10.0/Prius.Test.dll");
        Assert.NotNull(dllEntry);

        using var reader = new StreamReader(await dllEntry.OpenAsync(TestContext.Current.CancellationToken));
        Assert.Equal(OriginalContent, await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }
}
