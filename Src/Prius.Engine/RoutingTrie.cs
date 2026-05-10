namespace Prius.Engine;

using Core.Maps;
using Abstractions;

public sealed class RoutingTrie
{
    private readonly RoutingNode _root = new();

    /// <summary>
    /// Добавление правила маршрутизации при сборке вселенной (или в стазисе)
    /// </summary>
    public void AddRoute(string pathPattern, IReactor reactor)
    {
        if (string.IsNullOrWhiteSpace(pathPattern)) throw new ArgumentNullException(nameof(pathPattern));
        if (reactor == null) throw new ArgumentNullException(nameof(reactor));

        // Используем MapPath для разбора конфигурационного пути по сегментам
        MapPath path = pathPattern;
        var current = _root;

        while (!path.IsEmpty)
        {
            var segment = path.Head;
            path = path.Tail;

            if (segment == "**")
            {
                current.DeepWildcardReactor = reactor;
                return; // '**' всегда терминален для ветки, дальше идти нет смысла
            }
            
            if (segment == "*")
            {
                if (path.IsEmpty)
                {
                    current.WildcardReactor = reactor;
                    return;
                }
                // Если '*' в середине пути (например, "Orders/*/Settings"), 
                // нам нужен специальный узел-заглушка для обработки поддеревьев.
                segment = "@wildcard"; 
            }

            if (!current.Children.TryGetValue(segment, out var nextNode))
            {
                nextNode = new RoutingNode();
                current.Children.Add(segment, nextNode);
            }
            current = nextNode;
        }

        current.TerminalReactor = reactor;
    }

    /// <summary>
    /// Высокопроизводительный резолв пути за O(k) без аллокаций
    /// </summary>
    public IReactor Resolve(MapPath absolutePath)
    {
        var current = _root;
        IReactor? fallbackReactor = null;

        while (!absolutePath.IsEmpty)
        {
            // Если на пути встретился глубокий вайлдкард, он имеет наивысший приоритет для всех "детей"
            if (current.DeepWildcardReactor != null)
            {
                fallbackReactor = current.DeepWildcardReactor;
            }

            var segment = absolutePath.Head;
            absolutePath = absolutePath.Tail;

            // 1. Пытаемся идти по точному совпадению сегмента
            if (current.Children.TryGetValue(segment, out var nextNode))
            {
                current = nextNode;
                continue;
            }

            // 2. Если точного пути нет, проверяем одинарный вайлдкард в середине пути
            if (current.Children.TryGetValue("@wildcard", out var wildcardNode))
            {
                current = wildcardNode;
                continue;
            }

            // 3. Если и его нет, но у нас по пути был накоплен '**' — отдаем ему
            if (fallbackReactor != null) return fallbackReactor;

            // 4. Если путь оборвался на последнем сегменте, проверяем терминальный одинарный '*'
            if (absolutePath.IsEmpty && current.WildcardReactor != null)
            {
                return current.WildcardReactor;
            }

            // Ток некуда пустить — возвращаем пустой реактор-заглушку, система не должна падать
            return EmptyReactor.Instance; 
        }

        // Возвращаем точный реактор, либо накопленный '**', либо заглушку пустоты
        return current.TerminalReactor ?? fallbackReactor ?? EmptyReactor.Instance;
    }
}
