namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The closed classification of command shapes the safety validator knows how to prove safe.
/// GC-DHI-04E freezes this enum at exactly eight kinds.
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

    /// <summary>
    /// A multirecord <c>SELECT</c> over <c>pg_catalog</c> relation metadata, filtered by two
    /// bound schema arrays. It reads catalog rows and relation sizes only — never a business row.
    /// </summary>
    SelectTableMetadata,

    /// <summary>
    /// A multirecord <c>SELECT</c> over <c>pg_catalog</c> index metadata, filtered by two bound
    /// schema arrays. It reads catalog rows, index definitions and index sizes only — never a
    /// business row.
    /// </summary>
    /// <remarks>
    /// E002 deliberately does <b>not</b> use this kind: it reads a statistics view and therefore
    /// stays under <see cref="SelectStatistics"/>, the kind C004 already uses. The frozen contract
    /// still binds each statement id to exactly one SQL text, so sharing a kind grants nothing.
    /// </remarks>
    SelectIndexMetadata,
}
