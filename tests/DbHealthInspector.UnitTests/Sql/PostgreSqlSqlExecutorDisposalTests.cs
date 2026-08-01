using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sql.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// Reader disposal must never mask the failure that actually mattered
/// (GC-DHI-04B-C1, F-09), and a disposal failure with no primary must still surface.
/// </summary>
public sealed class PostgreSqlSqlExecutorDisposalTests
{
    private static PostgreSqlSqlExecutor Executor(FakeStatementGateway gateway) => new(new PostgreSqlSqlInventory(), gateway);

    private sealed class SyntheticDisposeException : Exception
    {
        internal SyntheticDisposeException(string message)
            : base(message)
        {
        }
    }

    [Fact]
    public async Task NoPrimary_SuccessfulDisposal_ReturnsTheResult()
    {
        var reader = FakeRowReader.VerificationRow();
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken);

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task NoPrimary_DisposalFailure_Surfaces()
    {
        var original = new SyntheticDisposeException("synthetic reader dispose");
        var reader = FakeRowReader.VerificationRow();
        reader.DisposeFailure = original;
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        Exception? thrown = await Record.ExceptionAsync(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
    }

    [Fact]
    public async Task ShapeFailure_WinsOverADisposalFailure()
    {
        var reader = FakeRowReader.Empty(5);
        reader.DisposeFailure = new SyntheticDisposeException("synthetic reader dispose");
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task B002ShapeFailure_WinsOverADisposalFailure()
    {
        var reader = FakeRowReader.Empty(3);
        reader.DisposeFailure = new SyntheticDisposeException("synthetic reader dispose");
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ApplyLocalTimeoutsAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task ExecutionFailure_WinsOverADisposalFailure()
    {
        var original = new NpgsqlException("synthetic execution failure");
        FakeStatementGateway gateway = FakeStatementGateway.FailingReader(original);

        Exception? thrown = await Record.ExceptionAsync(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
    }

    [Fact]
    public async Task RequestedCancellation_WinsOverADisposalFailure()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var original = new OperationCanceledException(cts.Token);
        FakeStatementGateway gateway = FakeStatementGateway.FailingReader(original);

        Exception? thrown = await Record.ExceptionAsync(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, cts.Token).AsTask());

        Assert.Same(original, thrown);
    }

    [Fact]
    public async Task DisposalIsAttemptedEvenWhenTheBodyFails()
    {
        var reader = FakeRowReader.Empty(5);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task CleanupHelper_AttemptsEveryStepAndKeepsOnlyTheFirstFailure()
    {
        // Directly exercises the shared mechanism both the executor and the gateway rely on:
        // a failing first step must not prevent the later steps from running, and the first
        // failure is the one preserved.
        var first = new SyntheticDisposeException("first");
        var second = new SyntheticDisposeException("second");
        var ran = new List<string>();

        System.Runtime.ExceptionServices.ExceptionDispatchInfo? captured = await PostgreSqlAsyncCleanup.RunAllAsync(
            () => { ran.Add("a"); return ValueTask.FromException(first); },
            () => { ran.Add("b"); return ValueTask.FromException(second); },
            () => { ran.Add("c"); return ValueTask.CompletedTask; });

        Assert.Equal(["a", "b", "c"], ran);
        Assert.NotNull(captured);
        Assert.Same(first, captured.SourceException);
    }

    [Fact]
    public async Task CleanupHelper_ReturnsNullWhenEveryStepSucceeds()
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? captured = await PostgreSqlAsyncCleanup.RunAllAsync(
            () => ValueTask.CompletedTask,
            () => ValueTask.CompletedTask);

        Assert.Null(captured);
    }
}
