using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Prius.Core.Maps;
using Prius.Engine.Nuspec;
using Xunit;

namespace Prius.Engine.Tests;

public sealed class PackageSystemTests
{
    private const string Tfm = "net10.0";

    [Fact]
    public async Task Import_Export_Cycle_Should_Preserve_Data()
    {
        // Arrange
        const string Content = "binary-meat-123";
        const string PkgId = "Prius.Base";
        const string Version = "1.0.0";
        
        using var sourceStream = CreateTestNupkg(PkgId, Version, Content);
        var repo = new MockPackageRepository();

        // Act
        var importedMap = PackageImporter.Import(sourceStream);
        var dllHash = importedMap.GetDeep("Assets/lib/net10.0/Prius.Base.dll/hash").AsString();
        repo.AddPackage(importedMap, new Dictionary<string, byte[]> { [dllHash] = Encoding.UTF8.GetBytes(Content) });
        
        using var outputStream = new MemoryStream();
        await PackageExporter.Export(importedMap, repo, outputStream, TestContext.Current.CancellationToken);
        
        // Assert
        outputStream.Position = 0;
        await using var archive = new ZipArchive(outputStream, ZipArchiveMode.Read);
        var entry = archive.GetEntry($"lib/{Tfm}/{PkgId}.dll");
        Assert.NotNull(entry);
        
        using var reader = new StreamReader(await entry.OpenAsync(TestContext.Current.CancellationToken));
        var actualContent = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);
        Assert.Equal(Content, actualContent);
    }

    [Fact]
    public async Task Resolver_Should_Handle_Complex_Graph_And_Highest_Wins()
    {
        // Arrange
        var repo = new MockPackageRepository();
        
        repo.AddPackage(CreateManifest("Common", "1.0.0"));
        repo.AddPackage(CreateManifest("Common", "2.0.0"));
        repo.AddPackage(CreateManifest("A", "1.0.0", Tfm, ("Common", "1.0.0")));
        repo.AddPackage(CreateManifest("B", "1.0.0", Tfm, ("Common", "2.0.0")));

        var resolver = new PackageResolver(repo);
        var targets = JsonReaderMap.From($$"""
        {
            "A": "1.0.0",
            "B": "1.0.0"
        }
        """);

        // Act
        var snapshot = await resolver.Resolve(Tfm, targets, TestContext.Current.CancellationToken);
        var order = snapshot["Order"].AsMap();
        var manifests = snapshot["Manifests"].AsMap();

        // Assert
        Assert.Equal("Common", order["0"].AsString());
        Assert.Equal("2.0.0", manifests.GetDeep("Common/Info/version").AsString());
        Assert.Equal(3, order.Keys().Count());
    }

    [Fact]
    public async Task Resolver_Should_Fallback_To_Any_Framework()
    {
        // Arrange
        var repo = new MockPackageRepository();

        repo.AddPackage(CreateManifest("AnyLib", "1.0.0", "any", ("Shared.Core", "1.0.0")));
        repo.AddPackage(CreateManifest("Shared.Core", "1.0.0"));

        var resolver = new PackageResolver(repo);

        // Act
        var targets = JsonReaderMap.From($$"""
        {
            "AnyLib": "1.0.0"
        }
        """);
        var snapshot = await resolver.Resolve(Tfm, targets, TestContext.Current.CancellationToken);
        var order = snapshot["Order"].AsMap();

        // Assert
        Assert.Contains(order.Keys().Select(k => order[k].AsString()), x => x == "Shared.Core");
    }

    private static MemoryStream CreateTestNupkg(string id, string version, string content)
    {
        var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            var nuspec = archive.CreateEntry($"{id}.nuspec");
            using (var writer = new StreamWriter(nuspec.Open()))
            {
                writer.Write($"""
                <package xmlns="http://microsoft.com">
                    <metadata>
                        <id>{id}</id>
                        <version>{version}</version>
                        <authors>TestAuthor</authors>
                    </metadata>
                </package>
                """);
            }
            
            var dll = archive.CreateEntry($"lib/{Tfm}/{id}.dll");
            using (var writer = new StreamWriter(dll.Open()))
                writer.Write(content);
        }
        ms.Position = 0;
        return ms;
    }

    private static IMap CreateManifest(string id, string version, string tfm = Tfm, params (string id, string version)[] deps)
    {
        var dependencies = new Dictionary<string, object>();
        var tfmGroup = new Dictionary<string, object>();

        foreach (var (depId, depVer) in deps)
            tfmGroup[depId] = new { version = depVer };
        
        dependencies[tfm] = tfmGroup;
        
        return JsonReaderMap.From($$"""
        {
            "Info": {
                "id": "{{id}}",
                "version": "{{version}}"
            },
            "Dependencies": {{JsonSerializer.Serialize(dependencies)}},
            "Assets": {}
        }
        """);
    }
}
