using Microsoft.Extensions.Configuration;
using Prius.Core.Maps;
using Prius.Engine.Abstractions;

namespace Prius.Engine;

public sealed class BusConfigurationProvider : ConfigurationProvider
{
    private readonly VirtualBus _bus;
    private readonly string _basePath;

    public BusConfigurationProvider(VirtualBus bus, string basePath = "/Configuration")
    {
        _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        _basePath = basePath;
    }

    public void NotifyConfigurationChanged()
    {
        OnReload();
    }

    public override bool TryGet(string key, out string? value)
    {
        var path = $"{_basePath}/{key.Replace(':', '/')}";
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
            ? _basePath 
            : $"{_basePath}/{parentPath.Replace(':', '/')}";

        var map = _bus.Get(path).AsMap();
        var keys = map.Keys().ToList();

        return keys.Concat(earlierKeys).OrderBy(k => k, ConfigurationKeyComparer.Instance);
    }
}

public sealed class BusConfigurationSource(VirtualBus bus, string basePath = "/Configuration") : IConfigurationSource
{
    public IConfigurationProvider Build(IConfigurationBuilder builder) => 
        new BusConfigurationProvider(bus, basePath);
}
