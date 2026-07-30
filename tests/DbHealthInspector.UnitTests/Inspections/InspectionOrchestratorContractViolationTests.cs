using DbHealthInspector.Core;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class InspectionOrchestratorContractViolationTests
{
    private static InspectionRuleRegistration Registration(FakeInspectionRule rule) => new(rule, []);

    private static async Task<DiagnosticExecution> RunSingleRuleAsync(FakeInspectionRule rule)
    {
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);
        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);
        return Assert.Single(result.DiagnosticExecutions);
    }

    [Fact]
    public async Task InspectAsync_TreatsANullEvaluateResultAsAContractViolation()
    {
        var rule = new FakeInspectionRule("DBH900", _ => null);

        DiagnosticExecution execution = await RunSingleRuleAsync(rule);

        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.RuleContractViolation, execution.Failure!.Kind);
    }

    [Fact]
    public async Task InspectAsync_TreatsANullFindingElementAsAContractViolation()
    {
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule("DBH900", _ => [InspectionFindingBuilder.For(rule), null!]);

        DiagnosticExecution execution = await RunSingleRuleAsync(rule);

        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.RuleContractViolation, execution.Failure!.Kind);
    }

    [Fact]
    public async Task InspectAsync_TreatsAMismatchedFindingCodeAsAContractViolation()
    {
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule(
            "DBH900", _ => [InspectionFindingBuilder.For(rule, code: new FindingCode("DBH901"))]);

        DiagnosticExecution execution = await RunSingleRuleAsync(rule);

        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.RuleContractViolation, execution.Failure!.Kind);
    }

    [Fact]
    public async Task InspectAsync_TreatsAMismatchedRuleVersionAsAContractViolation()
    {
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule(
            "DBH900", _ => [InspectionFindingBuilder.For(rule, ruleVersion: new RuleVersion(2))]);

        DiagnosticExecution execution = await RunSingleRuleAsync(rule);

        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.RuleContractViolation, execution.Failure!.Kind);
    }

    [Fact]
    public async Task InspectAsync_TreatsAMismatchedCategoryAsAContractViolation()
    {
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule(
            "DBH900",
            _ => [InspectionFindingBuilder.For(rule, category: FindingCategory.Capacity)],
            category: FindingCategory.Structure);

        DiagnosticExecution execution = await RunSingleRuleAsync(rule);

        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.RuleContractViolation, execution.Failure!.Kind);
    }

    [Fact]
    public async Task InspectAsync_TreatsAMismatchedEngineAsAContractViolation()
    {
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule(
            "DBH900", _ => [InspectionFindingBuilder.For(rule, engine: new DatabaseEngine("SqlServer"))]);

        DiagnosticExecution execution = await RunSingleRuleAsync(rule);

        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.RuleContractViolation, execution.Failure!.Kind);
    }

    [Fact]
    public async Task InspectAsync_TreatsADuplicateFingerprintWithinARuleAsAContractViolation()
    {
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule(
            "DBH900",
            _ =>
            [
                InspectionFindingBuilder.For(rule, objectName: "orders"),
                InspectionFindingBuilder.For(rule, objectName: "orders"),
            ]);

        DiagnosticExecution execution = await RunSingleRuleAsync(rule);

        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.RuleContractViolation, execution.Failure!.Kind);
    }

    [Fact]
    public async Task InspectAsync_DiscardsAllFindingsFromAnInvalidRule()
    {
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule(
            "DBH900",
            _ =>
            [
                InspectionFindingBuilder.For(rule, objectName: "orders"),
                InspectionFindingBuilder.For(rule, objectName: "orders"),
            ]);

        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);
        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task InspectAsync_OtherRulesContinueAfterAContractViolation()
    {
        FakeInspectionRule invalidRule = new("DBH900", _ => null);
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");

        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [Registration(invalidRule), Registration(laterRule)]);
        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, laterRule.EvaluateCallCount);
        Assert.Equal(2, result.DiagnosticExecutions.Count);
    }

    [Fact]
    public void TryValidateRuleOutput_TreatsAGloballyDuplicateFingerprintAsInvalid()
    {
        // A genuine cross-rule fingerprint collision cannot be produced through the public API:
        // Finding.Fingerprint always embeds Finding.Code, and InspectionOrchestrator's
        // constructor already rejects two registrations sharing a code, so two different rules'
        // findings can never legitimately collide. This drives the internal validation method
        // directly with a pre-populated "already seen" set to exercise that branch, mirroring
        // how FindingFingerprintGenerator.EncodeCanonicalField is tested directly for a
        // similarly unreachable-through-the-public-API scenario.
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule("DBH900", _ => []);
        Finding finding = InspectionFindingBuilder.For(rule, objectName: "orders");
        var globallySeen = new HashSet<string>(StringComparer.Ordinal) { finding.Fingerprint.Value };

        bool isValid = InspectionOrchestrator.TryValidateRuleOutput(
            rule, SampleSnapshots.Snapshot(), [finding], globallySeen, out _);

        Assert.False(isValid);
    }

    [Fact]
    public void TryValidateRuleOutput_AcceptsAFindingNotYetGloballySeen()
    {
        FakeInspectionRule rule = null!;
        rule = new FakeInspectionRule("DBH900", _ => []);
        Finding finding = InspectionFindingBuilder.For(rule, objectName: "orders");
        var globallySeen = new HashSet<string>(StringComparer.Ordinal);

        bool isValid = InspectionOrchestrator.TryValidateRuleOutput(
            rule, SampleSnapshots.Snapshot(), [finding], globallySeen, out IReadOnlyList<Finding> validated);

        Assert.True(isValid);
        Assert.Single(validated);
    }
}
