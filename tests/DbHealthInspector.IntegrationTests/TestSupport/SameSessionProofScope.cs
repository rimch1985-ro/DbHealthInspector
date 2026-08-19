using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// A <b>test-only</b> session scope that behaves exactly like the production one — real connection,
/// real <c>RepeatableRead</c> transaction, real executor — but hands the executor a gateway that
/// observes which backend each statement actually ran on (GC-DHI-04F §31).
/// </summary>
/// <remarks>
/// <para>
/// This is how "one provider, one connection, one session, one transaction" becomes a measurement
/// rather than an assumption. Before every executed C001–C004, D001, E001 and E002 statement the
/// decorator runs a test-only <c>SELECT pg_backend_pid()</c> on the same live connection and
/// transaction, so the recorded PIDs prove the whole composition shared one backend.
/// </para>
/// <para>
/// The probe never runs before B001: <c>SET TRANSACTION READ ONLY</c> must be the first statement
/// of the transaction, and issuing an ordinary query ahead of it would change the very thing under
/// test. B001–B003 are therefore recorded without any extra command.
/// </para>
/// <para>
/// The PID query exists only in this assembly. It is absent from product source, from the frozen
/// inventory and from the package, and it creates no new productive statement.
/// </para>
/// </remarks>
internal sealed class SameSessionProofScope : IPostgreSqlInspectionSessionScope, IPostgreSqlInspectionSessionScopeFactory
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlSqlInventory _inventory;

    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;
    private SameSessionProofGateway? _gateway;

    internal SameSessionProofScope(PostgreSqlConnectionFactory connectionFactory, PostgreSqlSqlInventory inventory)
    {
        _connectionFactory = connectionFactory;
        _inventory = inventory;
    }

    /// <summary>Every statement executed, in order, with the backend PID observed for it.</summary>
    internal IReadOnlyList<ObservedStatement> Observed => _gateway?.Observed ?? [];

    /// <summary>The one connection this scope opened, for reference-identity assertions.</summary>
    internal NpgsqlConnection? Connection => _connection;

    /// <summary>The one transaction this scope began, for reference-identity assertions.</summary>
    internal NpgsqlTransaction? Transaction => _transaction;

    /// <summary>
    /// Whether the executor's transaction belonged to this scope's own connection, recorded
    /// <b>while both were still alive</b>.
    /// </summary>
    /// <remarks>
    /// Captured at executor-creation time rather than read afterwards: by the time a test inspects
    /// the scope, cleanup has already disposed the transaction and asking it for its connection
    /// would throw. The fact under test is the association during the capture, not after it.
    /// </remarks>
    internal bool TransactionBelongedToConnection { get; private set; }

    /// <summary>
    /// Runs immediately before a chosen statement executes. Used by the same-transaction proof to
    /// commit out-of-band changes at an exact point in the sequence, with no sleep or timing race.
    /// </summary>
    internal Func<PostgreSqlSqlStatementId, Task>? BeforeStatementAsync { get; set; }

    /// <summary>
    /// The transaction state observed <b>inside the capture's own transaction</b>, immediately
    /// after B003 verified the session and before any metadata work.
    /// </summary>
    /// <remarks>
    /// Read directly from the server rather than inferred from the API that configured it. The
    /// isolation level and read-only flag are already covered by B003's own verification; this adds
    /// the deferrable flag, which B003 does not report, so the full
    /// <c>RepeatableRead</c>/read-only/non-deferrable claim is observed rather than assumed
    /// (GC-DHI-04F-C2, R1-05).
    /// </remarks>
    internal ObservedTransactionState? TransactionState => _gateway?.TransactionState;

    // One scope per capture, exactly as the production factory does.
    public IPostgreSqlInspectionSessionScope Create() =>
        new SameSessionProofScope(_connectionFactory, _inventory) { BeforeStatementAsync = BeforeStatementAsync };

    public async ValueTask OpenConnectionAsync(CancellationToken cancellationToken) =>
        _connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);

    public async ValueTask BeginTransactionAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = _connection
            ?? throw new InvalidOperationException("The connection was not opened.");

        _transaction = await connection.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead, cancellationToken);
    }

    public PostgreSqlSqlExecutor CreateExecutor()
    {
        NpgsqlConnection connection = _connection
            ?? throw new InvalidOperationException("The connection was not opened.");
        NpgsqlTransaction transaction = _transaction
            ?? throw new InvalidOperationException("The transaction was not begun.");

        TransactionBelongedToConnection = ReferenceEquals(transaction.Connection, connection);

        _gateway = new SameSessionProofGateway(
            new NpgsqlStatementGateway(connection, transaction),
            connection,
            transaction,
            BeforeStatementAsync);

        return new PostgreSqlSqlExecutor(_inventory, _gateway);
    }

    public async ValueTask RollbackAsync()
    {
        if (_transaction is { } transaction)
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
    }

    public async ValueTask DisposeTransactionAsync()
    {
        if (_transaction is { } transaction)
        {
            await transaction.DisposeAsync();
        }
    }

    public async ValueTask DisposeConnectionAsync()
    {
        if (_connection is { } connection)
        {
            await connection.DisposeAsync();
        }
    }
}

/// <summary>One executed statement and the backend it ran on.</summary>
internal sealed record ObservedStatement(PostgreSqlSqlStatementId Id, int? BackendProcessId);

/// <summary>
/// The three transaction-configuration settings, read from the live capture transaction itself.
/// </summary>
internal sealed record ObservedTransactionState(string IsolationLevel, string ReadOnly, string Deferrable);

/// <summary>
/// The passive decorator that performs the same-session observation. It changes no value, reorders
/// nothing and suppresses no exception; removing it would leave the executed sequence identical.
/// </summary>
internal sealed class SameSessionProofGateway : IPostgreSqlStatementGateway
{
    private readonly IPostgreSqlStatementGateway _inner;
    private readonly NpgsqlConnection _connection;
    private readonly NpgsqlTransaction _transaction;
    private readonly Func<PostgreSqlSqlStatementId, Task>? _beforeStatementAsync;
    private readonly List<ObservedStatement> _observed = [];

    internal SameSessionProofGateway(
        IPostgreSqlStatementGateway inner,
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Func<PostgreSqlSqlStatementId, Task>? beforeStatementAsync)
    {
        _inner = inner;
        _connection = connection;
        _transaction = transaction;
        _beforeStatementAsync = beforeStatementAsync;
    }

    internal IReadOnlyList<ObservedStatement> Observed => _observed.ToArray();

    /// <summary>Transaction state read once, immediately after B003.</summary>
    internal ObservedTransactionState? TransactionState { get; private set; }

    public async ValueTask ExecuteNonQueryAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        await ObserveAsync(statement.Id, cancellationToken);
        await _inner.ExecuteNonQueryAsync(statement, cancellationToken);
    }

    public async ValueTask<IPostgreSqlRowReader> ExecuteReaderAsync(
        PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        await ObserveAsync(statement.Id, cancellationToken);
        return await _inner.ExecuteReaderAsync(statement, cancellationToken);
    }

    private async Task ObserveAsync(PostgreSqlSqlStatementId id, CancellationToken cancellationToken)
    {
        if (_beforeStatementAsync is { } hook)
        {
            await hook(id);
        }

        // B001-B003 are recorded without a probe: SET TRANSACTION READ ONLY must remain the first
        // statement of the transaction.
        bool probes = id is not (PostgreSqlSqlStatementId.SetTransactionReadOnly
            or PostgreSqlSqlStatementId.ApplyLocalTimeouts
            or PostgreSqlSqlStatementId.VerifySessionState);

        int? backendProcessId = null;

        if (probes)
        {
            // Read once, at the first statement after B003 — the earliest point at which an
            // ordinary query is permitted and the transaction is fully configured.
            TransactionState ??= await ReadTransactionStateAsync(cancellationToken);

            await using var command = new NpgsqlCommand("SELECT pg_backend_pid()", _connection, _transaction);
            backendProcessId = (int)(await command.ExecuteScalarAsync(cancellationToken))!;
        }

        _observed.Add(new ObservedStatement(id, backendProcessId));
    }

    /// <summary>
    /// Reads the live transaction's own configuration. Test-only: these settings are never queried
    /// by the product and this SQL exists solely in the IntegrationTests assembly.
    /// </summary>
    private async Task<ObservedTransactionState> ReadTransactionStateAsync(CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT
                current_setting('transaction_isolation'),
                current_setting('transaction_read_only'),
                current_setting('transaction_deferrable')
            """,
            _connection,
            _transaction);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        _ = await reader.ReadAsync(cancellationToken);

        return new ObservedTransactionState(reader.GetString(0), reader.GetString(1), reader.GetString(2));
    }
}
