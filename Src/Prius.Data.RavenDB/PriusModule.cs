using Prius.Engine.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Prius.Data.RavenDB;

public sealed class PriusModule : IPriusModule
{
    private DataIntentsProcessor? _processor;

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

    public async ValueTask Activate(IServiceProvider serviceProvider, IConfiguration configuration, CancellationToken ct)
    {
        _processor = serviceProvider.GetService<DataIntentsProcessor>();
        if (_processor != null)
            await _processor.StartAsync(ct);
    }

    public async ValueTask Stasis(CancellationToken ct)
    {
        if (_processor != null)
            await _processor.StopAsync(ct);
    }
}
