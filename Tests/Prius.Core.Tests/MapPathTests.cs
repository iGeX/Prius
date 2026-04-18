namespace Prius.Core.Tests;

public class MapPathTests
{
    [Theory]
    [InlineData("data/users/1", "data", "users/1")]
    [InlineData("/data/users/1/", "data", "users/1")]
    [InlineData("  /data/users/1  ", "data", "users/1")]
    [InlineData("root / sub / leaf", "root", "sub / leaf")]
    [InlineData("  root  /  sub  ", "root", "sub")]
    [InlineData("////", "//", "")]
    [InlineData("///data///", "/data/", "")]
    [InlineData("single", "single", "")]
    [InlineData("  /  ", "", "")]
    public void Constructor_Should_NormalizeAndNavigate(string raw, string expectedHead, string expectedTail)
    {
        var path = new MapPath(raw.AsSpan());

        Assert.Equal(expectedHead, path.Head);
        Assert.Equal(expectedTail, path.Tail.ToString());
    }

    [Theory]
    [InlineData("", true)]
    [InlineData(" ", true)]
    [InlineData("/", true)]
    [InlineData(" / ", true)]
    [InlineData("///", false)]
    [InlineData("//", false)]
    public void EmptyPath_Should_BeIdenticalRegardlessOfSlashes(string raw, bool expectedEmpty)
    {
        var path = new MapPath(raw.AsSpan());
        var empty = new MapPath("".AsSpan());

        Assert.Equal(expectedEmpty, path.IsEmpty);
        if (expectedEmpty)
            Assert.Equal(empty, path);
    }

    [Theory]
    [InlineData("Order//ID / Status", "Order/ID", "Status")]
    [InlineData("  A  //  B  /  C  ", "A  /  B", "C")]
    [InlineData("A///B", "A/", "B")]
    public void Head_Should_UnescapeAndTrimSegment(string raw, string expectedHead, string expectedTail)
    {
        var path = new MapPath(raw.AsSpan());

        Assert.Equal(expectedHead, path.Head);
        Assert.Equal(expectedTail, path.Tail.ToString());
    }

    [Fact]
    public void IsHeadEquals_Should_WorkWithDirtyPaths()
    {
        var path = new MapPath("  System  /  Kernel  ".AsSpan());

        Assert.True(path.IsHeadEquals("System"));
        Assert.False(path.IsHeadEquals("System "));
    }

    [Fact]
    public void Tail_Should_RecursiveNavigateCleanly()
    {
        var path = new MapPath("  root / sub / leaf  ".AsSpan());

        var sub = path.Tail;
        var leaf = sub.Tail;

        Assert.Equal("root", path.Head);
        Assert.Equal("sub", sub.Head);
        Assert.Equal("leaf", leaf.Head);
        Assert.True(leaf.Tail.IsEmpty);
    }

    [Theory]
    [InlineData("Order//ID/Status", "Order/ID", "Status")]
    [InlineData("Companies/Company//Name//Branch/Dept", "Companies", "Company//Name//Branch/Dept")]
    [InlineData("Escaped//At//End//", "Escaped/At/End/", "")]
    [InlineData("////", "//", "")]
    [InlineData("Key////Value", "Key//Value", "")]
    [InlineData("A///B", "A/", "B")]
    public void Head_Should_UnescapeCorrectly(string raw, string expectedHead, string expectedTail)
    {
        var path = new MapPath(raw.AsSpan());

        Assert.Equal(expectedHead, path.Head);
        Assert.Equal(expectedTail, path.Tail.ToString());
    }

    [Fact]
    public void IsHeadEquals_Should_WorkWithoutAllocations()
    {
        var path = new MapPath("System/Kernel/Log".AsSpan());

        Assert.True(path.IsHeadEquals("System"));
        Assert.False(path.IsHeadEquals("Kernel"));
        Assert.False(path.IsHeadEquals("Sys"));
    }

    [Fact]
    public void Operators_Should_HandleConcatenationAndComparison()
    {
        MapPath path1 = "data";
        MapPath path2 = "users";

        var combined = path1 + path2;
        Assert.Equal("data/users", combined);

        Assert.Equal("data", path1);
        Assert.Equal(new MapPath(" /data/ ".AsSpan()), path1);
        Assert.Equal("data", path1);
    }

    [Fact]
    public void Length_Should_ReturnActualSpanLength()
    {
        var path = new MapPath(" /Data/Users/ ".AsSpan());
        Assert.Equal("Data/Users".Length, path.Length);
    }
}
