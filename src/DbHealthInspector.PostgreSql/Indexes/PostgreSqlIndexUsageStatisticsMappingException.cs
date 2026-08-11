namespace DbHealthInspector.PostgreSql.Indexes;

/// <summary>
/// Raised when an E002 row cannot be reconciled with the index metadata E001 produced.
/// </summary>
/// <remarks>
/// Distinct from <see cref="PostgreSqlIndexSnapshotMappingException"/> so a statistics fault is
/// never reported as a metadata fault, but identical in discipline: a fixed message, no inner
/// exception, empty <c>Data</c>, and no schema, table, index or counter value.
/// </remarks>
internal sealed class PostgreSqlIndexUsageStatisticsMappingException : Exception
{
    private const string SanitizedMessage = "The PostgreSQL index usage statistics row is invalid.";

    /// <summary>
    /// Creates the sanitized statistics exception. There is deliberately no constructor taking a
    /// message or an inner exception.
    /// </summary>
    internal PostgreSqlIndexUsageStatisticsMappingException()
        : base(SanitizedMessage)
    {
    }
}
