using System.Xml.Linq;
using Prius.Core.Maps;

namespace Prius.Core.Packages;

public static class NuspecMapper
{
    public static DictionaryMap ToMap(string xml)
    {
        var doc = XDocument.Parse(xml);
        var ns = doc.Root?.GetDefaultNamespace() ?? XNamespace.None;
        var metadata = doc.Root?.Element(ns + "metadata");
        var rootMap = DictionaryMap.New;

        if (metadata == null)
            return rootMap;

        var info = DictionaryMap.New;
        foreach (var element in metadata.Elements())
        {
            var name = element.Name.LocalName;

            //Dependencies обрабатываем отдельно по своей структуре
            if (name == "dependencies")
                continue;

            // Если у элемента есть вложенные элементы или атрибуты — делаем из него мапу
            if (element.HasElements || element.HasAttributes)
            {
                info.Put(name, ParseComplexElement(element));
                continue;
            }

            // Иначе просто кладем значение
            info.Put(name, element.Value);
        }

        rootMap.Put("Info", info);

        var dependenciesNode = metadata.Element(ns + "dependencies");
        if (dependenciesNode != null)
        {
            var depsMap = DictionaryMap.New;
            var groups = dependenciesNode.Elements(ns + "group").ToList();

            if (groups.Count > 0)
            {
                foreach (var group in groups)
                    depsMap.Put(group.Attribute("targetFramework")?.Value ?? "any", ParseDependencyGroup(group, ns));
            }
            else
                depsMap.Put("any", ParseDependencyGroup(dependenciesNode, ns));

            rootMap.Put("Dependencies", depsMap);
        }

        return rootMap;
    }

    private static DictionaryMap ParseComplexElement(XElement element)
    {
        var map = DictionaryMap.New;

        foreach (var attr in element.Attributes())
            map.Put(attr.Name.LocalName, attr.Value);

        foreach (var child in element.Elements())
        {
            var mapValue = child.HasElements || child.HasAttributes ? new MapValue(ParseComplexElement(child)) : (MapValue) child.Value;
            map.Put(child.Name.LocalName, mapValue);
        }

        // Если элементов нет, но был текст (например <tag attr="1">text</tag>)
        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
            map.Put("value", element.Value);

        return map;
    }

    private static DictionaryMap ParseDependencyGroup(XElement container, XNamespace ns)
    {
        var groupMap = DictionaryMap.New;
        foreach (var dep in container.Elements(ns + "dependency"))
        {
            var id = dep.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id))
                continue;

            var depInfo = DictionaryMap.New;
            foreach (var attr in dep.Attributes())
            {
                if (attr.Name.LocalName == "id")
                    continue;
                depInfo.Put(attr.Name.LocalName, attr.Value);
            }

            groupMap.Put(id, depInfo);
        }
        return groupMap;
    }
}
