namespace DbHealthInspector.PostgreSql.Indexes;

/// <summary>
/// Raised when an E001 row, or a group of them, cannot be mapped to a valid <c>IndexSnapshot</c>.
/// </summary>
/// <remarks>
/// Carries a fixed message and never the offending row. A schema name, table name, index name,
/// expression, predicate, collation, operator class, operator-class option or size is all either
/// customer structure or server detail, and this exception can cross into a session failure surface
/// — so the only information it conveys is that a row was rejected. <c>InnerException</c> is always
/// <see langword="null"/> and <c>Data</c> is always empty.
/// </remarks>
internal sealed class PostgreSqlIndexSnapshotMappingException : Exception
{
    private const string SanitizedMessage = "The PostgreSQL index metadata row is invalid.";

    /// <summary>
    /// Creates the sanitized mapping exception. There is deliberately no constructor taking a
    /// message or an inner exception, so no code path anywhere in the assembly can attach one.
    /// </summary>
    internal PostgreSqlIndexSnapshotMappingException()
        : base(SanitizedMessage)
    {
    }
}
