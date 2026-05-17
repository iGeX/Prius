using Raven.Client.Documents;
using Raven.TestDriver;
using Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using Prius.Data.RavenDB;
using Prius.Engine.Abstractions;
using System.Threading.Channels;

namespace Prius.Data.RavenDB.Tests;

public abstract class AbstractDataIntentsProcessorTests : RavenTestDriver
{
    static AbstractDataIntentsProcessorTests() => 
        ConfigureServer(new TestServerOptions { Licensing = { ThrowOnInvalidOrMissingLicense = false } });

    protected class TestDocumentStoreHolder(IDocumentStore store) : IDocumentStoreHolder
    {
        public IDocumentStore Store => store;
    }

    protected static async Task WaitForCompletion(MockDataIntentsProvider provider, CancellationToken ct)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (provider.PendingCount > 0 && DateTime.UtcNow < timeout)
            await Task.Delay(100, ct);

        if (provider.PendingCount == 0)
            await Task.Delay(500, ct);
    }

    protected static async Task ExecuteTest(IDocumentStore store, MockDataIntentsProvider provider, Func<Task> assertion)
    {
        using var processorCt = new CancellationTokenSource();
        using var assertCt = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var holder = new TestDocumentStoreHolder(store);
        var processor = new DataIntentsProcessor(holder, provider, NullLogger<DataIntentsProcessor>.Instance);
        
        var task = processor.StartAsync(processorCt.Token);
        await WaitForCompletion(provider, assertCt.Token);
        
        await processorCt.CancelAsync();
        try { await task; } catch (OperationCanceledException) { }

        await assertion();
    }
}
