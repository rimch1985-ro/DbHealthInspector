namespace DbHealthInspector.PostgreSql.Capabilities;

/// <summary>
/// Raised when the required catalog-metadata capability is unavailable on a supported server.
/// </summary>
/// <remarks>
/// <para>
/// Catalog metadata is not optional: without it there is nothing meaningful to inspect, so the
/// probe refuses to return a partial result and fails instead.
/// </para>
/// <para>
/// The only constructor is parameterless, so there is no code path — anywhere in this assembly —
/// that can attach a caller message, an inner exception or extra <see cref="Exception.Data"/>.
/// That makes "no server detail can leak through this type" true by construction: no object name,
/// SQL, current user, database name, SQLSTATE or PostgreSQL message.
/// </para>
/// </remarks>
internal sealed class PostgreSqlRequiredCatalogCapabilityException : Exception
{
    private const string SanitizedMessage = "Required PostgreSQL catalog metadata is unavailable.";

    /// <summary>
    /// Creates the sanitized required-capability exception.
    /// </summary>
    internal PostgreSqlRequiredCatalogCapabilityException()
        : base(SanitizedMessage)
    {
    }
}
