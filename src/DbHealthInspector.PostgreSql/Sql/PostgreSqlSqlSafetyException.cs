namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// Raised when a statement fails the fail-closed safety validator.
/// </summary>
/// <remarks>
/// <para>
/// Carries exactly one fixed message and never the offending SQL, the rule that fired, the
/// offending token or an inner exception. The rejected text is frequently the very thing that
/// must not be echoed, and a per-rule message would let a caller probe the validator by
/// difference.
/// </para>
/// <para>
/// This is a programming-time invariant, not a runtime input path: production only ever
/// validates the static inventory, which is validated once when the inventory is constructed. It
/// therefore never crosses the session boundary, and no session failure kind maps to it.
/// </para>
/// </remarks>
internal sealed class PostgreSqlSqlSafetyException : Exception
{
    private const string SanitizedMessage = "The PostgreSQL statement failed SQL safety validation.";

    /// <summary>
    /// Creates the sanitized safety-validation exception.
    /// </summary>
    internal PostgreSqlSqlSafetyException()
        : base(SanitizedMessage)
    {
    }
}
