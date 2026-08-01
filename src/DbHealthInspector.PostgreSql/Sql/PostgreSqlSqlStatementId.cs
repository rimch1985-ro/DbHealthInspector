namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The closed set of statement identifiers the productive SQL inventory recognises. GC-DHI-04B
/// freezes this enum at exactly three members (B001, B002, B003); a fourth productive statement
/// requires a later authorised gate.
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
}
