using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class ConfigurationElement(BusConfigurationProvider provider) : IElement
{
    public bool Put(IElementContext context, MapPath path, MapValue value)
    {
        context.Put(path, value);
        provider.NotifyConfigurationChanged();
        return true;
    }

    public MapValue Get(IElementContext context, MapPath path) => context.Get(path);
}
