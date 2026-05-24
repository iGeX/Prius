using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class ConfigurationReactor(BusConfigurationProvider provider) : IReactor
{
    public bool Put(IReactorContext context, MapPath path, MapValue value)
    {
        // Прямой доступ к данным шины через Put контекста (который упадет в Backing Map)
        // Но так как мы возвращаем true, шина сама в Backing Map не запишет.
        // Чтобы записать в Backing Map, мы должны вызвать context.Put("/", value) 
        // Но так как ConfigurationReactor это просто обертка над VirtualBus для ConfigurationProvider,
        // возможно он должен просто транслировать вниз и после этого уведомлять провайдера.
        // Так как мы хотим записать в память, мы вызываем context.Put("/", value)
        
        context.Put("/", value);
        provider.NotifyConfigurationChanged();
        return true;
    }

    public MapValue Get(IReactorContext context, MapPath path) => context.Get("/");
}
