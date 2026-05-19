namespace Prius.Engine;

using Abstractions;
using Core.Maps;

public sealed class EmptyReactor : IReactor
{
    public static EmptyReactor Instance { get; } = new();
    
    private EmptyReactor() { }

    public void Put(IReactorContext context, MapPath path, MapValue value) { }

    public MapValue Get(IReactorContext context, MapPath path) => Empty.Instance;
    
    public void Notify(IReactorContext context, MapPath path, MapValue value) { }
}
