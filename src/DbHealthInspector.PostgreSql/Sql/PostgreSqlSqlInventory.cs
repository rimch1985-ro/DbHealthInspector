using System.Collections.ObjectModel;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The single canonical source of every statement the product may execute. Built once, validated
/// once, immutable thereafter, and addressable only by <see cref="PostgreSqlSqlStatementId"/>.
/// </summary>
/// <remarks>
/// <para>
/// GC-DHI-04D freezes the productive inventory at exactly eight statements, in this order: the
/// three session-initialization statements — B001
/// (<see cref="PostgreSqlSqlStatementId.SetTransactionReadOnly"/>), B002
/// (<see cref="PostgreSqlSqlStatementId.ApplyLocalTimeouts"/>) and B003
/// (<see cref="PostgreSqlSqlStatementId.VerifySessionState"/>) — followed by the four
/// capability-probe statements: C001
/// (<see cref="PostgreSqlSqlStatementId.ReadServerIdentity"/>), C002
/// (<see cref="PostgreSqlSqlStatementId.CheckCatalogMetadataAccess"/>), C003
/// (<see cref="PostgreSqlSqlStatementId.CheckUsageStatisticsAccess"/>) and C004
/// (<see cref="PostgreSqlSqlStatementId.ReadStatisticsReset"/>) — and finally the single
/// table-snapshot query D001
/// (<see cref="PostgreSqlSqlStatementId.ReadTableSnapshots"/>).
/// </para>
/// <para>
/// B001–B003 are reserved to the session runner and are unreachable from an authorized operation;
/// C001–C004 and D001 are the typed operations an authorized callback may run. A ninth productive
/// statement — including any index query — requires a later authorised gate.
/// </para>
/// <para>
/// There is no lookup by SQL text, no runtime registration, no external SQL file, no assembly
/// reflection scan and no mutable collection reachable from outside. Every definition passes both
/// layers of <see cref="PostgreSqlSqlSafetyValidator"/> during construction — the lexical scan and
/// the frozen statement contract — so an inventory instance that exists at all is one whose every
/// statement has already been proven to be one of the eight canonical definitions. The executor
/// therefore never re-parses SQL on the hot path.
/// </para>
/// </remarks>
internal sealed class PostgreSqlSqlInventory
{
    /// <summary>
    /// B001 — establishes read-only mode. Must be the first statement in the transaction.
    /// </summary>
    internal const string SetTransactionReadOnlySql = "SET TRANSACTION READ ONLY";

    /// <summary>
    /// B002 — applies all three timeouts transaction-locally. The <c>|| 'ms'</c> concatenation is
    /// part of this fixed, static text: it appends a unit suffix to an already-bound integer
    /// parameter and never splices a caller value into the SQL.
    /// </summary>
    internal const string ApplyLocalTimeoutsSql = """
        SELECT
            pg_catalog.set_config(
                'statement_timeout',
                $1::text || 'ms',
                true),
            pg_catalog.set_config(
                'lock_timeout',
                $2::text || 'ms',
                true),
            pg_catalog.set_config(
                'idle_in_transaction_session_timeout',
                $3::text || 'ms',
                true)
        """;

    /// <summary>
    /// B003 — reads back effective state so the runner can refuse to proceed unless every value
    /// matches.
    /// </summary>
    internal const string VerifySessionStateSql = """
        SELECT
            pg_catalog.current_setting(
                'transaction_read_only')::boolean
                AS is_read_only,
            pg_catalog.current_setting(
                'transaction_isolation')
                AS isolation_level,
            pg_catalog.current_setting(
                'statement_timeout')::interval
                = ($1 * interval '1 millisecond')
                AS statement_timeout_matches,
            pg_catalog.current_setting(
                'lock_timeout')::interval
                = ($2 * interval '1 millisecond')
                AS lock_timeout_matches,
            pg_catalog.current_setting(
                'idle_in_transaction_session_timeout')::interval
                = ($3 * interval '1 millisecond')
                AS idle_timeout_matches
        """;

    /// <summary>
    /// C001 — reads the machine-readable server version plus the database name and current user.
    /// It reads the server's own identity only: no catalog row and no business row.
    /// </summary>
    internal const string ReadServerIdentitySql = """
        SELECT
            pg_catalog.current_setting(
                'server_version_num')::integer
                AS server_version_number,
            pg_catalog.current_database()::text
                AS database_name,
            current_user::text
                AS current_user
        """;

    /// <summary>
    /// C002 — the required catalog-metadata allowlist, plus the three relation-size functions
    /// D001 calls. Every relation and function named here is one the product needs; the list is a
    /// frozen baseline, and anything GC-DHI-04E needs beyond it must be added explicitly in its
    /// own gate. It asks PostgreSQL about privileges only and reads no catalog row.
    /// <para>
    /// The three <c>has_function_privilege</c> checks are reproduced verbatim from GC-DHI-04D §8,
    /// including that section's indentation, so the added text can be diffed against the
    /// definition character for character. SQL is whitespace-insensitive, so this affects nothing
    /// but the literal's appearance.
    /// </para>
    /// </summary>
    internal const string CheckCatalogMetadataAccessSql = """
        SELECT
            pg_catalog.has_schema_privilege(
                current_user,
                'pg_catalog',
                'USAGE')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_namespace',
                'SELECT')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_class',
                'SELECT')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_inherits',
                'SELECT')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_index',
                'SELECT')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_attribute',
                'SELECT')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_am',
                'SELECT')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_constraint',
                'SELECT')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_collation',
                'SELECT')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_opclass',
                'SELECT')
        AND pg_catalog.has_function_privilege(
            current_user,
            'pg_catalog.pg_table_size(regclass)',
            'EXECUTE')
        AND pg_catalog.has_function_privilege(
            current_user,
            'pg_catalog.pg_indexes_size(regclass)',
            'EXECUTE')
        AND pg_catalog.has_function_privilege(
            current_user,
            'pg_catalog.pg_total_relation_size(regclass)',
            'EXECUTE')
                AS catalog_metadata_available
        """;

    /// <summary>
    /// C003 — the optional usage-statistics check. It accepts any effective privilege path
    /// PostgreSQL recognises and deliberately does not require direct membership in a predefined
    /// role.
    /// </summary>
    internal const string CheckUsageStatisticsAccessSql = """
        SELECT
            pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_stat_database',
                'SELECT')
            AND pg_catalog.has_table_privilege(
                current_user,
                'pg_catalog.pg_stat_all_indexes',
                'SELECT')
                AS usage_statistics_available
        """;

    /// <summary>
    /// C004 — the nullable statistics-reset timestamp for the current database. A NULL means the
    /// server reported no reset, which does not make the capability unavailable.
    /// </summary>
    internal const string ReadStatisticsResetSql = """
        SELECT
            statistics.stats_reset
        FROM pg_catalog.pg_stat_database AS statistics
        WHERE statistics.datname = pg_catalog.current_database()
        """;

    /// <summary>
    /// D001 — one metadata row per eligible table-like relation. Frozen character-for-character by
    /// GC-DHI-04D §9. It reads <c>pg_catalog</c> relation metadata and the three relation-size
    /// functions only: no business row, no <c>COUNT(*)</c>, no dynamic identifier, no concatenated
    /// schema name. The two schema filters arrive exclusively as bound <c>text[]</c> parameters.
    /// </summary>
    internal const string ReadTableSnapshotsSql = """
        SELECT
            namespace.nspname::text
                AS schema_name,
            relation.relname::text
                AS table_name,
            relation.relkind::text
                AS relation_kind,
            relation.relpersistence::text
                AS relation_persistence,
            relation.relispartition
                AS is_partition,
            CASE
                WHEN relation.relkind = 'v'
                    OR relation.reltuples < 0
                    THEN NULL::bigint
                ELSE relation.reltuples::bigint
            END
                AS estimated_row_count,
            CASE
                WHEN relation.relkind IN ('r', 'm', 'p')
                    THEN pg_catalog.pg_table_size(relation.oid)
                ELSE 0::bigint
            END
                AS table_size_bytes,
            CASE
                WHEN relation.relkind IN ('r', 'm', 'p')
                    THEN pg_catalog.pg_indexes_size(relation.oid)
                ELSE 0::bigint
            END
                AS index_size_bytes,
            CASE
                WHEN relation.relkind IN ('r', 'm', 'p')
                    THEN pg_catalog.pg_total_relation_size(relation.oid)
                ELSE 0::bigint
            END
                AS total_size_bytes,
            EXISTS (
                SELECT 1
                FROM pg_catalog.pg_constraint AS constraint_record
                WHERE constraint_record.conrelid = relation.oid
                  AND constraint_record.contype = 'p'
            )
                AS has_primary_key
        FROM pg_catalog.pg_class AS relation
        INNER JOIN pg_catalog.pg_namespace AS namespace
            ON namespace.oid = relation.relnamespace
        WHERE relation.relkind IN ('r', 'p', 'v', 'm', 'f')
          AND namespace.nspname <> 'pg_catalog'
          AND namespace.nspname <> 'information_schema'
          AND namespace.nspname NOT LIKE 'pg_toast%'
          AND namespace.nspname NOT LIKE 'pg_temp_%'
          AND (
              pg_catalog.cardinality($1::text[]) = 0
              OR namespace.nspname::text = ANY($1::text[])
          )
          AND NOT (
              namespace.nspname::text = ANY($2::text[])
          )
        ORDER BY
            namespace.nspname,
            relation.relname
        """;

    /// <summary>
    /// The process-wide canonical inventory.
    /// </summary>
    internal static PostgreSqlSqlInventory Default { get; } = new();

    private readonly Dictionary<PostgreSqlSqlStatementId, PostgreSqlSqlStatementDefinition> _byId;

    /// <summary>
    /// Every definition in canonical order: B001, B002, B003, C001, C002, C003, C004, D001.
    /// </summary>
    internal ReadOnlyCollection<PostgreSqlSqlStatementDefinition> Statements { get; }

    internal PostgreSqlSqlInventory()
    {
        PostgreSqlSqlStatementDefinition[] definitions =
        [
            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlCommandKind.SetTransactionReadOnly,
                SetTransactionReadOnlySql,
                [],
                "Establishes read-only transaction mode before any query runs."),

            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlCommandKind.SelectConfiguration,
                ApplyLocalTimeoutsSql,
                [
                    new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "statement-timeout milliseconds"),
                    new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.Int32, "lock-timeout milliseconds"),
                    new PostgreSqlSqlParameterDefinition(3, PostgreSqlSqlParameterType.Int32, "idle-in-transaction-timeout milliseconds"),
                ],
                "Bounds every later statement with transaction-local timeouts."),

            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.VerifySessionState,
                PostgreSqlSqlCommandKind.SelectVerification,
                VerifySessionStateSql,
                [
                    new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "statement-timeout milliseconds"),
                    new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.Int32, "lock-timeout milliseconds"),
                    new PostgreSqlSqlParameterDefinition(3, PostgreSqlSqlParameterType.Int32, "idle-in-transaction-timeout milliseconds"),
                ],
                "Refuses to run an authorized operation unless the effective state is provably safe."),

            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlCommandKind.SelectServerIdentity,
                ReadServerIdentitySql,
                [],
                "Supplies the machine-readable version the supported-range policy is decided from."),

            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
                PostgreSqlSqlCommandKind.SelectCapabilityCheck,
                CheckCatalogMetadataAccessSql,
                [],
                "Refuses to inspect at all unless the required catalog metadata is reachable."),

            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.CheckUsageStatisticsAccess,
                PostgreSqlSqlCommandKind.SelectCapabilityCheck,
                CheckUsageStatisticsAccessSql,
                [],
                "Decides whether optional usage statistics may be read, before reading any."),

            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.ReadStatisticsReset,
                PostgreSqlSqlCommandKind.SelectStatistics,
                ReadStatisticsResetSql,
                [],
                "Reports when counters were last reset so later findings can be qualified."),

            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.ReadTableSnapshots,
                PostgreSqlSqlCommandKind.SelectTableMetadata,
                ReadTableSnapshotsSql,
                [
                    new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.TextArray, "included schema names"),
                    new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.TextArray, "excluded schema names"),
                ],
                "Reads relation metadata for eligible tables only, never a business row."),
        ];

        _byId = new Dictionary<PostgreSqlSqlStatementId, PostgreSqlSqlStatementDefinition>(definitions.Length);
        foreach (PostgreSqlSqlStatementDefinition definition in definitions)
        {
            if (!_byId.TryAdd(definition.Id, definition))
            {
                throw new InvalidOperationException("The PostgreSQL SQL inventory contains a duplicate statement id.");
            }

            PostgreSqlSqlSafetyValidator.Validate(definition);
        }

        Statements = Array.AsReadOnly(definitions);
    }

    /// <summary>
    /// Resolves the definition registered for <paramref name="id"/>. The only lookup the
    /// inventory offers: there is deliberately no overload accepting SQL text.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="id"/> is not a registered statement id.
    /// </exception>
    internal PostgreSqlSqlStatementDefinition Resolve(PostgreSqlSqlStatementId id) =>
        _byId.TryGetValue(id, out PostgreSqlSqlStatementDefinition? definition)
            ? definition
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown PostgreSQL statement id.");
}
