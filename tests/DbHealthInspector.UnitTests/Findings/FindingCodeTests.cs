using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.UnitTests.Findings;

public sealed class FindingCodeTests
{
    [Theory]
    [InlineData("DBH001")]
    [InlineData("DBH002")]
    [InlineData("DBH003")]
    [InlineData("DBH004")]
    [InlineData("DBH005")]
    public void Constructor_AcceptsApprovedCodes(string code)
    {
        var findingCode = new FindingCode(code);

        Assert.Equal(code, findingCode.Value);
    }

    [Fact]
    public void Catalog_ExposesTheFiveApprovedCodes()
    {
        Assert.Equal("DBH001", FindingCodes.TableWithoutPrimaryKey.Value);
        Assert.Equal("DBH002", FindingCodes.LargeTable.Value);
        Assert.Equal("DBH003", FindingCodes.ExactDuplicateIndex.Value);
        Assert.Equal("DBH004", FindingCodes.UnusedIndexCandidate.Value);
        Assert.Equal("DBH005", FindingCodes.InvalidIndex.Value);
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FindingCode(null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("DBH1")]
    [InlineData("DBH0001")]
    [InlineData("DBHA01")]
    [InlineData("DBH-01")]
    [InlineData("dbh001")]
    [InlineData(" DBH001")]
    [InlineData("DBH001 ")]
    [InlineData("XYZ001")]
    [InlineData("DBH001X")]
    public void Constructor_RejectsInvalidFormats(string value)
    {
        Assert.Throws<ArgumentException>(() => new FindingCode(value));
    }

    [Fact]
    public void Constructor_IsCaseSensitive()
    {
        Assert.Throws<ArgumentException>(() => new FindingCode("dbh001"));
    }

    [Fact]
    public void Equality_IsValueBasedAndOrdinal()
    {
        var first = new FindingCode("DBH001");
        var second = new FindingCode("DBH001");
        var different = new FindingCode("DBH002");

        Assert.Equal(first, second);
        Assert.True(first == second);
        Assert.NotEqual(first, different);
        Assert.False(first == different);
    }

    [Fact]
    public void GetHashCode_IsConsistentWithEquality()
    {
        var first = new FindingCode("DBH001");
        var second = new FindingCode("DBH001");

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void ToString_ReturnsTheCodeTextOnly()
    {
        var code = new FindingCode("DBH003");

        Assert.Equal("DBH003", code.ToString());
    }

    [Fact]
    public void ComparisonOperators_OrderOrdinally()
    {
        var lower = new FindingCode("DBH001");
        var higher = new FindingCode("DBH002");

        Assert.True(lower < higher);
        Assert.True(lower <= higher);
        Assert.True(higher > lower);
        Assert.True(higher >= lower);
        Assert.True(lower <= new FindingCode("DBH001"));
    }
}
