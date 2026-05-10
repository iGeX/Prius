using Raven.Client.Documents.BulkInsert;
using Raven.Client.Documents.Session;

namespace Prius.Engine.Abstractions;

public interface IRavenNativeContext
{
    IAsyncDocumentSession OpenLongSession(bool noTracking = true);
    
    BulkInsertOperation BulkInsert(string? database = null);
}
