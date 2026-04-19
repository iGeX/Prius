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
        if (metadata == null) return rootMap;

        var info = DictionaryMap.New;
        foreach (var element in metadata.Elements())
        {
            if (element.Name.LocalName == "dependencies" || element.Name.LocalName == "repository") 
                continue;
            
            info.Put(element.Name.LocalName, element.Value);
        }

        var repo = metadata.Element(ns + "repository");
        if (repo != null)
        {
            var repoMap = DictionaryMap.New;
            foreach (var attr in repo.Attributes())
                repoMap.Put(attr.Name.LocalName, attr.Value);
            
            info.Put("repository", repoMap);
        }
        rootMap.Put("Info", info);

        var dependenciesNode = metadata.Element(ns + "dependencies");
        if (dependenciesNode == null) 
            return rootMap;
        
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

        return rootMap;
    }

    private static DictionaryMap ParseDependencyGroup(XElement container, XNamespace ns)
    {
        var groupMap = DictionaryMap.New;
        foreach (var dep in container.Elements(ns + "dependency"))
        {
            var id = dep.Attribute("id")?.Value;
            if (string.IsNullOrEmpty(id)) continue;

            var depInfo = DictionaryMap.New;
            foreach (var attr in dep.Attributes())
            {
                if (attr.Name.LocalName == "id") continue;
                depInfo.Put(attr.Name.LocalName, attr.Value);
            }

            groupMap.Put(id, depInfo);
        }
        return groupMap;
    }
}
