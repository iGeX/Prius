namespace Prius.Data.RavenDB;

using System;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Raven.Client.Documents;

public class DocumentStoreHolder : IDisposable
{
    private readonly Lock _sync = new();
    private readonly ILogger<DocumentStoreHolder> _logger;
    private IDocumentStore _store;

    public DocumentStoreHolder(
        string[] urls, 
        string database, 
        byte[]? certBytes, 
        string? certPass, 
        ILogger<DocumentStoreHolder> logger)
    {
        _logger = logger;
        _store = CreateStore(urls, database, certBytes, certPass);
    }

    public IDocumentStore Store 
    { 
        get 
        { 
            lock (_sync) 
                return _store; 
        } 
    }

    public void UpdateCredentials(string[] urls, string database, byte[]? certBytes, string? certPass)
    {
        IDocumentStore newStore;
        try
        {
            newStore = CreateStore(urls, database, certBytes, certPass);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update RavenDB credentials");
            return;
        }

        lock (_sync)
        {
            var oldStore = _store;
            _store = newStore;
            _logger.LogInformation("Document store updated successfully");
            oldStore?.Dispose();
        }
    }

    private IDocumentStore CreateStore(string[] urls, string database, byte[]? certBytes, string? certPass)
    {
        _logger.LogInformation("Initializing DocumentStore for {Database} at {Urls}", database, string.Join(", ", urls));
        var store = new DocumentStore { Urls = urls, Database = database };
        
        if (certBytes != null)
        {
            store.Certificate = X509CertificateLoader.LoadPkcs12(
                certBytes, 
                certPass, 
                X509KeyStorageFlags.EphemeralKeySet
            );
        }

        return store.Initialize();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _store?.Dispose();
        }
    }
}
