namespace Prius.Engine.Abstractions;

using Core.Maps;

public interface IBinaryManager
{
    void Store(string path, MapValue metadata, Stream stream);
    void Delete(string path);
    IBinaryAccessor Get(string path);
}

public interface IBinaryAccessor
{
    MapValue Metadata { get; }
    bool Exists { get; }
    Stream OpenStream();
}
