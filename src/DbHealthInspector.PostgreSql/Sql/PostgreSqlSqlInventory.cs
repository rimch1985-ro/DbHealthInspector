using System.Collections.ObjectModel;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The single canonical source of every statement the product may execute. Built once, validated
/// once, immutable thereafter, and addressable only by <see cref="PostgreSqlSqlStatementId"/>.
/// </summary>
/// <remarks>
/// <para>
/// GC-DHI-04E freezes the productive inventory at exactly ten statements, in this order: the three
/// session-initialization statements — B001
/// (<see cref="PostgreSqlSqlStatementId.SetTransactionReadOnly"/>), B002
/// (<see cref="PostgreSqlSqlStatementId.ApplyLocalTimeouts"/>) and B003
/// (<see cref="PostgreSqlSqlStatementId.VerifySessionState"/>) — followed by the four
/// capability-probe statements: C001
/// (<see cref="PostgreSqlSqlStatementId.ReadServerIdentity"/>), C002
/// (<see cref="PostgreSqlSqlStatementId.CheckCatalogMetadataAccess"/>), C003
/// (<see cref="PostgreSqlSqlStatementId.CheckUsageStatisticsAccess"/>) and C004
/// (<see cref="PostgreSqlSqlStatementId.ReadStatisticsReset"/>) — and finally the single
/// table-snapshot query D001
/// (<see cref="PostgreSqlSqlStatementId.ReadTableSnapshots"/>), structural index query E001
/// (<see cref="PostgreSqlSqlStatementId.ReadIndexMetadata"/>) and optional usage-statistics query
/// E002 (<see cref="PostgreSqlSqlStatementId.ReadIndexUsageStatistics"/>).
/// </para>
/// <para>
/// B001–B003 are reserved to the session runner and are unreachable from an authorized operation;
/// C001–C004, D001 and the composite E001/E002 index-snapshot read are the typed operations an
/// authorized callback may run. An eleventh productive statement requires a later authorised gate.
/// </para>
/// <para>
/// There is no lookup by SQL text, no runtime registration, no external SQL file, no assembly
/// reflection scan and no mutable collection reachable from outside. Every definition passes both
/// layers of <see cref="PostgreSqlSqlSafetyValidator"/> during construction — the lexical scan and
/// the frozen statement contract — so an inventory instance that exists at all is one whose every
/// statement has already been proven to be one of the ten canonical definitions. The executor
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
        AND pg_catalog.has_function_privilege(
            current_user,
            'pg_catalog.pg_relation_size(regclass)',
            'EXECUTE')
        AND pg_catalog.has_function_privilege(
            current_user,
            'pg_catalog.pg_get_indexdef(oid,integer,boolean)',
            'EXECUTE')
        AND pg_catalog.has_function_privilege(
            current_user,
            'pg_catalog.pg_get_expr(pg_node_tree,oid,boolean)',
            'EXECUTE')
        AND pg_catalog.has_function_privilege(
            current_user,
            'pg_catalog.pg_index_column_has_property(regclass,integer,text)',
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
    /// E001 — one metadata row per index attribute. Frozen character-for-character by
    /// GC-DHI-04E §9. It reads <c>pg_catalog</c> index metadata plus
    /// <c>pg_relation_size</c>, <c>pg_get_indexdef</c>, <c>pg_get_expr</c> and
    /// <c>pg_index_column_has_property</c> only: no business row, no <c>COUNT(*)</c>, no
    /// dynamic identifier, no concatenated schema name, and no descendant aggregation. The two
    /// schema filters arrive exclusively as bound <c>text[]</c> parameters.
    /// </summary>
    internal const string ReadIndexMetadataSql = """
        SELECT
            table_namespace.nspname::text
                AS schema_name,
            table_relation.relname::text
                AS table_name,
            index_relation.relname::text
                AS index_name,
            access_method.amname::text
                AS access_method,
            index_relation.relkind::text
                AS index_relation_kind,
            index_relation.relispartition
                AS is_index_partition,
            index_record.indnatts::integer
                AS attribute_count,
            index_record.indnkeyatts::integer
                AS key_attribute_count,
            index_attribute.attnum::integer
                AS attribute_position,
            (index_attribute.attnum <= index_record.indnkeyatts)
                AS is_key,
            CASE
                WHEN index_record.indkey[index_attribute.attnum - 1] <> 0
                    THEN table_attribute.attname::text
                ELSE NULL::text
            END
                AS column_name,
            CASE
                WHEN index_attribute.attnum <= index_record.indnkeyatts
                     AND index_record.indkey[index_attribute.attnum - 1] = 0
                    THEN pg_catalog.pg_get_indexdef(
                        index_relation.oid,
                        index_attribute.attnum,
                        false)
                ELSE NULL::text
            END
                AS expression,
            collation_namespace.nspname::text
                AS collation_schema,
            collation_record.collname::text
                AS collation_name,
            operator_class_namespace.nspname::text
                AS operator_class_schema,
            operator_class.opcname::text
                AS operator_class_name,
            CASE
                WHEN index_attribute.attnum <= index_record.indnkeyatts
                    THEN index_attribute.attoptions
                ELSE NULL::text[]
            END
                AS operator_class_options,
            CASE
                WHEN index_attribute.attnum <= index_record.indnkeyatts
                    THEN pg_catalog.pg_index_column_has_property(
                        index_relation.oid,
                        index_attribute.attnum,
                        'orderable')
                ELSE NULL::boolean
            END
                AS is_orderable,
            CASE
                WHEN index_attribute.attnum <= index_record.indnkeyatts
                    THEN pg_catalog.pg_index_column_has_property(
                        index_relation.oid,
                        index_attribute.attnum,
                        'asc')
                ELSE NULL::boolean
            END
                AS is_ascending,
            CASE
                WHEN index_attribute.attnum <= index_record.indnkeyatts
                    THEN pg_catalog.pg_index_column_has_property(
                        index_relation.oid,
                        index_attribute.attnum,
                        'desc')
                ELSE NULL::boolean
            END
                AS is_descending,
            CASE
                WHEN index_attribute.attnum <= index_record.indnkeyatts
                    THEN pg_catalog.pg_index_column_has_property(
                        index_relation.oid,
                        index_attribute.attnum,
                        'nulls_first')
                ELSE NULL::boolean
            END
                AS nulls_first,
            CASE
                WHEN index_attribute.attnum <= index_record.indnkeyatts
                    THEN pg_catalog.pg_index_column_has_property(
                        index_relation.oid,
                        index_attribute.attnum,
                        'nulls_last')
                ELSE NULL::boolean
            END
                AS nulls_last,
            CASE
                WHEN index_record.indpred IS NULL
                    THEN NULL::text
                ELSE pg_catalog.pg_get_expr(
                    index_record.indpred,
                    index_record.indrelid,
                    false)
            END
                AS partial_predicate,
            index_record.indisunique
                AS is_unique,
            CASE
                WHEN index_record.indisunique
                    THEN index_record.indnullsnotdistinct
                ELSE NULL::boolean
            END
                AS nulls_not_distinct,
            index_record.indisprimary
                AS is_primary_key,
            EXISTS (
                SELECT 1
                FROM pg_catalog.pg_constraint AS constraint_record
                WHERE constraint_record.conindid = index_relation.oid
                  AND constraint_record.contype IN ('p', 'u', 'x')
            )
                AS backs_constraint,
            index_record.indisvalid
                AS is_valid,
            index_record.indisready
                AS is_ready,
            index_record.indislive
                AS is_live,
            CASE index_relation.relkind
                WHEN 'i' THEN pg_catalog.pg_relation_size(index_relation.oid)
                WHEN 'I' THEN 0::bigint
            END
                AS size_bytes
        FROM pg_catalog.pg_index AS index_record
        INNER JOIN pg_catalog.pg_class AS index_relation
            ON index_relation.oid = index_record.indexrelid
        INNER JOIN pg_catalog.pg_class AS table_relation
            ON table_relation.oid = index_record.indrelid
        INNER JOIN pg_catalog.pg_namespace AS table_namespace
            ON table_namespace.oid = table_relation.relnamespace
        INNER JOIN pg_catalog.pg_am AS access_method
            ON access_method.oid = index_relation.relam
        INNER JOIN pg_catalog.pg_attribute AS index_attribute
            ON index_attribute.attrelid = index_relation.oid
           AND index_attribute.attnum > 0
           AND index_attribute.attnum <= index_record.indnatts
           AND NOT index_attribute.attisdropped
        LEFT JOIN pg_catalog.pg_attribute AS table_attribute
            ON table_attribute.attrelid = table_relation.oid
           AND table_attribute.attnum =
               index_record.indkey[index_attribute.attnum - 1]
           AND NOT table_attribute.attisdropped
        LEFT JOIN pg_catalog.pg_collation AS collation_record
            ON index_attribute.attnum <= index_record.indnkeyatts
           AND collation_record.oid =
               index_record.indcollation[index_attribute.attnum - 1]
        LEFT JOIN pg_catalog.pg_namespace AS collation_namespace
            ON collation_namespace.oid = collation_record.collnamespace
        LEFT JOIN pg_catalog.pg_opclass AS operator_class
            ON index_attribute.attnum <= index_record.indnkeyatts
           AND operator_class.oid =
               index_record.indclass[index_attribute.attnum - 1]
        LEFT JOIN pg_catalog.pg_namespace AS operator_class_namespace
            ON operator_class_namespace.oid = operator_class.opcnamespace
        WHERE index_relation.relkind IN ('i', 'I')
          AND table_namespace.nspname <> 'pg_catalog'
          AND table_namespace.nspname <> 'information_schema'
          AND table_namespace.nspname NOT LIKE 'pg_toast%'
          AND table_namespace.nspname NOT LIKE 'pg_temp_%'
          AND (
              pg_catalog.cardinality($1::text[]) = 0
              OR table_namespace.nspname::text = ANY($1::text[])
          )
          AND NOT (
              table_namespace.nspname::text = ANY($2::text[])
          )
        ORDER BY
            table_namespace.nspname,
            table_relation.relname,
            index_relation.relname,
            index_attribute.attnum
        """;

    /// <summary>
    /// E002 — one usage-statistics row per index. Frozen character-for-character by
    /// GC-DHI-04E §10. Executed only when the optional usage-statistics capability is
    /// available; its absence yields a null scan count, never zero.
    /// </summary>
    internal const string ReadIndexUsageStatisticsSql = """
        SELECT
            statistics.schemaname::text
                AS schema_name,
            statistics.relname::text
                AS table_name,
            statistics.indexrelname::text
                AS index_name,
            statistics.idx_scan::bigint
                AS scan_count
        FROM pg_catalog.pg_stat_all_indexes AS statistics
        WHERE statistics.schemaname <> 'pg_catalog'
          AND statistics.schemaname <> 'information_schema'
          AND statistics.schemaname NOT LIKE 'pg_toast%'
          AND statistics.schemaname NOT LIKE 'pg_temp_%'
          AND (
              pg_catalog.cardinality($1::text[]) = 0
              OR statistics.schemaname::text = ANY($1::text[])
          )
          AND NOT (
              statistics.schemaname::text = ANY($2::text[])
          )
        ORDER BY
            statistics.schemaname,
            statistics.relname,
            statistics.indexrelname
        """;

    /// <summary>
    /// The process-wide canonical inventory.
    /// </summary>
    internal static PostgreSqlSqlInventory Default { get; } = new();

    private readonly Dictionary<PostgreSqlSqlStatementId, PostgreSqlSqlStatementDefinition> _byId;

    /// <summary>
    /// Every definition in canonical order: B001, B002, B003, C001, C002, C003, C004, D001, E001,
    /// E002.
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

            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.ReadIndexMetadata,
                PostgreSqlSqlCommandKind.SelectIndexMetadata,
                ReadIndexMetadataSql,
                [
                    new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.TextArray, "included schema names"),
                    new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.TextArray, "excluded schema names"),
                ],
                "Reads index metadata for eligible indexes only, never a business row."),

            new PostgreSqlSqlStatementDefinition(
                PostgreSqlSqlStatementId.ReadIndexUsageStatistics,
                PostgreSqlSqlCommandKind.SelectStatistics,
                ReadIndexUsageStatisticsSql,
                [
                    new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.TextArray, "included schema names"),
                    new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.TextArray, "excluded schema names"),
                ],
                "Reads optional per-index scan counters; absence means unknown, never zero."),
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
