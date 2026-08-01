using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sessions.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Sessions;

/// <summary>
/// Cancellation association applied at every stage (GC-DHI-04B-C1, F-08). An
/// <see cref="OperationCanceledException"/> genuinely associated with the requested token always
/// propagates unchanged; an unrelated one is sanitized according to the stage that raised it.
/// </summary>
public sealed class PostgreSqlInspectionSessionCancellationMatrixTests
{
    private static PostgreSqlInspectionSessionOptions Options() => PostgreSqlInspectionSessionOptions.Default;

    /// <summary>The five stages that can raise an <see cref="OperationCanceledException"/>.</summary>
    public static TheoryData<string, string> StagesAndExpectedKinds() => new()
    {
        { "Begin", nameof(PostgreSqlInspectionSessionFailureKind.InitializationFailed) },
        { "B001", nameof(PostgreSqlInspectionSessionFailureKind.InitializationFailed) },
        { "B002", nameof(PostgreSqlInspectionSessionFailureKind.InitializationFailed) },
        { "B003", nameof(PostgreSqlInspectionSessionFailureKind.VerificationFailed) },
        { "Callback", nameof(PostgreSqlInspectionSessionFailureKind.ExecutionFailed) },
    };

    public static TheoryData<string> Stages() =>
    [
        "Begin", "B001", "B002", "B003", "Callback",
    ];

    private static FakeInspectionSessionScope ScopeFailingAt(string stage, Exception failure)
    {
        var gateway = ScriptedStatementGateway.HealthySession();
        var scope = new FakeInspectionSessionScope(gateway);

        switch (stage)
        {
            case "Begin":
                scope.FailingAt(SessionScopeStep.BeginTransaction, failure);
                break;
            case "B001":
                gateway.FailingAt(PostgreSqlSqlStatementId.SetTransactionReadOnly, failure);
                break;
            case "B002":
                gateway.FailingAt(PostgreSqlSqlStatementId.ApplyLocalTimeouts, failure);
                break;
            case "B003":
                gateway.FailingAt(PostgreSqlSqlStatementId.VerifySessionState, failure);
                break;
            case "Callback":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown stage.");
        }

        return scope;
    }

    private static ValueTask<int> RunAsync(FakeInspectionSessionScope scope, string stage, Exception? callbackFailure, CancellationToken token) =>
        new PostgreSqlInspectionSessionRunner(scope).RunAsync<int>(
            Options(),
            (_, _) => stage == "Callback" && callbackFailure is not null
                ? throw callbackFailure
                : ValueTask.FromResult(1),
            token);

    // --- Associated cancellation always propagates -----------------------------------------------

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task AssociatedOce_CarryingTheSameToken_PropagatesUnchanged(string stage)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var associated = new OperationCanceledException(cts.Token);
        FakeInspectionSessionScope scope = ScopeFailingAt(stage, associated);

        Exception? thrown = await Record.ExceptionAsync(() => RunAsync(scope, stage, associated, cts.Token).AsTask());

        Assert.Same(associated, thrown);
        Assert.True(scope.AllCleanupStepsAttempted);
    }

    [Theory]
    [MemberData(nameof(Stages))]
    public async Task CallerCancellationRacingAnNpgsqlFailure_LetsCancellationWin(string stage)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var gateway = ScriptedStatementGateway.HealthySession();
        var scope = new FakeInspectionSessionScope(gateway);
        var failure = new NpgsqlException("synthetic");

        switch (stage)
        {
            case "Begin":
                // Cancel from inside the Begin step, so the runner has genuinely reached that
                // stage and its cleanup still runs — rather than short-circuiting before entry.
                scope.BeforeStep(SessionScopeStep.BeginTransaction, cts.Cancel)
                    .FailingAt(SessionScopeStep.BeginTransaction, failure);
                break;
            case "B001":
                gateway.BeforeStatement(PostgreSqlSqlStatementId.SetTransactionReadOnly, cts.Cancel)
                    .FailingAt(PostgreSqlSqlStatementId.SetTransactionReadOnly, failure);
                break;
            case "B002":
                gateway.BeforeStatement(PostgreSqlSqlStatementId.ApplyLocalTimeouts, cts.Cancel)
                    .FailingAt(PostgreSqlSqlStatementId.ApplyLocalTimeouts, failure);
                break;
            case "B003":
                gateway.BeforeStatement(PostgreSqlSqlStatementId.VerifySessionState, cts.Cancel)
                    .FailingAt(PostgreSqlSqlStatementId.VerifySessionState, failure);
                break;
            case "Callback":
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown stage.");
        }

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new PostgreSqlInspectionSessionRunner(scope).RunAsync<int>(
                Options(),
                (_, _) =>
                {
                    if (stage != "Callback")
                    {
                        return ValueTask.FromResult(1);
                    }

                    cts.Cancel();
                    throw failure;
                },
                cts.Token).AsTask());

        Assert.True(scope.AllCleanupStepsAttempted);
    }

    // --- Unrelated cancellation is sanitized by stage ----------------------------------------------

    [Theory]
    [MemberData(nameof(StagesAndExpectedKinds))]
    public async Task UnrelatedOce_CarryingCancellationTokenNone_IsSanitizedForItsStage(string stage, string expectedKindName)
    {
        var expected = Enum.Parse<PostgreSqlInspectionSessionFailureKind>(expectedKindName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var unrelated = new OperationCanceledException(CancellationToken.None);
        FakeInspectionSessionScope scope = ScopeFailingAt(stage, unrelated);

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => RunAsync(scope, stage, unrelated, cts.Token).AsTask());

        Assert.Equal(expected, exception.FailureKind);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    [Theory]
    [MemberData(nameof(StagesAndExpectedKinds))]
    public async Task UnrelatedOce_CarryingAnActiveForeignToken_IsSanitizedForItsStage(string stage, string expectedKindName)
    {
        var expected = Enum.Parse<PostgreSqlInspectionSessionFailureKind>(expectedKindName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var foreign = new CancellationTokenSource();
        var unrelated = new OperationCanceledException(foreign.Token);
        FakeInspectionSessionScope scope = ScopeFailingAt(stage, unrelated);

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => RunAsync(scope, stage, unrelated, cts.Token).AsTask());

        Assert.Equal(expected, exception.FailureKind);
    }

    [Theory]
    [MemberData(nameof(StagesAndExpectedKinds))]
    public async Task UnrelatedOce_CarryingACanceledForeignToken_IsSanitizedForItsStage(string stage, string expectedKindName)
    {
        var expected = Enum.Parse<PostgreSqlInspectionSessionFailureKind>(expectedKindName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var foreign = new CancellationTokenSource();
        foreign.Cancel();
        var unrelated = new OperationCanceledException(foreign.Token);
        FakeInspectionSessionScope scope = ScopeFailingAt(stage, unrelated);

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => RunAsync(scope, stage, unrelated, cts.Token).AsTask());

        Assert.Equal(expected, exception.FailureKind);
    }

    [Theory]
    [MemberData(nameof(StagesAndExpectedKinds))]
    public async Task BothTokensAreNone_IsNotAssociationAndIsSanitized(string stage, string expectedKindName)
    {
        // CancellationToken.None equals CancellationToken.None structurally, but neither is
        // cancelable, so this must not count as association.
        var expected = Enum.Parse<PostgreSqlInspectionSessionFailureKind>(expectedKindName);
        var unrelated = new OperationCanceledException(CancellationToken.None);
        FakeInspectionSessionScope scope = ScopeFailingAt(stage, unrelated);

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => RunAsync(scope, stage, unrelated, CancellationToken.None).AsTask());

        Assert.Equal(expected, exception.FailureKind);
    }

    // --- Exact token reaches every stage ------------------------------------------------------------

    [Fact]
    public async Task TheExactRequestedTokenReachesEveryStage()
    {
        // A genuinely cancelable token, not CancellationToken.None, so equality is meaningful
        // (GC-DHI-04B-C2, R2-03).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Assert.True(cts.Token.CanBeCanceled);

        var scope = new FakeInspectionSessionScope();
        CancellationToken callbackToken = default;

        await new PostgreSqlInspectionSessionRunner(scope).RunAsync(
            Options(),
            (_, token) =>
            {
                callbackToken = token;
                return ValueTask.FromResult(1);
            },
            cts.Token);

        Assert.Equal(cts.Token, scope.OpenConnectionCancellationToken);
        Assert.Equal(cts.Token, scope.BeginTransactionCancellationToken);

        // B001, B002 and B003 all received the caller's exact token.
        Assert.Equal(3, scope.Gateway.Tokens.Count);
        Assert.All(scope.Gateway.Tokens, token => Assert.Equal(cts.Token, token));
        Assert.Equal(cts.Token, callbackToken);
    }

    [Fact]
    public async Task RollbackDeliberatelyUsesCancellationTokenNone()
    {
        // Rollback is the one operation that must still be attempted when the caller's token is
        // already canceled, so it is issued with CancellationToken.None on purpose. The scope
        // interface encodes this: RollbackAsync takes no token at all.
        System.Reflection.MethodInfo rollback = typeof(IPostgreSqlInspectionSessionScope)
            .GetMethod(nameof(IPostgreSqlInspectionSessionScope.RollbackAsync))!;

        Assert.Empty(rollback.GetParameters());

        // And the rollback still happens after the caller cancels.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scope = new FakeInspectionSessionScope();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new PostgreSqlInspectionSessionRunner(scope).RunAsync<int>(
                Options(),
                (_, token) =>
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(1);
                },
                cts.Token).AsTask());

        Assert.Contains(SessionScopeStep.Rollback, scope.Steps);
    }

    [Fact]
    public void AssociationRule_IsTheOneFrozenByGcDhi04A()
    {
        using var cts = new CancellationTokenSource();
        using var foreign = new CancellationTokenSource();

        // Associated → not sanitized (propagates).
        Assert.False(PostgreSqlInspectionSessionRunner.IsUnrelatedCancellation(new OperationCanceledException(cts.Token), cts.Token));

        // Unrelated → sanitized.
        Assert.True(PostgreSqlInspectionSessionRunner.IsUnrelatedCancellation(new OperationCanceledException(CancellationToken.None), cts.Token));
        Assert.True(PostgreSqlInspectionSessionRunner.IsUnrelatedCancellation(new OperationCanceledException(foreign.Token), cts.Token));

        // None vs None is never association.
        Assert.True(PostgreSqlInspectionSessionRunner.IsUnrelatedCancellation(new OperationCanceledException(CancellationToken.None), CancellationToken.None));

        // Once the requested token is canceled, any OCE counts as that cancellation.
        cts.Cancel();
        Assert.False(PostgreSqlInspectionSessionRunner.IsUnrelatedCancellation(new OperationCanceledException(CancellationToken.None), cts.Token));
    }
}
