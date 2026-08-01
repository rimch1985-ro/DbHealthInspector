using System.Data;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// A test-owned session that performs the <b>exact production initialization sequence</b> —
/// open through <see cref="PostgreSqlConnectionFactory"/>, begin
/// <see cref="IsolationLevel.RepeatableRead"/>, then B001 → B002 → B003 through the real
/// <see cref="PostgreSqlSqlExecutor"/> — and then exposes the same connection and transaction so
/// a test can run SQL that must never exist in the product.
/// </summary>
/// <remarks>
/// <para>
/// This type lives only in the IntegrationTests assembly. It exists precisely so the product does
/// <b>not</b> need a raw-SQL escape hatch: writes, <c>pg_sleep</c> and lock-provoking selects are
/// built here, on a connection this harness owns, using the internal
/// <c>PostgreSqlSqlExecutor(inventory, connection, transaction)</c> constructor rather than by
/// extracting a connection out of a real <see cref="PostgreSqlInspectionSessionRunner"/> session.
/// </para>
/// <para>
/// It always finishes through rollback, mirroring the production completion policy.
/// </para>
/// </remarks>
internal sealed class TestOwnedInspectionSession : IAsyncDisposable
{
    private readonly PostgreSqlConnectionFactory _factory;
    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    private TestOwnedInspectionSession(PostgreSqlConnectionFactory factory) => _factory = factory;

    internal NpgsqlConnection Connection => _connection
        ?? throw new InvalidOperationException("The test session has not been started.");

    internal NpgsqlTransaction Transaction => _transaction
        ?? throw new InvalidOperationException("The test session has not been started.");

    internal PostgreSqlInspectionSessionState State { get; private set; } = null!;

    /// <summary>
    /// Opens, begins and initializes a session using the production sequence and executor.
    /// </summary>
    internal static async Task<TestOwnedInspectionSession> StartAsync(
        string connectionString,
        PostgreSqlInspectionSessionOptions options,
        CancellationToken cancellationToken)
    {
        PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(connectionString);
        var session = new TestOwnedInspectionSession(factory);
        try
        {
            session._connection = await factory.OpenConnectionAsync(cancellationToken);
            session._transaction = await session._connection
                .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);

            var executor = new PostgreSqlSqlExecutor(
                PostgreSqlSqlInventory.Default, session._connection, session._transaction);

            await executor.ExecuteSetTransactionReadOnlyAsync(cancellationToken);
            await executor.ApplyLocalTimeoutsAsync(
                options.StatementTimeoutMilliseconds,
                options.LockTimeoutMilliseconds,
                options.IdleInTransactionTimeoutMilliseconds,
                cancellationToken);

            session.State = await executor.VerifySessionStateAsync(
                options.StatementTimeoutMilliseconds,
                options.LockTimeoutMilliseconds,
                options.IdleInTransactionTimeoutMilliseconds,
                cancellationToken);

            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// Creates a command bound to this session's connection and transaction, for SQL that exists
    /// only in tests.
    /// </summary>
    internal NpgsqlCommand CreateTestOnlyCommand(string sql) => new(sql, Connection, Transaction);

    public async ValueTask DisposeAsync()
    {
        if (_transaction is { } transaction)
        {
            _transaction = null;
            try
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
            catch (NpgsqlException)
            {
                // The server may already have terminated the transaction (idle-in-transaction
                // timeout); cleanup must tolerate that.
            }
            catch (InvalidOperationException)
            {
                // Already completed or already disposed (ObjectDisposedException derives from
                // InvalidOperationException, so this covers both).
            }

            await transaction.DisposeAsync();
        }

        if (_connection is { } connection)
        {
            _connection = null;
            await connection.DisposeAsync();
        }

        await _factory.DisposeAsync();
    }
}
