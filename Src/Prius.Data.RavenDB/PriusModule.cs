using Prius.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Prius.Data.RavenDB;

public sealed class PriusModule : IPriusModule
{
    private Task? _processingTask;
    private CancellationTokenSource? _cts;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        if (services.All(r => r.ServiceType != typeof(IDocumentStoreHolder)))
        {
            var urls = configuration.GetSection("RavenDB:Urls").GetChildren().Select(c => c.Value).Where(v => v != null).Select(v => v!).ToArray();
            var database = configuration["RavenDB:Database"];
            
            if (urls.Length > 0 && !string.IsNullOrEmpty(database))
            {
                var certBase64 = configuration["RavenDB:CertBase64"];
                var certBytes = string.IsNullOrEmpty(certBase64) ? null : Convert.FromBase64String(certBase64);
                var certPass = configuration["RavenDB:CertPass"];

                services.AddSingleton<IDocumentStoreHolder>(sp => 
                    new DocumentStoreHolder(urls, database, certBytes, certPass, sp.GetRequiredService<ILogger<DocumentStoreHolder>>()));
            }
        }

        if (services.All(r => r.ServiceType != typeof(DataIntentsProcessor))) 
            services.AddSingleton<DataIntentsProcessor>();
    }

    public ValueTask Activate(IServiceProvider serviceProvider, IConfiguration configuration, CancellationToken ct)
    {
        var processor = serviceProvider.GetService<DataIntentsProcessor>();
        if (processor == null) 
            return ValueTask.CompletedTask;
        
        _cts = new CancellationTokenSource();
        _processingTask = processor.StartAsync(_cts.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask Stasis(CancellationToken ct)
    {
        if (_cts != null)
            await _cts.CancelAsync();

        if (_processingTask != null)
        {
            try
            {
                // Await completion with Bootstrap's CancellationToken timeout
                await _processingTask.WaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[SHUTDOWN] DataIntentsProcessor stasis waiting timed out or was cancelled.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[SHUTDOWN] DataIntentsProcessor encountered an error during stasis: {ex}");
            }
        }

        _cts?.Dispose();
    }
}
