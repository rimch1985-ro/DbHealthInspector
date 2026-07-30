using DbHealthInspector.Core.Inspections;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

/// <summary>
/// Covers DHI-B-R1-003: <see cref="OutOfMemoryException"/>, <see cref="StackOverflowException"/>
/// and <see cref="AccessViolationException"/> must never be treated as an isolated, recoverable
/// rule failure. Each is tested separately, asserting its exact type, so a change that
/// accidentally narrows or widens the filter is caught precisely. Tests throw these exception
/// types manually; none simulates an actual stack overflow or memory exhaustion.
/// </summary>
// CA2201: these three runtime-reserved types are exactly what IsRecoverableRuleException must
// exclude (DHI-B-R1-003); testing that requires constructing them directly. File-scoped only.
#pragma warning disable CA2201
public sealed class InspectionOrchestratorProcessExceptionTests
{
    private static InspectionRuleRegistration Registration(FakeInspectionRule rule) => new(rule, []);

    [Fact]
    public async Task InspectAsync_PropagatesOutOfMemoryExceptionWithoutRecordingAFailure()
    {
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", new OutOfMemoryException());
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule), Registration(laterRule)]);

        Task<InspectionResult> inspectTask = orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<OutOfMemoryException>(() => inspectTask);
        Assert.Equal(0, laterRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_PropagatesStackOverflowExceptionWithoutRecordingAFailure()
    {
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", new StackOverflowException());
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule), Registration(laterRule)]);

        Task<InspectionResult> inspectTask = orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<StackOverflowException>(() => inspectTask);
        Assert.Equal(0, laterRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_PropagatesAccessViolationExceptionWithoutRecordingAFailure()
    {
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", new AccessViolationException());
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule), Registration(laterRule)]);

        Task<InspectionResult> inspectTask = orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<AccessViolationException>(() => inspectTask);
        Assert.Equal(0, laterRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_ReturnsNoResultWhenAProcessExceptionIsThrown()
    {
        FakeInspectionRule rule = FakeInspectionRule.Throwing("DBH900", new OutOfMemoryException());
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        Task<InspectionResult> inspectTask = orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<OutOfMemoryException>(() => inspectTask);
        Assert.True(inspectTask.IsFaulted);
    }
}
#pragma warning restore CA2201
