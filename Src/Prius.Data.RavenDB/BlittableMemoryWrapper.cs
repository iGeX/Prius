using System.Buffers;
using Sparrow.Json;

namespace Prius.Data.RavenDB;

internal unsafe class BlittableMemoryWrapper(BlittableJsonReaderObject blittableObj) : MemoryManager<byte>
{
    private readonly byte* _pointer = blittableObj.BasePointer;
    private readonly int _length = blittableObj.Size;

    public override Span<byte> GetSpan() => new(_pointer, _length);

    public override MemoryHandle Pin(int elementIndex = 0) => new(_pointer + elementIndex);

    public override void Unpin() { }

    protected override void Dispose(bool disposing) { }
}
