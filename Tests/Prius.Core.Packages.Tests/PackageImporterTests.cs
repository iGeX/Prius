using System.IO.Compression;
using Prius.Core.Maps;
using Xunit;

namespace Prius.Core.Packages.Tests;

public sealed class PackageImporterTests
{
    [Fact]
    public void Should_Import_Package_With_Assets_And_Hashes()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            var nuspec = archive.CreateEntry("Test.Package.nuspec");
            using (var writer = new StreamWriter(nuspec.Open()))
            {
                writer.Write("""
                             <?xml version="1.0" encoding="utf-8"?>
                             <package xmlns="http://microsoft.com">
                               <metadata>
                                 <id>Test.Package</id>
                                 <version>1.2.3</version>
                                 <authors>GeX</authors>
                               </metadata>
                             </package>
                             """);
            }
            
            var dll = archive.CreateEntry("lib/net8.0/Test.dll");
            using (var writer = new StreamWriter(dll.Open()))
            {
                writer.Write("binary meat content");
            }
        }

        stream.Position = 0;
        var map = PackageImporter.Import(stream);
        
        Assert.Equal("Test.Package", map.DeepGet("Info/id").AsValue<string>());
        Assert.Equal("1.2.3", map.DeepGet("Info/version").AsValue<string>());
        
        var asset = map.DeepGet("Assets/lib/net8.0/Test.dll").AsMap();
        Assert.False(asset.IsEmpty);
        
        Assert.True(asset.Get("Size").AsValue<long>() > 0);
        Assert.Equal(64, asset.Get("Hash").AsValue<string>().Length);
    }
}
