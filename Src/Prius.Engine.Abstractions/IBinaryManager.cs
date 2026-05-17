namespace Prius.Engine.Abstractions;

using Core.Maps;

public interface IBinaryManager
{
    Task StoreAsync(string path, MapValue metadata, Stream stream);
    Task DeleteAsync(string path);
    BinaryAccessor Get(string path);
}

public interface BinaryAccessor
{
    MapValue Metadata { get; }
    bool Exists { get; }
    ValueTask<Stream> OpenStreamAsync();
}
