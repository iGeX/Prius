using Prius.Core.Maps;
using Prius.Engine.Nuspec;
using Xunit;

namespace Prius.Engine.Tests;

public sealed class NuspecMapperTests
{
    [Fact]
    public void Should_Parse_Full_Nuspec_To_Map()
    {
        const string Xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://microsoft.com">
              <metadata>
                <id>Prius.Core</id>
                <version>1.0.0-beta</version>
                <authors>iGeX</authors>
                <description>Core universe logic</description>
                <repository type="git" url="https://github.com" commit="abcdef" />
                <dependencies>
                  <group targetFramework="net8.0">
                    <dependency id="System.Text.Json" version="8.0.0" exclude="Build,Analyzers" />
                  </group>
                  <group targetFramework="netstandard2.1">
                    <dependency id="Newtonsoft.Json" version="13.0.3" privateAssets="All" />
                  </group>
                </dependencies>
              </metadata>
            </package>
            """;

        var map = NuspecMapper.ToMap(Xml);

        Assert.Equal("Prius.Core", map.Get("Info/id").AsValue<string>());
        Assert.Equal("1.0.0-beta", map.Get("Info/version").AsValue<string>());
        Assert.Equal("git", map.Get("Info/repository/type").AsValue<string>());
        Assert.Equal("abcdef", map.Get("Info/repository/commit").AsValue<string>());
        
        Assert.Equal("8.0.0", map.Get("Dependencies/net8.0/System.Text.Json/version").AsValue<string>());
        Assert.Equal("Build,Analyzers", map.Get("Dependencies/net8.0/System.Text.Json/exclude").AsValue<string>());
        
        Assert.Equal("13.0.3", map.Get("Dependencies/netstandard2.1/Newtonsoft.Json/version").AsValue<string>());
        Assert.Equal("All", map.Get("Dependencies/netstandard2.1/Newtonsoft.Json/privateAssets").AsValue<string>());
    }

    [Fact]
    public void Should_Handle_Old_Format_Without_Groups_As_Any()
    {
        const string Xml = """
            <?xml version="1.0"?>
            <package xmlns="http://microsoft.com">
              <metadata>
                <id>Old.Pkg</id>
                <version>1.0.0</version>
                <dependencies>
                  <dependency id="Lib.A" version="2.0.0" />
                </dependencies>
              </metadata>
            </package>
            """;

        var map = NuspecMapper.ToMap(Xml);

        Assert.Equal("2.0.0", map.Get("Dependencies/any/Lib.A/version").AsValue<string>());
    }
}
