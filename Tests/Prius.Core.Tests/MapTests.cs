namespace Prius.Core.Tests;

public class MapTests
{
    [Fact]
    public void DictionaryMap_Should_DeepPutAndDeepGetValues()
    {
        var map = DictionaryMap.New;
        var path = (MapPath)"orders/active/1";
        
        map.DeepPut(path, 42L);
        var result = map.DeepGet(path);

        Assert.True(result.IsLong);
        Assert.Equal(42L, (long)result);
        
        Assert.True(map.Get("orders").IsMap);
    }

    [Fact]
    public void JsonReaderMap_Should_BeLazyAndEqualDictionaryMap()
    {
        const string Json = "{\"id\": 1, \"data\": {\"status\": \"active\"}}";
        var jsonMap = JsonReaderMap.From(Json);
        
        var dictMap = DictionaryMap.New;
        dictMap.Put("id", 1L);
        var sub = DictionaryMap.New;
        sub.Put("status", "active");
        dictMap.Put("data", sub);
        
        Assert.True(jsonMap.DeepEquals(dictMap));
        Assert.Equal("active", jsonMap.DeepGet("data/status").AsValue<string>());
    }

    [Fact]
    public void DeepEquals_Should_HandleJsStyleConversion()
    {
        var map1 = DictionaryMap.New;
        var map2 = DictionaryMap.New;

        map1.Put("val", 100L);
        map2.Put("val", "100");
        
        Assert.True(map1.DeepEquals(map2));
    }
    
    [Fact]
    public void DeepCopy_Should_CreateIsolatedIterativeClone()
    {
        var root = DictionaryMap.New;
        root.DeepPut("a/b/c", "origin");

        var copy = new DictionaryMap(root.DeepCopy());
        copy.DeepPut("a/b/c", "mutated");
        
        Assert.Equal("origin", root.DeepGet("a/b/c").AsValue<string>());
        Assert.Equal("mutated", copy.DeepGet("a/b/c").AsValue<string>());
    }

    [Fact]
    public void Put_EmptyValue_Should_RemoveKey()
    {
        var map = DictionaryMap.New;
        map.Put("temp", "to_delete");
        Assert.False(map.IsEmpty);

        map.PutEmpty("temp");
        
        Assert.True(map.IsEmpty);
        Assert.True(map.Get("temp").IsEmpty);
    }

    [Fact]
    public void DeepPut_Should_OverwritePrimitivesWithMaps()
    {
        var map = DictionaryMap.New;
        
        map.DeepPut("path", 1L);
        map.DeepPut("path/sub", 2L);

        Assert.True(map.Get("path").IsMap);
        Assert.Equal(2L, map.DeepGet("path/sub").AsValue<long>());
    }

    [Fact]
    public void JsonReaderMap_Should_MaterializeOnlyOnMutation()
    {
        const string Json = "{\"key\": \"value\"}";
        var lazyMap = JsonReaderMap.From(Json);
        
        Assert.IsType<JsonReaderMap>(lazyMap);

        lazyMap.Put("new", 1L);
        
        Assert.Equal("value", lazyMap.Get("key").AsValue<string>());
        Assert.Equal(1L, lazyMap.Get("new").AsValue<long>());
    }
}
