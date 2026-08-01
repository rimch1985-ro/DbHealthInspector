using System.Data;
using System.Diagnostics;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Sessions;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// Proves the three transaction-local timeouts applied by B002 are really in force on the server.
/// Each test uses reduced but still valid options, a test-only statement that exists nowhere in
/// the product, and a hard external deadline so a hang fails fast instead of stalling CI.
/// </summary>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class TimeoutTests
{
    /// <summary>PostgreSQL <c>query_canceled</c> — raised by <c>statement_timeout</c>.</summary>
    private const string QueryCanceledSqlState = "57014";

    /// <summary>PostgreSQL <c>lock_not_available</c> — raised by <c>lock_timeout</c>.</summary>
    private const string LockNotAvailableSqlState = "55P03";

    private readonly PostgreSqlServerFixture _fixture;

    public TimeoutTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource Deadline(TimeSpan budget, CancellationToken cancellationToken)
    {
        var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        source.CancelAfter(budget);
        return source;
    }

    // --- Statement timeout ----------------------------------------------------------------------

    [Fact]
    public async Task StatementTimeout_CancelsALongRunningTestOnlyStatement()
    {
        using CancellationTokenSource deadline = Deadline(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        var options = new PostgreSqlInspectionSessionOptions(
            statementTimeout: TimeSpan.FromMilliseconds(500),
            lockTimeout: TimeSpan.FromMilliseconds(200),
            idleInTransactionTimeout: TimeSpan.FromSeconds(60));

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString, options, deadline.Token);

        Assert.True(session.State.IsVerified);

        // pg_sleep exists only here; the product inventory has no such statement. The sleep is
        // 4x the statement timeout, so the server must interrupt it.
        await using NpgsqlCommand command = session.CreateTestOnlyCommand("SELECT pg_catalog.pg_sleep(2)");

        var stopwatch = Stopwatch.StartNew();
        PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
            () => command.ExecuteNonQueryAsync(deadline.Token));
        stopwatch.Stop();

        Assert.Equal(QueryCanceledSqlState, exception.SqlState);

        // The server, not the client, ended it: well before the 2 s sleep would have completed.
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(2), $"Elapsed {stopwatch.Elapsed} suggests the sleep was not interrupted.");

        Assert.Equal(PostgreSqlServerFixture.OriginalMarkerValue, await _fixture.ReadControlMarkerAsync(deadline.Token));
    }

    [Fact]
    public async Task StatementTimeout_LeavesThePoolUsable()
    {
        using CancellationTokenSource deadline = Deadline(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken);

        var options = new PostgreSqlInspectionSessionOptions(
            TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(60));

        await using (TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString, options, deadline.Token))
        {
            await using NpgsqlCommand command = session.CreateTestOnlyCommand("SELECT pg_catalog.pg_sleep(2)");
            await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync(deadline.Token));
        }

        await using TestOwnedInspectionSession next = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString, PostgreSqlInspectionSessionOptions.Default, deadline.Token);

        Assert.True(next.State.IsVerified);
    }

    // --- Lock timeout ----------------------------------------------------------------------------

    [Fact]
    public async Task LockTimeout_FiresBeforeStatementTimeoutWhenAnIncompatibleLockIsHeld()
    {
        using CancellationTokenSource deadline = Deadline(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        // Lock timeout is deliberately far below statement timeout, so the failure is provably a
        // lock timeout rather than a statement timeout.
        var options = new PostgreSqlInspectionSessionOptions(
            statementTimeout: TimeSpan.FromSeconds(10),
            lockTimeout: TimeSpan.FromMilliseconds(300),
            idleInTransactionTimeout: TimeSpan.FromSeconds(60));

        await using NpgsqlConnection locker = await _fixture.OpenAdminConnectionAsync(deadline.Token);
        await using NpgsqlTransaction lockerTransaction = await locker.BeginTransactionAsync(IsolationLevel.ReadCommitted, deadline.Token);
        try
        {
            // Deterministic synchronisation: the lock is definitely held once this command
            // returns, so no sleep-based guessing is involved.
            await using (var lockCommand = new NpgsqlCommand(
                $"LOCK TABLE {PostgreSqlServerFixture.QualifiedControlTable} IN ACCESS EXCLUSIVE MODE", locker, lockerTransaction))
            {
                await lockCommand.ExecuteNonQueryAsync(deadline.Token);
            }

            await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
                _fixture.InspectionConnectionString, options, deadline.Token);

            // A plain SELECT needs ACCESS SHARE, which conflicts with ACCESS EXCLUSIVE, so it
            // must wait and then hit lock_timeout.
            await using NpgsqlCommand blocked = session.CreateTestOnlyCommand(
                $"SELECT id FROM {PostgreSqlServerFixture.QualifiedControlTable} LIMIT 1");

            var stopwatch = Stopwatch.StartNew();
            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                () => blocked.ExecuteNonQueryAsync(deadline.Token));
            stopwatch.Stop();

            Assert.Equal(LockNotAvailableSqlState, exception.SqlState);
            Assert.True(
                stopwatch.Elapsed < TimeSpan.FromSeconds(10),
                $"Elapsed {stopwatch.Elapsed} suggests the statement timeout fired instead of the lock timeout.");
        }
        finally
        {
            // The locker is always released, even if an assertion above failed.
            await lockerTransaction.RollbackAsync(CancellationToken.None);
        }

        Assert.Equal(PostgreSqlServerFixture.OriginalMarkerValue, await _fixture.ReadControlMarkerAsync(deadline.Token));

        await using TestOwnedInspectionSession afterwards = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString, PostgreSqlInspectionSessionOptions.Default, deadline.Token);
        Assert.True(afterwards.State.IsVerified);
    }

    // --- Idle-in-transaction timeout ---------------------------------------------------------------

    [Fact]
    public async Task IdleInTransactionTimeout_TerminatesAnIdleTransactionAndThePoolRecovers()
    {
        using CancellationTokenSource deadline = Deadline(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);

        var options = new PostgreSqlInspectionSessionOptions(
            statementTimeout: TimeSpan.FromSeconds(10),
            lockTimeout: TimeSpan.FromMilliseconds(300),
            idleInTransactionTimeout: TimeSpan.FromSeconds(1));

        await using (TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString, options, deadline.Token))
        {
            Assert.True(session.State.IsVerified);

            // Stay idle well past the 1 s limit; PostgreSQL terminates the backend.
            await Task.Delay(TimeSpan.FromSeconds(4), deadline.Token);

            await using NpgsqlCommand afterIdle = session.CreateTestOnlyCommand("SELECT 1");

            // Npgsql surfaces a server-terminated backend as one of a small, known set depending
            // on whether it notices while writing or while reading, so the assertion names that
            // set explicitly rather than accepting any Exception (GC-DHI-04B-C1, F-10).
            Exception? exception = await Record.ExceptionAsync(() => afterIdle.ExecuteNonQueryAsync(deadline.Token));

            Assert.NotNull(exception);
            Assert.True(
                exception is NpgsqlException or InvalidOperationException,
                $"Expected NpgsqlException or InvalidOperationException, got {exception.GetType().FullName}.");
        }

        // Cleanup above had to tolerate an already-terminated transaction. A brand new session
        // from the same pool must still work. It is scoped and disposed before the check below,
        // so it cannot be counted as a lingering transaction of its own.
        await using (TestOwnedInspectionSession recovered = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString, PostgreSqlInspectionSessionOptions.Default, deadline.Token))
        {
            Assert.True(recovered.State.IsVerified);
        }

        Assert.Equal(PostgreSqlServerFixture.OriginalMarkerValue, await _fixture.ReadControlMarkerAsync(deadline.Token));

        await using NpgsqlConnection admin = await _fixture.OpenAdminConnectionAsync(deadline.Token);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE usename = @role AND state IN ('idle in transaction', 'idle in transaction (aborted)')",
            admin);
        command.Parameters.AddWithValue("role", PostgreSqlServerFixture.InspectionRoleName);

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(deadline.Token))!);
    }
}
