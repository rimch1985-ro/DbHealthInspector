using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.Rules.TestSupport;

namespace DbHealthInspector.UnitTests.Rules;

/// <summary>
/// The acceptance test for GC-DHI-05A: one snapshot exercising all five approved diagnostics,
/// run through the existing <see cref="InspectionOrchestrator"/>.
/// </summary>
public sealed class ApprovedDiagnosticsInspectionTests
{
    private const long LargeRows = 1_000_000;
    private const long UnusedIndexSize = 10_485_760;

    private sealed class FixedSnapshotProvider(DatabaseSnapshot snapshot) : IDatabaseSnapshotProvider
    {
        public Task<DatabaseSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }

    /// <summary>
    /// A database containing exactly one instance of each approved condition, and nothing that
    /// would incidentally trigger a second finding from another rule.
    /// </summary>
    private static DatabaseSnapshot UnhealthySnapshot(
        CapabilityStatus usageStatistics = CapabilityStatus.Available) =>
        DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                // DBH001: an ordinary table with no primary key, small enough not to be large.
                DiagnosticSnapshotBuilder.Table("audit_log", hasPrimaryKey: false),

                // DBH002: a large table that does have a primary key.
                DiagnosticSnapshotBuilder.Table(
                    "orders", hasPrimaryKey: true, estimatedRowCount: LargeRows),
            ],
            indexes:
            [
                // DBH003: two structurally identical indexes. Both record scans, so neither is
                // also an unused candidate.
                DiagnosticSnapshotBuilder.Index(
                    "idx_orders_customer_a",
                    keyParts: [DiagnosticSnapshotBuilder.KeyPart(1, "customer_id")],
                    sizeBytes: UnusedIndexSize,
                    scanCount: 500),
                DiagnosticSnapshotBuilder.Index(
                    "idx_orders_customer_b",
                    keyParts: [DiagnosticSnapshotBuilder.KeyPart(1, "customer_id")],
                    sizeBytes: UnusedIndexSize,
                    scanCount: 500),

                // DBH004: sizeable, never scanned, and structurally unique so it forms no
                // duplicate group.
                DiagnosticSnapshotBuilder.Index(
                    "idx_orders_region",
                    keyParts: [DiagnosticSnapshotBuilder.KeyPart(1, "region")],
                    sizeBytes: UnusedIndexSize,
                    scanCount: 0),

                // DBH005: invalid. Being invalid also disqualifies it from DBH004.
                DiagnosticSnapshotBuilder.Index(
                    "idx_orders_status",
                    keyParts: [DiagnosticSnapshotBuilder.KeyPart(1, "status")],
                    isValid: false,
                    sizeBytes: UnusedIndexSize,
                    scanCount: 0),
            ],
            statisticsResetAtUtc: new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            usageStatistics: usageStatistics);

    private static InspectionOrchestrator Orchestrator(DatabaseSnapshot snapshot) =>
        new(new FixedSnapshotProvider(snapshot), [.. ApprovedDiagnostics.CreateRegistrations()]);

    [Fact]
    public async Task OneSnapshotWithAllFiveConditions_ProducesTheApprovedResult()
    {
        InspectionResult result = await Orchestrator(UnhealthySnapshot())
            .InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(5, result.Summary.TotalFindings);
        Assert.Equal(2, result.Summary.InfoFindings);
        Assert.Equal(2, result.Summary.WarningFindings);
        Assert.Equal(1, result.Summary.CriticalFindings);
        Assert.Equal(OverallRisk.High, result.OverallRisk);
        Assert.False(result.HasErrors);

        Assert.Equal(5, result.Summary.TotalDiagnostics);
        Assert.Equal(5, result.Summary.CompletedDiagnostics);
        Assert.Equal(0, result.Summary.SkippedDiagnostics);
        Assert.Equal(0, result.Summary.FailedDiagnostics);

        // Exactly one finding per approved code, in the frozen ordinal order.
        Assert.Equal(
            ["DBH001", "DBH002", "DBH003", "DBH004", "DBH005"],
            result.Findings.Select(finding => finding.Code.Value).ToArray());
    }

    [Fact]
    public async Task EachFinding_CarriesTheApprovedSeverityAndObject()
    {
        InspectionResult result = await Orchestrator(UnhealthySnapshot())
            .InspectAsync(TestContext.Current.CancellationToken);

        Finding ByCode(string code) =>
            Assert.Single(result.Findings, finding => finding.Code.Value == code);

        Assert.Equal(FindingSeverity.Warning, ByCode("DBH001").Severity);
        Assert.Equal("audit_log", ByCode("DBH001").ObjectReference.ObjectName);

        Assert.Equal(FindingSeverity.Info, ByCode("DBH002").Severity);
        Assert.Equal("orders", ByCode("DBH002").ObjectReference.ObjectName);

        Assert.Equal(FindingSeverity.Warning, ByCode("DBH003").Severity);
        Assert.Equal("idx_orders_customer_a", ByCode("DBH003").ObjectReference.ObjectName);

        Assert.Equal(FindingSeverity.Info, ByCode("DBH004").Severity);
        Assert.Equal("idx_orders_region", ByCode("DBH004").ObjectReference.ObjectName);

        Assert.Equal(FindingSeverity.Critical, ByCode("DBH005").Severity);
        Assert.Equal("idx_orders_status", ByCode("DBH005").ObjectReference.ObjectName);
    }

    [Fact]
    public async Task WhenUsageStatisticsIsUnavailable_OnlyDbh004IsSkipped()
    {
        InspectionResult result = await Orchestrator(UnhealthySnapshot(CapabilityStatus.Unavailable))
            .InspectAsync(TestContext.Current.CancellationToken);

        DiagnosticExecution skipped = Assert.Single(
            result.DiagnosticExecutions,
            execution => execution.Status == DiagnosticExecutionStatus.SkippedUnavailableCapability);

        Assert.Equal("DBH004", skipped.Code.Value);
        Assert.Equal(CapabilityKind.UsageStatistics, Assert.Single(skipped.UnavailableCapabilities));

        Assert.Equal(1, result.Summary.SkippedDiagnostics);
        Assert.Equal(4, result.Summary.CompletedDiagnostics);
        Assert.Equal(4, result.Summary.TotalFindings);
        Assert.DoesNotContain(result.Findings, finding => finding.Code.Value == "DBH004");

        // Losing the optional statistic must not change the other four verdicts.
        Assert.Equal(OverallRisk.High, result.OverallRisk);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public async Task HealthySnapshot_ProducesNoFindingsAndIsNotAnError()
    {
        DatabaseSnapshot healthy = DiagnosticSnapshotBuilder.Snapshot(
            tables: [DiagnosticSnapshotBuilder.Table("orders", hasPrimaryKey: true)],
            indexes: [DiagnosticSnapshotBuilder.Index("idx_orders_customer")]);

        InspectionResult result = await Orchestrator(healthy)
            .InspectAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.Findings);
        Assert.Equal(0, result.Summary.TotalFindings);
        Assert.Equal(OverallRisk.None, result.OverallRisk);
        Assert.False(result.HasErrors);
        Assert.Equal(5, result.Summary.CompletedDiagnostics);
    }

    [Fact]
    public async Task EmptySnapshot_ProducesNoFindingsAndNoRuleFailure()
    {
        InspectionResult result = await Orchestrator(DiagnosticSnapshotBuilder.Snapshot())
            .InspectAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.Findings);
        Assert.Equal(OverallRisk.None, result.OverallRisk);
        Assert.Equal(0, result.Summary.FailedDiagnostics);
        Assert.Equal(5, result.Summary.CompletedDiagnostics);
    }

    [Fact]
    public async Task Result_IsDeterministicAcrossRepeatedInspections()
    {
        DatabaseSnapshot snapshot = UnhealthySnapshot();

        InspectionResult first = await Orchestrator(snapshot)
            .InspectAsync(TestContext.Current.CancellationToken);
        InspectionResult second = await Orchestrator(snapshot)
            .InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            first.Findings.Select(finding => finding.Fingerprint.Value),
            second.Findings.Select(finding => finding.Fingerprint.Value));
    }

    [Fact]
    public void Registrations_DeclareUsageStatisticsForDbh004Only()
    {
        IReadOnlyList<InspectionRuleRegistration> registrations =
            ApprovedDiagnostics.CreateRegistrations();

        Assert.Equal(5, registrations.Count);
        foreach (InspectionRuleRegistration registration in registrations)
        {
            if (registration.Rule.Code.Value == "DBH004")
            {
                Assert.Equal(
                    CapabilityKind.UsageStatistics,
                    Assert.Single(registration.RequiredCapabilities));
            }
            else
            {
                Assert.Empty(registration.RequiredCapabilities);
            }
        }
    }

    [Fact]
    public void Registrations_CoverExactlyTheApprovedCatalog()
    {
        string[] codes =
        [
            .. ApprovedDiagnostics.CreateRegistrations()
                .Select(registration => registration.Rule.Code.Value)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal(["DBH001", "DBH002", "DBH003", "DBH004", "DBH005"], codes);
    }
}
