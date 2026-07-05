using Raven.Client.Documents;
using Raven.TestDriver;
using Microsoft.Extensions.Logging.Abstractions;
using Prius.Engine;

namespace Prius.Data.RavenDB.Tests;

public abstract class AbstractDataIntentsProcessorTests : RavenTestDriver
{
    static AbstractDataIntentsProcessorTests()
    {
        try
        {
            ConfigureServer(new TestServerOptions { Licensing = { ThrowOnInvalidOrMissingLicense = false } });
        }
        catch (InvalidOperationException)
        {
        }
    }

    private class TestDocumentStoreHolder(IDocumentStore store) : IDocumentStoreHolder
    {
        public IDocumentStore Store => store;
    }

    protected static async Task WaitForCompletion(MockDataIntentsProvider provider, CancellationToken ct)
    {
        var timeout = DateTime.UtcNow.AddSeconds(15);
        while (DateTime.UtcNow < timeout)
        {
            var intents = provider.AllIntents;
            if (intents.Count > 0)
            {
                var allDone = true;
                foreach (var intent in intents)
                {
                    if (intent.Context is not MockElementContext context) 
                        continue;
                    
                    var hasSuccess = context.PutCalls.ContainsKey(intent.SuccessPath);
                    var hasFailure = context.PutCalls.ContainsKey(intent.FailurePath);
                    if (hasSuccess || hasFailure) 
                        continue;
                        
                    allDone = false;
                    break;
                }
                if (allDone)
                {
                    await Task.Delay(50, ct);
                    return;
                }
            }
            await Task.Delay(50, ct);
        }
        throw new TimeoutException("Timed out waiting for intents to be processed.");
    }

    protected static async Task ExecuteTest(IDocumentStore store, MockDataIntentsProvider provider, Func<Task> assertion) =>
        await ExecuteTest(store, provider, new BinaryManager(), assertion);
    
    protected static async Task ExecuteTest(IDocumentStore store, MockDataIntentsProvider provider, BinaryManager binaryManager, Func<Task> assertion)
    {
        using var assertCt = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var holder = new TestDocumentStoreHolder(store);
        var processor = new DataIntentsProcessor(holder, provider, binaryManager, NullLogger<DataIntentsProcessor>.Instance);
        
        await processor.StartAsync(CancellationToken.None);
        await WaitForCompletion(provider, assertCt.Token);
        
        await processor.StopAsync(CancellationToken.None);

        await assertion();
    }
}
