namespace Prius.Data.RavenDB;

using Raven.Client.Documents;

public interface IDocumentStoreHolder
{
    IDocumentStore Store { get; }
}
