using System.IO.Compression;
using System.Text;
using Prius.Core.Maps;
using Xunit;

namespace Prius.Core.Packages.Tests;

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
        var dllHash = importedMap.DeepGet("Assets/lib/net10.0/Prius.Base.dll/hash").AsString();
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
        var targets = DictionaryMap.New.With("A", "1.0.0").With("B", "1.0.0");

        // Act
        var snapshot = await resolver.Resolve(Tfm, targets, TestContext.Current.CancellationToken);
        var order = snapshot.Get("Order").AsMap();
        var manifests = snapshot.Get("Manifests").AsMap();

        // Assert
        Assert.Equal("Common", order.Get("0").AsString());
        Assert.Equal("2.0.0", manifests.DeepGet("Common/Info/version").AsString());
        Assert.Equal(3, order.Values.Count());
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
        var snapshot = await resolver.Resolve(Tfm, DictionaryMap.New.With("AnyLib", "1.0.0"), TestContext.Current.CancellationToken);
        var order = snapshot.Get("Order").AsMap();

        // Assert
        Assert.Contains(order.Values.Select(v => v.AsString()), x => x == "Shared.Core");
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

    private static DictionaryMap CreateManifest(string id, string version, string tfm = Tfm, params (string id, string version)[] deps)
    {
        var info = DictionaryMap.New
            .With("id", id)
            .With("version", version);

        var dependencies = DictionaryMap.New;
        var tfmGroup = DictionaryMap.New;

        foreach (var (depId, depVer) in deps)
            tfmGroup.Put(depId, DictionaryMap.New.With("version", depVer));
        
        dependencies.Put(tfm, tfmGroup);

        return DictionaryMap.New
            .With("Info", info)
            .With("Dependencies", dependencies)
            .With("Assets", DictionaryMap.New);
    }
}
