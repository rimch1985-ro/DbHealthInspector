namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The closed set of statement identifiers the productive SQL inventory recognises. GC-DHI-04E
/// freezes this enum at exactly ten members — the three session-initialization statements
/// (B001–B003), the four capability-probe statements (C001–C004), the table-snapshot query (D001)
/// and the two index statements (E001–E002). An eleventh productive statement requires a later
/// authorised gate.
/// </summary>
internal enum PostgreSqlSqlStatementId
{
    /// <summary>
    /// B001 — establishes read-only transaction mode. Always the first statement executed
    /// inside the inspection transaction.
    /// </summary>
    SetTransactionReadOnly,

    /// <summary>
    /// B002 — applies the three transaction-local timeouts.
    /// </summary>
    ApplyLocalTimeouts,

    /// <summary>
    /// B003 — reads back the effective session state so it can be verified before any
    /// authorised operation runs.
    /// </summary>
    VerifySessionState,

    /// <summary>
    /// C001 — reads the numeric server version, database name and current user.
    /// </summary>
    ReadServerIdentity,

    /// <summary>
    /// C002 — checks the required catalog-metadata access allowlist.
    /// </summary>
    CheckCatalogMetadataAccess,

    /// <summary>
    /// C003 — checks the optional usage-statistics access.
    /// </summary>
    CheckUsageStatisticsAccess,

    /// <summary>
    /// C004 — reads the nullable statistics-reset timestamp.
    /// </summary>
    ReadStatisticsReset,

    /// <summary>
    /// D001 — reads one metadata row per eligible table-like relation.
    /// </summary>
    ReadTableSnapshots,

    /// <summary>
    /// E001 — reads one metadata row per index attribute, for every eligible index.
    /// </summary>
    ReadIndexMetadata,

    /// <summary>
    /// E002 — reads the optional per-index scan counters. Executed only when the usage-statistics
    /// capability is available.
    /// </summary>
    ReadIndexUsageStatistics,
}
