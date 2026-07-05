using Prius.Core.Maps;

namespace Prius.Data.RavenDB.Metadata;

public interface IMetadataHandler
{
    ValueTask ProjectAsync(
        IMetadataProjectionContext context, 
        IMap oldSnapshot, 
        IMap newSnapshot, 
        CancellationToken ct);
}
