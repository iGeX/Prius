using System.IO.Compression;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Prius.Core.Maps;
using Prius.Core.Packages;
using Prius.Core.Packages.Registry;
using Xunit;

namespace Prius.Core.Packages.Registry.Tests;

public sealed class RegistryIntegrationTests : IDisposable
{
    private readonly string _tempPath;
    private readonly WebApplicationFactory<Program> _factory;

    public RegistryIntegrationTests()
    {
        _tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempPath);

        // Создаем тестовый пакет, чтобы было что искать
        CreateTestPackage("Prius.Test.Package", "1.0.0");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Заменяем реальный репозиторий на Directory с временной папкой и NullLogger
                services.AddSingleton<IPackageRepository>(new DirectoryPackageRepository(
                    _tempPath, 
                    NullLogger<DirectoryPackageRepository>.Instance));
                
                services.AddPackagesRegistry();
            });
        });
    }


    private void CreateTestPackage(string id, string version)
    {
        var path = Path.Combine(_tempPath, $"{id}.{version}.nupkg");
        using var stream = File.OpenWrite(path);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        
        var nuspec = archive.CreateEntry($"{id}.nuspec");
        using var writer = new StreamWriter(nuspec.Open());
        writer.Write($"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://microsoft.com">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <authors>Prius</authors>
                <description>Test</description>
              </metadata>
            </package>
            """);
    }

    public void Dispose()
    {
        _factory.Dispose();
        if (Directory.Exists(_tempPath)) Directory.Delete(_tempPath, true);
    }
    
    [Fact]
    public async Task Search_Should_Return_Packages_From_Live_Server()
    {
        var ct = TestContext.Current.CancellationToken;
        var logger = NuGet.Common.NullLogger.Instance;

        // 1. Инициализируем репозиторий по реальному URL
        var source = new PackageSource("https://localhost:5001/nuget/index.json");
        var providers = Repository.Provider.GetCoreV3();
        var repository = new SourceRepository(source, providers);

        var searchResource = await repository.GetResourceAsync<PackageSearchResource>(ct);
        var autocompleteResource = await repository.GetResourceAsync<AutoCompleteResource>(ct);
        var metadataResource = await repository.GetResourceAsync<RegistrationResourceV3>(ct);

        Assert.NotNull(searchResource);
        Assert.NotNull(autocompleteResource);
        Assert.NotNull(metadataResource);

        // 3. Выполняем поиск
        var searchFilter = new SearchFilter(includePrerelease: true);
        var results = await searchResource.SearchAsync(
            "Prius", 
            searchFilter, 
            0, 10, 
            logger, 
            ct);

        // 4. Проверяем результаты
        var resultsList = results.ToList();
        
        // Логируем в консоль теста, что мы нашли, чтобы не гадать
        foreach (var pkg in resultsList)
            TestContext.Current.SendDiagnosticMessage($"Found package: {pkg.Identity.Id} v{pkg.Identity.Version}");

        Assert.NotEmpty(resultsList);
    }
}
