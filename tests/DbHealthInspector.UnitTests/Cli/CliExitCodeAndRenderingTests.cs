using DbHealthInspector.Cli;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.UnitTests.Cli.TestSupport;

namespace DbHealthInspector.UnitTests.Cli;

public sealed class CliExitCodeAndRenderingTests
{
    private static CliHarness Harness() =>
        new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", "Host=h;Database=d");

    // --- Exit codes -------------------------------------------------------------------------

    [Fact]
    public async Task ZeroFindings_ExitsZero()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.Healthy());

        Assert.Equal(0, await harness.RunAsync("inspect", "postgresql"));
    }

    [Fact]
    public async Task InfoOnlyFindings_ExitZero()
    {
        CliHarness harness = Harness();
        InspectionResult result = CliHarness.Inspections.WithInfoOnly();
        harness.Behavior = _ => Task.FromResult(result);

        Assert.Equal(OverallRisk.Low, result.OverallRisk);
        Assert.True(result.Summary.InfoFindings > 0);
        Assert.Equal(0, result.Summary.WarningFindings + result.Summary.CriticalFindings);
        Assert.Equal(0, await harness.RunAsync("inspect", "postgresql"));
    }

    [Fact]
    public async Task WarningFinding_ExitsOne()
    {
        CliHarness harness = Harness();
        InspectionResult result = CliHarness.Inspections.WithWarning();
        harness.Behavior = _ => Task.FromResult(result);

        Assert.True(result.Summary.WarningFindings > 0);
        Assert.Equal(1, await harness.RunAsync("inspect", "postgresql"));
    }

    [Fact]
    public async Task CriticalFinding_ExitsOne()
    {
        CliHarness harness = Harness();
        InspectionResult result = CliHarness.Inspections.WithCritical();
        harness.Behavior = _ => Task.FromResult(result);

        Assert.True(result.Summary.CriticalFindings > 0);
        Assert.Equal(1, await harness.RunAsync("inspect", "postgresql"));
    }

    [Fact]
    public async Task SkippedOptionalDiagnosticAlone_DoesNotForceFailure()
    {
        // The rule that is easiest to get wrong: losing an optional capability degrades the
        // picture, but it is not an error and must not be reported as one.
        CliHarness harness = Harness();
        InspectionResult result = CliHarness.Inspections.WithStatisticsUnavailable();
        harness.Behavior = _ => Task.FromResult(result);

        Assert.Equal(1, result.Summary.SkippedDiagnostics);
        Assert.False(result.HasErrors);
        Assert.Equal(0, await harness.RunAsync("inspect", "postgresql"));
    }

    [Fact]
    public async Task CancellationDuringInspection_ExitsTwo()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => throw new OperationCanceledException();

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.Contains("The inspection was cancelled.", harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RejectedConnectionConfiguration_ExitsTwo()
    {
        // The configuration branch is now reached only through the marker the production executor
        // raises around provider creation. A bare ArgumentException from the seam represents a
        // failure *after* configuration and is deliberately classified generically (Codex R1-01);
        // that case is covered in CliFailureClassificationTests.
        CliHarness harness = Harness();
        harness.Behavior = _ => throw new PostgreSqlConfigurationRejectedException();

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.Contains(
            "The PostgreSQL connection configuration is invalid.", harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderFailure_ExitsTwoWithTheGenericMessage()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => throw new InvalidOperationException("anything at all");

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.Contains(
            "The PostgreSQL inspection could not be completed.", harness.Error, StringComparison.Ordinal);
    }

    // --- Rendering --------------------------------------------------------------------------

    [Fact]
    public async Task ZeroFindings_PrintsBothMandatorySentences()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.Healthy());

        await harness.RunAsync("inspect", "postgresql");

        Assert.Contains(
            "No health issues were detected by the enabled diagnostics.",
            harness.Output,
            StringComparison.Ordinal);

        // The caveat is mandatory: five structural rules finding nothing is not a clean bill of
        // health, and the tool must not imply otherwise.
        Assert.Contains(
            "This does not guarantee the database has no other problems.",
            harness.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZeroFindings_NeverClaimsThatTheDatabaseIsHealthy()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.Healthy());

        await harness.RunAsync("inspect", "postgresql");

        foreach (string overclaim in new[] { "perfect", "problem-free", "fully healthy", "no problems" })
        {
            Assert.DoesNotContain(overclaim, harness.Output, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task PositiveFinding_RendersCodeSeverityObjectConfidenceAndRecommendation()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.WithWarning());

        await harness.RunAsync("inspect", "postgresql");

        Assert.Contains("DBH001", harness.Output, StringComparison.Ordinal);
        Assert.Contains("[Warning]", harness.Output, StringComparison.Ordinal);
        Assert.Contains("app.audit_log", harness.Output, StringComparison.Ordinal);
        Assert.Contains("Confidence", harness.Output, StringComparison.Ordinal);
        Assert.Contains("Recommendation", harness.Output, StringComparison.Ordinal);
        Assert.Contains("Evidence", harness.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CriticalFinding_RendersItsIndexIdentityWithTheParentTable()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.WithCritical());

        await harness.RunAsync("inspect", "postgresql");

        Assert.Contains("[Critical]", harness.Output, StringComparison.Ordinal);
        Assert.Contains("DBH005", harness.Output, StringComparison.Ordinal);
        Assert.Contains("app.idx_broken (on orders)", harness.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_RenderInFrozenOrdinalOrder()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.Healthy());

        await harness.RunAsync("inspect", "postgresql");

        int previous = -1;
        foreach (string code in new[] { "DBH001", "DBH002", "DBH003", "DBH004", "DBH005" })
        {
            int index = harness.Output.IndexOf(code, StringComparison.Ordinal);
            Assert.True(index > previous, $"{code} is out of order.");
            previous = index;
        }
    }

    [Fact]
    public async Task SkippedDiagnostic_IsVisiblyDistinctFromOneThatFoundNothing()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.WithStatisticsUnavailable());

        await harness.RunAsync("inspect", "postgresql");

        Assert.Contains("SkippedUnavailableCapability", harness.Output, StringComparison.Ordinal);
        Assert.Contains("UsageStatistics", harness.Output, StringComparison.Ordinal);
        Assert.Contains("WARNING:", harness.Output, StringComparison.Ordinal);
        Assert.Contains("not a clean result", harness.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_ContainsNoAnsiEscapeSequences()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.WithWarning());

        await harness.RunAsync("inspect", "postgresql");

        Assert.DoesNotContain('', harness.All);
    }

    [Fact]
    public async Task Output_IsByteIdenticalAcrossRepeatedRuns()
    {
        InspectionResult result = CliHarness.Inspections.WithWarning();

        CliHarness first = Harness();
        first.Behavior = _ => Task.FromResult(result);
        await first.RunAsync("inspect", "postgresql");

        CliHarness second = Harness();
        second.Behavior = _ => Task.FromResult(result);
        await second.RunAsync("inspect", "postgresql");

        Assert.Equal(first.Output, second.Output);
    }

    [Fact]
    public async Task SuccessfulOutput_IdentifiesTheTargetDatabaseEngineAndVersion()
    {
        CliHarness harness = Harness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.Healthy());

        await harness.RunAsync("inspect", "postgresql");

        Assert.Contains("TARGET", harness.Output, StringComparison.Ordinal);
        Assert.Contains("inspector_test", harness.Output, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL", harness.Output, StringComparison.Ordinal);
        Assert.Contains("18.4", harness.Output, StringComparison.Ordinal);
    }
}
