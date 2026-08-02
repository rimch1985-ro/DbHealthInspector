namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The closed classification of command shapes the safety validator knows how to prove safe.
/// GC-DHI-04C freezes this enum at exactly six kinds; no kind for table or index queries exists
/// yet.
/// </summary>
internal enum PostgreSqlSqlCommandKind
{
    /// <summary>
    /// The single authorised <c>SET</c> form: <c>SET TRANSACTION READ ONLY</c> and nothing else.
    /// </summary>
    SetTransactionReadOnly,

    /// <summary>
    /// A <c>SELECT</c> whose only effect is applying transaction-local configuration through
    /// <c>pg_catalog.set_config(..., true)</c>.
    /// </summary>
    SelectConfiguration,

    /// <summary>
    /// A <c>SELECT</c> that only reads back effective session settings for verification.
    /// </summary>
    SelectVerification,

    /// <summary>
    /// A <c>SELECT</c> that reads the server's own identity — numeric version, database name and
    /// current user — and no user data.
    /// </summary>
    SelectServerIdentity,

    /// <summary>
    /// A <c>SELECT</c> that asks PostgreSQL whether the current user holds a set of privileges,
    /// returning only a boolean. It reads no catalog row and no user data.
    /// </summary>
    SelectCapabilityCheck,

    /// <summary>
    /// A <c>SELECT</c> over a server statistics view. It reads no business row.
    /// </summary>
    SelectStatistics,
}
