using Xunit;
using Prius.Engine.Abstractions;
using Prius.Core.Maps;
using Newtonsoft.Json.Linq;
using System.Reflection;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Prius.Engine;
// ReSharper disable AccessToDisposedClosure

namespace Prius.Data.RavenDB.Tests;

public class RetryPolicyTests : AbstractDataIntentsProcessorTests
{
    public class CustomProxy<T> : DispatchProxy where T : class
    {
        private T _target = null!;
        private Func<MethodInfo, object?[]?, object?>? _interceptor;

        public static T Create(T target, Func<MethodInfo, object?[]?, object?>? interceptor)
        {
            var proxy = Create<T, CustomProxy<T>>();
            var customProxy = (CustomProxy<T>)(object)proxy;
            customProxy._target = target;
            customProxy._interceptor = interceptor;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod == null) 
                return null;
                
            if (_interceptor != null)
            {
                var result = _interceptor(targetMethod, args);
                if (result != null) 
                    return result;
            }
            
            return targetMethod.Invoke(_target, args);
        }
    }

    [Fact]
    public async Task ShouldRetryOnTransientFailureAndSucceed()
    {
        using var realStore = GetDocumentStore();
        var saveChangesCalls = 0;

        var sessionProxyInterceptor = new Func<MethodInfo, object?[]?, object?>((method, _) =>
        {
            if (method.Name != nameof(IAsyncDocumentSession.SaveChangesAsync)) 
                return null; // forward to real session
            
            saveChangesCalls++;
            return saveChangesCalls < 3 ?
                throw new TimeoutException("Transient timeout error") : null; // forward to real session
        });

        var storeProxyInterceptor = new Func<MethodInfo, object?[]?, object?>((method, args) =>
        {
            if (method.Name != nameof(IDocumentStore.OpenAsyncSession)) 
                return null; // forward to real store
            var realSession = (IAsyncDocumentSession)method.Invoke(obj: realStore, args)!;
            return CustomProxy<IAsyncDocumentSession>.Create(realSession, sessionProxyInterceptor);
        });

        var store = CustomProxy<IDocumentStore>.Create(realStore, storeProxyInterceptor);

        var context = new MockElementContext();
        var doc = DictionaryMap.New
            .With("@metadata", DictionaryMap.New
                .With("@id", "users/1")
                .With("@collection", "Users"))
            .With("Name", "Alice");

        var provider = new MockDataIntentsProvider();
        provider.AddTransaction(context, [
            new StoreIntent(context, doc, "success/1", "failures/1", TestContext.Current.CancellationToken)
        ]);

        await ExecuteTest(store, provider, async () =>
        {
            Assert.True(context.PutCalls.ContainsKey("success/1"));
            Assert.False(context.PutCalls.ContainsKey("failures/1"));
            Assert.Equal(3, saveChangesCalls);

            using var session = realStore.OpenAsyncSession();
            var u1 = await session.LoadAsync<JObject>("users/1", TestContext.Current.CancellationToken);
            Assert.NotNull(u1);
            Assert.Equal("Alice", u1["Name"]?.ToString());
        });
    }

    [Fact]
    public async Task ShouldAbortImmediatelyOnFatalFailure()
    {
        using var realStore = GetDocumentStore();
        var saveChangesCalls = 0;

        var sessionProxyInterceptor = new Func<MethodInfo, object?[]?, object?>((method, _) =>
        {
            if (method.Name == nameof(IAsyncDocumentSession.SaveChangesAsync))
            {
                saveChangesCalls++;
                throw new InvalidOperationException("Fatal database error");
            }
            return null;
        });

        var storeProxyInterceptor = new Func<MethodInfo, object?[]?, object?>((method, args) =>
        {
            if (method.Name == nameof(IDocumentStore.OpenAsyncSession))
            {
                var realSession = (IAsyncDocumentSession)method.Invoke(realStore, args)!;
                return CustomProxy<IAsyncDocumentSession>.Create(realSession, sessionProxyInterceptor);
            }
            return null;
        });

        var store = CustomProxy<IDocumentStore>.Create(realStore, storeProxyInterceptor);

        var context = new MockElementContext();
        var doc = DictionaryMap.New
            .With("@metadata", DictionaryMap.New
                .With("@id", "users/1")
                .With("@collection", "Users"))
            .With("Name", "Alice");

        var provider = new MockDataIntentsProvider();
        provider.AddTransaction(context, [
            new StoreIntent(context, doc, "success/1", "failures/1", TestContext.Current.CancellationToken)
        ]);

        await ExecuteTest(store, provider, async () =>
        {
            Assert.False(context.PutCalls.ContainsKey("success/1"));
            Assert.True(context.PutCalls.ContainsKey("failures/1"));
            Assert.Equal(1, saveChangesCalls);

            using var session = realStore.OpenAsyncSession();
            var u1 = await session.LoadAsync<JObject>("users/1", TestContext.Current.CancellationToken);
            Assert.Null(u1);
        });
    }

    [Fact]
    public async Task ShouldFailAfterMaxRetriesOnTransientFailure()
    {
        using var realStore = GetDocumentStore();
        var saveChangesCalls = 0;

        var sessionProxyInterceptor = new Func<MethodInfo, object?[]?, object?>((method, _) =>
        {
            if (method.Name != nameof(IAsyncDocumentSession.SaveChangesAsync)) 
                return null;
            
            saveChangesCalls++;
            throw new TimeoutException("Persistent transient error");
        });

        var storeProxyInterceptor = new Func<MethodInfo, object?[]?, object?>((method, args) =>
        {
            if (method.Name == nameof(IDocumentStore.OpenAsyncSession))
            {
                var realSession = (IAsyncDocumentSession)method.Invoke(realStore, args)!;
                return CustomProxy<IAsyncDocumentSession>.Create(realSession, sessionProxyInterceptor);
            }
            return null;
        });

        var store = CustomProxy<IDocumentStore>.Create(realStore, storeProxyInterceptor);

        var context = new MockElementContext();
        var doc = DictionaryMap.New
            .With("@metadata", DictionaryMap.New
                .With("@id", "users/1")
                .With("@collection", "Users"))
            .With("Name", "Alice");

        var provider = new MockDataIntentsProvider();
        provider.AddTransaction(context, [
            new StoreIntent(context, doc, "success/1", "failures/1", TestContext.Current.CancellationToken)
        ]);

        await ExecuteTest(store, provider, async () =>
        {
            Assert.False(context.PutCalls.ContainsKey("success/1"));
            Assert.True(context.PutCalls.ContainsKey("failures/1"));
            Assert.Equal(3, saveChangesCalls);

            using var session = realStore.OpenAsyncSession();
            var u1 = await session.LoadAsync<JObject>("users/1", TestContext.Current.CancellationToken);
            Assert.Null(u1);
        });
    }

    private class LocalTestDocumentStoreHolder(IDocumentStore store) : IDocumentStoreHolder
    {
        public IDocumentStore Store => store;
    }

    [Fact]
    public async Task ShouldStartAndStopProcessorInModuleLifecycle()
    {
        using var store = GetDocumentStore();
        var services = new ServiceCollection();
        
        services.AddSingleton<IDocumentStoreHolder>(new LocalTestDocumentStoreHolder(store));
        services.AddSingleton<IDataIntentsProvider>(new MockDataIntentsProvider());
        services.AddSingleton<IBinaryManager>(new BinaryManager());
        services.AddLogging();
        services.AddSingleton<Microsoft.Extensions.Logging.ILogger<DataIntentsProcessor>>(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<DataIntentsProcessor>.Instance);

        var configuration = new ConfigurationBuilder().Build();
        var module = new PriusModule();
        
        module.ConfigureServices(services, configuration);
        
        var serviceProvider = services.BuildServiceProvider();
        
        using var activateCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await module.Activate(serviceProvider, configuration, activateCts.Token);

        using var stasisCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await module.Stasis(stasisCts.Token);
        
        Assert.True(true);
    }
}
