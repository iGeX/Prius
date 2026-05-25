namespace Prius.Engine;

using Abstractions;
using Core.Maps;

public sealed class EmptyElement : IElement
{
    public static EmptyElement Instance { get; } = new();
    
    private EmptyElement() { }

    public bool Put(IElementContext context, MapPath path, MapValue value) => false;

    public MapValue Get(IElementContext context, MapPath path) => Empty.Instance;
}
