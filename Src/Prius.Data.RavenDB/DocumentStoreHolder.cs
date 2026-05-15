namespace Prius.Data.RavenDB;

using System;
using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents;

public class DocumentStoreHolder(string[] urls, string database, byte[]? certBytes, string? certPass) : IDisposable
{
    private readonly Lock _sync = new();
    private IDocumentStore _store = CreateStore(urls, database, certBytes, certPass);

    public IDocumentStore Store 
    { 
        get { lock(_sync) return _store; } 
    }

    public void UpdateCredentials(string[] urls, string database, byte[]? certBytes, string? certPass)
    {
        IDocumentStore newStore;
        try
        {
            newStore = CreateStore(urls, database, certBytes, certPass);
        }
        catch (Exception)
        {
            return;
        }

        lock (_sync)
        {
            var oldStore = _store;
            _store = newStore;
            oldStore?.Dispose();
        }
    }

    private static IDocumentStore CreateStore(string[] urls, string database, byte[]? certBytes, string? certPass)
    {
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
