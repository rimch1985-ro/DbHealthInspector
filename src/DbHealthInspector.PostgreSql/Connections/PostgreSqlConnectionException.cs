namespace DbHealthInspector.PostgreSql.Connections;

/// <summary>
/// Represents a sanitized connection-open failure. Always carries exactly the same fixed,
/// generic message and never an inner exception, extra data, or any detail derived from the
/// original failure.
/// </summary>
/// <remarks>
/// Deliberately exposes only a parameterless constructor: there is no way, even from other code
/// in this assembly, to construct one carrying a caller-supplied message, inner exception, or
/// extra data, which is what makes "no secret can leak through this type" true by construction
/// rather than by convention. See docs/design/postgresql-connection-boundary.md.
/// </remarks>
internal sealed class PostgreSqlConnectionException : Exception
{
    private const string SanitizedMessage = "The PostgreSQL connection could not be opened.";

    /// <summary>
    /// Creates the sanitized connection exception.
    /// </summary>
    internal PostgreSqlConnectionException()
        : base(SanitizedMessage)
    {
    }
}
