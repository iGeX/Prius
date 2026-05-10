namespace Prius.Engine;

using Abstractions;
using Core.Maps;

public sealed class EmptyReactor : IReactor
{
    public static EmptyReactor Instance { get; } = new();
    private EmptyReactor() { }

    public void Put(IReactorContext context, MapValue value) { }

    public MapValue Get(IReactorContext context) => Empty.Instance;
}
