using DbHealthInspector.Core.Inspections;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class InspectionOrchestratorSnapshotProviderTests
{
    [Fact]
    public async Task InspectAsync_InvokesTheProviderExactlyOnce()
    {
        FakeSnapshotProvider provider = FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot());
        var orchestrator = new InspectionOrchestrator(provider, []);

        await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, provider.CallCount);
    }

    [Fact]
    public async Task InspectAsync_PassesTheSameCancellationTokenToTheProvider()
    {
        FakeSnapshotProvider provider = FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot());
        var orchestrator = new InspectionOrchestrator(provider, []);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        await orchestrator.InspectAsync(cts.Token);

        Assert.Equal(cts.Token, provider.LastReceivedToken);
    }

    [Fact]
    public async Task InspectAsync_PassesTheCapturedSnapshotToEveryRule()
    {
        var expectedSnapshot = SampleSnapshots.Snapshot();
        FakeSnapshotProvider provider = FakeSnapshotProvider.Returning(expectedSnapshot);

        DbHealthInspector.Core.Snapshots.DatabaseSnapshot? receivedByRuleA = null;
        DbHealthInspector.Core.Snapshots.DatabaseSnapshot? receivedByRuleB = null;

        var ruleA = new FakeInspectionRule("DBH900", snapshot =>
        {
            receivedByRuleA = snapshot;
            return [];
        });
        var ruleB = new FakeInspectionRule("DBH901", snapshot =>
        {
            receivedByRuleB = snapshot;
            return [];
        });

        var orchestrator = new InspectionOrchestrator(
            provider, [new InspectionRuleRegistration(ruleA, []), new InspectionRuleRegistration(ruleB, [])]);

        InspectionResult result = await orchestrator.InspectAsync(TestContext.Current.CancellationToken);

        Assert.Same(expectedSnapshot, result.Snapshot);
        Assert.Same(expectedSnapshot, receivedByRuleA);
        Assert.Same(expectedSnapshot, receivedByRuleB);
    }

    [Fact]
    public async Task InspectAsync_ThrowsInvalidOperationExceptionForANullSnapshot()
    {
        FakeSnapshotProvider provider = FakeSnapshotProvider.ReturningNull();
        var orchestrator = new InspectionOrchestrator(provider, []);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.InspectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InspectAsync_PropagatesTheProvidersException()
    {
        var exception = new InvalidOperationException("Simulated provider failure.");
        FakeSnapshotProvider provider = FakeSnapshotProvider.Throwing(exception);
        var orchestrator = new InspectionOrchestrator(provider, []);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.InspectAsync(TestContext.Current.CancellationToken));
        Assert.Same(exception, thrown);
    }

    [Fact]
    public async Task InspectAsync_PropagatesTheProvidersCancellation()
    {
        FakeSnapshotProvider provider = FakeSnapshotProvider.Canceling();
        var orchestrator = new InspectionOrchestrator(provider, []);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => orchestrator.InspectAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task InspectAsync_RunsNoRuleWhenTheProviderFails()
    {
        FakeSnapshotProvider provider = FakeSnapshotProvider.Throwing(new InvalidOperationException("Simulated failure."));
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(provider, [new InspectionRuleRegistration(rule, [])]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => orchestrator.InspectAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, rule.EvaluateCallCount);
    }
}
