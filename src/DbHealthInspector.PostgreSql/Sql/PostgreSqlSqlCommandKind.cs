namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The closed classification of command shapes the safety validator knows how to prove safe.
/// GC-DHI-04B needs exactly these three; no kind for capability, table or index queries exists
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
}
