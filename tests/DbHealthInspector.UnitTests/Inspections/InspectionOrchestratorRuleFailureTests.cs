using DbHealthInspector.Core.Inspections;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class InspectionOrchestratorRuleFailureTests
{
    private static InspectionRuleRegistration Registration(FakeInspectionRule rule) => new(rule, []);

    [Fact]
    public async Task InspectAsync_MarksARuleThatThrowsAsFailed()
    {
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", new InvalidOperationException("boom"));
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        DiagnosticExecution execution = Assert.Single(result.DiagnosticExecutions);
        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.NotNull(execution.Failure);
        Assert.Equal(DiagnosticFailureKind.UnhandledRuleException, execution.Failure.Kind);
    }

    [Fact]
    public async Task InspectAsync_ContinuesWithTheNextRuleAfterAFailure()
    {
        FakeInspectionRule failingRule = FakeInspectionRule.Throwing("DBH900", new InvalidOperationException("boom"));
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(failingRule), Registration(laterRule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, laterRule.EvaluateCallCount);
        Assert.Equal(2, result.DiagnosticExecutions.Count);
    }

    [Fact]
    public async Task InspectAsync_PreservesFindingsFromEarlierSuccessfulRules()
    {
        FakeInspectionRule earlierRule = null!;
        earlierRule = new FakeInspectionRule("DBH899", _ => [InspectionFindingBuilder.For(earlierRule)]);
        FakeInspectionRule failingRule = FakeInspectionRule.Throwing("DBH900", new InvalidOperationException("boom"));

        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(earlierRule), Registration(failingRule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Single(result.Findings);
        Assert.Equal("DBH899", result.Findings[0].Code.Value);
    }

    [Fact]
    public async Task InspectAsync_DoesNotStoreTheOriginalExceptionMessage()
    {
        const string sensitiveMessage = "Connection string: Host=prod-db;Password=hunter2";
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", new InvalidOperationException(sensitiveMessage));
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        DiagnosticExecutionFailure failure = Assert.Single(result.DiagnosticExecutions).Failure!;
        Assert.DoesNotContain("hunter2", failure.Message);
        Assert.DoesNotContain("Connection string", failure.Message);
    }

    [Fact]
    public async Task InspectAsync_UsesTheGenericUnhandledExceptionMessage()
    {
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", new InvalidOperationException("boom"));
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        DiagnosticExecutionFailure failure = Assert.Single(result.DiagnosticExecutions).Failure!;
        Assert.Equal("The diagnostic rule failed during evaluation.", failure.Message);
    }

    [Fact]
    public async Task InspectAsync_SetsHasErrorsWhenARuleFails()
    {
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", new InvalidOperationException("boom"));
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.True(result.HasErrors);
    }

    [Fact]
    public async Task InspectAsync_ReportsZeroFindingCountForAFailedRule()
    {
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", new InvalidOperationException("boom"));
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, Assert.Single(result.DiagnosticExecutions).FindingCount);
    }

    [Theory]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(ArgumentException))]
    [InlineData(typeof(FormatException))]
    [InlineData(typeof(IndexOutOfRangeException))]
    public async Task InspectAsync_IsolatesAVarietyOfExceptionTypes(Type exceptionType)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType)!;
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", exception);
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DiagnosticExecutionStatus.Failed, Assert.Single(result.DiagnosticExecutions).Status);
    }
}
