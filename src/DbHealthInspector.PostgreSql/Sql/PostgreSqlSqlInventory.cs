using System.Collections.ObjectModel;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The single canonical source of every statement the product may execute. Built once, validated
/// once, immutable thereafter, and addressable only by <see cref="PostgreSqlSqlStatementId"/>.
/// </summary>
/// <remarks>
/// <para>
/// GC-DHI-04B freezes the productive inventory at exactly three statements: B001
/// (<see cref="PostgreSqlSqlStatementId.SetTransactionReadOnly"/>), B002
/// (<see cref="PostgreSqlSqlStatementId.ApplyLocalTimeouts"/>) and B003
/// (<see cref="PostgreSqlSqlStatementId.VerifySessionState"/>), in that order.
/// </para>
/// <para>
/// There is no lookup by SQL text, no runtime registration, no external SQL file, no assembly
/// reflection scan and no mutable collection reachable from outside. Every definition passes
/// <see cref="PostgreSqlSqlSafetyValidator"/> during construction, so an inventory instance that
/// exists at all is one whose every statement has already been proven safe — the executor
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
    /// The process-wide canonical inventory.
    /// </summary>
    internal static PostgreSqlSqlInventory Default { get; } = new();

    private readonly Dictionary<PostgreSqlSqlStatementId, PostgreSqlSqlStatementDefinition> _byId;

    /// <summary>
    /// Every definition in canonical order: B001, B002, B003.
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
