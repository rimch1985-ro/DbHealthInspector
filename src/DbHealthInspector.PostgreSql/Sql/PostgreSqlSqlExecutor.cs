using System.Runtime.ExceptionServices;
using DbHealthInspector.PostgreSql.Sessions;
using Npgsql;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// Executes only inventoried statements, resolved by closed
/// <see cref="PostgreSqlSqlStatementId"/>, against one connection and transaction.
/// </summary>
/// <remarks>
/// <para>
/// There is deliberately no method accepting SQL text, no property exposing the connection, the
/// transaction or a command, and no way to reach a mutable <c>CommandText</c>. The session runner
/// uses this type only for B001–B003; the authorized operation receives a restricted
/// <see cref="PostgreSqlInspectionOperationExecutor"/> that cannot execute those initialization
/// statements or reach this executor.
/// </para>
/// <para>
/// The executor owns no unmanaged resource of its own — the connection and transaction belong to
/// the session scope that created it — so it intentionally implements no disposal. It is not safe
/// for concurrent use, and never outlives the runner callback that received it.
/// </para>
/// </remarks>
internal sealed class PostgreSqlSqlExecutor
{
    private readonly PostgreSqlSqlInventory _inventory;
    private readonly IPostgreSqlStatementGateway _gateway;

    /// <summary>
    /// Creates an executor over an explicit gateway. Used in production by the session scope and
    /// in unit tests by a deterministic fake gateway.
    /// </summary>
    internal PostgreSqlSqlExecutor(PostgreSqlSqlInventory inventory, IPostgreSqlStatementGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(gateway);

        _inventory = inventory;
        _gateway = gateway;
    }

    /// <summary>
    /// Creates an executor bound to a live connection and transaction through the production
    /// <see cref="NpgsqlStatementGateway"/>.
    /// </summary>
    internal PostgreSqlSqlExecutor(PostgreSqlSqlInventory inventory, NpgsqlConnection connection, NpgsqlTransaction transaction)
        : this(inventory, new NpgsqlStatementGateway(connection, transaction))
    {
    }

    /// <summary>
    /// B001 — establishes read-only transaction mode. Returns no result; the runner does not rely
    /// on any affected-row count, which <c>SET</c> does not meaningfully produce.
    /// </summary>
    internal async ValueTask ExecuteSetTransactionReadOnlyAsync(CancellationToken cancellationToken)
    {
        PostgreSqlPreparedStatement statement = Prepare(
            _inventory, PostgreSqlSqlStatementId.SetTransactionReadOnly, []);

        await _gateway.ExecuteNonQueryAsync(statement, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// B002 — applies the three transaction-local timeouts. Consumes exactly one row of three
    /// columns and discards the values: what <c>set_config</c> echoes back is of no interest, and
    /// exposing it would widen the surface for nothing.
    /// </summary>
    internal async ValueTask ApplyLocalTimeoutsAsync(
        int statementTimeoutMilliseconds,
        int lockTimeoutMilliseconds,
        int idleInTransactionTimeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        PostgreSqlPreparedStatement statement = Prepare(
            _inventory,
            PostgreSqlSqlStatementId.ApplyLocalTimeouts,
            TimeoutParameters(statementTimeoutMilliseconds, lockTimeoutMilliseconds, idleInTransactionTimeoutMilliseconds));

        _ = await ReadSingleRowAsync(
            statement,
            expectedFieldCount: 3,
            project: static _ => 0,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// B003 — reads back the effective session state. Requires exactly one row of five non-null
    /// columns.
    /// </summary>
    internal async ValueTask<PostgreSqlInspectionSessionState> VerifySessionStateAsync(
        int statementTimeoutMilliseconds,
        int lockTimeoutMilliseconds,
        int idleInTransactionTimeoutMilliseconds,
        CancellationToken cancellationToken)
    {
        PostgreSqlPreparedStatement statement = Prepare(
            _inventory,
            PostgreSqlSqlStatementId.VerifySessionState,
            TimeoutParameters(statementTimeoutMilliseconds, lockTimeoutMilliseconds, idleInTransactionTimeoutMilliseconds));

        return await ReadSingleRowAsync(
            statement,
            expectedFieldCount: 5,
            project: static reader => new PostgreSqlInspectionSessionState(
                reader.GetBoolean(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.GetBoolean(4)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs a statement that must return exactly one row of <paramref name="expectedFieldCount"/>
    /// non-null columns, projects it, and rejects a second row.
    /// </summary>
    /// <remarks>
    /// The reader is released through <see cref="PostgreSqlAsyncCleanup"/> rather than
    /// <c>await using</c>, so a disposal failure can never replace the execution, shape or
    /// cancellation failure that was already propagating (GC-DHI-04B-C1, F-09).
    /// </remarks>
    private async ValueTask<TResult> ReadSingleRowAsync<TResult>(
        PostgreSqlPreparedStatement statement,
        int expectedFieldCount,
        Func<IPostgreSqlRowReader, TResult> project,
        CancellationToken cancellationToken)
    {
        IPostgreSqlRowReader reader = await _gateway
            .ExecuteReaderAsync(statement, cancellationToken)
            .ConfigureAwait(false);

        ExceptionDispatchInfo? primary = null;
        TResult result = default!;
        try
        {
            if (!await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new PostgreSqlSqlResultShapeException();
            }

            if (reader.FieldCount != expectedFieldCount)
            {
                throw new PostgreSqlSqlResultShapeException();
            }

            // Every column of every inventoried statement is non-nullable by construction, so a
            // NULL means the server did not answer what the definition promised.
            for (var ordinal = 0; ordinal < expectedFieldCount; ordinal++)
            {
                if (reader.IsNull(ordinal))
                {
                    throw new PostgreSqlSqlResultShapeException();
                }
            }

            result = project(reader);

            if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                throw new PostgreSqlSqlResultShapeException();
            }
        }
        catch (Exception exception)
        {
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        ExceptionDispatchInfo? disposal = await PostgreSqlAsyncCleanup
            .RunAllAsync(reader.DisposeAsync)
            .ConfigureAwait(false);

        primary?.Throw();
        disposal?.Throw();
        return result;
    }

    /// <summary>
    /// Resolves <paramref name="id"/> through the canonical inventory and checks the supplied
    /// values against the resolved declaration: exact count, exact ascending positions and exact
    /// declared types. Pure and directly unit-tested; the single place an ID becomes SQL.
    /// </summary>
    internal static PostgreSqlPreparedStatement Prepare(
        PostgreSqlSqlInventory inventory,
        PostgreSqlSqlStatementId id,
        IReadOnlyList<PostgreSqlSqlParameterValue> values)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(values);

        PostgreSqlSqlStatementDefinition definition = inventory.Resolve(id);

        if (values.Count != definition.Parameters.Count)
        {
            throw new PostgreSqlSqlParameterBindingException();
        }

        for (var index = 0; index < values.Count; index++)
        {
            PostgreSqlSqlParameterValue value = values[index];
            PostgreSqlSqlParameterDefinition declared = definition.Parameters[index];

            if (value.Position != declared.Position || value.Type != declared.Type)
            {
                throw new PostgreSqlSqlParameterBindingException();
            }
        }

        return new PostgreSqlPreparedStatement(definition.Id, definition.Sql, values);
    }

    private static PostgreSqlSqlParameterValue[] TimeoutParameters(
        int statementTimeoutMilliseconds,
        int lockTimeoutMilliseconds,
        int idleInTransactionTimeoutMilliseconds) =>
    [
        PostgreSqlSqlParameterValue.Int32(1, statementTimeoutMilliseconds),
        PostgreSqlSqlParameterValue.Int32(2, lockTimeoutMilliseconds),
        PostgreSqlSqlParameterValue.Int32(3, idleInTransactionTimeoutMilliseconds),
    ];

}
