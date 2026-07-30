using DbHealthInspector.Core.Inspections;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class InspectionOrchestratorCancellationTests
{
    private static InspectionRuleRegistration Registration(FakeInspectionRule rule) => new(rule, []);

    [Fact]
    public async Task InspectAsync_ThrowsWhenTheTokenIsAlreadyCanceledBeforeStarting()
    {
        FakeSnapshotProvider provider = FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot());
        var orchestrator = new InspectionOrchestrator(provider, []);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.InspectAsync(cts.Token));

        Assert.Equal(0, provider.CallCount);
    }

    [Fact]
    public async Task InspectAsync_PropagatesCancellationDuringSnapshotCapture()
    {
        FakeSnapshotProvider provider = FakeSnapshotProvider.Canceling();
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(provider, [Registration(rule)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.InspectAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, rule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_ThrowsWhenCanceledAfterSnapshotAndBeforeRules()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        FakeSnapshotProvider provider = FakeSnapshotProvider.CancelingSourceThenReturning(cts, SampleSnapshots.Snapshot());
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var orchestrator = new InspectionOrchestrator(provider, [Registration(rule)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.InspectAsync(cts.Token));

        Assert.Equal(0, rule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_ThrowsWhenCanceledBetweenTwoRules()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var firstRule = new FakeInspectionRule("DBH900", _ =>
        {
            cts.Cancel();
            return [];
        });
        FakeInspectionRule secondRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [Registration(firstRule), Registration(secondRule)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.InspectAsync(cts.Token));

        Assert.Equal(1, firstRule.EvaluateCallCount);
        Assert.Equal(0, secondRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_DoesNotRunRulesAfterAMidRunCancellation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var firstRule = new FakeInspectionRule("DBH900", _ =>
        {
            cts.Cancel();
            return [];
        });
        FakeInspectionRule secondRule = FakeInspectionRule.NoFindings("DBH901");
        FakeInspectionRule thirdRule = FakeInspectionRule.NoFindings("DBH902");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [Registration(firstRule), Registration(secondRule), Registration(thirdRule)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.InspectAsync(cts.Token));

        Assert.Equal(0, secondRule.EvaluateCallCount);
        Assert.Equal(0, thirdRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_ReturnsNoPartialResultWhenCanceled()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var firstRule = new FakeInspectionRule("DBH900", _ =>
        {
            cts.Cancel();
            return [];
        });
        FakeInspectionRule secondRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()),
            [Registration(firstRule), Registration(secondRule)]);

        Task<InspectionResult> inspectTask = orchestrator.InspectAsync(cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspectTask);
        Assert.True(inspectTask.IsCanceled || inspectTask.IsFaulted);
    }

    // --- OCE association matrix (C1) -----------------------------------------------------

    [Fact]
    public async Task InspectAsync_PropagatesWhenTheRequestedTokenIsCanceledEvenIfTheOceIsUnrelated()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var rule = new FakeInspectionRule("DBH900", _ =>
        {
            cts.Cancel();
            throw new OperationCanceledException(CancellationToken.None);
        });
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule), Registration(laterRule)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.InspectAsync(cts.Token));

        Assert.Equal(0, laterRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_PropagatesAnOceCarryingTheRequestedTokenEvenBeforeItIsCanceled()
    {
        // The requested token is cancelable but not yet canceled; the exception's own token is
        // exactly the requested token, which alone establishes association (IsRequestedCancellation
        // condition 2), independent of IsCancellationRequested.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var rule = new FakeInspectionRule("DBH900", _ => throw new OperationCanceledException(cts.Token));
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule), Registration(laterRule)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.InspectAsync(cts.Token));

        Assert.Equal(0, laterRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_TreatsAnOceCarryingCancellationTokenNoneAsARecoverableFailure()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var rule = new FakeInspectionRule("DBH900", _ => throw new OperationCanceledException(CancellationToken.None));
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule), Registration(laterRule)]);

        InspectionResult result = await orchestrator.InspectAsync(cts.Token);

        DiagnosticExecution execution = result.DiagnosticExecutions[0];
        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.UnhandledRuleException, execution.Failure!.Kind);
        Assert.Equal("The diagnostic rule failed during evaluation.", execution.Failure.Message);
        Assert.Equal(0, execution.FindingCount);
        Assert.True(result.HasErrors);
        Assert.Equal(1, laterRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_TreatsAnOceCarryingAnUnrelatedNonCanceledTokenAsARecoverableFailure()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var unrelatedSource = new CancellationTokenSource();
        var rule = new FakeInspectionRule("DBH900", _ => throw new OperationCanceledException(unrelatedSource.Token));
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule), Registration(laterRule)]);

        InspectionResult result = await orchestrator.InspectAsync(cts.Token);

        DiagnosticExecution execution = result.DiagnosticExecutions[0];
        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.UnhandledRuleException, execution.Failure!.Kind);
        Assert.Equal("The diagnostic rule failed during evaluation.", execution.Failure.Message);
        Assert.Equal(0, execution.FindingCount);
        Assert.True(result.HasErrors);
        Assert.Equal(1, laterRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_TreatsAnOceCarryingAnUnrelatedAlreadyCanceledTokenAsARecoverableFailure()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var unrelatedSource = new CancellationTokenSource();
        unrelatedSource.Cancel();
        var rule = new FakeInspectionRule("DBH900", _ => throw new OperationCanceledException(unrelatedSource.Token));
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule), Registration(laterRule)]);

        InspectionResult result = await orchestrator.InspectAsync(cts.Token);

        DiagnosticExecution execution = result.DiagnosticExecutions[0];
        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(DiagnosticFailureKind.UnhandledRuleException, execution.Failure!.Kind);
        Assert.Equal("The diagnostic rule failed during evaluation.", execution.Failure.Message);
        Assert.Equal(0, execution.FindingCount);
        Assert.True(result.HasErrors);
        Assert.Equal(1, laterRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_AnUnrelatedOceDoesNotChangeOverallRiskAndSetsHasErrors()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var rule = new FakeInspectionRule("DBH900", _ => throw new OperationCanceledException());
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(cts.Token);

        Assert.Equal(OverallRisk.None, result.OverallRisk);
        Assert.True(result.HasErrors);
        Assert.Equal(0, result.DiagnosticExecutions[0].FindingCount);
    }

    [Fact]
    public async Task InspectAsync_AnUnrelatedOceDoesNotRetainItsMessageOrToken()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        using var unrelatedSource = new CancellationTokenSource();
        const string sensitiveMessage = "Operation canceled while holding a database connection to prod-db.";
        var rule = new FakeInspectionRule(
            "DBH900", _ => throw new OperationCanceledException(sensitiveMessage, unrelatedSource.Token));
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule)]);

        InspectionResult result = await orchestrator.InspectAsync(cts.Token);

        string failureMessage = result.DiagnosticExecutions[0].Failure!.Message;
        Assert.DoesNotContain("prod-db", failureMessage);
        Assert.Equal("The diagnostic rule failed during evaluation.", failureMessage);
    }

    // --- Cancellation precedence over an ordinary exception (C2) -------------------------

    [Fact]
    public async Task InspectAsync_PrioritizesCancellationOverAnOrdinaryExceptionFromTheSameRule()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        var rule = new FakeInspectionRule("DBH900", _ =>
        {
            cts.Cancel();
            throw new InvalidOperationException("boom");
        });
        FakeInspectionRule laterRule = FakeInspectionRule.NoFindings("DBH901");
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(rule), Registration(laterRule)]);

        Task<InspectionResult> inspectTask = orchestrator.InspectAsync(cts.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inspectTask);
        Assert.Equal(0, laterRule.EvaluateCallCount);
    }

    [Fact]
    public async Task InspectAsync_PrioritizesCancellationOverAnOrdinaryExceptionFromTheLastRule()
    {
        // Regression guard: the priority check must not rely on there being a "next" rule to
        // observe cancellation before — it must trigger even when this is the last registration.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        FakeInspectionRule earlierRule = FakeInspectionRule.NoFindings("DBH899");
        var lastRule = new FakeInspectionRule("DBH900", _ =>
        {
            cts.Cancel();
            throw new InvalidOperationException("boom");
        });
        var orchestrator = new InspectionOrchestrator(
            FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot()), [Registration(earlierRule), Registration(lastRule)]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => orchestrator.InspectAsync(cts.Token));
    }
}
