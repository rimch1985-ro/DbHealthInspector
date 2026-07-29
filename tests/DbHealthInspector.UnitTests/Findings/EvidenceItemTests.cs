using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.UnitTests.Findings;

public sealed class EvidenceItemTests
{
    [Fact]
    public void Constructor_AllowsValidItemWithUnit()
    {
        var item = new EvidenceItem("estimatedRows", "25000", FingerprintParticipation.Exclude, "rows");

        Assert.Equal("estimatedRows", item.Key);
        Assert.Equal("25000", item.Value);
        Assert.Equal("rows", item.Unit);
        Assert.Equal(FingerprintParticipation.Exclude, item.FingerprintParticipation);
    }

    [Fact]
    public void Constructor_AllowsNullUnit()
    {
        var item = new EvidenceItem("indexDefinition", "CREATE INDEX ...", FingerprintParticipation.Include);

        Assert.Null(item.Unit);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankUnit(string unit)
    {
        Assert.Throws<ArgumentException>(() =>
            new EvidenceItem("k", "v", FingerprintParticipation.Include, unit));
    }

    [Fact]
    public void Constructor_RejectsNullKey()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EvidenceItem(null!, "value", FingerprintParticipation.Include));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankKey(string key)
    {
        Assert.Throws<ArgumentException>(() =>
            new EvidenceItem(key, "value", FingerprintParticipation.Include));
    }

    [Fact]
    public void Constructor_RejectsNullValue()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new EvidenceItem("key", null!, FingerprintParticipation.Include));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankValue(string value)
    {
        Assert.Throws<ArgumentException>(() =>
            new EvidenceItem("key", value, FingerprintParticipation.Include));
    }

    [Fact]
    public void Constructor_RejectsUndefinedParticipation()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new EvidenceItem("key", "value", (FingerprintParticipation)999));
    }
}
