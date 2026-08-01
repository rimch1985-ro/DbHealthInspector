using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sql.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// The gateway's command lifecycle (GC-DHI-04B-C2, R2-01): a construction failure or a
/// reader-acquisition failure is always the exception that surfaces, even when releasing the
/// partially built or unowned command also fails, and the command is released asynchronously
/// exactly once on every path.
/// </summary>
public sealed class NpgsqlStatementGatewayLifecycleTests
{
    private sealed class SyntheticGatewayException : Exception
    {
        internal SyntheticGatewayException(string message)
            : base(message)
        {
        }
    }

    private static PostgreSqlPreparedStatement Statement() =>
        PostgreSqlSqlExecutor.Prepare(
            new PostgreSqlSqlInventory(),
            PostgreSqlSqlStatementId.ApplyLocalTimeouts,
            [
                PostgreSqlSqlParameterValue.Int32(1, 30_000),
                PostgreSqlSqlParameterValue.Int32(2, 5_000),
                PostgreSqlSqlParameterValue.Int32(3, 60_000),
            ]);

    private static PostgreSqlPreparedStatement NonQueryStatement() =>
        PostgreSqlSqlExecutor.Prepare(new PostgreSqlSqlInventory(), PostgreSqlSqlStatementId.SetTransactionReadOnly, []);

    private static NpgsqlStatementGateway Gateway(IPostgreSqlCommandHandle handle) => new(_ => handle);

    /// <summary>Primary failure kinds, named because several are not constructible inline.</summary>
    public static TheoryData<string> PrimaryKinds() =>
    [
        "Npgsql", "InvalidOperation", "Argument", "Custom",
    ];

    private static Exception Primary(string kind) => kind switch
    {
        "Npgsql" => new NpgsqlException("synthetic primary"),
        "InvalidOperation" => new InvalidOperationException("synthetic primary"),
        "Argument" => new ArgumentException("synthetic primary", nameof(kind)),
        "Custom" => new SyntheticGatewayException("synthetic primary"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown primary kind."),
    };

    // --- Construction ------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(PrimaryKinds))]
    public async Task ConstructionFailure_SurfacesEvenWhenPartialDisposalAlsoFails(string kind)
    {
        Exception primary = Primary(kind);
        var handle = FakeCommandHandle.FailingToBind(primary, new SyntheticGatewayException("synthetic dispose"));

        Exception? thrown = await Record.ExceptionAsync(
            () => Gateway(handle).ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(primary, thrown);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Theory]
    [MemberData(nameof(PrimaryKinds))]
    public async Task ConstructionFailure_SurfacesWhenPartialDisposalSucceeds(string kind)
    {
        Exception primary = Primary(kind);
        var handle = FakeCommandHandle.FailingToBind(primary);

        Exception? thrown = await Record.ExceptionAsync(
            () => Gateway(handle).ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(primary, thrown);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task ConstructionFailure_WithRequestedCancellation_SurfacesTheSameOce()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var primary = new OperationCanceledException(cts.Token);
        var handle = FakeCommandHandle.FailingToBind(primary, new SyntheticGatewayException("synthetic dispose"));

        Exception? thrown = await Record.ExceptionAsync(
            () => Gateway(handle).ExecuteReaderAsync(Statement(), cts.Token).AsTask());

        Assert.Same(primary, thrown);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task FactoryFailure_NeverAttemptsToDisposeANullCommand()
    {
        var primary = new SyntheticGatewayException("synthetic factory failure");
        var gateway = new NpgsqlStatementGateway(_ => throw primary);

        Exception? thrown = await Record.ExceptionAsync(
            () => gateway.ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken).AsTask());

        // No NullReferenceException from disposing a command that was never created.
        Assert.Same(primary, thrown);
    }

    [Fact]
    public async Task ConstructionFailure_PreservesTheOriginalThrowSite()
    {
        var primary = new SyntheticGatewayException("synthetic factory failure");
        var gateway = new NpgsqlStatementGateway(_ => ThrowFromNamedFactory(primary));

        Exception? thrown = await Record.ExceptionAsync(
            () => gateway.ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(primary, thrown);
        Assert.Contains(nameof(ThrowFromNamedFactory), thrown!.StackTrace, StringComparison.Ordinal);
    }

    // --- Reader acquisition ---------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(PrimaryKinds))]
    public async Task AcquisitionFailure_SurfacesEvenWhenCommandDisposalAlsoFails(string kind)
    {
        Exception primary = Primary(kind);
        var handle = FakeCommandHandle.FailingToAcquire(primary, new SyntheticGatewayException("synthetic dispose"));

        Exception? thrown = await Record.ExceptionAsync(
            () => Gateway(handle).ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(primary, thrown);
        Assert.Equal(1, handle.AcquireCount);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Theory]
    [MemberData(nameof(PrimaryKinds))]
    public async Task AcquisitionFailure_SurfacesWhenCommandDisposalSucceeds(string kind)
    {
        Exception primary = Primary(kind);
        var handle = FakeCommandHandle.FailingToAcquire(primary);

        Exception? thrown = await Record.ExceptionAsync(
            () => Gateway(handle).ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(primary, thrown);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task AcquisitionFailure_WithRequestedCancellation_SurfacesTheSameOce()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var primary = new OperationCanceledException(cts.Token);
        var handle = FakeCommandHandle.FailingToAcquire(primary, new SyntheticGatewayException("synthetic dispose"));

        Exception? thrown = await Record.ExceptionAsync(
            () => Gateway(handle).ExecuteReaderAsync(Statement(), cts.Token).AsTask());

        Assert.Same(primary, thrown);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task AcquisitionSucceeds_CommandIsNotDisposedUntilTheReaderIs()
    {
        var handle = FakeCommandHandle.Succeeding();

        IPostgreSqlRowReader reader = await Gateway(handle)
            .ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken);

        Assert.Equal(0, handle.DisposeCount);

        await reader.DisposeAsync();

        Assert.Equal(1, handle.DisposeCount);
        Assert.Equal(1, handle.Rows!.DisposeCount);
    }

    [Fact]
    public async Task AcquisitionForwardsTheExactToken()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var handle = FakeCommandHandle.Succeeding();

        IPostgreSqlRowReader reader = await Gateway(handle).ExecuteReaderAsync(Statement(), cts.Token);
        await reader.DisposeAsync();

        Assert.Equal(cts.Token, Assert.Single(handle.Tokens));
    }

    // --- Reader/command disposal independence -----------------------------------------------------

    [Fact]
    public async Task ReaderDisposalFailure_StillDisposesTheCommandAndSurfacesTheFirstFailure()
    {
        var rowsFailure = new SyntheticGatewayException("rows dispose");
        var rows = FakeRowSource.SingleRow(3, rowsFailure);
        var handle = FakeCommandHandle.Succeeding(rows, new SyntheticGatewayException("command dispose"));

        IPostgreSqlRowReader reader = await Gateway(handle)
            .ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken);

        Exception? thrown = await Record.ExceptionAsync(() => reader.DisposeAsync().AsTask());

        Assert.Same(rowsFailure, thrown);
        Assert.Equal(1, rows.DisposeCount);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task CommandDisposalFailureAlone_Surfaces()
    {
        var commandFailure = new SyntheticGatewayException("command dispose");
        var rows = FakeRowSource.SingleRow(3);
        var handle = FakeCommandHandle.Succeeding(rows, commandFailure);

        IPostgreSqlRowReader reader = await Gateway(handle)
            .ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken);

        Exception? thrown = await Record.ExceptionAsync(() => reader.DisposeAsync().AsTask());

        Assert.Same(commandFailure, thrown);
        Assert.Equal(1, rows.DisposeCount);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task SuccessfulDisposal_SurfacesNothing()
    {
        var rows = FakeRowSource.SingleRow(3);
        var handle = FakeCommandHandle.Succeeding(rows);

        IPostgreSqlRowReader reader = await Gateway(handle)
            .ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken);

        Exception? thrown = await Record.ExceptionAsync(() => reader.DisposeAsync().AsTask());

        Assert.Null(thrown);
        Assert.Equal(1, rows.DisposeCount);
        Assert.Equal(1, handle.DisposeCount);
    }

    // --- Non-query path ------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(PrimaryKinds))]
    public async Task NonQueryFailure_SurfacesEvenWhenDisposalAlsoFails(string kind)
    {
        Exception primary = Primary(kind);
        var handle = FakeCommandHandle.FailingNonQuery(primary, new SyntheticGatewayException("synthetic dispose"));

        Exception? thrown = await Record.ExceptionAsync(
            () => Gateway(handle).ExecuteNonQueryAsync(NonQueryStatement(), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(primary, thrown);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task NonQueryDisposalFailureAlone_Surfaces()
    {
        var disposeFailure = new SyntheticGatewayException("synthetic dispose");
        var handle = FakeCommandHandle.Succeeding(disposeFailure: disposeFailure);

        Exception? thrown = await Record.ExceptionAsync(
            () => Gateway(handle).ExecuteNonQueryAsync(NonQueryStatement(), TestContext.Current.CancellationToken).AsTask());

        Assert.Same(disposeFailure, thrown);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task NonQuerySuccess_DisposesTheCommandExactlyOnce()
    {
        var handle = FakeCommandHandle.Succeeding();

        await Gateway(handle).ExecuteNonQueryAsync(NonQueryStatement(), TestContext.Current.CancellationToken);

        Assert.Equal(1, handle.ExecuteNonQueryCount);
        Assert.Equal(1, handle.DisposeCount);
    }

    [Fact]
    public async Task NonQueryForwardsTheExactToken()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var handle = FakeCommandHandle.Succeeding();

        await Gateway(handle).ExecuteNonQueryAsync(NonQueryStatement(), cts.Token);

        Assert.Equal(cts.Token, Assert.Single(handle.Tokens));
    }

    [Fact]
    public async Task ParametersAreBoundInAscendingOrder()
    {
        var handle = FakeCommandHandle.Succeeding();

        IPostgreSqlRowReader reader = await Gateway(handle)
            .ExecuteReaderAsync(Statement(), TestContext.Current.CancellationToken);
        await reader.DisposeAsync();

        Assert.Equal(3, handle.AddParameterCount);
    }

    private static IPostgreSqlCommandHandle ThrowFromNamedFactory(Exception primary) => throw primary;
}
