using DbHealthInspector.Cli;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.Cli.TestSupport;
using DbHealthInspector.UnitTests.Rules.TestSupport;

namespace DbHealthInspector.UnitTests.Cli;

/// <summary>
/// Regressions for the two defects Codex R1 found: an over-broad
/// <see cref="ArgumentException"/> catch (R1-01), and an unsupported server rendering as a clean
/// zero-finding inspection (R1-02).
/// </summary>
public sealed class CliFailureClassificationTests
{
    private const string ConfigurationInvalid = "The PostgreSQL connection configuration is invalid.";
    private const string InspectionFailed = "The PostgreSQL inspection could not be completed.";
    private const string Sentinel = "SUPER_SECRET_SENTINEL";

    private static CliHarness Harness() =>
        new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", "Host=db;Database=app");

    // --- R1-01: ArgumentException classification -------------------------------------------------

    [Fact]
    public async Task ProviderRejectingTheConfiguration_ReportsInvalidConfiguration()
    {
        // The real provider path translates only Create's own ArgumentException into this marker.
        var harness = Harness();
        harness.Behavior = _ => throw new PostgreSqlConfigurationRejectedException();

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.Contains(ConfigurationInvalid, harness.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(InspectionFailed, harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RealProviderRejectsAnUnusableConnectionString_ReportsInvalidConfiguration()
    {
        // Exercises the genuine production executor, so the narrowed translation in
        // PostgreSqlInspectionExecution.CreateProvider is what classifies the failure.
        var harness = new CliHarness { UseProductionExecutor = true }
            .WithEnvironment("DBHEALTH_CONNECTION", "this-is-not-a-connection-string");

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.Contains(ConfigurationInvalid, harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArgumentExceptionAfterConfiguration_ReportsGenericInspectionFailure()
    {
        // The defect Codex found: this used to be misreported as invalid connection
        // configuration, sending the user to fix something that was not broken.
        var harness = Harness();
        harness.Behavior = _ => throw new ArgumentException("internal composition defect", "someParameter");

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.Contains(InspectionFailed, harness.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(ConfigurationInvalid, harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ArgumentOutOfRangeExceptionAfterConfiguration_ReportsGenericInspectionFailure()
    {
        var harness = Harness();
        harness.Behavior = _ => throw new ArgumentOutOfRangeException("someParameter", 42, "out of range");

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.Contains(InspectionFailed, harness.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(ConfigurationInvalid, harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostConfigurationArgumentExceptionCarryingASecret_LeaksNothing()
    {
        var harness = Harness();
        harness.Behavior = _ => throw new ArgumentException(
            $"Host=db;Password={Sentinel}", "connectionString");

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.DoesNotContain(Sentinel, harness.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, harness.Error, StringComparison.Ordinal);
        Assert.Contains(InspectionFailed, harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigurationMarker_CarriesNoInnerExceptionAndNoCallerText()
    {
        // The marker must not become a channel for text outside this repository's control.
        var exception = new PostgreSqlConfigurationRejectedException();

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain(Sentinel, exception.Message, StringComparison.Ordinal);
        Assert.Empty(exception.Data);
    }

    // --- R1-02: unsupported server must never look clean -----------------------------------------

    [Fact]
    public async Task UnsupportedServer_IsReportedAsFailureNotAsAHealthyDatabase()
    {
        // What an unsupported major produces: real metadata, both capabilities unavailable, and
        // empty collections because nothing was ever queried.
        var harness = Harness();
        harness.Behavior = _ => Task.FromResult(
            CliHarness.Inspections.Run(UnsupportedServerSnapshot()));

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.Contains(InspectionFailed, harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnsupportedServer_NeverPrintsACleanResult()
    {
        var harness = Harness();
        harness.Behavior = _ => Task.FromResult(
            CliHarness.Inspections.Run(UnsupportedServerSnapshot()));

        await harness.RunAsync("inspect", "postgresql");

        // The false-clean signature: an empty inspection that reads as a healthy one.
        Assert.DoesNotContain(
            "No health issues were detected by the enabled diagnostics.",
            harness.Output,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Overall risk", harness.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("None", harness.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("SUMMARY", harness.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("TARGET", harness.Output, StringComparison.Ordinal);

        // Nothing at all is rendered: there is no result worth showing.
        Assert.Empty(harness.Output);
    }

    [Fact]
    public async Task UnsupportedServer_DoesNotDiscloseTheCapabilityReason()
    {
        var harness = Harness();
        harness.Behavior = _ => Task.FromResult(
            CliHarness.Inspections.Run(UnsupportedServerSnapshot()));

        await harness.RunAsync("inspect", "postgresql");

        // Only the fixed generic message; the underlying reason text stays internal.
        Assert.Equal(InspectionFailed, harness.Error.Trim());
    }

    [Fact]
    public async Task OptionalStatisticsLossStillSucceeds_ProvingTheGateIsRequiredCapabilityOnly()
    {
        // The regression guard for the fix itself: losing an OPTIONAL capability must not be
        // swept up by the required-capability gate.
        var harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.WithStatisticsUnavailable());

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(0, exitCode);
        Assert.Contains("SkippedUnavailableCapability", harness.Output, StringComparison.Ordinal);
        Assert.Contains(
            "No health issues were detected by the enabled diagnostics.",
            harness.Output,
            StringComparison.Ordinal);
        Assert.Empty(harness.Error);
    }

    /// <summary>
    /// The snapshot shape the provider composes for a server whose major version this product has
    /// not been validated against: both capabilities unavailable, no relations, no indexes.
    /// </summary>
    private static DatabaseSnapshot UnsupportedServerSnapshot() =>
        DiagnosticSnapshotBuilder.Snapshot(
            tables: [],
            indexes: [],
            usageStatistics: CapabilityStatus.Unavailable,
            catalogMetadata: CapabilityStatus.Unavailable);
}
