using System.Runtime.ExceptionServices;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Indexes;
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

    /// <summary>
    /// E001 + E002 — reads every eligible index, and merges the optional scan counters when the
    /// usage-statistics capability was reported available.
    /// </summary>
    /// <param name="filter">The already-validated schema filter, bound as two <c>text[]</c> arrays.</param>
    /// <param name="usageStatisticsAvailable">
    /// The capability probe's verdict. E002 runs exactly once when this is <see langword="true"/>
    /// and not at all when it is <see langword="false"/> — in which case every scan count stays
    /// <see langword="null"/>, which means <i>unknown</i> and never zero.
    /// </param>
    /// <param name="cancellationToken">Forwarded unchanged to both statements and every read.</param>
    /// <remarks>
    /// <para>
    /// E001 is streamed and grouped as it is read: rows of one index arrive consecutively because
    /// the statement orders by schema, table, index and attribute, so a group is finalised as soon
    /// as the identity changes. A failure at any row abandons the whole read — no partial
    /// collection is ever returned, including at end of rows.
    /// </para>
    /// <para>
    /// Both readers are released through the same EDI-safe cleanup every other statement uses, so a
    /// shape failure, a mapping failure or a cancellation is never replaced by a disposal failure.
    /// </para>
    /// </remarks>
    internal async ValueTask<PostgreSqlIndexSnapshotQueryResult> ReadIndexSnapshotsAsync(
        PostgreSqlSchemaFilter filter,
        bool usageStatisticsAvailable,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(filter);

        // A caller who has already asked to stop must not reach the server at all.
        cancellationToken.ThrowIfCancellationRequested();

        List<PostgreSqlIndexGroup> groups = await ReadIndexMetadataAsync(filter, cancellationToken)
            .ConfigureAwait(false);

        // Absence of statistics is a distinct state from a zero counter, so the map stays null
        // and every lookup misses, yielding null rather than 0.
        Dictionary<PostgreSqlIndexIdentity, long>? statistics = null;
        if (usageStatisticsAvailable)
        {
            // The E001 identities are handed to the E002 read so that every cross-statement
            // contradiction — a statistics row naming an index E001 never reported, or naming a
            // virtual one — is detected while the E002 reader is still open. Reconciling here
            // instead would let a reader-disposal failure preempt the semantic failure that must
            // stay primary (GC-DHI-04E §28).
            var knownIndexes = new Dictionary<PostgreSqlIndexIdentity, bool>(groups.Count);
            foreach (PostgreSqlIndexGroup group in groups)
            {
                knownIndexes[group.Identity] = group.IsVirtual;
            }

            statistics = await ReadIndexUsageStatisticsAsync(filter, knownIndexes, cancellationToken)
                .ConfigureAwait(false);
        }

        // Reached only with fully reconciled state: every retained statistic is already proven to
        // name a physical index E001 reported, so no lookup below can fail.
        var snapshots = new List<IndexSnapshot>(groups.Count);

        foreach (PostgreSqlIndexGroup group in groups)
        {
            long? scanCount =
                statistics is not null && statistics.TryGetValue(group.Identity, out long observed)
                    ? observed
                    : null;

            snapshots.Add(PostgreSqlIndexSnapshotMapper.Map(group.Rows, scanCount));
        }

        return new PostgreSqlIndexSnapshotQueryResult(snapshots);
    }

    /// <summary>The (schema, table, index) triple both statements are keyed by, compared ordinally.</summary>
    private readonly record struct PostgreSqlIndexIdentity(string SchemaName, string TableName, string IndexName);

    /// <summary>
    /// The (schema, index) pair that identifies an index in the final result, compared ordinally.
    /// </summary>
    /// <remarks>
    /// Deliberately narrower than <see cref="PostgreSqlIndexIdentity"/>. An index name is unique
    /// within its schema, not within its table, so two groups that differ only by table name are
    /// distinct <i>raw</i> groups yet the same <i>final</i> index — a contradiction that must be
    /// rejected. Because the two names can be arbitrarily far apart in the stream, this cannot be
    /// detected by comparing neighbours and needs a set spanning the whole read.
    /// </remarks>
    private readonly record struct PostgreSqlFinalIndexIdentity(string SchemaName, string IndexName);

    /// <summary>One index's complete set of E001 attribute rows, still unmapped.</summary>
    private sealed class PostgreSqlIndexGroup
    {
        internal PostgreSqlIndexGroup(PostgreSqlIndexIdentity identity, bool isVirtual, List<PostgreSqlIndexMetadataRow> rows)
        {
            Identity = identity;
            IsVirtual = isVirtual;
            Rows = rows;
        }

        internal PostgreSqlIndexIdentity Identity { get; }

        internal bool IsVirtual { get; }

        internal List<PostgreSqlIndexMetadataRow> Rows { get; }
    }

    /// <summary>
    /// Runs E001 and returns one group per index, in the order the server produced them.
    /// </summary>
    private async ValueTask<List<PostgreSqlIndexGroup>> ReadIndexMetadataAsync(
        PostgreSqlSchemaFilter filter,
        CancellationToken cancellationToken)
    {
        PostgreSqlPreparedStatement statement = Prepare(
            _inventory,
            PostgreSqlSqlStatementId.ReadIndexMetadata,
            [
                PostgreSqlSqlParameterValue.TextArray(1, filter.IncludedSchemas),
                PostgreSqlSqlParameterValue.TextArray(2, filter.ExcludedSchemas),
            ]);

        IPostgreSqlRowReader reader = await _gateway
            .ExecuteReaderAsync(statement, cancellationToken)
            .ConfigureAwait(false);

        ExceptionDispatchInfo? primary = null;
        var groups = new List<PostgreSqlIndexGroup>();
        var seen = new HashSet<PostgreSqlIndexIdentity>();

        // Spans the whole read rather than comparing neighbours: two groups sharing a final
        // identity may be separated by any number of unrelated groups.
        var seenFinal = new HashSet<PostgreSqlFinalIndexIdentity>();

        try
        {
            List<PostgreSqlIndexMetadataRow>? pending = null;
            PostgreSqlIndexIdentity pendingIdentity = default;

            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.FieldCount != PostgreSqlIndexMetadataRow.FieldCount)
                {
                    throw new PostgreSqlSqlResultShapeException();
                }

                // Nullability is checked explicitly, before any typed read, exactly as D001 does.
                // Relying on the driver to raise on a NULL would make the contract depend on
                // provider behaviour rather than on the shape E001 promises.
                foreach (int ordinal in RequiredIndexMetadataOrdinals)
                {
                    if (reader.IsNull(ordinal))
                    {
                        throw new PostgreSqlSqlResultShapeException();
                    }
                }

                PostgreSqlIndexMetadataRow row = ReadIndexMetadataRow(reader);
                var identity = new PostgreSqlIndexIdentity(row.SchemaName, row.TableName, row.IndexName);

                if (pending is null)
                {
                    pending = [row];
                    pendingIdentity = identity;
                    continue;
                }

                if (identity == pendingIdentity)
                {
                    pending.Add(row);
                    continue;
                }

                // Identity changed, so the previous group is complete.
                CloseGroup(groups, seen, seenFinal, pendingIdentity, pending);
                pending = [row];
                pendingIdentity = identity;
            }

            // End of rows finalises a valid pending group and rejects a malformed one; it never
            // discards it silently.
            if (pending is not null)
            {
                CloseGroup(groups, seen, seenFinal, pendingIdentity, pending);
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

        return groups;
    }

    /// <summary>
    /// Accepts one finished group, rejecting an index that appeared twice in the stream and one
    /// whose final identity collides with a group seen anywhere earlier.
    /// </summary>
    private static void CloseGroup(
        List<PostgreSqlIndexGroup> groups,
        HashSet<PostgreSqlIndexIdentity> seen,
        HashSet<PostgreSqlFinalIndexIdentity> seenFinal,
        PostgreSqlIndexIdentity identity,
        List<PostgreSqlIndexMetadataRow> rows)
    {
        // E001 orders by identity, so a repeat means the rows of one index were not contiguous —
        // the grouping assumption itself is broken, not merely a duplicate to collapse.
        if (!seen.Add(identity))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        // The raw check above cannot catch two groups that differ only by table name: both are
        // legitimately distinct raw identities, yet they would produce two entries with the same
        // final (schema, index) identity. They need not be adjacent even after ordering, so this
        // set spans the entire read rather than comparing each group with its predecessor.
        if (!seenFinal.Add(new PostgreSqlFinalIndexIdentity(identity.SchemaName, identity.IndexName)))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        // Validated here, while the reader is still open, so a malformed group is captured as the
        // primary failure. Deferring it until after the read would let a reader-disposal failure
        // preempt it, and the primary must always win (GC-DHI-04E §28). Both duplicate checks above
        // are inside the same protected block for exactly the same reason.
        PostgreSqlIndexSnapshotMapper.ValidateGroup(rows);

        // Inlined rather than extracted to a helper: no method on this type may accept a string,
        // because that is the invariant keeping a raw-SQL entry point impossible by construction.
        bool isVirtual = string.Equals(rows[0].IndexRelationKind, "I", StringComparison.Ordinal);

        groups.Add(new PostgreSqlIndexGroup(identity, isVirtual, rows));
    }

    /// <summary>
    /// Runs E002 and returns the scan counters keyed by index identity, fully reconciled against
    /// the indexes E001 reported.
    /// </summary>
    /// <param name="filter">The same schema filter E001 was bound with.</param>
    /// <param name="knownIndexes">
    /// Every identity E001 produced, mapped to whether that index is virtual. Supplied so the
    /// cross-statement rules can be enforced <b>inside</b> this method's protected block.
    /// </param>
    /// <param name="cancellationToken">Forwarded unchanged to the gateway and the reader.</param>
    /// <remarks>
    /// Every rule that can reject a statistics row — shape, negative counter, duplicate identity,
    /// an identity E001 never reported, and an identity naming a virtual index — is applied here,
    /// while the reader is still open. Nothing about the merge can fail after this method returns,
    /// so a disposal failure can never displace one of those semantic failures.
    /// </remarks>
    private async ValueTask<Dictionary<PostgreSqlIndexIdentity, long>> ReadIndexUsageStatisticsAsync(
        PostgreSqlSchemaFilter filter,
        IReadOnlyDictionary<PostgreSqlIndexIdentity, bool> knownIndexes,
        CancellationToken cancellationToken)
    {
        PostgreSqlPreparedStatement statement = Prepare(
            _inventory,
            PostgreSqlSqlStatementId.ReadIndexUsageStatistics,
            [
                PostgreSqlSqlParameterValue.TextArray(1, filter.IncludedSchemas),
                PostgreSqlSqlParameterValue.TextArray(2, filter.ExcludedSchemas),
            ]);

        IPostgreSqlRowReader reader = await _gateway
            .ExecuteReaderAsync(statement, cancellationToken)
            .ConfigureAwait(false);

        ExceptionDispatchInfo? primary = null;
        var statistics = new Dictionary<PostgreSqlIndexIdentity, long>();

        try
        {
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                if (reader.FieldCount != IndexUsageStatisticsFieldCount)
                {
                    throw new PostgreSqlSqlResultShapeException();
                }

                for (var ordinal = 0; ordinal < IndexUsageStatisticsFieldCount; ordinal++)
                {
                    // E002 declares all four columns non-nullable.
                    if (reader.IsNull(ordinal))
                    {
                        throw new PostgreSqlSqlResultShapeException();
                    }
                }

                (PostgreSqlIndexIdentity identity, long scanCount) = ReadIndexUsageStatisticsRow(reader);

                if (scanCount < 0)
                {
                    throw new PostgreSqlIndexUsageStatisticsMappingException();
                }

                // Two rows for one index would make the merge order-dependent; there is no
                // last-write-wins.
                if (!statistics.TryAdd(identity, scanCount))
                {
                    throw new PostgreSqlIndexUsageStatisticsMappingException();
                }

                // Every statistics row must name an index E001 also reported. One that does not
                // means the two statements saw different catalogs, which is never silently
                // ignored — and a virtual index has no storage and therefore no scan counter of
                // its own, so a row naming one is the same kind of disagreement.
                //
                // Both checks live here, inside the protected block, rather than after the read:
                // that is what keeps them primary when reader disposal also fails.
                if (!knownIndexes.TryGetValue(identity, out bool isVirtual) || isVirtual)
                {
                    throw new PostgreSqlIndexUsageStatisticsMappingException();
                }
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

        return statistics;
    }

    /// <summary>
    /// Reads the thirty-one typed columns of the current E001 row.
    /// </summary>
    /// <remarks>
    /// Identical discipline to D001's row seam: a column whose runtime type is not the one E001
    /// promises makes the reader raise <see cref="InvalidCastException"/>, and that is translated
    /// here — around these reads and nowhere else — into the fixed, valueless mapping exception, so
    /// no driver-authored message naming CLR types or values escapes. A cancellation, an Npgsql
    /// failure or any other exception passes through untouched.
    /// </remarks>
    private static PostgreSqlIndexMetadataRow ReadIndexMetadataRow(IPostgreSqlRowReader reader)
    {
        try
        {
            return new PostgreSqlIndexMetadataRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetBoolean(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetBoolean(9),
                reader.IsNull(10) ? null : reader.GetString(10),
                reader.IsNull(11) ? null : reader.GetString(11),
                reader.IsNull(12) ? null : reader.GetString(12),
                reader.IsNull(13) ? null : reader.GetString(13),
                reader.IsNull(14) ? null : reader.GetString(14),
                reader.IsNull(15) ? null : reader.GetString(15),
                reader.IsNull(16) ? null : reader.GetStringArray(16),
                reader.IsNull(17) ? null : reader.GetBoolean(17),
                reader.IsNull(18) ? null : reader.GetBoolean(18),
                reader.IsNull(19) ? null : reader.GetBoolean(19),
                reader.IsNull(20) ? null : reader.GetBoolean(20),
                reader.IsNull(21) ? null : reader.GetBoolean(21),
                reader.IsNull(22) ? null : reader.GetString(22),
                reader.GetBoolean(23),
                reader.IsNull(24) ? null : reader.GetBoolean(24),
                reader.GetBoolean(25),
                reader.GetBoolean(26),
                reader.GetBoolean(27),
                reader.GetBoolean(28),
                reader.GetBoolean(29),
                reader.GetInt64(30));
        }
        catch (InvalidCastException)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }
    }

    private static (PostgreSqlIndexIdentity Identity, long ScanCount) ReadIndexUsageStatisticsRow(
        IPostgreSqlRowReader reader)
    {
        try
        {
            return (
                new PostgreSqlIndexIdentity(reader.GetString(0), reader.GetString(1), reader.GetString(2)),
                reader.GetInt64(3));
        }
        catch (InvalidCastException)
        {
            throw new PostgreSqlIndexUsageStatisticsMappingException();
        }
    }

    /// <summary>
    /// The E001 ordinals the frozen shape declares non-nullable. Ordinals 10–22 describe one
    /// attribute and are legitimately NULL for an INCLUDE column or an inapplicable property, and
    /// ordinal 24 is NULL for every non-unique index; those are the only ones omitted.
    /// </summary>
    private static readonly int[] RequiredIndexMetadataOrdinals =
        [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 23, 25, 26, 27, 28, 29, 30];

    /// <summary>The exact column count E002 promises.</summary>
    private const int IndexUsageStatisticsFieldCount = 4;

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
