using Xunit;
using Prius.Engine.Abstractions;
using Prius.Core.Maps;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Sparrow.Json;
using Raven.Client.Documents.Subscriptions;

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

        var modulesField = typeof(Engine.Bootstrap).GetField("_modules", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(modulesField);
        var modules = (List<IPriusModule>)modulesField.GetValue(bootstrap)!;
        var priusModule = Assert.Single(modules);
        
        var processorField = typeof(PriusModule).GetField("_processor", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(processorField);
        var processor = (DataIntentsProcessor)processorField.GetValue(priusModule)!;
        Assert.NotNull(processor);

        var processingTaskField = typeof(DataIntentsProcessor).GetField("_processingTask", BindingFlags.NonPublic | BindingFlags.Instance);
        var ctsField = typeof(DataIntentsProcessor).GetField("_cts", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(processingTaskField);
        Assert.NotNull(ctsField);
        
        var processingTask = (Task)processingTaskField.GetValue(processor)!;
        var cts = (CancellationTokenSource)ctsField.GetValue(processor)!;
        
        Assert.NotNull(processingTask);
        Assert.NotNull(cts);
        Assert.False(processingTask.IsCompleted);
        Assert.False(cts.IsCancellationRequested);

        // 2. Stasis (should call PriusModule.Stasis and execute graceful shutdown)
        await bootstrap.Stasis();

        Assert.True(processingTask.IsCompleted);
        Assert.True(cts.IsCancellationRequested);
    }

    private class LocalMockBootstrapRuntimeForTestModule : IBootstrapRuntime
    {
        public string Tfm => "net10.0";
        public ValueTask Prepare() => ValueTask.CompletedTask;
        
        public ValueTask<Assembly> LoadAssembly(Stream stream) => 
            ValueTask.FromResult(typeof(ConfigurableTestModule).Assembly);
            
        public ValueTask WriteAsset(string relativePath, Stream stream) => ValueTask.CompletedTask;
        public ValueTask Unload() => ValueTask.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Bootstrap_ShouldGracefullyHandleHungModule()
    {
        var repo = new LocalMockPackageRepository();
        
        var manifest = JsonReaderMap.From("""
        {
            "Info": {
                "id": "Prius.Data.RavenDB.Tests",
                "version": "1.0.0"
            },
            "Dependencies": {},
            "Assets": {
                "lib": {
                    "net10.0": {
                        "Prius.Data.RavenDB.Tests.dll": {
                            "hash": "some-hash"
                        }
                    }
                }
            }
        }
        """);
        repo.AddPackage(manifest);

        var runtime = new LocalMockBootstrapRuntimeForTestModule();
        var metadataRegistry = new LocalMockMetadataRegistry();

        // Test 1: Hang on Activate
        ConfigurableTestModule.ConfigureCalled = false;
        ConfigurableTestModule.ActivateCalled = false;
        ConfigurableTestModule.StasisCalled = false;
        ConfigurableTestModule.ShouldHangActivate = true;
        ConfigurableTestModule.ShouldHangStasis = false;

        var bootstrapActivateHang = new Engine.Bootstrap(repo, runtime, metadataRegistry)
        {
            StartupTargets = JsonReaderMap.From("""
            {
                "Prius.Data.RavenDB.Tests": "1.0.0"
            }
            """),
            ModuleTimeout = TimeSpan.FromMilliseconds(50)
        };

        var busField = typeof(Engine.Bootstrap).GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(busField);
        var bus1 = (IElementContext)busField.GetValue(bootstrapActivateHang)!;
        bus1.Put(new MapPath("Configuration/RavenDB/Database".AsSpan()), new MapValue("TestDb"));
        
        var exception = await Record.ExceptionAsync(async () => await bootstrapActivateHang.Activate());
        
        Assert.NotNull(exception);
        Assert.IsType<TimeoutException>(exception);
        Assert.True(ConfigurableTestModule.ConfigureCalled);
        Assert.True(ConfigurableTestModule.ActivateCalled);

        // Test 2: Hang on Stasis
        ConfigurableTestModule.ConfigureCalled = false;
        ConfigurableTestModule.ActivateCalled = false;
        ConfigurableTestModule.StasisCalled = false;
        ConfigurableTestModule.ShouldHangActivate = false;
        ConfigurableTestModule.ShouldHangStasis = true;

        var bootstrapStasisHang = new Engine.Bootstrap(repo, runtime, metadataRegistry)
        {
            StartupTargets = JsonReaderMap.From("""
            {
                "Prius.Data.RavenDB.Tests": "1.0.0"
            }
            """),
            ModuleTimeout = TimeSpan.FromMilliseconds(50)
        };

        var bus2 = (IElementContext)busField.GetValue(bootstrapStasisHang)!;
        bus2.Put(new MapPath("Configuration/RavenDB/Database".AsSpan()), new MapValue("TestDb"));

        await bootstrapStasisHang.Activate();
        
        Assert.True(ConfigurableTestModule.ConfigureCalled);
        Assert.True(ConfigurableTestModule.ActivateCalled);
        Assert.False(ConfigurableTestModule.StasisCalled);

        // This call should NOT throw/hang because DoStasis handles the timeout internally and logs it
        var stasisException = await Record.ExceptionAsync(async () => await bootstrapStasisHang.Stasis());
        Assert.Null(stasisException);
        Assert.True(ConfigurableTestModule.StasisCalled);
    }

    [Fact]
    public async Task Bootstrap_ShouldReadTimeoutFromConfiguration()
    {
        var repo = new LocalMockPackageRepository();
        
        var manifest = JsonReaderMap.From("""
        {
            "Info": {
                "id": "Prius.Data.RavenDB.Tests",
                "version": "1.0.0"
            },
            "Dependencies": {},
            "Assets": {
                "lib": {
                    "net10.0": {
                        "Prius.Data.RavenDB.Tests.dll": {
                            "hash": "some-hash"
                        }
                    }
                }
            }
        }
        """);
        repo.AddPackage(manifest);

        var runtime = new LocalMockBootstrapRuntimeForTestModule();
        var metadataRegistry = new LocalMockMetadataRegistry();

        ConfigurableTestModule.ConfigureCalled = false;
        ConfigurableTestModule.ActivateCalled = false;
        ConfigurableTestModule.ShouldHangActivate = true;

        var bootstrap = new Engine.Bootstrap(repo, runtime, metadataRegistry)
        {
            StartupTargets = JsonReaderMap.From("""
            {
                "Prius.Data.RavenDB.Tests": "1.0.0"
            }
            """)
        };

        var busField = typeof(Engine.Bootstrap).GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(busField);
        var bus = (IElementContext)busField.GetValue(bootstrap)!;
        bus.Put(new MapPath("Configuration/RavenDB/Database".AsSpan()), new MapValue("TestDb"));
        
        // Put the timeout config as a string representing seconds
        bus.Put(new MapPath("Configuration/Bootstrap/ModuleTimeout".AsSpan()), new MapValue("0.05"));

        var exception = await Record.ExceptionAsync(async () => await bootstrap.Activate());
        
        Assert.NotNull(exception);
        Assert.IsType<TimeoutException>(exception);
        Assert.True(ConfigurableTestModule.ActivateCalled);
    }

    [Fact]
    public async Task Bootstrap_ShouldStopSubscriptionWorkersOnStasis()
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

        // 1. Activate
        await bootstrap.Activate();

        var serviceProviderField = typeof(Engine.Bootstrap).GetField("_serviceProvider", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(serviceProviderField);
        var serviceProvider = (IServiceProvider)serviceProviderField.GetValue(bootstrap)!;
        var registry = serviceProvider.GetRequiredService<IDataIntentsRegistry>();

        // 2. Start a mock subscription transaction
        var mockContext = new MockElementContext { AbsolutePath = "/mock" };
        var cts = registry.Subscription(mockContext, "test-topic", "test-path", new MapPath("success"), new MapPath("failure"));
        Assert.NotNull(cts);
        
        // Complete transaction to dispatch the SubscriptionIntent to DataIntentsProcessor
        mockContext.Complete();

        // Give a tiny bit of time for processing loop to pick up the transaction
        await Task.Delay(100, TestContext.Current.CancellationToken);

        var modulesField = typeof(Engine.Bootstrap).GetField("_modules", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(modulesField);
        var modules = (List<IPriusModule>)modulesField.GetValue(bootstrap)!;
        var priusModule = Assert.Single(modules);
        
        var processorField = typeof(PriusModule).GetField("_processor", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(processorField);
        var processor = (DataIntentsProcessor)processorField.GetValue(priusModule)!;
        Assert.NotNull(processor);

        // Verify active subscriptions list has our subscription
        var activeSubsField = typeof(DataIntentsProcessor).GetField("_activeSubscriptions", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(activeSubsField);
        var activeSubs = (System.Collections.Concurrent.ConcurrentDictionary<string, (SubscriptionWorker<BlittableJsonReaderObject> Worker, Task ExecutionTask, CancellationTokenSource Cts)>)activeSubsField.GetValue(processor)!;
        
        Assert.Contains("test-topic", activeSubs.Keys);
        var subData = activeSubs["test-topic"];
        Assert.False(subData.Cts.IsCancellationRequested);

        // 3. Stasis (should call PriusModule.Stasis and StopAsync, canceling active subscription workers)
        await bootstrap.Stasis();

        // Verify that the subscription's own CTS is canceled, and it is removed from active subscriptions
        Assert.True(subData.Cts.IsCancellationRequested);
        Assert.Empty(activeSubs);
    }

    [Fact]
    public async Task Bootstrap_ShouldSupportReactivation()
    {
        var repo = new LocalMockPackageRepository();
        
        var manifest = JsonReaderMap.From("""
        {
            "Info": {
                "id": "Prius.Data.RavenDB.Tests",
                "version": "1.0.0"
            },
            "Dependencies": {},
            "Assets": {
                "lib": {
                    "net10.0": {
                        "Prius.Data.RavenDB.Tests.dll": {
                            "hash": "some-hash"
                        }
                    }
                }
            }
        }
        """);
        repo.AddPackage(manifest);

        var runtime = new LocalMockBootstrapRuntimeForTestModule();
        var metadataRegistry = new LocalMockMetadataRegistry();

        ConfigurableTestModule.ConfigureCalled = false;
        ConfigurableTestModule.ActivateCalled = false;
        ConfigurableTestModule.StasisCalled = false;
        ConfigurableTestModule.ShouldHangActivate = false;
        ConfigurableTestModule.ShouldHangStasis = false;

        var bootstrap = new Engine.Bootstrap(repo, runtime, metadataRegistry)
        {
            StartupTargets = JsonReaderMap.From("""
            {
                "Prius.Data.RavenDB.Tests": "1.0.0"
            }
            """)
        };

        var busField = typeof(Engine.Bootstrap).GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(busField);
        var bus = (IElementContext)busField.GetValue(bootstrap)!;
        bus.Put(new MapPath("Configuration/RavenDB/Database".AsSpan()), new MapValue("TestDb"));

        // 1. First Activation
        await bootstrap.Activate();
        Assert.True(ConfigurableTestModule.ActivateCalled);
        
        // Reset flags
        ConfigurableTestModule.ActivateCalled = false;
        ConfigurableTestModule.StasisCalled = false;

        // 2. Second Activation (should automatically trigger Stasis on the first instance)
        await bootstrap.Activate();
        
        Assert.True(ConfigurableTestModule.StasisCalled);
        Assert.True(ConfigurableTestModule.ActivateCalled);

        // 3. Final Stasis
        ConfigurableTestModule.StasisCalled = false;
        await bootstrap.Stasis();
        Assert.True(ConfigurableTestModule.StasisCalled);
    }

    [Fact]
    public async Task Bootstrap_ShouldDisposeRegisteredServicesOnStasis()
    {
        var repo = new LocalMockPackageRepository();
        
        var manifest = JsonReaderMap.From("""
        {
            "Info": {
                "id": "Prius.Data.RavenDB.Tests",
                "version": "1.0.0"
            },
            "Dependencies": {},
            "Assets": {
                "lib": {
                    "net10.0": {
                        "Prius.Data.RavenDB.Tests.dll": {
                            "hash": "some-hash"
                        }
                    }
                }
            }
        }
        """);
        repo.AddPackage(manifest);

        var runtime = new LocalMockBootstrapRuntimeForTestModule();
        var metadataRegistry = new LocalMockMetadataRegistry();

        var bootstrap = new Engine.Bootstrap(repo, runtime, metadataRegistry)
        {
            StartupTargets = JsonReaderMap.From("""
            {
                "Prius.Data.RavenDB.Tests": "1.0.0"
            }
            """)
        };

        var busField = typeof(Engine.Bootstrap).GetField("_bus", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(busField);
        var bus = (IElementContext)busField.GetValue(bootstrap)!;
        bus.Put(new MapPath("Configuration/RavenDB/Database".AsSpan()), new MapValue("TestDb"));

        DisposableTestService.Disposed = false;
        ConfigurableTestModule.ConfigureCalled = false;
        ConfigurableTestModule.ActivateCalled = false;
        ConfigurableTestModule.StasisCalled = false;
        ConfigurableTestModule.ShouldHangActivate = false;
        ConfigurableTestModule.ShouldHangStasis = false;

        await bootstrap.Activate();
        
        var serviceProviderField = typeof(Engine.Bootstrap).GetField("_serviceProvider", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(serviceProviderField);
        var serviceProvider = (IServiceProvider)serviceProviderField.GetValue(bootstrap)!;
        var service = serviceProvider.GetRequiredService<DisposableTestService>();
        Assert.NotNull(service);
        Assert.False(DisposableTestService.Disposed);

        await bootstrap.Stasis();
        
        Assert.True(DisposableTestService.Disposed);
    }
}

public sealed class ConfigurableTestModule : IPriusModule
{
    public static bool ConfigureCalled;
    public static bool ActivateCalled;
    public static bool StasisCalled;
    
    public static bool ShouldHangActivate;
    public static bool ShouldHangStasis;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ConfigureCalled = true;
        services.AddSingleton<DisposableTestService>();
    }

    public async ValueTask Activate(IServiceProvider serviceProvider, IConfiguration configuration, CancellationToken ct)
    {
        ActivateCalled = true;
        
        if (ShouldHangActivate)
            await Task.Delay(-1, ct);
    }

    public async ValueTask Stasis(CancellationToken ct)
    {
        StasisCalled = true;
        
        if (ShouldHangStasis)
            await Task.Delay(-1, ct);
    }
}

internal sealed class DisposableTestService : IDisposable
{
    public static bool Disposed;

    public void Dispose() => Disposed = true;
}
