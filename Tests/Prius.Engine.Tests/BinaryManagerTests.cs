using System.Text;
using Prius.Core.Maps;
using Prius.Engine;
using Xunit;

namespace Prius.Engine.Tests;

public class BinaryManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PriusTests_" + Guid.NewGuid());

    public BinaryManagerTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void ShouldStoreAndRetrieveBinaryData()
    {
        var manager = new BinaryManager(_tempDir);
        var content = Encoding.UTF8.GetBytes("hello world");
        var metadata = DictionaryMap.New.With("Type", "text").AsMapValue();
        
        using (var stream = new MemoryStream(content))
        {
            manager.Store("path/1", metadata, stream);
        }

        var accessor = manager.Get("path/1");
        Assert.True(accessor.Exists);
        Assert.Equal("text", accessor.Metadata.AsMap().Get("Type").AsString());

        using (var stream = accessor.OpenStream())
        using (var reader = new StreamReader(stream))
        {
            Assert.Equal("hello world", reader.ReadToEnd());
        }
    }

    [Fact]
    public void ShouldDeleteData()
    {
        var manager = new BinaryManager(_tempDir);
        manager.Store("path/1", Empty.Instance, new MemoryStream(Encoding.UTF8.GetBytes("data")));
        
        manager.Delete("path/1");
        
        Assert.False(manager.Get("path/1").Exists);
    }

    [Fact]
    public void ShouldSpillToDiskAfterTimeout()
    {
        var timeProvider = new MockTimeProvider();
        var manager = new BinaryManager(_tempDir, timeProvider, TimeSpan.FromMinutes(1));
        var content = Encoding.UTF8.GetBytes("persistent data");
        
        manager.Store("path/1", Empty.Instance, new MemoryStream(content));

        // Перемещаем время вперед
        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(2);
        
        // Принудительно вызываем Spiller
        manager.ForceSpill();

        var files = Directory.GetFiles(_tempDir);
        Assert.True(files.Length == 1, $"Expected 1 file, but found {files.Length}. Files: {string.Join(", ", files)}");
        
        var accessor = manager.Get("path/1");
        Assert.True(accessor.Exists);
    }
}
