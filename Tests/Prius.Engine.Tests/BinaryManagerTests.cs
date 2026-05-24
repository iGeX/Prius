using System.Text;
using Prius.Core.Maps;
using Xunit;

namespace Prius.Engine.Tests;

public sealed class BinaryManagerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "PriusTests_" + Guid.NewGuid());

    public BinaryManagerTests() => Directory.CreateDirectory(_tempDir);

    public void Dispose()
    {
        if (!Directory.Exists(_tempDir)) 
            return;
        
        try
        {
            Directory.Delete(_tempDir, true);
        }
        catch
        {
            //ignored
        }
    }

    [Fact]
    public void ShouldStoreAndRetrieveBinaryData()
    {
        var manager = new BinaryManager(_tempDir);
        var content = "hello world"u8.ToArray();
        var metadata = DictionaryMap.New.With("Type", "text").AsMapValue();
        
        using (var stream = new MemoryStream(content)) manager.Store("path/1", metadata, stream);

        var accessor = manager.Get("path/1");
        Assert.True(accessor.Exists);
        Assert.Equal("text", accessor.Metadata["Type"].AsString());

        using (var stream = accessor.OpenStream())
        using (var reader = new StreamReader(stream))
            Assert.Equal("hello world", reader.ReadToEnd());
    }

    [Fact(Timeout = 5000)]
    public void Should_Deadlock_Or_Fail_On_Delete_Due_To_Lock_Recursion()
    {
        var manager = new BinaryManager(_tempDir);
        manager.Store("path/deadlock", Empty.Instance, new MemoryStream("data"u8.ToArray()));

        var exception = Record.Exception(() => manager.Delete("path/deadlock"));
        Assert.Null(exception); 
    }
    
    [Fact]
    public async Task Should_Lose_New_User_Data_Due_To_Spiller_Loop_Race_Condition()
    {
        var timeProvider = new MockTimeProvider();
        var manager = new BinaryManager(_tempDir, timeProvider, TimeSpan.FromMinutes(10));
        
        manager.Store("path/race", Empty.Instance, new MemoryStream("OLD DATA"u8.ToArray()));
        
        var accessor = manager.Get("path/race");
        manager.ForceSpill(); 

        manager.Store("path/race", Empty.Instance, new MemoryStream("NEW FRESH USER DATA"u8.ToArray()));

        await using var stream = accessor.OpenStream();
        using var reader = new StreamReader(stream);
        var actualData = await reader.ReadToEndAsync(TestContext.Current.CancellationToken);

        Assert.Equal("NEW FRESH USER DATA", actualData);
    }
    
    [Fact]
    public void Should_Leave_Orphaned_Files_On_Disk_Due_To_OpenStream_Race()
    {
        var manager = new BinaryManager(_tempDir);
        manager.Store("path/orphan", Empty.Instance, new MemoryStream("some binary data"u8.ToArray()));
        
        manager.ForceSpill();
        var accessor = manager.Get("path/orphan");
        
        using (var stream = accessor.OpenStream()) stream.ReadByte();

        var filesOnDisk = Directory.GetFiles(_tempDir);
        
        Assert.Empty(filesOnDisk);
    }

    [Fact]
    public void ShouldSpillToDiskAfterTimeout()
    {
        var timeProvider = new MockTimeProvider();
        var manager = new BinaryManager(_tempDir, timeProvider, TimeSpan.FromMinutes(1));
        var content = "persistent data"u8.ToArray();
        
        manager.Store("path/1", Empty.Instance, new MemoryStream(content));

        timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(2);
        manager.ForceSpill();

        var files = Directory.GetFiles(_tempDir);
        Assert.True(files.Length == 1, $"Expected 1 file, but found {files.Length}. Files: {string.Join(", ", files)}");
        
        var accessor = manager.Get("path/1");
        Assert.True(accessor.Exists);
    }
    
    [Fact(Timeout = 10000)]
    public void Should_Concurrently_Deadlock_On_Delete()
    {
        var manager = new BinaryManager(_tempDir);
        const int TotalPaths = 100;
        
        for (var i = 0; i < TotalPaths; i++) 
            manager.Store($"stress/path/{i}", Empty.Instance, new MemoryStream(new byte[1024]));
        
        var exception = Record.Exception(() =>
        {
            Parallel.For(0, TotalPaths, new ParallelOptions { MaxDegreeOfParallelism = -1 }, i =>
            {
                manager.Delete($"stress/path/{i}");
                manager.Store($"stress/path/{i}", Empty.Instance, new MemoryStream(new byte[10]));
            });
        });

        Assert.Null(exception);
    }
    
    [Fact]
    public async Task Should_Concurrently_Lose_Data_Under_Spiller_Race()
    {
        var timeProvider = new MockTimeProvider();
        var manager = new BinaryManager(_tempDir, timeProvider, TimeSpan.FromMilliseconds(10));
        
        const string Path = "concurrent/race/path";
        manager.Store(Path, Empty.Instance, new MemoryStream(Encoding.UTF8.GetBytes("INITIAL")));

        var accessor = manager.Get(Path);
        var tasks = new List<Task>();

        for (var i = 0; i < 500; i++)
        {
            var iteration = i;
            tasks.Add(Task.Run(async () =>
            {
                timeProvider.UtcNow = timeProvider.UtcNow.AddMinutes(5); // Вынуждаем SpillerLoop постоянно триггериться
                
                var expectedString = $"FRESH_DATA_{iteration}";
                manager.Store(Path, Empty.Instance, new MemoryStream(Encoding.UTF8.GetBytes(expectedString)));

                await Task.Delay(1);

                await using var stream = accessor.OpenStream();
                using var reader = new StreamReader(stream);
                var actualData = await reader.ReadToEndAsync();

                Assert.StartsWith("FRESH_DATA_", actualData);
            }, TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task Should_Fail_Or_Leak_Files_Under_Concurrent_OpenStream()
    {
        var manager = new BinaryManager(_tempDir);
        const string Path = "concurrent/orphan/path";
        
        manager.Store(Path, Empty.Instance, new MemoryStream(Encoding.UTF8.GetBytes("THREAD_SAFE_TEST")));
        manager.ForceSpill();

        var accessor = manager.Get(Path);
        var tasks = new List<Task>();

        for (var i = 0; i < 200; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    using var stream = accessor.OpenStream();
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    Assert.Equal("THREAD_SAFE_TEST", text);
                }
                catch (InvalidOperationException)
                {
                    //ignored
                }
            }, TestContext.Current.CancellationToken));
        }

        await Task.WhenAll(tasks);

        var filesOnDisk = Directory.GetFiles(_tempDir);
        Assert.Empty(filesOnDisk);
    }
    
    [Fact]
    public void Should_Respect_Max_Memory_Limit_Under_High_Volume_Store()
    {
        const long MaxMemoryLimit = 5 * 1024 * 1024;
        var manager = new BinaryManager(_tempDir, maxMemory: MaxMemoryLimit);

        var heavyData = new byte[2 * 1024 * 1024];
        new Random().NextBytes(heavyData);

        Parallel.For(0, 20, new ParallelOptions { MaxDegreeOfParallelism = -1 }, i =>
        {
            manager.Store($"heavy/path/{i}", Empty.Instance, new MemoryStream(heavyData));
        });

        var nodesInMemory = 0;
        
        var field = typeof(BinaryManager).GetField("_nodes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var nodes = (System.Collections.IDictionary)field!.GetValue(manager)!;

        foreach (var value in nodes.Values)
        {
            var nodeDataField = value.GetType().GetField("Data", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (nodeDataField!.GetValue(value) != null)
                nodesInMemory++;
        }

        Assert.True(nodesInMemory <= 2, $"Memory limit violated! Found {nodesInMemory} heavy nodes kept in RAM instead of being spilled to disk.");
    }
    
    [Fact]
    public async Task Should_Crash_With_IOException_Under_Concurrent_Disk_IO()
    {
        var manager = new BinaryManager(_tempDir);
        const string Path = "io/crash/path";
        var data = "CRASH_TEST_LONG_STRING_FOR_IO_ENGAGEMENT"u8.ToArray();
        
        manager.Store(Path, Empty.Instance, new MemoryStream(data));

        var accessor = manager.Get(Path);
        var cts = new CancellationTokenSource();

        var spillTask = Task.Run(() =>
        {
            while (!cts.Token.IsCancellationRequested) manager.ForceSpill();
        }, TestContext.Current.CancellationToken);

        var readTask = Task.Run(() =>
        {
            for (var i = 0; i < 500; i++)
            {
                try
                {
                    using var stream = accessor.OpenStream();
                    using var reader = new StreamReader(stream);
                    var text = reader.ReadToEnd();
                    Assert.True(text.Length > 0);
                }
                catch (InvalidOperationException)
                {
                    //ignored
                }
            }
        }, TestContext.Current.CancellationToken);
        
        await readTask;
        await cts.CancelAsync();
        await spillTask;
    }
}
