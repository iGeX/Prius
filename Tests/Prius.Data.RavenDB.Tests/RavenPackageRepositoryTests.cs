using Raven.Client.Documents;
using Raven.TestDriver;
using Microsoft.Extensions.Logging.Abstractions;
using Prius.Engine;
using Prius.Core.Maps;
using Prius.Data.RavenDB.Indexes;
using Xunit;

namespace Prius.Data.RavenDB.Tests;

[Collection("RavenDB-Repository-Tests")]
public sealed class RavenPackageRepositoryTests : RavenTestDriver
{
    static RavenPackageRepositoryTests() => 
        ConfigureServer(new TestServerOptions { Licensing = { ThrowOnInvalidOrMissingLicense = false } });

    private sealed class TestDocumentStoreHolder(IDocumentStore store) : IDocumentStoreHolder
    {
        public IDocumentStore Store => store;
    }
    
    [Fact]
    public async Task GetPackages_ReturnsUniquePackageIdsFromNuspecStructure()
    {
        using var store = GetDocumentStore();
        await new Packages_Packages_ByIdAndVersion().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        using (var session = store.OpenAsyncSession())
        {
            var p1 = new { Info = new { id = "Newtonsoft.Json", version = "13.0.3" } };
            await session.StoreAsync(p1, "Packages/Newtonsoft.Json/13.0.3", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p1)["@collection"] = "Packages";
            
            var p2 = new { Info = new { id = "Newtonsoft.Json", version = "13.0.4" } };
            await session.StoreAsync(p2, "Packages/Newtonsoft.Json/13.0.4", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p2)["@collection"] = "Packages";
            
            var p3 = new { Info = new { id = "Prius.Core", version = "1.0.0" } };
            await session.StoreAsync(p3, "Packages/Prius.Core/1.0.0", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p3)["@collection"] = "Packages";
            
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var packages = await repo.GetPackages(TestContext.Current.CancellationToken);
        
        Assert.Equal(2, packages.Keys().Count());
        Assert.True(packages["Newtonsoft.Json"].AsBool());
        Assert.True(packages["Prius.Core"].AsBool());
    }

    [Fact]
    public async Task GetVersions_RetrievesVersionsCorrectly()
    {
        using var store = GetDocumentStore();
        await new Packages_Packages_ByIdAndVersion().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        using (var session = store.OpenAsyncSession())
        {
            var p1 = new { Info = new { id = "Prius.Core", version = "1.0.0" } };
            await session.StoreAsync(p1, "Packages/Prius.Core/1.0.0", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p1)["@collection"] = "Packages";

            var p2 = new { Info = new { id = "Prius.Core", version = "2.0.0" } };
            await session.StoreAsync(p2, "Packages/Prius.Core/2.0.0", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p2)["@collection"] = "Packages";

            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var requestIds = DictionaryMap.New;
        requestIds["Prius.Core"] = true;

        var result = await repo.GetVersions("net8.0", requestIds, TestContext.Current.CancellationToken);
        var versions = result["Prius.Core"].AsMap();

        Assert.NotNull(versions);
        Assert.True(versions["1.0.0"].AsBool());
        Assert.True(versions["2.0.0"].AsBool());
    }

    [Fact]
    public async Task OpenStream_FindsRecursiveAssetAndEagerLoadsAllAttachments()
    {
        using var store = GetDocumentStore();
        await new Packages_Assets_ByHash().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string TargetHash = "sha256-dll-hash";
        const string CompanionHash = "sha256-xml-hash";
        const string DocId = "Packages/Prius.Core/1.0.0";
        
        using (var session = store.OpenAsyncSession())
        {
            var p = new
            {
                Assets = new
                {
                    lib = new
                    {
                        net10_0 = new
                        {
                            Prius_Core_dll = new { Hash = TargetHash },
                            Prius_Core_xml = new { Hash = CompanionHash }
                        }
                    }
                }
            };

            await session.StoreAsync(p, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);

            session.Advanced.Attachments.Store(DocId, TargetHash, new MemoryStream("dll-bytes"u8.ToArray()), "application/octet-stream");
            session.Advanced.Attachments.Store(DocId, CompanionHash, new MemoryStream("xml-bytes"u8.ToArray()), "application/octet-stream");
            
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        await using var stream = await repo.OpenStream(TargetHash, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal("dll-bytes", content);
        Assert.True(binaryManager.Get(new MapPath($"Packages/{TargetHash}".AsSpan())).Exists);
        Assert.True(binaryManager.Get(new MapPath($"Packages/{CompanionHash}".AsSpan())).Exists);
    }
    
    [Fact]
    public async Task GetVersions_ReturnsEmptyMap_WhenRequestedIdsAreEmpty()
    {
        using var store = GetDocumentStore();
        await new Packages_Packages_ByIdAndVersion().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        var emptyRequest = DictionaryMap.New;
        var result = await repo.GetVersions("net8.0", emptyRequest, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public async Task GetManifests_FiltersDependenciesBySpecificTfm_AccordingToNuspecMapperStructure()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "Newtonsoft.Json";
        const string VersionStr = "13.0.3";
        const string DocId = $"Packages/{PackageId}/{VersionStr}";

        using (var session = store.OpenAsyncSession())
        {
            // Наглядная и декларативная инициализация графа NuspecMapper через JsonReaderMap
            var rootMap = JsonReaderMap.From($$"""
            {
                "Info": {
                    "id": "{{PackageId}}",
                    "version": "{{VersionStr}}"
                },
                "Dependencies": {
                    "net8.0": {
                        "System.Text.Json": { "version": "8.0.0" }
                    },
                    "any": {
                        "Microsoft.CSharp": { "version": "4.7.0" }
                    }
                }
            }
            """);

            // Превращаем IMap в валидный Dictionary<string, object?> для RavenDB драйвера
            var dict = rootMap.DeepCopy();

            await session.StoreAsync(dict, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(dict)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var requestPackages = DictionaryMap.New;
        requestPackages[PackageId] = VersionStr;

        var manifests = await repo.GetManifests("net8.0", requestPackages, TestContext.Current.CancellationToken);
        var targetPackageManifest = manifests[PackageId].AsMap();

        Assert.NotNull(targetPackageManifest);
        
        var filteredDeps = targetPackageManifest.Get(new MapPath("Dependencies".AsSpan())).AsMap();
        
        // Должна остаться только ветка net8.0, ветка any отсекается алгоритмом FilterManifestByTfm
        Assert.True(filteredDeps.ContainsKey("net8.0"));
        Assert.False(filteredDeps.ContainsKey("any"));
        Assert.False(filteredDeps["net8.0"].AsMap()["System.Text.Json"].IsEmpty);
    }

    [Fact]
    public async Task GetManifests_FillsDependenciesWithAny_WhenSpecificTfmIsMissing()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "Prius.Core";
        const string VersionStr = "1.0.0";
        const string DocId = $"Packages/{PackageId}/{VersionStr}";

        using (var session = store.OpenAsyncSession())
        {
            // Декларативный граф Prius с общими зависимостями any
            var rootMap = JsonReaderMap.From($$"""
            {
                "Info": {
                    "id": "{{PackageId}}",
                    "version": "{{VersionStr}}"
                },
                "Dependencies": {
                    "any": {
                        "Microsoft.Build": { "version": "17.0.0" }
                    }
                }
            }
            """);

            var dict = rootMap.DeepCopy();

            await session.StoreAsync(dict, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(dict)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var requestPackages = DictionaryMap.New;
        requestPackages[PackageId] = VersionStr;

        // Запрашиваем фреймворк net10.0, которого нет в манифесте, система обязана откатиться к "any"
        var manifests = await repo.GetManifests("net10.0", requestPackages, TestContext.Current.CancellationToken);
        var filteredDeps = manifests[PackageId].AsMap().Get(new MapPath("Dependencies".AsSpan())).AsMap();

        Assert.True(filteredDeps.ContainsKey("any"));
        Assert.False(filteredDeps.ContainsKey("net10.0"));
    }

    [Fact]
    public async Task OpenStream_ThrowsFileNotFoundException_WhenHashDoesNotExistInIndex()
    {
        using var store = GetDocumentStore();
        await new Packages_Assets_ByHash().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        await Assert.ThrowsAsync<FileNotFoundException>(async () => 
            await repo.OpenStream("non-existent-sha256-hash", TestContext.Current.CancellationToken));
    }
    
        [Fact]
    public async Task GetVersions_ReturnsEmptyMap_WhenRequestedIdDoesNotExistInDatabase()
    {
        using var store = GetDocumentStore();
        await new Packages_Packages_ByIdAndVersion().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        var requestIds = DictionaryMap.New;
        requestIds["Some.Missing.Package"] = true;

        var result = await repo.GetVersions("net8.0", requestIds, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsEmpty);
    }

    [Fact]
    public async Task GetVersions_HandlesBatchRequestsCorrectly_ForMultipleDifferentPackages()
    {
        using var store = GetDocumentStore();
        await new Packages_Packages_ByIdAndVersion().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        using (var session = store.OpenAsyncSession())
        {
            var p1 = JsonReaderMap.From("""{ "Info": { "id": "A.Package", "version": "1.0.0" } }""");
            var dict1 = p1.DeepCopy();
            await session.StoreAsync(dict1, "Packages/A.Package/1.0.0", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(dict1)["@collection"] = "Packages";

            var p2 = JsonReaderMap.From("""{ "Info": { "id": "B.Package", "version": "2.5.0" } }""");
            var dict2 = p2.DeepCopy();
            await session.StoreAsync(dict2, "Packages/B.Package/2.5.0", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(dict2)["@collection"] = "Packages";

            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var requestIds = DictionaryMap.New;
        requestIds["A.Package"] = true;
        requestIds["B.Package"] = true;

        var result = await repo.GetVersions("net8.0", requestIds, TestContext.Current.CancellationToken);

        Assert.True(result.ContainsKey("A.Package"));
        Assert.True(result.ContainsKey("B.Package"));
        Assert.True(result["A.Package"].AsMap().ContainsKey("1.0.0"));
        Assert.True(result["B.Package"].AsMap().ContainsKey("2.5.0"));
    }

    [Fact]
    public async Task GetManifests_SkipsMissingPackages_WithoutThrowingExceptions()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        var requestPackages = DictionaryMap.New;
        requestPackages["Ghost.Package"] = "6.6.6";

        // Запрашиваем манифест пакета, которого нет в RavenDB, проверяем защиту от NullReferenceException
        var manifests = await repo.GetManifests("net8.0", requestPackages, TestContext.Current.CancellationToken);

        Assert.NotNull(manifests);
        Assert.True(manifests.IsEmpty);
    }

    [Fact]
    public async Task GetManifests_UsesInternalCache_OnSubsequentCalls()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "Cached.Package";
        const string VersionStr = "1.0.0";
        const string DocId = $"Packages/{PackageId}/{VersionStr}";

        using (var session = store.OpenAsyncSession())
        {
            var rootMap = JsonReaderMap.From($$"""
            {
                "Info": { "id": "{{PackageId}}", "version": "{{VersionStr}}" },
                "Dependencies": { "any": {} }
            }
            """);

            var dict = rootMap.DeepCopy();
            await session.StoreAsync(dict, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(dict)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var requestPackages = DictionaryMap.New;
        requestPackages[PackageId] = VersionStr;

        // Первый вызов — прогревает MemoryCache репозитория
        await repo.GetManifests("net8.0", requestPackages, TestContext.Current.CancellationToken);
        
        // Удаляем документ физически из базы данных, чтобы проверить работу кэша
        using (var session = store.OpenAsyncSession())
        {
            session.Delete(DocId);
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Второй вызов — документ удален из БД, но если кэш работает, манифест успешно вернется из памяти
        var secondResult = await repo.GetManifests("net8.0", requestPackages, TestContext.Current.CancellationToken);
        var cachedManifest = secondResult[PackageId].AsMap();

        Assert.NotNull(cachedManifest);
        Assert.Equal(PackageId, cachedManifest.Get(new MapPath("Info/id".AsSpan())).AsString());
    }
    
    [Fact]
    public async Task GetVersions_FiltersOutIncompatibleFrameworks_WhenSupportedFrameworksArePresent()
    {
        using var store = GetDocumentStore();
        await new Packages_Packages_ByIdAndVersion().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "Incompatible.Pkg";
        const string DocId = $"Packages/{PackageId}/1.0.0";

        using (var session = store.OpenAsyncSession())
        {
            // Симулируем структуру, где пакет явно заявляет поддержку только устаревшего фреймворка net461
            var rootMap = JsonReaderMap.From($$"""
            {
                "Info": {
                    "id": "{{PackageId}}",
                    "version": "1.0.0"
                },
                "SupportedFrameworks": {
                    "net461": true,
                    "Order": { "0": "net461" }
                }
            }
            """);

            var dict = rootMap.DeepCopy();
            await session.StoreAsync(dict, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(dict)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var requestIds = DictionaryMap.New;
        requestIds[PackageId] = true;

        // Запрашиваем современный TFM (net10.0). Пакет должен отсечься на уровне IsFrameworkCompatible
        var result = await repo.GetVersions("net10.0", requestIds, TestContext.Current.CancellationToken);
        
        // Так как пакет несовместим с net10.0, мапа для него должна отсутствовать или быть пустой
        Assert.True(result[PackageId].IsEmpty);
    }

    [Fact]
    public async Task GetManifests_FiltersOutFrameworkDependencies_LeavesOnlyRequestedAndAny()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "MultiTfm.Pkg";
        const string VersionStr = "2.1.0";
        const string DocId = $"Packages/{PackageId}/{VersionStr}";

        using (var session = store.OpenAsyncSession())
        {
            // Пакет содержит зависимости для трех разных TFM
            var rootMap = JsonReaderMap.From($$"""
            {
                "Info": { "id": "{{PackageId}}", "version": "{{VersionStr}}" },
                "Dependencies": {
                    "net10.0": { "Prius.Engine": { "version": "1.0.0" } },
                    "netstandard2.0": { "NETStandard.Library": { "version": "2.0.3" } },
                    "any": { "System.Collections": { "version": "4.3.0" } }
                }
            }
            """);

            var dict = rootMap.DeepCopy();
            await session.StoreAsync(dict, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(dict)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var requestPackages = DictionaryMap.New;
        requestPackages[PackageId] = VersionStr;

        // Запрашиваем манифест строго для net10.0
        var manifests = await repo.GetManifests("net10.0", requestPackages, TestContext.Current.CancellationToken);
        var filteredDeps = manifests[PackageId].AsMap().Get(new MapPath("Dependencies".AsSpan())).AsMap();

        // Проверяем, что осталась только ветка net10.0, а netstandard2.0 и any вырезаны (или any вырезается, если есть точный TFM)
        Assert.True(filteredDeps.ContainsKey("net10.0"));
        Assert.False(filteredDeps.ContainsKey("netstandard2.0"));
        Assert.False(filteredDeps.ContainsKey("any"));
    }

    [Fact]
    public async Task OpenStream_SkipsDownloading_IfHashAlreadyExistsInBinaryManager()
    {
        using var store = GetDocumentStore();
        await new Packages_Assets_ByHash().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string DuplicateHash = "sha256-shared-hash";
        const string DocId = "Packages/Shared.Pkg/1.0.0";

        // Предварительно укладываем файл в binaryManager вручную, симулируя горячий кэш
        var preCachedPath = new MapPath($"Packages/{DuplicateHash}".AsSpan());
        var fakeData = new MemoryStream("cached-data"u8.ToArray());
        binaryManager.Store(preCachedPath, Empty.Instance, fakeData);

        using (var session = store.OpenAsyncSession())
        {
            var p = JsonReaderMap.From($$"""
            {
                "Assets": {
                    "lib": {
                        "net10": {
                            "Shared.dll": { "Hash": "{{DuplicateHash}}" }
                        }
                    }
                }
            }
            """).DeepCopy();

            await session.StoreAsync(p, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);

            // Сохраняем аттачмент с "неправильными" байтами. Если репозиторий полезет скачивать его повторно, 
            // он перезапишет кэш и контент изменится, что завалит тест.
            session.Advanced.Attachments.Store(DocId, DuplicateHash, new MemoryStream("corrupted-bytes"u8.ToArray()), "application/octet-stream");
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        // Хит по хэшу должен мгновенно выйти по условию accessor.Exists на первой строке метода OpenStream
        await using var stream = await repo.OpenStream(DuplicateHash, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        // Гарантируем, что вернулись данные из кэша binaryManager, а не битые байты из вложения RavenDB
        Assert.Equal("cached-data", content);
    }
 
    [Fact]
    public async Task GetManifests_DoesNotMixCache_ForDifferentTfmsOfSamePackageVersion()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "CacheMix.Pkg";
        const string VersionStr = "1.0.0";

        using (var session = store.OpenAsyncSession())
        {
            // Манифест содержит кардинально разные зависимости для net8.0 и net10.0
            var rootMap = JsonReaderMap.From($$"""
            {
                "Info": { "id": "{{PackageId}}", "version": "{{VersionStr}}" },
                "Dependencies": {
                    "net10.0": { "Prius.Core": { "version": "2.0.0" } },
                    "net8.0": { "Newtonsoft.Json": { "version": "13.0.3" } }
                }
            }
            """);
            var dict = rootMap.DeepCopy();
            await session.StoreAsync(dict, $"Packages/{PackageId}/{VersionStr}", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(dict)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var requestPackages = DictionaryMap.New;
        requestPackages[PackageId] = VersionStr;

        // Первый вызов: Запрашиваем манифест под net10.0 (Должен осесть в памяти)
        var call1 = await repo.GetManifests("net10.0", requestPackages, TestContext.Current.CancellationToken);
        var deps1 = call1[PackageId].AsMap().Get(new MapPath("Dependencies".AsSpan())).AsMap();
        Assert.True(deps1.ContainsKey("net10.0"));

        // Второй вызов: Запрашиваем тот же пакет и версию, но под другой TFM (net8.0)
        // Потенциальная точка падения: Если кэш завязан ТОЛЬКО на пару "Имя:Версия", репозиторий 
        // вернет закешированный net10.0 манифест, проигнорировав новый TFM-фильтр!
        var call2 = await repo.GetManifests("net8.0", requestPackages, TestContext.Current.CancellationToken);
        var deps2 = call2[PackageId].AsMap().Get(new MapPath("Dependencies".AsSpan())).AsMap();
        
        Assert.True(deps2.ContainsKey("net8.0"));
        Assert.False(deps2.ContainsKey("net10.0"));
    }

    [Fact]
    public async Task GetManifests_HandlesMassiveBatchRequest_WithoutSessionTimeouts()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        var requestPackages = DictionaryMap.New;
        
        // Создаем пачку из 100 уникальных документов пакетов в базе данных за одну сессию
        using (var session = store.OpenAsyncSession())
        {
            for (var i = 0; i < 100; i++)
            {
                var pkgId = $"MassPkg.{i}";
                var docMap = JsonReaderMap.From($$"""{ "Info": { "id": "{{pkgId}}", "version": "1.0" }, "Dependencies": {} }""").DeepCopy();
                await session.StoreAsync(docMap, $"Packages/{pkgId}/1.0", TestContext.Current.CancellationToken);
                session.Advanced.GetMetadataFor(docMap)["@collection"] = "Packages";
                
                requestPackages[pkgId] = "1.0";
            }
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        // Потенциальная точка падения: Стресс-тест на лимиты сессии RavenDB.
        // Если внутри GetManifests при обходе entry в docs происходит скрытый N+1 или 
        // повторные вызовы LoadAsync внутри циклов, встроенный лимит сессии (по умолчанию 30 запросов) 
        // выбросит "InvalidOperationException: The maximum number of requests (30) ... has been reached".
        var results = await repo.GetManifests("any", requestPackages, TestContext.Current.CancellationToken);

        Assert.Equal(100, results.Keys().Count());
        Assert.NotNull(results["MassPkg.50"].AsMap());
    }
    
    [Fact]
    public async Task GetVersions_StrictlyRespectsShortCircuit_WhenMultipleCompatibleFrameworksArePresent()
    {
        using var store = GetDocumentStore();
        await new Packages_Packages_ByIdAndVersion().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "ShortCircuit.Pkg";

        using (var session = store.OpenAsyncSession())
        {
            // У пакета есть две версии: одна под специфичный net8.0, вторая под общий any
            var v1 = JsonReaderMap.From("{ \"Info\": { \"id\": \"" + PackageId + "\", \"version\": \"1.0.0\" }, \"Dependencies\": { \"net8.0\": {} } }").DeepCopy();
            await session.StoreAsync(v1, $"Packages/{PackageId}/1.0.0", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(v1)["@collection"] = "Packages";

            var v2 = JsonReaderMap.From("{ \"Info\": { \"id\": \"" + PackageId + "\", \"version\": \"2.0.0\" }, \"Dependencies\": { \"any\": {} } }").DeepCopy();
            await session.StoreAsync(v2, $"Packages/{PackageId}/2.0.0", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(v2)["@collection"] = "Packages";

            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var requestIds = DictionaryMap.New;
        requestIds[PackageId] = true;

        // Запрашиваем фреймворк net8.0
        var result = await repo.GetVersions("net8.0", requestIds, TestContext.Current.CancellationToken);
        var versions = result[PackageId].AsMap();

        // По логике шорт-сиркита Prius: так как нашлась версия под net8.0, 
        // репозиторий обязан вернуть ТОЛЬКО её ("1.0.0") и прекратить поиск, не сваливаясь до "any" ("2.0.0")!
        Assert.True(versions["1.0.0"].AsBool());
        Assert.False(versions["2.0.0"].AsBool());
    }

    [Fact]
    public async Task GetManifests_CorrectlyCachesAndFiltersDifferentTfms_WithoutDataLeaks()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "Isolation.Pkg";
        const string VersionStr = "1.0.0";

        using (var session = store.OpenAsyncSession())
        {
            var rootMap = JsonReaderMap.From("{ \"Info\": { \"id\": \"" + PackageId + "\", \"version\": \"" + VersionStr + "\" }, \"Dependencies\": { \"net10.0\": { \"A\": {} }, \"net8.0\": { \"B\": {} } } }").DeepCopy();
            await session.StoreAsync(rootMap, $"Packages/{PackageId}/{VersionStr}", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(rootMap)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var requestPackages = DictionaryMap.New;
        requestPackages[PackageId] = VersionStr;

        // Первый вызов: просим net10.0. Манифест оседает в кэше.
        var call1 = await repo.GetManifests("net10.0", requestPackages, TestContext.Current.CancellationToken);
        var deps1 = call1[PackageId].AsMap().Get(new MapPath("Dependencies".AsSpan())).AsMap();
        Assert.True(deps1.ContainsKey("net10.0"));

        // Второй вызов: просим net8.0. Если кэш работает правильно и FilterManifestByTfm 
        // вызывается ПОСЛЕ TryGetValue, мы должны получить чистую ветку net8.0, а не застрять в net10.0
        var call2 = await repo.GetManifests("net8.0", requestPackages, TestContext.Current.CancellationToken);
        var deps2 = call2[PackageId].AsMap().Get(new MapPath("Dependencies".AsSpan())).AsMap();

        Assert.True(deps2.ContainsKey("net8.0"));
        Assert.False(deps2.ContainsKey("net10.0"));
    }

    [Fact]
    public async Task OpenStream_ExecutesEagerLoadingAtonally_WithoutDoubleDownloadingExistingHashes()
    {
        using var store = GetDocumentStore();
        await new Packages_Assets_ByHash().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string SharedHash = "sha256-shared-dll";
        const string DocId = "Packages/EagerSkip.Pkg/1.0.0";

        // Заранее греем локальный кэш файлом с контентом "pre-cached"
        var targetPath = $"Packages/{SharedHash}";
        binaryManager.Store(targetPath, Empty.Instance, new MemoryStream("pre-cached"u8.ToArray()));

        using (var session = store.OpenAsyncSession())
        {
            var p = JsonReaderMap.From("{ \"Assets\": { \"lib\": { \"net10_0\": { \"file.dll\": { \"Hash\": \"" + SharedHash + "\" } } } } }").DeepCopy();
            await session.StoreAsync(p, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);

            // В базу льем "server-bytes". Но из-за проверки Exists репозиторий НЕ ДОЛЖЕН переписывать кэш
            session.Advanced.Attachments.Store(DocId, SharedHash, new MemoryStream("server-bytes"u8.ToArray()), "application/octet-stream");
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        await using var stream = await repo.OpenStream(SharedHash, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        // Проверяем, что иммутабельность CAS-хранилища соблюдена и старые байты не затёрлись из сети
        Assert.Equal("pre-cached", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetManifests_HandlesEmptyResponse_WhenBatchIdsAreEmpty()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        var emptyRequest = DictionaryMap.New;
        var result = await repo.GetManifests("any", emptyRequest, TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.True(result.IsEmpty);
    }
    
    [Fact]
    public async Task GetVersions_ReturnsEmptyMap_WhenPackageHasNoDependenciesAndNoAssets()
    {
        using var store = GetDocumentStore();
        await new Packages_Packages_ByIdAndVersion().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "EmptyStructure.Pkg";

        using (var session = store.OpenAsyncSession())
        {
            // Пакет-пустышка без Dependencies и без Assets. JS-индекс должен сгруппировать его как 'any'
            var p = JsonReaderMap.From("{ \"Info\": { \"id\": \"" + PackageId + "\", \"version\": \"1.0.0\" } }").DeepCopy();
            await session.StoreAsync(p, $"Packages/{PackageId}/1.0.0", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var requestIds = DictionaryMap.New;
        requestIds[PackageId] = true;

        // Запрашиваем конкретный TFM (net10.0). Пакет должен отфолбечиться к 'any' и вернуться, так как у него нет ограничений
        var result = await repo.GetVersions("net10.0", requestIds, TestContext.Current.CancellationToken);
        
        Assert.False(result.IsEmpty);
        Assert.True(result[PackageId].AsMap()["1.0.0"].AsBool());
    }

    [Fact]
    public async Task GetManifests_ReturnsManifestCorrectly_WhenPackageHasNoDependenciesLayer()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "NoDeps.Pkg";
        const string VersionStr = "1.1.0";

        using (var session = store.OpenAsyncSession())
        {
            // Пакет содержит только слой Info, секция Dependencies отсутствует полностью
            var rootMap = JsonReaderMap.From("{ \"Info\": { \"id\": \"" + PackageId + "\", \"version\": \"" + VersionStr + "\" } }").DeepCopy();
            await session.StoreAsync(rootMap, $"Packages/{PackageId}/{VersionStr}", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(rootMap)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var requestPackages = DictionaryMap.New;
        requestPackages[PackageId] = VersionStr;

        var manifests = await repo.GetManifests("net10.0", requestPackages, TestContext.Current.CancellationToken);
        var manifest = manifests[PackageId].AsMap();

        // FilterManifestByTfm должен вернуть исходный манифест целиком (ранний выход при отсутствии Dependencies)
        Assert.NotNull(manifest);
        Assert.Equal(PackageId, manifest.Get(new MapPath("Info/id".AsSpan())).ToString());
        Assert.True(manifest.Get(new MapPath("Dependencies".AsSpan())).IsEmpty);
    }

    [Fact]
    public async Task GetVersions_ReturnsVersionsCorrectly_WhenPackageOnlyHasSupportedFrameworksSection()
    {
        using var store = GetDocumentStore();
        await new Packages_Packages_ByIdAndVersion().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "SupportedFx.Pkg";

        using (var session = store.OpenAsyncSession())
        {
            // Пакет заявляет фреймворки строго через SupportedFrameworks (как бывает в некоторых Prius-моделях)
            var p = JsonReaderMap.From("{ \"Info\": { \"id\": \"" + PackageId + "\", \"version\": \"2.5.0\" }, \"SupportedFrameworks\": { \"net8.0\": true } }").DeepCopy();
            await session.StoreAsync(p, $"Packages/{PackageId}/2.5.0", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        var requestIds = DictionaryMap.New;
        requestIds[PackageId] = true;

        // Запрашиваем net10.0 (совместим с net8.0 через фолбек цепочку). Индекс обязан распарсить SupportedFrameworks
        var result = await repo.GetVersions("net10.0", requestIds, TestContext.Current.CancellationToken);
        
        Assert.False(result.IsEmpty);
        Assert.True(result[PackageId].AsMap()["2.5.0"].AsBool());
    }

    [Fact]
    public async Task GetManifests_CorrectlyReturnsCachedData_WhenRequestedMultipleTimesInParallel()
    {
        using var store = GetDocumentStore();
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string PackageId = "Parallel.Pkg";
        const string VersionStr = "1.0.0";

        using (var session = store.OpenAsyncSession())
        {
            var rootMap = JsonReaderMap.From("{ \"Info\": { \"id\": \"" + PackageId + "\", \"version\": \"" + VersionStr + "\" }, \"Dependencies\": { \"any\": {} } }").DeepCopy();
            await session.StoreAsync(rootMap, $"Packages/{PackageId}/{VersionStr}", TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(rootMap)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var requestPackages = DictionaryMap.New;
        requestPackages[PackageId] = VersionStr;

        // Запускаем 3 запроса параллельно. Проверяем потокобезопасность DictionaryMap и MemoryCache в репозитории
        var t1 = repo.GetManifests("any", requestPackages, TestContext.Current.CancellationToken).AsTask();
        var t2 = repo.GetManifests("any", requestPackages, TestContext.Current.CancellationToken).AsTask();
        var t3 = repo.GetManifests("any", requestPackages, TestContext.Current.CancellationToken).AsTask();

        await Task.WhenAll(t1, t2, t3);

        Assert.NotNull((await t1)[PackageId].AsMap());
        Assert.NotNull((await t2)[PackageId].AsMap());
        Assert.NotNull((await t3)[PackageId].AsMap());
    }

    [Fact]
    public async Task OpenStream_ReturnsValidStream_WhenPackageOnlyHasAssetsLibSection()
    {
        using var store = GetDocumentStore();
        await new Packages_Assets_ByHash().ExecuteAsync(store, token: TestContext.Current.CancellationToken);
        
        var binaryManager = new BinaryManager();
        var holder = new TestDocumentStoreHolder(store);
        var repo = new RavenPackageRepository(holder, binaryManager, NullLogger<RavenPackageRepository>.Instance);

        const string Hash = "sha256-pure-lib-hash";
        const string DocId = "Packages/PureLib.Pkg/1.0.0";

        using (var session = store.OpenAsyncSession())
        {
            // Пакет заявляет файлы строго по NuGet-конвенции Assets.lib (без кастомных корневых полей)
            var p = JsonReaderMap.From("{ \"Assets\": { \"lib\": { \"net10_0\": { \"pure.dll\": { \"hash\": \"" + Hash + "\" } } } } }").DeepCopy();
            await session.StoreAsync(p, DocId, TestContext.Current.CancellationToken);
            session.Advanced.GetMetadataFor(p)["@collection"] = "Packages";
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);

            using var ms = new MemoryStream("pure-bytes"u8.ToArray());
            session.Advanced.Attachments.Store(DocId, Hash, ms, "application/octet-stream");
            await session.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        WaitForIndexing(store);

        // Наш детерминированный JS-индекс обязан успешно распарсить Assets.lib.file.hash под нагрузкой
        await using var stream = await repo.OpenStream(Hash, TestContext.Current.CancellationToken);
        using var reader = new StreamReader(stream);

        Assert.Equal("pure-bytes", await reader.ReadToEndAsync(TestContext.Current.CancellationToken));
    }
}
