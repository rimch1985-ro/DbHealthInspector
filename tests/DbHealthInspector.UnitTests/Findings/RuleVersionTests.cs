using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.UnitTests.Findings;

public sealed class RuleVersionTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(1000)]
    public void Constructor_AcceptsPositiveIntegers(int value)
    {
        var version = new RuleVersion(value);

        Assert.Equal(value, version.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Constructor_RejectsZeroOrNegative(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RuleVersion(value));
    }

    [Fact]
    public void Initial_IsOne()
    {
        Assert.Equal(1, RuleVersion.Initial.Value);
    }

    [Fact]
    public void ToString_ReturnsTheNumberAsText()
    {
        Assert.Equal("3", new RuleVersion(3).ToString());
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        Assert.Equal(new RuleVersion(2), new RuleVersion(2));
        Assert.NotEqual(new RuleVersion(2), new RuleVersion(3));
    }
}
