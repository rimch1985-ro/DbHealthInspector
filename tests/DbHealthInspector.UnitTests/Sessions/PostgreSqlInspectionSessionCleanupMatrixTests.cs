using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.UnitTests.Sessions.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Sessions;

/// <summary>
/// The absolute precedence contract (GC-DHI-04B-C1, F-01): a cleanup failure can never hide a
/// primary failure or a requested cancellation, every cleanup step is always attempted, and only
/// the first cleanup failure is surfaced when nothing else went wrong.
/// </summary>
public sealed class PostgreSqlInspectionSessionCleanupMatrixTests
{
    private static PostgreSqlInspectionSessionOptions Options() => PostgreSqlInspectionSessionOptions.Default;

    /// <summary>
    /// Every cleanup step that can independently fail, named as strings because
    /// <c>SessionScopeStep</c> is internal and these theory methods must be public.
    /// </summary>
    public static TheoryData<string> CleanupSteps() =>
    [
        nameof(SessionScopeStep.Rollback),
        nameof(SessionScopeStep.DisposeTransaction),
        nameof(SessionScopeStep.DisposeConnection),
    ];

    private static SessionScopeStep Step(string name) => Enum.Parse<SessionScopeStep>(name);

    private static Exception CleanupException(string kind) => kind switch
    {
        "Npgsql" => new NpgsqlException("synthetic cleanup"),
        "Postgres" => new PostgresException("synthetic cleanup", "ERROR", "ERROR", "58000"),
        "InvalidOperation" => new InvalidOperationException("synthetic cleanup"),
        "ObjectDisposed" => new ObjectDisposedException("synthetic-cleanup-object"),
        "Timeout" => new TimeoutException("synthetic cleanup"),
        "Argument" => new ArgumentException("synthetic cleanup", nameof(kind)),
        "Custom" => new SyntheticCleanupException(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown cleanup exception kind."),
    };

    /// <summary>Kinds that are expected PostgreSQL failures and become <c>CleanupFailed</c>.</summary>
    private static readonly string[] SanitizedKinds = ["Npgsql", "Postgres"];

    /// <summary>Kinds that are defects and must surface as the very same instance.</summary>
    private static readonly string[] PropagatedKinds = ["InvalidOperation", "ObjectDisposed", "Timeout", "Argument", "Custom"];

    private static readonly string[] AllSteps =
    [
        nameof(SessionScopeStep.Rollback),
        nameof(SessionScopeStep.DisposeTransaction),
        nameof(SessionScopeStep.DisposeConnection),
    ];

    /// <summary>Every step × every kind that must be translated to <c>CleanupFailed</c>.</summary>
    public static TheoryData<string, string> StepsAndSanitizedKinds()
    {
        var data = new TheoryData<string, string>();
        foreach (string step in AllSteps)
        {
            foreach (string kind in SanitizedKinds)
            {
                data.Add(step, kind);
            }
        }

        return data;
    }

    /// <summary>Every step × every kind that must propagate as the same instance.</summary>
    public static TheoryData<string, string> StepsAndPropagatedKinds()
    {
        var data = new TheoryData<string, string>();
        foreach (string step in AllSteps)
        {
            foreach (string kind in PropagatedKinds)
            {
                data.Add(step, kind);
            }
        }

        return data;
    }

    /// <summary>A type outside every enumerated set, to prove nothing is special-cased by name.</summary>
    private sealed class SyntheticCleanupException : Exception
    {
        internal SyntheticCleanupException()
            : base("synthetic custom cleanup failure")
        {
        }
    }

    public static TheoryData<string, string> StepsAndExceptionKinds()
    {
        var data = new TheoryData<string, string>();
        foreach (string step in AllSteps)
        {
            foreach (string kind in SanitizedKinds.Concat(PropagatedKinds))
            {
                data.Add(step, kind);
            }
        }

        return data;
    }

    // --- No primary: an Npgsql cleanup failure is sanitized, anything else propagates intact ------

    [Theory]
    [MemberData(nameof(StepsAndSanitizedKinds))]
    public async Task NoPrimary_ExpectedPostgreSqlCleanupFailure_BecomesCleanupFailed(string stepName, string cleanupKind)
    {
        // Covers both NpgsqlException and PostgresException explicitly: PostgresException derives
        // from NpgsqlException, but the translation is asserted for it directly rather than
        // inferred from the base case.
        var scope = new FakeInspectionSessionScope().FailingAt(Step(stepName), CleanupException(cleanupKind));

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync(Options(), (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(PostgreSqlInspectionSessionFailureKind.CleanupFailed, exception.FailureKind);
        Assert.Equal("The PostgreSQL inspection session could not be closed safely.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.DoesNotContain("synthetic cleanup", exception.ToString(), StringComparison.Ordinal);
        Assert.True(scope.AllCleanupStepsAttempted);
        Assert.True(scope.TransactionDisposedBeforeConnection);
    }

    [Theory]
    [MemberData(nameof(StepsAndPropagatedKinds))]
    public async Task NoPrimary_UnexpectedCleanupFailure_PropagatesTheSameInstance(string stepName, string cleanupKind)
    {
        Exception original = CleanupException(cleanupKind);
        var scope = new FakeInspectionSessionScope().FailingAt(Step(stepName), original);

        Exception? thrown = await Record.ExceptionAsync(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync(Options(), (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken).AsTask());

        // An unexpected cleanup exception is a defect, not an expected server outcome, so it is
        // never rewritten into CleanupFailed.
        Assert.Same(original, thrown);
        Assert.IsNotType<PostgreSqlInspectionSessionException>(thrown);
        Assert.True(scope.AllCleanupStepsAttempted);
        Assert.True(scope.TransactionDisposedBeforeConnection);
    }

    // --- Any primary always wins ------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(StepsAndExceptionKinds))]
    public async Task ExpectedPrimary_AlwaysWinsOverAnyCleanupFailure(string stepName, string cleanupKind)
    {
        var scope = new FakeInspectionSessionScope().FailingAt(Step(stepName), CleanupException(cleanupKind));

        PostgreSqlInspectionSessionException exception = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync<int>(Options(), (_, _) => throw new NpgsqlException("synthetic primary"), TestContext.Current.CancellationToken)
                .AsTask());

        // The primary's kind and message survive; the cleanup failure appears nowhere.
        Assert.Equal(PostgreSqlInspectionSessionFailureKind.ExecutionFailed, exception.FailureKind);
        Assert.Equal("The PostgreSQL inspection operation failed.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.DoesNotContain("synthetic cleanup", exception.ToString(), StringComparison.Ordinal);
        Assert.True(scope.AllCleanupStepsAttempted);
    }

    [Theory]
    [MemberData(nameof(StepsAndExceptionKinds))]
    public async Task UnexpectedPrimary_AlwaysWinsOverAnyCleanupFailure(string stepName, string cleanupKind)
    {
        var primary = new InvalidOperationException("synthetic primary");
        var scope = new FakeInspectionSessionScope().FailingAt(Step(stepName), CleanupException(cleanupKind));

        Exception? thrown = await Record.ExceptionAsync(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync<int>(Options(), (_, _) => throw primary, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(primary, thrown);
        Assert.True(scope.AllCleanupStepsAttempted);
    }

    [Theory]
    [MemberData(nameof(StepsAndExceptionKinds))]
    public async Task RequestedCancellation_AlwaysWinsOverAnyCleanupFailure(string stepName, string cleanupKind)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var primary = new OperationCanceledException(cts.Token);
        var scope = new FakeInspectionSessionScope().FailingAt(Step(stepName), CleanupException(cleanupKind));

        Exception? thrown = await Record.ExceptionAsync(
            () => new PostgreSqlInspectionSessionRunner(scope).RunAsync<int>(
                Options(),
                (_, _) =>
                {
                    cts.Cancel();
                    throw primary;
                },
                cts.Token).AsTask());

        Assert.Same(primary, thrown);
        Assert.True(scope.AllCleanupStepsAttempted);
    }

    // --- Ordering, first-failure policy and stack preservation --------------------------------------

    [Theory]
    [MemberData(nameof(StepsAndExceptionKinds))]
    public async Task EveryCleanupStepIsAttemptedAndTransactionIsReleasedBeforeConnection(string stepName, string cleanupKind)
    {
        var scope = new FakeInspectionSessionScope().FailingAt(Step(stepName), CleanupException(cleanupKind));

        _ = await Record.ExceptionAsync(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync(Options(), (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken).AsTask());

        Assert.True(scope.AllCleanupStepsAttempted);
        Assert.True(scope.TransactionDisposedBeforeConnection);
    }

    [Fact]
    public async Task FirstCleanupFailureIsTheOneSurfaced()
    {
        // Rollback fails with a distinctive unexpected type; connection disposal fails afterwards
        // with a different one. The first must win and the second must not be attached anywhere.
        var first = new SyntheticCleanupException();
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.Rollback, first)
            .FailingAt(SessionScopeStep.DisposeConnection, new InvalidOperationException("second cleanup failure"));

        Exception? thrown = await Record.ExceptionAsync(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync(Options(), (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(first, thrown);
        Assert.Null(thrown!.InnerException);
        Assert.Empty(thrown.Data);
        Assert.DoesNotContain("second cleanup failure", thrown.ToString(), StringComparison.Ordinal);
        Assert.True(scope.AllCleanupStepsAttempted);
    }

    [Fact]
    public async Task PrimaryStackTraceIsPreservedThroughCleanup()
    {
        var primary = new InvalidOperationException("synthetic primary");
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.Rollback, new NpgsqlException("synthetic cleanup"));

        Exception? thrown = await Record.ExceptionAsync(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync<int>(Options(), (_, _) => ThrowFromNamedHelper(primary), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(primary, thrown);
        Assert.NotNull(thrown!.StackTrace);

        // ExceptionDispatchInfo.Throw preserves the original throw site rather than resetting it.
        Assert.Contains(nameof(ThrowFromNamedHelper), thrown.StackTrace, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FactoryIsNeverDisposedByTheRunner()
    {
        var scope = new FakeInspectionSessionScope();

        await new PostgreSqlInspectionSessionRunner(scope)
            .RunAsync(Options(), (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken);

        // The scope exposes only the six lifecycle steps; there is no factory-disposal step at
        // all, which is what makes "the runner never disposes the factory" structural.
        Assert.DoesNotContain(
            typeof(IPostgreSqlInspectionSessionScope)
                .GetMethods()
                .Select(method => method.Name),
            name => name.Contains("Factory", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RollbackHappensExactlyOnce()
    {
        var scope = new FakeInspectionSessionScope();

        await new PostgreSqlInspectionSessionRunner(scope)
            .RunAsync(Options(), (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken);

        Assert.Equal(1, scope.Steps.Count(step => step == SessionScopeStep.Rollback));
    }

    private static ValueTask<int> ThrowFromNamedHelper(Exception primary) => throw primary;
}
