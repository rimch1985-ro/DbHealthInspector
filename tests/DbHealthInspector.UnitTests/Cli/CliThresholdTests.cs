using System.Globalization;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.UnitTests.Cli.TestSupport;

namespace DbHealthInspector.UnitTests.Cli;

public sealed class CliThresholdTests
{
    private const string Connection = "Host=h;Database=d";

    private static CliHarness Harness() =>
        new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", Connection);

    [Fact]
    public async Task NoOverrides_PassTheFrozenDefaultsThrough()
    {
        CliHarness harness = Harness();

        Assert.Equal(0, await harness.RunAsync("inspect", "postgresql"));

        // The default instance itself, not a reconstruction of its values.
        Assert.Same(DiagnosticThresholds.Default, harness.ObservedThresholds);
        Assert.Equal(1_000_000, harness.ObservedThresholds!.LargeTableRowThreshold);
        Assert.Equal(1_073_741_824, harness.ObservedThresholds.LargeTableSizeThresholdBytes);
        Assert.Equal(10_485_760, harness.ObservedThresholds.UnusedIndexSizeThresholdBytes);
    }

    [Fact]
    public async Task RowOverride_IsUsedExactlyWithNoConversion()
    {
        CliHarness harness = Harness();

        Assert.Equal(0, await harness.RunAsync(
            "inspect", "postgresql", "--large-table-row-threshold", "250000"));

        Assert.Equal(250_000, harness.ObservedThresholds!.LargeTableRowThreshold);
        Assert.Equal(1_073_741_824, harness.ObservedThresholds.LargeTableSizeThresholdBytes);
        Assert.Equal(10_485_760, harness.ObservedThresholds.UnusedIndexSizeThresholdBytes);
    }

    [Fact]
    public async Task LargeTableSizeInBinaryMegabytes_ReproducesTheByteDefaultExactly()
    {
        CliHarness harness = Harness();

        Assert.Equal(0, await harness.RunAsync(
            "inspect", "postgresql", "--large-table-size-threshold-mb", "1024"));

        Assert.Equal(1_073_741_824, harness.ObservedThresholds!.LargeTableSizeThresholdBytes);
    }

    [Fact]
    public async Task UnusedIndexSizeInBinaryMegabytes_ReproducesTheByteDefaultExactly()
    {
        CliHarness harness = Harness();

        Assert.Equal(0, await harness.RunAsync(
            "inspect", "postgresql", "--unused-index-size-threshold-mb", "10"));

        Assert.Equal(10_485_760, harness.ObservedThresholds!.UnusedIndexSizeThresholdBytes);
    }

    [Theory]
    [InlineData("1", 1_048_576L)]
    [InlineData("2", 2_097_152L)]
    [InlineData("512", 536_870_912L)]
    public async Task MegabyteConversion_UsesTheBinaryFactor(string supplied, long expectedBytes)
    {
        CliHarness harness = Harness();

        Assert.Equal(0, await harness.RunAsync(
            "inspect", "postgresql", "--large-table-size-threshold-mb", supplied));

        Assert.Equal(expectedBytes, harness.ObservedThresholds!.LargeTableSizeThresholdBytes);
    }

    [Fact]
    public async Task AllThreeOverrides_ApplyTogether()
    {
        CliHarness harness = Harness();

        Assert.Equal(0, await harness.RunAsync(
            "inspect", "postgresql",
            "--large-table-row-threshold", "42",
            "--large-table-size-threshold-mb", "3",
            "--unused-index-size-threshold-mb", "7"));

        Assert.Equal(42, harness.ObservedThresholds!.LargeTableRowThreshold);
        Assert.Equal(3 * 1_048_576L, harness.ObservedThresholds.LargeTableSizeThresholdBytes);
        Assert.Equal(7 * 1_048_576L, harness.ObservedThresholds.UnusedIndexSizeThresholdBytes);
    }

    public static TheoryData<string, string> InvalidValues()
    {
        var data = new TheoryData<string, string>();
        foreach (string option in new[]
        {
            "--large-table-row-threshold",
            "--large-table-size-threshold-mb",
            "--unused-index-size-threshold-mb",
        })
        {
            data.Add(option, "0");
            data.Add(option, "-1");
            data.Add(option, "abc");
            data.Add(option, "1.5");
            data.Add(option, string.Empty);

            // Beyond Int64 entirely.
            data.Add(option, "9223372036854775808");
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(InvalidValues))]
    public async Task InvalidThresholdValue_IsRejectedWithTheFixedMessage(string option, string value)
    {
        CliHarness harness = Harness();

        int exitCode = await harness.RunAsync("inspect", "postgresql", option, value);

        Assert.Equal(2, exitCode);
        Assert.Contains("A diagnostic threshold value is invalid.", harness.Error, StringComparison.Ordinal);
        Assert.Null(harness.ObservedThresholds);
    }

    [Theory]
    [InlineData("--large-table-size-threshold-mb")]
    [InlineData("--unused-index-size-threshold-mb")]
    public async Task MultiplicationOverflow_IsRejectedRatherThanThrowing(string option)
    {
        // Int64.MaxValue parses fine but overflows once multiplied by 1,048,576. The checked
        // conversion must turn that into a usage failure, never an OverflowException.
        CliHarness harness = Harness();
        string justOverflowing = ((long.MaxValue / 1_048_576L) + 1).ToString(CultureInfo.InvariantCulture);

        foreach (string value in new[] { long.MaxValue.ToString(CultureInfo.InvariantCulture), justOverflowing })
        {
            int exitCode = await harness.RunAsync("inspect", "postgresql", option, value);

            Assert.Equal(2, exitCode);
            Assert.Contains("A diagnostic threshold value is invalid.", harness.Error, StringComparison.Ordinal);
            Assert.DoesNotContain("Overflow", harness.All, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task LargestNonOverflowingValue_IsAccepted()
    {
        CliHarness harness = Harness();
        long maximum = long.MaxValue / 1_048_576L;

        int exitCode = await harness.RunAsync(
            "inspect", "postgresql",
            "--large-table-size-threshold-mb", maximum.ToString(CultureInfo.InvariantCulture));

        Assert.Equal(0, exitCode);
        Assert.Equal(maximum * 1_048_576L, harness.ObservedThresholds!.LargeTableSizeThresholdBytes);
    }

    [Fact]
    public async Task InvalidThreshold_IsRejectedBeforeAnyConnectionAttempt()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => throw new InvalidOperationException("must not run");

        Assert.Equal(2, await harness.RunAsync(
            "inspect", "postgresql", "--large-table-row-threshold", "0"));
        Assert.Null(harness.ObservedConnectionString);
    }
}
