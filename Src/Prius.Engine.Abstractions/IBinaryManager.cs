namespace Prius.Engine.Abstractions;

using Core.Maps;

public interface IBinaryManager
{
    void Store(MapPath path, MapValue metadata, Stream stream);
    void Delete(MapPath path);
    IBinaryAccessor Get(MapPath path);
}

public interface IBinaryAccessor
{
    MapValue Metadata { get; }
    bool Exists { get; }
    Stream OpenStream();
}
