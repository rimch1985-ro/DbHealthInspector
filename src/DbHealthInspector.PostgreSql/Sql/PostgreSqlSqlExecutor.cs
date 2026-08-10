using System.Runtime.ExceptionServices;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Tables;
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
    private ValueTask<TResult> ReadSingleRowAsync<TResult>(
        PostgreSqlPreparedStatement statement,
        int expectedFieldCount,
        Func<IPostgreSqlRowReader, TResult> project,
        CancellationToken cancellationToken) =>
        ReadSingleRowAsync(statement, expectedFieldCount, allowNullColumns: false, project, cancellationToken);

    /// <summary>
    /// Runs a statement that must return exactly one row of <paramref name="expectedFieldCount"/>
    /// columns, projects it, and rejects a second row.
    /// </summary>
    /// <param name="statement">The resolved statement to run.</param>
    /// <param name="expectedFieldCount">The exact column count the definition promises.</param>
    /// <param name="allowNullColumns">
    /// <see langword="false"/> for every statement whose columns are non-nullable by construction
    /// — the default. Only C004 sets this, because its single column is legitimately nullable and
    /// the projection performs its own null check.
    /// </param>
    /// <param name="project">Projects the single row into the caller's result type.</param>
    /// <param name="cancellationToken">Forwarded unchanged to the gateway and the reader.</param>
    private async ValueTask<TResult> ReadSingleRowAsync<TResult>(
        PostgreSqlPreparedStatement statement,
        int expectedFieldCount,
        bool allowNullColumns,
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

            // Columns of a non-nullable statement must not be NULL: a NULL there means the server
            // did not answer what the definition promised.
            if (!allowNullColumns)
            {
                for (var ordinal = 0; ordinal < expectedFieldCount; ordinal++)
                {
                    if (reader.IsNull(ordinal))
                    {
                        throw new PostgreSqlSqlResultShapeException();
                    }
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
    /// C001 — reads the server's numeric version, database name and current user. Requires
    /// exactly one row of three non-null columns.
    /// </summary>
    internal async ValueTask<PostgreSqlServerIdentity> ReadServerIdentityAsync(CancellationToken cancellationToken)
    {
        PostgreSqlPreparedStatement statement = Prepare(_inventory, PostgreSqlSqlStatementId.ReadServerIdentity, []);

        return await ReadSingleRowAsync(
            statement,
            expectedFieldCount: 3,
            project: static reader => new PostgreSqlServerIdentity(
                reader.GetInt32(0),
                reader.GetString(1),
                reader.GetString(2)),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// C002 — asks whether the required catalog-metadata allowlist is readable. Requires exactly
    /// one row of one non-null boolean column.
    /// </summary>
    internal async ValueTask<bool> CheckCatalogMetadataAccessAsync(CancellationToken cancellationToken)
    {
        PostgreSqlPreparedStatement statement = Prepare(_inventory, PostgreSqlSqlStatementId.CheckCatalogMetadataAccess, []);

        return await ReadSingleRowAsync(
            statement,
            expectedFieldCount: 1,
            project: static reader => reader.GetBoolean(0),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// C003 — asks whether the optional usage-statistics views are readable. Requires exactly one
    /// row of one non-null boolean column.
    /// </summary>
    internal async ValueTask<bool> CheckUsageStatisticsAccessAsync(CancellationToken cancellationToken)
    {
        PostgreSqlPreparedStatement statement = Prepare(_inventory, PostgreSqlSqlStatementId.CheckUsageStatisticsAccess, []);

        return await ReadSingleRowAsync(
            statement,
            expectedFieldCount: 1,
            project: static reader => reader.GetBoolean(0),
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// C004 — reads the nullable statistics-reset timestamp. Requires exactly one row of one
    /// column; that column may be NULL, which is a valid answer meaning the server reported no
    /// reset. A non-null value must already be UTC — a non-zero offset is a mapping failure and is
    /// never normalised silently.
    /// </summary>
    internal async ValueTask<DateTimeOffset?> ReadStatisticsResetAsync(CancellationToken cancellationToken)
    {
        PostgreSqlPreparedStatement statement = Prepare(_inventory, PostgreSqlSqlStatementId.ReadStatisticsReset, []);

        return await ReadSingleRowAsync(
            statement,
            expectedFieldCount: 1,
            allowNullColumns: true,
            project: static reader =>
            {
                if (reader.IsNull(0))
                {
                    return (DateTimeOffset?)null;
                }

                DateTimeOffset value = reader.GetDateTimeOffset(0);
                if (value.Offset != TimeSpan.Zero)
                {
                    throw new PostgreSqlSqlResultShapeException();
                }

                return value;
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// D001 — reads one metadata row per eligible relation. The only multirecord statement in the
    /// inventory: zero rows is a valid answer, and each row must have exactly ten columns of which
    /// only the estimate may be NULL.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two schema arrays are bound, never interpolated. Rows are mapped one at a time as they
    /// are read, and a failure at any row abandons the whole read: no partial collection is ever
    /// returned.
    /// </para>
    /// <para>
    /// The reader is released through the same EDI-safe cleanup every other statement uses, so a
    /// shape failure, a mapping failure or a cancellation is never replaced by a disposal failure.
    /// </para>
    /// </remarks>
    internal async ValueTask<PostgreSqlTableSnapshotQueryResult> ReadTableSnapshotsAsync(
        PostgreSqlSchemaFilter filter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // A caller who has already asked to stop must not reach the server at all. Npgsql would
        // very likely refuse too, but the contract is stated here rather than inherited from
        // driver behaviour.
        cancellationToken.ThrowIfCancellationRequested();

        PostgreSqlPreparedStatement statement = Prepare(
            _inventory,
            PostgreSqlSqlStatementId.ReadTableSnapshots,
            [
                PostgreSqlSqlParameterValue.TextArray(1, filter.IncludedSchemas),
                PostgreSqlSqlParameterValue.TextArray(2, filter.ExcludedSchemas),
            ]);

        IPostgreSqlRowReader reader = await _gateway
            .ExecuteReaderAsync(statement, cancellationToken)
            .ConfigureAwait(false);

        // Deliberately not `await using`: that compiles to a try/finally in which a disposal
        // failure would replace the primary failure.
        ExceptionDispatchInfo? primary = null;
        var snapshots = new List<TableSnapshot>();
        try
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.FieldCount != TableSnapshotFieldCount)
                {
                    throw new PostgreSqlSqlResultShapeException();
                }

                for (var ordinal = 0; ordinal < TableSnapshotFieldCount; ordinal++)
                {
                    // Ordinal 5 — the row estimate — is the only column D001 may return as NULL.
                    if (ordinal != EstimatedRowCountOrdinal && reader.IsNull(ordinal))
                    {
                        throw new PostgreSqlSqlResultShapeException();
                    }
                }

                TableSnapshotRow row = ReadTableSnapshotRow(reader);

                snapshots.Add(PostgreSqlTableSnapshotMapper.Map(
                    row.SchemaName,
                    row.TableName,
                    row.RelationKind,
                    row.RelationPersistence,
                    row.IsPartition,
                    row.EstimatedRowCount,
                    row.TableSizeBytes,
                    row.IndexSizeBytes,
                    row.TotalSizeBytes,
                    row.HasPrimaryKey));
            }
        }
        catch (Exception exception)
        {
            // Transparent capture: nothing is inspected, classified, sanitized or rewritten.
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        ExceptionDispatchInfo? disposal = await PostgreSqlAsyncCleanup
            .RunAllAsync(reader.DisposeAsync)
            .ConfigureAwait(false);

        primary?.Throw();
        disposal?.Throw();

        // Reached only when every row mapped, so the result is always complete or absent.
        return new PostgreSqlTableSnapshotQueryResult(snapshots);
    }

    /// <summary>
    /// The ten typed values of one D001 row, captured before any of them is validated. Exists so
    /// the typed reads occupy one narrow, provable seam instead of being spread across a call.
    /// </summary>
    private readonly record struct TableSnapshotRow(
        string SchemaName,
        string TableName,
        string RelationKind,
        string RelationPersistence,
        bool IsPartition,
        long? EstimatedRowCount,
        long TableSizeBytes,
        long IndexSizeBytes,
        long TotalSizeBytes,
        bool HasPrimaryKey);

    /// <summary>
    /// Reads the ten typed columns of the current D001 row.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A column whose runtime type is not the one D001 promises makes the reader raise
    /// <see cref="InvalidCastException"/>. That is a bad row like any other, so it is translated
    /// here — at the exact seam where it can arise — into the fixed, valueless
    /// <see cref="PostgreSqlTableSnapshotMappingException"/>. Letting it escape would put a
    /// driver-authored message naming the CLR types, and potentially the offending value, on a
    /// surface that crosses the session boundary.
    /// </para>
    /// <para>
    /// The catch is deliberately narrow in both dimensions: one concrete exception type, and only
    /// the ten reads. Nothing is inspected, no message is parsed, no inner exception is attached
    /// and no <c>Data</c> entry is added. A cancellation, an Npgsql failure, a disposal failure or
    /// any other exception passes through completely untouched — none of them means "wrong type",
    /// and classifying them here would hide a real fault behind a mapping error.
    /// </para>
    /// </remarks>
    private static TableSnapshotRow ReadTableSnapshotRow(IPostgreSqlRowReader reader)
    {
        try
        {
            return new TableSnapshotRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetBoolean(4),
                reader.IsNull(EstimatedRowCountOrdinal) ? null : reader.GetInt64(EstimatedRowCountOrdinal),
                reader.GetInt64(6),
                reader.GetInt64(7),
                reader.GetInt64(8),
                reader.GetBoolean(9));
        }
        catch (InvalidCastException)
        {
            throw new PostgreSqlTableSnapshotMappingException();
        }
    }

    /// <summary>The exact column count D001 promises.</summary>
    private const int TableSnapshotFieldCount = 10;

    /// <summary>The only D001 column that may be NULL.</summary>
    private const int EstimatedRowCountOrdinal = 5;

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
