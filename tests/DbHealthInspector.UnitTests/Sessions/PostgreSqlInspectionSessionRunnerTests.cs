using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sessions.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Sessions;

/// <summary>
/// The invariant session lifecycle (GC-DHI-04B §11–§13): exact sequence, callback only after
/// verification, stage-specific sanitization, cancellation precedence and cleanup precedence.
/// </summary>
public sealed class PostgreSqlInspectionSessionRunnerTests
{
    private static PostgreSqlInspectionSessionOptions Options() => PostgreSqlInspectionSessionOptions.Default;

    private static PostgreSqlInspectionSessionRunner Runner(FakeInspectionSessionScope scope) => new(scope);

    private static Func<PostgreSqlInspectionOperationExecutor, CancellationToken, ValueTask<int>> Returning(int value, Action? onInvoke = null) =>
        (_, _) =>
        {
            onInvoke?.Invoke();
            return ValueTask.FromResult(value);
        };

    // --- Sequence -------------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_DrivesTheExactResourceSequence()
    {
        var scope = new FakeInspectionSessionScope();

        await Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                SessionScopeStep.OpenConnection,
                SessionScopeStep.BeginTransaction,
                SessionScopeStep.CreateExecutor,
                SessionScopeStep.Rollback,
                SessionScopeStep.DisposeTransaction,
                SessionScopeStep.DisposeConnection,
            ],
            scope.Steps);
    }

    [Fact]
    public async Task RunAsync_ExecutesB001ThenB002ThenB003InOrder()
    {
        var scope = new FakeInspectionSessionScope();

        await Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
            ],
            scope.Gateway.ExecutedIds);
    }

    [Fact]
    public async Task RunAsync_MakesB001TheFirstStatementInTheTransaction()
    {
        var scope = new FakeInspectionSessionScope();

        await Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken);

        Assert.Equal(PostgreSqlSqlStatementId.SetTransactionReadOnly, scope.Gateway.ExecutedIds[0]);
    }

    [Fact]
    public async Task RunAsync_DisposesTransactionBeforeConnection()
    {
        var scope = new FakeInspectionSessionScope();

        await Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken);

        Assert.True(scope.Steps.IndexOf(SessionScopeStep.DisposeTransaction) < scope.Steps.IndexOf(SessionScopeStep.DisposeConnection));
    }

    [Fact]
    public async Task RunAsync_PreservesTheOperationResult()
    {
        var scope = new FakeInspectionSessionScope();

        int result = await Runner(scope).RunAsync(Options(), Returning(4242), TestContext.Current.CancellationToken);

        Assert.Equal(4242, result);
    }

    [Fact]
    public async Task RunAsync_InvokesTheOperationExactlyOnce()
    {
        var scope = new FakeInspectionSessionScope();
        var invocations = 0;

        await Runner(scope).RunAsync(Options(), Returning(1, () => invocations++), TestContext.Current.CancellationToken);

        Assert.Equal(1, invocations);
    }

    [Fact]
    public async Task RunAsync_ForwardsTheExactTokenToEveryStatement()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scope = new FakeInspectionSessionScope();

        await Runner(scope).RunAsync(Options(), Returning(1), cts.Token);

        Assert.All(scope.Gateway.Tokens, token => Assert.Equal(cts.Token, token));
    }

    [Fact]
    public async Task RunAsync_ForwardsTheExactTokenToTheOperation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scope = new FakeInspectionSessionScope();
        CancellationToken observed = default;

        await Runner(scope).RunAsync(
            Options(),
            (_, token) =>
            {
                observed = token;
                return ValueTask.FromResult(1);
            },
            cts.Token);

        Assert.Equal(cts.Token, observed);
    }

    // --- Argument validation ------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_RejectsNullOptions()
    {
        var scope = new FakeInspectionSessionScope();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Runner(scope).RunAsync(null!, Returning(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(scope.Steps);
    }

    [Fact]
    public async Task RunAsync_RejectsNullOperation()
    {
        var scope = new FakeInspectionSessionScope();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Runner(scope).RunAsync<int>(Options(), null!, TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(scope.Steps);
    }

    [Fact]
    public void Constructor_RejectsNullScopeFactory()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgreSqlInspectionSessionRunner((IPostgreSqlInspectionSessionScopeFactory)null!));
    }

    [Fact]
    public async Task RunAsync_ValidatesOptionsBeforeOpeningAConnection()
    {
        // Options validate themselves at construction, so an invalid policy can never reach a
        // runner at all — the connection is provably never opened.
        var scope = new FakeInspectionSessionScope();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PostgreSqlInspectionSessionOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(60)));

        Assert.Empty(scope.Steps);
        await Task.CompletedTask;
    }

    // --- Callback gating ------------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_DoesNotInvokeTheOperationWhenTheTokenIsAlreadyCanceled()
    {
        var scope = new FakeInspectionSessionScope();
        var invoked = false;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Runner(scope).RunAsync(Options(), Returning(1, () => invoked = true), cts.Token).AsTask());

        Assert.False(invoked);
        Assert.Empty(scope.Steps);
    }

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.SetTransactionReadOnly))]
    [InlineData(nameof(PostgreSqlSqlStatementId.ApplyLocalTimeouts))]
    [InlineData(nameof(PostgreSqlSqlStatementId.VerifySessionState))]
    public async Task RunAsync_DoesNotInvokeTheOperationWhenAnInitializationStatementFails(string idName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        var scope = new FakeInspectionSessionScope(
            ScriptedStatementGateway.HealthySession().FailingAt(id, new NpgsqlException("synthetic")));
        var invoked = false;

        await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync(Options(), Returning(1, () => invoked = true), TestContext.Current.CancellationToken).AsTask());

        Assert.False(invoked);
    }

    [Fact]
    public async Task RunAsync_DoesNotInvokeTheOperationWhenBeginTransactionFails()
    {
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.BeginTransaction, new NpgsqlException("synthetic"));
        var invoked = false;

        await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync(Options(), Returning(1, () => invoked = true), TestContext.Current.CancellationToken).AsTask());

        Assert.False(invoked);
    }

    [Theory]
    [InlineData(false, "repeatable read", true, true, true)]
    [InlineData(true, "read committed", true, true, true)]
    [InlineData(true, "repeatable read", false, true, true)]
    [InlineData(true, "repeatable read", true, false, true)]
    [InlineData(true, "repeatable read", true, true, false)]
    public async Task RunAsync_BlocksTheOperationWhenAnyVerifiedConditionIsFalse(
        bool isReadOnly, string isolationLevel, bool statementMatches, bool lockMatches, bool idleMatches)
    {
        var scope = new FakeInspectionSessionScope(
            ScriptedStatementGateway.HealthySession()
                .WithVerificationState(isReadOnly, isolationLevel, statementMatches, lockMatches, idleMatches));
        var invoked = false;

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync(Options(), Returning(1, () => invoked = true), TestContext.Current.CancellationToken).AsTask());

        Assert.False(invoked);
        Assert.Equal(PostgreSqlInspectionSessionFailureKind.VerificationFailed, exception.FailureKind);

        // Canonical contract (F-07): a verification failure reuses the initialization message
        // while keeping its distinct FailureKind.
        Assert.Equal("The PostgreSQL inspection session could not be initialized.", exception.Message);
    }

    [Fact]
    public async Task RunAsync_StillRollsBackAndDisposesWhenVerificationBlocksTheOperation()
    {
        var scope = new FakeInspectionSessionScope(
            ScriptedStatementGateway.HealthySession().WithVerificationState(isReadOnly: false));

        await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(SessionScopeStep.Rollback, scope.Steps);
        Assert.Contains(SessionScopeStep.DisposeTransaction, scope.Steps);
        Assert.Contains(SessionScopeStep.DisposeConnection, scope.Steps);
    }

    [Fact]
    public async Task RunAsync_TreatsAMissingVerificationRowAsVerificationFailed()
    {
        var scope = new FakeInspectionSessionScope(
            ScriptedStatementGateway.HealthySession().WithNoVerificationRow());

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(PostgreSqlInspectionSessionFailureKind.VerificationFailed, exception.FailureKind);
    }

    // --- Stage classification --------------------------------------------------------------------------

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.SetTransactionReadOnly), nameof(PostgreSqlInspectionSessionFailureKind.InitializationFailed))]
    [InlineData(nameof(PostgreSqlSqlStatementId.ApplyLocalTimeouts), nameof(PostgreSqlInspectionSessionFailureKind.InitializationFailed))]
    [InlineData(nameof(PostgreSqlSqlStatementId.VerifySessionState), nameof(PostgreSqlInspectionSessionFailureKind.VerificationFailed))]
    public async Task RunAsync_ClassifiesAnNpgsqlFailureByStage(string idName, string expectedKindName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        var expected = Enum.Parse<PostgreSqlInspectionSessionFailureKind>(expectedKindName);
        var scope = new FakeInspectionSessionScope(
            ScriptedStatementGateway.HealthySession().FailingAt(id, new NpgsqlException("synthetic")));

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(expected, exception.FailureKind);
    }

    [Fact]
    public async Task RunAsync_ClassifiesABeginTransactionFailureAsInitializationFailed()
    {
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.BeginTransaction, new NpgsqlException("synthetic"));

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(PostgreSqlInspectionSessionFailureKind.InitializationFailed, exception.FailureKind);
        Assert.Equal("The PostgreSQL inspection session could not be initialized.", exception.Message);
    }

    [Fact]
    public async Task RunAsync_ClassifiesAnOperationNpgsqlFailureAsExecutionFailed()
    {
        var scope = new FakeInspectionSessionScope();

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync<int>(
                Options(),
                (_, _) => throw new NpgsqlException("synthetic"),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(PostgreSqlInspectionSessionFailureKind.ExecutionFailed, exception.FailureKind);
        Assert.Equal("The PostgreSQL inspection operation failed.", exception.Message);
    }

    // --- Unexpected exception propagation -------------------------------------------------------------------

    public static TheoryData<Exception> UnexpectedExceptions() =>
    [
        new InvalidOperationException("synthetic invalid operation"),
        new ObjectDisposedException("synthetic-object"),
        new ArgumentException("synthetic argument", "someParameter"),
        new TimeoutException("synthetic non-npgsql timeout"),

        // OutOfMemoryException and AccessViolationException are reserved for the runtime (CA2201)
        // and cannot be constructed with `new`, but propagation through the runner is real
        // behaviour worth proving rather than merely asserting structurally, so instances are
        // obtained without a construction expression. StackOverflowException is deliberately
        // absent: it cannot be caught or thrown meaningfully in .NET, and the runner's freedom
        // from any catch-all is verified structurally instead.
        Activator.CreateInstance<OutOfMemoryException>(),
        Activator.CreateInstance<AccessViolationException>(),
    ];

    [Theory]
    [MemberData(nameof(UnexpectedExceptions))]
    public async Task RunAsync_PropagatesAnUnexpectedOperationExceptionUnchanged(Exception original)
    {
        var scope = new FakeInspectionSessionScope();

        // Record.ExceptionAsync rather than ThrowsAny: the assertion below is on the exact
        // instance, which is stricter than any type-family assertion would be (F-10).
        Exception? thrown = await Record.ExceptionAsync(
            () => Runner(scope).RunAsync<int>(Options(), (_, _) => throw original, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
        Assert.IsNotType<PostgreSqlInspectionSessionException>(thrown);
    }

    [Fact]
    public async Task RunAsync_PropagatesAnUnexpectedNullReferenceExceptionUnchanged()
    {
        // NullReferenceException is reserved for the runtime (CA2201), so a genuine one is
        // provoked rather than constructed.
        NullReferenceException original;
        try
        {
            string? nothing = null;
            _ = nothing!.Length;
            throw new InvalidOperationException("unreachable");
        }
        catch (NullReferenceException runtimeException)
        {
            original = runtimeException;
        }

        var scope = new FakeInspectionSessionScope();

        NullReferenceException thrown = await Assert.ThrowsAsync<NullReferenceException>(
            () => Runner(scope).RunAsync<int>(Options(), (_, _) => throw original, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
    }

    [Fact]
    public async Task RunAsync_StillCleansUpWhenAnUnexpectedExceptionPropagates()
    {
        var scope = new FakeInspectionSessionScope();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner(scope).RunAsync<int>(
                Options(),
                (_, _) => throw new InvalidOperationException("synthetic"),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(SessionScopeStep.Rollback, scope.Steps);
        Assert.Contains(SessionScopeStep.DisposeTransaction, scope.Steps);
        Assert.Contains(SessionScopeStep.DisposeConnection, scope.Steps);
    }

    // --- Cleanup precedence ------------------------------------------------------------------------------------

    [Fact]
    public async Task RunAsync_SurfacesCleanupFailureWhenNoEarlierFailureExists()
    {
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.Rollback, new NpgsqlException("synthetic cleanup failure"));

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(PostgreSqlInspectionSessionFailureKind.CleanupFailed, exception.FailureKind);
        Assert.Equal("The PostgreSQL inspection session could not be closed safely.", exception.Message);
    }

    [Fact]
    public async Task RunAsync_StillDisposesEverythingWhenRollbackFails()
    {
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.Rollback, new NpgsqlException("synthetic"));

        await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync(Options(), Returning(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Contains(SessionScopeStep.DisposeTransaction, scope.Steps);
        Assert.Contains(SessionScopeStep.DisposeConnection, scope.Steps);
    }

    [Fact]
    public async Task RunAsync_LetsAPrimaryOperationFailureWinOverACleanupFailure()
    {
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.Rollback, new NpgsqlException("synthetic cleanup failure"));

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => Runner(scope).RunAsync<int>(
                Options(),
                (_, _) => throw new NpgsqlException("synthetic primary failure"),
                TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(PostgreSqlInspectionSessionFailureKind.ExecutionFailed, exception.FailureKind);
    }

    [Fact]
    public async Task RunAsync_LetsAnUnexpectedPrimaryFailureWinOverACleanupFailure()
    {
        var original = new InvalidOperationException("synthetic primary failure");
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.Rollback, new NpgsqlException("synthetic cleanup failure"));

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Runner(scope).RunAsync<int>(Options(), (_, _) => throw original, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
    }

    [Fact]
    public async Task RunAsync_LetsCancellationWinOverACleanupFailure()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.Rollback, new NpgsqlException("synthetic cleanup failure"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Runner(scope).RunAsync<int>(
                Options(),
                (_, token) =>
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(1);
                },
                cts.Token).AsTask());
    }

    // --- Cancellation precedence over an expected failure -----------------------------------------------------

    [Fact]
    public async Task RunAsync_LetsCancellationWinOverAnExpectedInitializationFailure()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        // The cancellation is raised from inside the B002 seam, so B001 and B002 are provably
        // reached first and the runner genuinely races a real stage failure against a real
        // cancellation (GC-DHI-04B-C1, F-08) — rather than being pre-canceled before entry.
        var gateway = ScriptedStatementGateway.HealthySession()
            .BeforeStatement(PostgreSqlSqlStatementId.ApplyLocalTimeouts, cts.Cancel)
            .FailingAt(PostgreSqlSqlStatementId.ApplyLocalTimeouts, new NpgsqlException("synthetic"));
        var scope = new FakeInspectionSessionScope(gateway);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Runner(scope).RunAsync(Options(), Returning(1), cts.Token).AsTask());

        // B001 and B002 were really executed before the cancellation surfaced.
        Assert.Equal(
            [PostgreSqlSqlStatementId.SetTransactionReadOnly, PostgreSqlSqlStatementId.ApplyLocalTimeouts],
            scope.Gateway.ExecutedIds);
    }

    [Fact]
    public async Task RunAsync_LetsCancellationWinOverAnExpectedExecutionFailure()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scope = new FakeInspectionSessionScope();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Runner(scope).RunAsync<int>(
                Options(),
                (_, _) =>
                {
                    cts.Cancel();
                    throw new NpgsqlException("synthetic");
                },
                cts.Token).AsTask());
    }

    [Fact]
    public async Task RunAsync_LetsCancellationDuringVerificationPropagate()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var gateway = ScriptedStatementGateway.HealthySession()
            .FailingAt(PostgreSqlSqlStatementId.VerifySessionState, new NpgsqlException("synthetic"))
            .BeforeVerification(cts.Cancel);
        var scope = new FakeInspectionSessionScope(gateway);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Runner(scope).RunAsync(Options(), Returning(1), cts.Token).AsTask());
    }

    [Fact]
    public async Task RunAsync_PropagatesARequestedCancellationFromTheOperation()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var scope = new FakeInspectionSessionScope();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Runner(scope).RunAsync<int>(
                Options(),
                (_, token) =>
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(1);
                },
                cts.Token).AsTask());

        Assert.Contains(SessionScopeStep.DisposeConnection, scope.Steps);
    }

    // --- Absence of a commit path ---------------------------------------------------------------------------------

    [Fact]
    public void Runner_ExposesNoCommitApi() => AssertNoCommitApi(typeof(PostgreSqlInspectionSessionRunner));

    [Fact]
    public void Scope_ExposesNoCommitApi() => AssertNoCommitApi(typeof(IPostgreSqlInspectionSessionScope));

    [Fact]
    public void ProductionScope_ExposesNoCommitApi() => AssertNoCommitApi(typeof(PostgreSqlInspectionSessionScope));

    [Fact]
    public void Executor_ExposesNoCommitApi() => AssertNoCommitApi(typeof(PostgreSqlSqlExecutor));

    [Fact]
    public void OperationExecutor_ExposesNoCommitApi() => AssertNoCommitApi(typeof(PostgreSqlInspectionOperationExecutor));

    [Fact]
    public void SessionTypes_ExposeNoSavepointOrNestedTransactionApi()
    {
        foreach (Type type in new[]
                 {
                     typeof(PostgreSqlInspectionSessionRunner),
                     typeof(PostgreSqlInspectionSessionScope),
                     typeof(PostgreSqlSqlExecutor),
                 })
        {
            IEnumerable<string> names = type
                .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
                .Where(method => method.DeclaringType == type)
                .Select(method => method.Name);

            Assert.DoesNotContain(names, name =>
                name.Contains("Savepoint", StringComparison.OrdinalIgnoreCase)
                || name.Contains("Nested", StringComparison.OrdinalIgnoreCase));
        }
    }

    private static void AssertNoCommitApi(Type type)
    {
        IEnumerable<string> names = type
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Where(method => method.DeclaringType == type)
            .Select(method => method.Name)
            .Concat(type
                .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .Select(property => property.Name));

        Assert.DoesNotContain(names, name =>
            name.Contains("Commit", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Complete", StringComparison.OrdinalIgnoreCase)
            || name.Equals("Save", StringComparison.OrdinalIgnoreCase));
    }

}
