using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class InspectionSummaryTests
{
    private static readonly FakeInspectionRule Rule = FakeInspectionRule.NoFindings("DBH900");

    private static Finding Build(FindingSeverity severity, string objectName = "orders") =>
        InspectionFindingBuilder.For(Rule, severity: severity, objectName: objectName);

    private static DiagnosticExecution CompletedExecution(int findingCount = 0) =>
        DiagnosticExecution.Completed(Rule.Code, Rule.Version, Rule.Name, Rule.Category, findingCount);

    private static DiagnosticExecution SkippedExecution() =>
        DiagnosticExecution.SkippedUnavailableCapability(
            Rule.Code, Rule.Version, Rule.Name, Rule.Category, [CapabilityKind.UsageStatistics]);

    private static DiagnosticExecution FailedExecution() =>
        DiagnosticExecution.Failed(
            Rule.Code, Rule.Version, Rule.Name, Rule.Category,
            new DiagnosticExecutionFailure(DiagnosticFailureKind.UnhandledRuleException, "The diagnostic rule failed during evaluation."));

    [Fact]
    public void Constructor_ProducesZeroCountsForEmptyInput()
    {
        var summary = new InspectionSummary([], []);

        Assert.Equal(0, summary.TotalFindings);
        Assert.Equal(0, summary.InfoFindings);
        Assert.Equal(0, summary.WarningFindings);
        Assert.Equal(0, summary.CriticalFindings);
        Assert.Equal(0, summary.TotalDiagnostics);
        Assert.Equal(0, summary.CompletedDiagnostics);
        Assert.Equal(0, summary.SkippedDiagnostics);
        Assert.Equal(0, summary.FailedDiagnostics);
    }

    [Fact]
    public void Constructor_CountsOnlyInfoFindings()
    {
        Finding[] findings = [Build(FindingSeverity.Info, "a"), Build(FindingSeverity.Info, "b")];

        var summary = new InspectionSummary(findings, []);

        Assert.Equal(2, summary.InfoFindings);
        Assert.Equal(0, summary.WarningFindings);
        Assert.Equal(0, summary.CriticalFindings);
        Assert.Equal(2, summary.TotalFindings);
    }

    [Fact]
    public void Constructor_CountsAMixOfSeverities()
    {
        Finding[] findings =
        [
            Build(FindingSeverity.Info, "a"),
            Build(FindingSeverity.Warning, "b"),
            Build(FindingSeverity.Warning, "c"),
            Build(FindingSeverity.Critical, "d"),
        ];

        var summary = new InspectionSummary(findings, []);

        Assert.Equal(1, summary.InfoFindings);
        Assert.Equal(2, summary.WarningFindings);
        Assert.Equal(1, summary.CriticalFindings);
        Assert.Equal(4, summary.TotalFindings);
        Assert.Equal(summary.InfoFindings + summary.WarningFindings + summary.CriticalFindings, summary.TotalFindings);
    }

    [Fact]
    public void Constructor_CountsAMixOfDiagnosticExecutionStatuses()
    {
        DiagnosticExecution[] executions =
        [
            CompletedExecution(),
            CompletedExecution(),
            SkippedExecution(),
            FailedExecution(),
        ];

        var summary = new InspectionSummary([], executions);

        Assert.Equal(2, summary.CompletedDiagnostics);
        Assert.Equal(1, summary.SkippedDiagnostics);
        Assert.Equal(1, summary.FailedDiagnostics);
        Assert.Equal(4, summary.TotalDiagnostics);
        Assert.Equal(
            summary.CompletedDiagnostics + summary.SkippedDiagnostics + summary.FailedDiagnostics,
            summary.TotalDiagnostics);
    }

    [Fact]
    public void Constructor_RejectsNullFindings()
    {
        Assert.Throws<ArgumentNullException>(() => new InspectionSummary(null!, []));
    }

    [Fact]
    public void Constructor_RejectsNullDiagnosticExecutions()
    {
        Assert.Throws<ArgumentNullException>(() => new InspectionSummary([], null!));
    }
}
