using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class ConfigurationReactor(BusConfigurationProvider provider) : IReactor
{
    public void Put(IReactorContext context, MapPath path, MapValue value)
    {
        // Прямой доступ к данным шины через Put контекста
    }

    public MapValue Get(IReactorContext context, MapPath path) => Empty.Instance;

    public void Notify(IReactorContext context, MapPath path, MapValue value)
    {
        provider.NotifyConfigurationChanged();
    }
}
