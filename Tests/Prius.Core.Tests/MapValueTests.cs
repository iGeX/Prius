using Prius.Core.Maps;
using Xunit;

namespace Prius.Core.Tests;

public class MapValueTests
{
    [Fact]
    public void Constructor_Default_ShouldBeEmpty()
    {
        var value = new MapValue();
        
        Assert.True(value.IsEmpty);
        Assert.False(value.IsValue);
        Assert.Equal(string.Empty, value.ToString());
    }

    [Theory]
    [InlineData("123.45", 123.45)]
    [InlineData("123,45", 123.45)]
    [InlineData("  123.45  ", 123.45)]
    public void DecimalParsing_ShouldHandleDotsAndCommas(string input, double expected)
    {
        var value = new MapValue(input);
        
        Assert.Equal((decimal)expected, value.AsDecimal());
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("TRUE", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("any_other_string", true)]
    public void BooleanParsing_ShouldFollowJsSemantics(string input, bool expected)
    {
        var value = new MapValue(input);
        
        Assert.Equal(expected, value.AsBool());
    }

    [Fact]
    public void BoolToStringConversion_ShouldReturnZeroOrOne()
    {
        var trueValue = new MapValue(true);
        var falseValue = new MapValue(false);

        Assert.Equal("1", trueValue.AsString());
        Assert.Equal("0", falseValue.AsString());
    }

    [Fact]
    public void CrossTypeEquality_ShouldBeSymmetric()
    {
        var number = new MapValue(1L);
        var str = new MapValue("1");
        var boolean = new MapValue(true);

        Assert.True(number == str);
        Assert.True(str == number);
        Assert.True(boolean == str);
        Assert.True(boolean == number);
    }

    [Fact]
    public void EmptyAndBlankStrings_ShouldBeEquivalent()
    {
        var empty = new MapValue();
        var blankStr = new MapValue("   ");
        var actualEmptyStr = new MapValue("");

        Assert.True(empty == blankStr);
        Assert.True(blankStr == actualEmptyStr);
        Assert.Equal(empty.GetHashCode(), blankStr.GetHashCode());
    }

    [Fact]
    public void GetHashCode_ShouldBeConsistentWithEquals()
    {
        var number = new MapValue(1L);
        var str = new MapValue("1");
        var boolean = new MapValue(true);

        var hashNum = number.GetHashCode();
        var hashStr = str.GetHashCode();
        var hashBool = boolean.GetHashCode();

        Assert.Equal(hashNum, hashStr);
        Assert.Equal(hashStr, hashBool);

        var expectedHash = "1".AsSpan().GetSpanHashCode();
        Assert.Equal(expectedHash, hashNum);
    }

    [Fact]
    public void GetHashCode_OnSpan_ShouldNotThrowException()
    {
        var value = new MapValue("some_test_string");
        
        var exception = Record.Exception(() => value.GetHashCode());
        
        Assert.Null(exception);
    }

    [Fact]
    public void Indexer_OnNonMap_ShouldReturnEmptyInstanceSecurely()
    {
        var value = new MapValue(42L);
        
        var result = value["level1"]["level2"]["level3"];

        Assert.True(result.IsEmpty);
    }

    [Fact]
    public void Operators_Comparison_ShouldWorkCorrectly()
    {
        var small = new MapValue(10L);
        var bigStr = new MapValue("20");

        Assert.True(small < bigStr);
        Assert.True(bigStr > small);
        Assert.True(small <= bigStr);
        Assert.True(bigStr >= small);
    }

    [Fact]
    public void DateTimeOffset_RoundTrip_ShouldPreservePrecision()
    {
        var now = DateTimeOffset.UtcNow;
        var value = new MapValue(now);

        var serialized = value.AsString();
        var deserialized = new MapValue(serialized).AsDateTimeOffset();

        Assert.Equal(now, deserialized);
    }
}
