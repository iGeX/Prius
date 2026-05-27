using Microsoft.Extensions.Configuration;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class BusConfigurationProvider(IElementContext bus, string basePath = "/Configuration") : ConfigurationProvider
{
    private readonly IElementContext _bus = bus ?? throw new ArgumentNullException(nameof(bus));

    public void NotifyConfigurationChanged() => OnReload();

    public override bool TryGet(string key, out string? value)
    {
        var path = $"{basePath}/{key.Replace(':', '/')}";
        var mapValue = _bus.Get(path);

        if (mapValue.IsEmpty)
        {
            value = null;
            return false;
        }

        value = mapValue.ToString();
        return true;
    }

    public override IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        var path = string.IsNullOrEmpty(parentPath) 
            ? basePath 
            : $"{basePath}/{parentPath.Replace(':', '/')}";

        var map = _bus.Get(path).AsMap();
        var keys = map.Keys().ToList();

        return keys.Concat(earlierKeys).OrderBy(k => k, ConfigurationKeyComparer.Instance);
    }
}

public sealed class BusConfigurationSource(IElementContext bus, string basePath = "/Configuration") : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => 
        new BusConfigurationProvider(bus, basePath);
}
