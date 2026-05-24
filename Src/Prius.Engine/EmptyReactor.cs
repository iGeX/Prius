namespace Prius.Engine;

using Abstractions;
using Core.Maps;

public sealed class EmptyReactor : IReactor
{
    public static EmptyReactor Instance { get; } = new();
    
    private EmptyReactor() { }

    public bool Put(IReactorContext context, MapPath path, MapValue value) => false;

    public MapValue Get(IReactorContext context, MapPath path) => Empty.Instance;
}
