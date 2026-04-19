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
        info.Put("Id", metadata.Element(ns + "id")?.Value ?? string.Empty);
        info.Put("Version", metadata.Element(ns + "version")?.Value ?? string.Empty);
        info.Put("Authors", metadata.Element(ns + "authors")?.Value ?? string.Empty);
        info.Put("Description", metadata.Element(ns + "description")?.Value ?? string.Empty);
        info.Put("ProjectUrl", metadata.Element(ns + "projectUrl")?.Value ?? string.Empty);
        info.Put("Icon", metadata.Element(ns + "icon")?.Value ?? string.Empty);
        info.Put("Tags", metadata.Element(ns + "tags")?.Value ?? string.Empty);
        info.Put("License", metadata.Element(ns + "license")?.Value ?? string.Empty);
        
        var repo = metadata.Element(ns + "repository");
        if (repo != null)
        {
            var repoMap = DictionaryMap.New;
            repoMap.Put("Type", repo.Attribute("type")?.Value ?? string.Empty);
            repoMap.Put("Url", repo.Attribute("url")?.Value ?? string.Empty);
            repoMap.Put("Commit", repo.Attribute("commit")?.Value ?? string.Empty);
            info.Put("Repository", repoMap);
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
                {
                    var tfm = group.Attribute("targetFramework")?.Value ?? "any";
                    depsMap.Put(tfm, ParseDependencyGroup(group, ns));
                }
            }
            else
            {
                depsMap.Put("any", ParseDependencyGroup(dependenciesNode, ns));
            }
            rootMap.Put("Dependencies", depsMap);
        }

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
            depInfo.Put("Version", dep.Attribute("version")?.Value ?? "0.0.0");
            
            var include = dep.Attribute("include")?.Value;
            if (!string.IsNullOrEmpty(include)) depInfo.Put("Include", include);

            var exclude = dep.Attribute("exclude")?.Value;
            if (!string.IsNullOrEmpty(exclude)) depInfo.Put("Exclude", exclude);

            var privateAssets = dep.Attribute("privateAssets")?.Value;
            if (!string.IsNullOrEmpty(privateAssets)) depInfo.Put("PrivateAssets", privateAssets);

            groupMap.Put(id, depInfo);
        }
        return groupMap;
    }
}
