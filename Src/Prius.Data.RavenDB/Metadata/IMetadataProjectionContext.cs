using Prius.Core.Maps;

namespace Prius.Data.RavenDB.Metadata;

public interface IMetadataProjectionContext
{
    void StoreDocument(string documentId, IMap documentData);

    void DeleteDocument(string documentId);

    void StoreAttachment(string documentId, string attachmentName, Stream stream);

    void DeleteAttachment(string documentId, string attachmentName);
}
