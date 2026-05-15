namespace Prius.Data.RavenDB;

using System;
using System.Security.Cryptography.X509Certificates;
using Raven.Client.Documents;

public class DocumentStoreHolder : IDisposable
{
    private readonly object _lock = new();
    private IDocumentStore _store;

    public IDocumentStore Store 
    { 
        get { lock(_lock) return _store; } 
    }

    public DocumentStoreHolder(string[] urls, string database, byte[]? certBytes, string? certPass)
    {
        _store = CreateStore(urls, database, certBytes, certPass);
    }

    public void UpdateCredentials(string[] urls, string database, byte[]? certBytes, string? certPass)
    {
        lock (_lock)
        {
            var oldStore = _store;
            _store = CreateStore(urls, database, certBytes, certPass);
            oldStore?.Dispose();
        }
    }

    private static IDocumentStore CreateStore(string[] urls, string database, byte[]? certBytes, string? certPass)
    {
        var store = new DocumentStore { Urls = urls, Database = database };
        if (certBytes != null)
            store.Certificate = new X509Certificate2(certBytes, certPass);
        return store.Initialize();
    }

    public void Dispose() => _store?.Dispose();
}
