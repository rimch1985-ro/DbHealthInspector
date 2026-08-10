namespace DbHealthInspector.PostgreSql.Tables;

/// <summary>
/// Raised when a caller supplies a schema filter this adapter refuses to build.
/// </summary>
/// <remarks>
/// Carries a fixed message and never the offending name. A schema name is caller data that may
/// identify a customer's structure, and this exception can cross into a session failure surface,
/// so the only information it conveys is that the filter was rejected.
/// </remarks>
internal sealed class PostgreSqlSchemaFilterException : Exception
{
    private const string SanitizedMessage = "The PostgreSQL schema filter is invalid.";

    /// <summary>
    /// Creates the sanitized schema-filter exception. There is deliberately no constructor taking
    /// a message or an inner exception, so no code path anywhere in the assembly can attach one.
    /// </summary>
    internal PostgreSqlSchemaFilterException()
        : base(SanitizedMessage)
    {
    }
}
