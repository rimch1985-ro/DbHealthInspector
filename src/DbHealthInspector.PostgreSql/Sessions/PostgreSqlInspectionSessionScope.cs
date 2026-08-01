using System.Data;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sql;
using Npgsql;

namespace DbHealthInspector.PostgreSql.Sessions;

/// <summary>
/// The production session scope: opens a connection through the GC-DHI-04A
/// <see cref="PostgreSqlConnectionFactory"/>, begins one <see cref="IsolationLevel.RepeatableRead"/>
/// transaction, and releases both in the required order.
/// </summary>
/// <remarks>
/// <para>
/// The scope owns the connection and the transaction it creates; it does <b>not</b> own the
/// connection factory and never disposes it. Neither the connection nor the transaction is
/// exposed — the only thing that leaves is a <see cref="PostgreSqlSqlExecutor"/>, which itself
/// exposes neither.
/// </para>
/// <para>
/// There is no commit path here, by construction: <see cref="NpgsqlTransaction.CommitAsync"/> is
/// never called and no method of this type exposes it.
/// </para>
/// </remarks>
internal sealed class PostgreSqlInspectionSessionScope : IPostgreSqlInspectionSessionScope
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlSqlInventory _inventory;

    private NpgsqlConnection? _connection;
    private NpgsqlTransaction? _transaction;

    internal PostgreSqlInspectionSessionScope(PostgreSqlConnectionFactory connectionFactory, PostgreSqlSqlInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(inventory);

        _connectionFactory = connectionFactory;
        _inventory = inventory;
    }

    public async ValueTask OpenConnectionAsync(CancellationToken cancellationToken)
    {
        _connection = await _connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask BeginTransactionAsync(CancellationToken cancellationToken)
    {
        NpgsqlConnection connection = _connection
            ?? throw new InvalidOperationException("The connection must be opened before beginning a transaction.");

        _transaction = await connection
            .BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)
            .ConfigureAwait(false);
    }

    public PostgreSqlSqlExecutor CreateExecutor()
    {
        NpgsqlConnection connection = _connection
            ?? throw new InvalidOperationException("The connection must be opened before creating an executor.");
        NpgsqlTransaction transaction = _transaction
            ?? throw new InvalidOperationException("The transaction must be started before creating an executor.");

        return new PostgreSqlSqlExecutor(_inventory, connection, transaction);
    }

    public async ValueTask RollbackAsync()
    {
        if (_transaction is { } transaction)
        {
            // CancellationToken.None on purpose: rollback is the one operation that must still be
            // attempted when the caller's token has already been canceled.
            await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeTransactionAsync()
    {
        if (_transaction is { } transaction)
        {
            _transaction = null;

            // After the explicit RollbackAsync above the transaction is already complete, so this
            // disposal releases resources without performing a second logical rollback.
            await transaction.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeConnectionAsync()
    {
        if (_connection is { } connection)
        {
            _connection = null;
            await connection.DisposeAsync().ConfigureAwait(false);
        }
    }
}

/// <summary>
/// Creates production scopes over a shared connection factory and the canonical inventory.
/// </summary>
internal sealed class PostgreSqlInspectionSessionScopeFactory : IPostgreSqlInspectionSessionScopeFactory
{
    private readonly PostgreSqlConnectionFactory _connectionFactory;
    private readonly PostgreSqlSqlInventory _inventory;

    internal PostgreSqlInspectionSessionScopeFactory(PostgreSqlConnectionFactory connectionFactory, PostgreSqlSqlInventory inventory)
    {
        ArgumentNullException.ThrowIfNull(connectionFactory);
        ArgumentNullException.ThrowIfNull(inventory);

        _connectionFactory = connectionFactory;
        _inventory = inventory;
    }

    public IPostgreSqlInspectionSessionScope Create() =>
        new PostgreSqlInspectionSessionScope(_connectionFactory, _inventory);
}
