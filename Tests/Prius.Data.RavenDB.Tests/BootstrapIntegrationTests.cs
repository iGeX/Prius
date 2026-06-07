using Xunit;
using Prius.Engine.Abstractions;
using Prius.Core.Maps;
using System.Reflection;

namespace Prius.Data.RavenDB.Tests;

public class BootstrapIntegrationTests : AbstractDataIntentsProcessorTests
{
    private class LocalMockPackageRepository : IPackageRepository
    {
        private readonly Dictionary<string, IMap> _manifests = new();

        public void AddPackage(IMap manifest)
        {
            var id = manifest.DeepGet("Info/id").AsString();
            var ver = manifest.DeepGet("Info/version").AsString();
            _manifests[$"{id}_{ver}"] = manifest;
        }

        public ValueTask<IMap> GetPackages(CancellationToken ct = default) => throw new NotSupportedException();

        public ValueTask<IMap> GetVersions(string tfm, IMap ids, CancellationToken ct = default)
        {
            var res = DictionaryMap.New;
            foreach (var id in ids.Keys())
            {
                var versions = DictionaryMap.New;
                foreach (var key in _manifests.Keys.Where(k => k.StartsWith(id + "_")))
                    versions[key.Split('_')[1]] = true;
                res[id] = new MapValue(versions);
            }
            return ValueTask.FromResult<IMap>(res);
        }

        public ValueTask<IMap> GetManifests(string tfm, IMap packages, CancellationToken ct = default)
        {
            var res = DictionaryMap.New;
            foreach (var p in packages.Keys())
            {
                var key = $"{p}_{packages[p].AsString()}";
                if (_manifests.TryGetValue(key, out var m)) 
                    res[p] = new MapValue(m);
            }
            return ValueTask.FromResult<IMap>(res);
        }

        public ValueTask<Stream> OpenStream(string hash, CancellationToken ct = default) => 
            ValueTask.FromResult<Stream>(new MemoryStream("fake-dll-bytes"u8.ToArray()));

        public event Func<ValueTask>? OnTransitionToStasis;
        public event Func<ValueTask>? OnTransitionToActive;
        public event Func<ValueTask>? OnTransitionToTerminated;
    }

    private class LocalMockMetadataRegistry : IMetadataRegistry
    {
        public ValueTask<IMap> GetBlueprint(CancellationToken ct = default) => 
            ValueTask.FromResult<IMap>(DictionaryMap.New);

        public event Func<ValueTask>? OnTransitionToStasis;
        public event Func<ValueTask>? OnTransitionToActive;
        public event Func<ValueTask>? OnTransitionToTerminated;
    }

    private class LocalMockBootstrapRuntime : IBootstrapRuntime
    {
        public string Tfm => "net10.0";
        public ValueTask Prepare() => ValueTask.CompletedTask;
        
        public ValueTask<Assembly> LoadAssembly(Stream stream) => 
            ValueTask.FromResult(typeof(PriusModule).Assembly);
            
        public ValueTask WriteAsset(string relativePath, Stream stream) => ValueTask.CompletedTask;
        public ValueTask Unload() => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Bootstrap_ShouldDiscoverAndLifecyclePriusModule()
    {
        var repo = new LocalMockPackageRepository();
        
        var manifest = JsonReaderMap.From("""
        {
            "Info": {
                "id": "Prius.Data.RavenDB",
                "version": "1.0.0"
            },
            "Dependencies": {},
            "Assets": {
                "lib": {
                    "net10.0": {
                        "Prius.Data.RavenDB.dll": {
                            "hash": "some-hash"
                        }
                    }
                }
            }
        }
        """);
        repo.AddPackage(manifest);

        var runtime = new LocalMockBootstrapRuntime();
        var metadataRegistry = new LocalMockMetadataRegistry();

        var bootstrap = new Engine.Bootstrap(repo, runtime, metadataRegistry)
        {
            StartupTargets = JsonReaderMap.From("""
            {
                "Prius.Data.RavenDB": "1.0.0"
            }
            """)
        };
        
        var busField = typeof(Engine.Bootstrap).GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(busField);
        var bus = (IElementContext)busField.GetValue(bootstrap)!;
        
        bus.Put(new MapPath("Configuration/RavenDB/Database".AsSpan()), new MapValue("TestDb"));
        
        var urlsMap = DictionaryMap.New.With("0", new MapValue("http://localhost:8080"));
        bus.Put(new MapPath("Configuration/RavenDB/Urls".AsSpan()), urlsMap.AsMapValue());

        await bootstrap.Activate();
        await bootstrap.Stasis();

        Assert.True(true);
    }
}
