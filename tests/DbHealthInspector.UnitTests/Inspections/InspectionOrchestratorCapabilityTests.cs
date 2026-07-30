using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class InspectionOrchestratorCapabilityTests
{
    [Fact]
    public async Task InspectAsync_RunsARuleWhenAllRequiredCapabilitiesAreAvailable()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(
            capabilities: SampleSnapshots.Capabilities(usageStatistics: CapabilityStatus.Available));
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot),
            [new InspectionRuleRegistration(rule, [CapabilityKind.UsageStatistics])]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, rule.EvaluateCallCount);
        Assert.Equal(DiagnosticExecutionStatus.Completed, Assert.Single(result.DiagnosticExecutions).Status);
    }

    [Fact]
    public async Task InspectAsync_SkipsARuleWhenARequiredCapabilityIsUnavailable()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(
            capabilities: SampleSnapshots.Capabilities(usageStatistics: CapabilityStatus.Unavailable));
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot),
            [new InspectionRuleRegistration(rule, [CapabilityKind.UsageStatistics])]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        DiagnosticExecution execution = Assert.Single(result.DiagnosticExecutions);
        Assert.Equal(DiagnosticExecutionStatus.SkippedUnavailableCapability, execution.Status);
        Assert.Equal([CapabilityKind.UsageStatistics], execution.UnavailableCapabilities);
        Assert.Equal(0, execution.FindingCount);
    }

    [Fact]
    public async Task InspectAsync_SkipsARuleWhenARequiredCapabilityIsDisabled()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(
            capabilities: SampleSnapshots.Capabilities(dataProfiling: CapabilityStatus.Disabled));
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot),
            [new InspectionRuleRegistration(rule, [CapabilityKind.DataProfiling])]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            DiagnosticExecutionStatus.SkippedUnavailableCapability, Assert.Single(result.DiagnosticExecutions).Status);
    }

    [Fact]
    public async Task InspectAsync_RecordsEveryUnavailableCapability()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(
            capabilities: SampleSnapshots.Capabilities(
                catalogMetadata: CapabilityStatus.Unavailable,
                usageStatistics: CapabilityStatus.Unavailable));
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot),
            [new InspectionRuleRegistration(rule, [CapabilityKind.CatalogMetadata, CapabilityKind.UsageStatistics])]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        DiagnosticExecution execution = Assert.Single(result.DiagnosticExecutions);
        Assert.Equal(2, execution.UnavailableCapabilities.Count);
        Assert.Contains(CapabilityKind.CatalogMetadata, execution.UnavailableCapabilities);
        Assert.Contains(CapabilityKind.UsageStatistics, execution.UnavailableCapabilities);
    }

    [Fact]
    public async Task InspectAsync_ReportsUnavailableCapabilitiesInCanonicalOrderRegardlessOfRegistrationOrder()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(
            capabilities: SampleSnapshots.Capabilities(
                catalogMetadata: CapabilityStatus.Unavailable,
                usageStatistics: CapabilityStatus.Unavailable));

        FakeInspectionRule ruleA = FakeInspectionRule.NoFindings("DBH900");
        var orchestratorA = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot),
            [new InspectionRuleRegistration(ruleA, [CapabilityKind.UsageStatistics, CapabilityKind.CatalogMetadata])]);

        FakeInspectionRule ruleB = FakeInspectionRule.NoFindings("DBH900");
        var orchestratorB = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot),
            [new InspectionRuleRegistration(ruleB, [CapabilityKind.CatalogMetadata, CapabilityKind.UsageStatistics])]);

        InspectionResult resultA = await orchestratorA.InspectAsync(TestContext.Current.CancellationToken);
        InspectionResult resultB = await orchestratorB.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [CapabilityKind.CatalogMetadata, CapabilityKind.UsageStatistics],
            resultA.DiagnosticExecutions[0].UnavailableCapabilities);
        Assert.Equal(
            resultA.DiagnosticExecutions[0].UnavailableCapabilities,
            resultB.DiagnosticExecutions[0].UnavailableCapabilities);
    }

    [Fact]
    public async Task InspectAsync_RunsARuleWithNoRequiredCapabilities()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [new InspectionRuleRegistration(rule, [])]);

        await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, rule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_DoesNotCallEvaluateForASkippedRule()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(
            capabilities: SampleSnapshots.Capabilities(usageStatistics: CapabilityStatus.Unavailable));
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot),
            [new InspectionRuleRegistration(rule, [CapabilityKind.UsageStatistics])]);

        await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, rule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_ASkippedExecutionDoesNotSetHasErrors()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(
            capabilities: SampleSnapshots.Capabilities(usageStatistics: CapabilityStatus.Unavailable));
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot),
            [new InspectionRuleRegistration(rule, [CapabilityKind.UsageStatistics])]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.False(result.HasErrors);
    }
}
