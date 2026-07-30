using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class InspectionOrchestratorExecutionTests
{
    private static InspectionRuleRegistration Registration(
        FakeInspectionRule rule, params CapabilityKind[] requiredCapabilities) =>
        new(rule, requiredCapabilities);

    /// <summary>
    /// Creates a rule whose <see cref="DbHealthInspector.Core.Rules.IInspectionRule.Evaluate"/>
    /// returns one finding matching its own code/version/category, tagged with
    /// <paramref name="objectName"/> so multiple such rules never accidentally collide on
    /// fingerprint.
    /// </summary>
    private static FakeInspectionRule OneFindingRule(
        string code, string objectName, FindingSeverity severity = FindingSeverity.Info)
    {
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule(
            code, _ => [InspectionFindingBuilder.For(rule, severity: severity, objectName: objectName)]);
        return rule;
    }

    [Fact]
    public async Task InspectAsync_HandlesARuleThatProducesNoFindings()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        DiagnosticExecution execution = Assert.Single(result.DiagnosticExecutions);
        Assert.Equal(DiagnosticExecutionStatus.Completed, execution.Status);
        Assert.Equal(0, execution.FindingCount);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task InspectAsync_HandlesARuleThatProducesOneFinding()
    {
        FakeInspectionRule rule = OneFindingRule("DBH900", "orders");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        DiagnosticExecution execution = Assert.Single(result.DiagnosticExecutions);
        Assert.Equal(DiagnosticExecutionStatus.Completed, execution.Status);
        Assert.Equal(1, execution.FindingCount);
        Assert.Single(result.Findings);
    }

    [Fact]
    public async Task InspectAsync_HandlesSeveralRules()
    {
        FakeInspectionRule ruleA = OneFindingRule("DBH900", "orders");
        FakeInspectionRule ruleB = FakeInspectionRule.NoFindings("DBH901");
        FakeInspectionRule ruleC = OneFindingRule("DBH902", "customers");

        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [Registration(ruleA), Registration(ruleB), Registration(ruleC)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, result.DiagnosticExecutions.Count);
        Assert.Equal(2, result.Findings.Count);
    }

    [Fact]
    public async Task InspectAsync_CountsFindingsBySeverity()
    {
        FakeInspectionRule ruleInfo = OneFindingRule("DBH900", "a", FindingSeverity.Info);
        FakeInspectionRule ruleWarning = OneFindingRule("DBH901", "b", FindingSeverity.Warning);
        FakeInspectionRule ruleCritical = OneFindingRule("DBH902", "c", FindingSeverity.Critical);

        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [Registration(ruleInfo), Registration(ruleWarning), Registration(ruleCritical)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Summary.InfoFindings);
        Assert.Equal(1, result.Summary.WarningFindings);
        Assert.Equal(1, result.Summary.CriticalFindings);
        Assert.Equal(3, result.Summary.TotalFindings);
        Assert.Equal(OverallRisk.High, result.OverallRisk);
    }

    [Fact]
    public async Task InspectAsync_CountsExecutionsByStatus()
    {
        FakeInspectionRule completedRule = FakeInspectionRule.NoFindings("DBH900");
        FakeInspectionRule skippedRule = FakeInspectionRule.NoFindings("DBH901");
        FakeInspectionRule failedRule = FakeInspectionRule.Throwing("DBH902", new InvalidOperationException("boom"));

        CapabilitySnapshot capabilities =
            SampleSnapshots.Capabilities(usageStatistics: CapabilityStatus.Unavailable);
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot(capabilities: capabilities)),
            [
                Registration(completedRule),
                Registration(skippedRule, CapabilityKind.UsageStatistics),
                Registration(failedRule),
            ]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, result.Summary.CompletedDiagnostics);
        Assert.Equal(1, result.Summary.SkippedDiagnostics);
        Assert.Equal(1, result.Summary.FailedDiagnostics);
        Assert.Equal(3, result.Summary.TotalDiagnostics);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public async Task InspectAsync_ResultCollectionsRejectAllMutation()
    {
        FakeInspectionRule rule = OneFindingRule("DBH900", "orders");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        var executionsList = Assert.IsAssignableFrom<IList<DiagnosticExecution>>(result.DiagnosticExecutions);
        Assert.Throws<NotSupportedException>(() => executionsList.Add(executionsList[0]));

        var findingsList = Assert.IsAssignableFrom<IList<Finding>>(result.Findings);
        Assert.Throws<NotSupportedException>(() => findingsList[0] = findingsList[0]);
    }

    [Fact]
    public async Task InspectAsync_MutatingTheOriginalRegistrationListDoesNotAffectAConstructedOrchestrator()
    {
        FakeInspectionRule ruleA = FakeInspectionRule.NoFindings("DBH900");
        var registrations = new List<InspectionRuleRegistration> { Registration(ruleA) };
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), registrations);

        FakeInspectionRule ruleB = FakeInspectionRule.NoFindings("DBH901");
        registrations.Add(Registration(ruleB));

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Single(result.DiagnosticExecutions);
        Assert.Equal(0, ruleB.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_DifferentRegistrationOrderProducesIdenticalOutput()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot();

        FakeInspectionRule rule900A = OneFindingRule("DBH900", "orders");
        FakeInspectionRule rule901A = OneFindingRule("DBH901", "customers");
        var orchestratorA = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot), [Registration(rule900A), Registration(rule901A)]);

        FakeInspectionRule rule901B = OneFindingRule("DBH901", "customers");
        FakeInspectionRule rule900B = OneFindingRule("DBH900", "orders");
        var orchestratorB = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(snapshot), [Registration(rule901B), Registration(rule900B)]);

        InspectionResult resultA = await orchestratorA.InspectAsync(TestContext.Current.CancellationToken);
        InspectionResult resultB = await orchestratorB.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            resultA.DiagnosticExecutions.Select(e => e.Code.Value),
            resultB.DiagnosticExecutions.Select(e => e.Code.Value));
        Assert.Equal(
            resultA.Findings.Select(f => f.Fingerprint.Value),
            resultB.Findings.Select(f => f.Fingerprint.Value));
    }

    [Fact]
    public async Task InspectAsync_OrdersDiagnosticExecutionsByFindingCodeOrdinal()
    {
        FakeInspectionRule ruleHigh = FakeInspectionRule.NoFindings("DBH902");
        FakeInspectionRule ruleLow = FakeInspectionRule.NoFindings("DBH900");
        FakeInspectionRule ruleMid = FakeInspectionRule.NoFindings("DBH901");

        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [Registration(ruleHigh), Registration(ruleLow), Registration(ruleMid)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            ["DBH900", "DBH901", "DBH902"], result.DiagnosticExecutions.Select(e => e.Code.Value));
    }

    [Fact]
    public async Task InspectAsync_ARuleFailureDoesNotChangeOverallRiskDirectly()
    {
        FakeInspectionRule infoRule = OneFindingRule("DBH900", "orders", FindingSeverity.Info);
        FakeInspectionRule failingRule = FakeInspectionRule.Throwing("DBH901", new InvalidOperationException("boom"));

        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [Registration(infoRule), Registration(failingRule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        // Only Info findings exist; the failed rule contributed none, so risk is Low, not
        // elevated by the failure itself. HasErrors, separately, is true.
        Assert.Equal(OverallRisk.Low, result.OverallRisk);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public async Task InspectAsync_ASkippedRuleDoesNotChangeOverallRiskDirectly()
    {
        CapabilitySnapshot capabilities = SampleSnapshots.Capabilities(usageStatistics: CapabilityStatus.Unavailable);
        FakeInspectionRule infoRule = OneFindingRule("DBH900", "orders", FindingSeverity.Info);
        FakeInspectionRule skippedRule = FakeInspectionRule.NoFindings("DBH901");

        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot(capabilities: capabilities)),
            [Registration(infoRule), new InspectionRuleRegistration(skippedRule, [CapabilityKind.UsageStatistics])]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OverallRisk.Low, result.OverallRisk);
        Assert.False(result.HasErrors);
    }

    [Fact]
    public async Task InspectAsync_NoFindingsButAFailedRuleProducesNoneRiskWithErrors()
    {
        FakeInspectionRule failingRule = FakeInspectionRule.Throwing("DBH900", new InvalidOperationException("boom"));
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(failingRule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(OverallRisk.None, result.OverallRisk);
        Assert.True(result.HasErrors);
    }

    [Fact]
    public async Task InspectAsync_OrdersFindingsByCodeThenFingerprint()
    {
        FakeInspectionRule rule900 = null!;
        rule900 = new FakeInspectionRule("DBH900", _ =>
        [
            InspectionFindingBuilder.For(rule900, objectName: "zzz"),
            InspectionFindingBuilder.For(rule900, objectName: "aaa"),
        ]);
        FakeInspectionRule rule901 = OneFindingRule("DBH901", "orders");

        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [Registration(rule901), Registration(rule900)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Findings.Count);
        // All DBH900 findings sort before the DBH901 finding, and are themselves ordered by
        // fingerprint (ordinal), not by insertion order.
        Assert.All(result.Findings.Take(2), finding => Assert.Equal("DBH900", finding.Code.Value));
        Assert.Equal("DBH901", result.Findings[2].Code.Value);
        Assert.True(
            string.CompareOrdinal(result.Findings[0].Fingerprint.Value, result.Findings[1].Fingerprint.Value) < 0);
    }

    [Fact]
    public async Task InspectAsync_InvokesTheSnapshotProviderOnlyOnceAcrossMultipleRules()
    {
        FakeSnapshotProvider provider = FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot());
        FakeInspectionRule ruleA = FakeInspectionRule.NoFindings("DBH900");
        FakeInspectionRule ruleB = FakeInspectionRule.NoFindings("DBH901");
        FakeInspectionRule ruleC = FakeInspectionRule.NoFindings("DBH902");

        var orchestrator = new InspectionOrchestrator(
            provider, [Registration(ruleA), Registration(ruleB), Registration(ruleC)]);

        await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.CallCount);
    }
}
