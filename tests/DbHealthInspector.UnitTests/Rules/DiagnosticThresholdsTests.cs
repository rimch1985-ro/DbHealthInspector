using DbHealthInspector.Core.Rules;

namespace DbHealthInspector.UnitTests.Rules;

public sealed class DiagnosticThresholdsTests
{
    [Fact]
    public void Defaults_AreTheFrozenProductValues()
    {
        DiagnosticThresholds thresholds = DiagnosticThresholds.Default;

        Assert.Equal(1_000_000, thresholds.LargeTableRowThreshold);
        Assert.Equal(1_073_741_824, thresholds.LargeTableSizeThresholdBytes);
        Assert.Equal(10_485_760, thresholds.UnusedIndexSizeThresholdBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveRowThreshold_IsRejected(long value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new DiagnosticThresholds(value, 1, 1));

        Assert.Equal("largeTableRowThreshold", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveSizeThreshold_IsRejected(long value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new DiagnosticThresholds(1, value, 1));

        Assert.Equal("largeTableSizeThresholdBytes", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveUnusedIndexThreshold_IsRejected(long value)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => new DiagnosticThresholds(1, 1, value));

        Assert.Equal("unusedIndexSizeThresholdBytes", exception.ParamName);
    }

    [Fact]
    public void AnyPositiveValueIsAccepted_BecausePositivityIsTheOnlyInvariant()
    {
        var thresholds = new DiagnosticThresholds(1, 1, long.MaxValue);

        Assert.Equal(1, thresholds.LargeTableRowThreshold);
        Assert.Equal(long.MaxValue, thresholds.UnusedIndexSizeThresholdBytes);
    }
}
