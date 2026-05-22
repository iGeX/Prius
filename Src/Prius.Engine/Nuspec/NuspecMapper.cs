using System.Xml.Linq;
using Prius.Core.Maps;

namespace Prius.Engine.Nuspec;

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
            if (name == "dependencies")
                continue;
            
            if (element.HasElements || element.HasAttributes)
            {
                info[name] = new MapValue(ParseComplexElement(element));
                continue;
            }
            
            info[name] = element.Value;
        }

        rootMap["Info"] = new MapValue(info);

        var dependenciesNode = metadata.Element(ns + "dependencies");
        if (dependenciesNode == null)
            return rootMap;
        
        var depsMap = DictionaryMap.New;
        var groups = dependenciesNode.Elements(ns + "group").ToList();

        if (groups.Count > 0)
        {
            foreach (var group in groups)
                depsMap[group.Attribute("targetFramework")?.Value ?? "any"] = new MapValue(ParseDependencyGroup(group, ns));
        }
        else
            depsMap["any"] = new MapValue(ParseDependencyGroup(dependenciesNode, ns));

        rootMap["Dependencies"] = new MapValue(depsMap);

        return rootMap;
    }

    private static DictionaryMap ParseComplexElement(XElement element)
    {
        var map = DictionaryMap.New;

        foreach (var attr in element.Attributes())
            map[attr.Name.LocalName] = attr.Value;

        foreach (var child in element.Elements())
        {
            var mapValue = child.HasElements || child.HasAttributes ? new MapValue(ParseComplexElement(child)) : (MapValue) child.Value;
            map[child.Name.LocalName] = mapValue;
        }
        
        if (!element.HasElements && !string.IsNullOrWhiteSpace(element.Value))
            map["value"] = element.Value;

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
                depInfo[attr.Name.LocalName] = attr.Value;
            }

            groupMap[id] = new MapValue(depInfo);
        }
        return groupMap;
    }
}
