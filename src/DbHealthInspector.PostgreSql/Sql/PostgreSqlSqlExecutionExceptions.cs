namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// Raised when supplied parameter values do not match the resolved statement declaration exactly
/// — wrong count, wrong position or wrong declared type.
/// </summary>
/// <remarks>
/// Carries a fixed message and never the offending value, position or statement text. Bound
/// values are among the things this boundary exists to keep out of exceptions.
/// </remarks>
internal sealed class PostgreSqlSqlParameterBindingException : Exception
{
    private const string SanitizedMessage = "The PostgreSQL statement parameters did not match the statement definition.";

    /// <summary>
    /// Creates the sanitized parameter-binding exception.
    /// </summary>
    internal PostgreSqlSqlParameterBindingException()
        : base(SanitizedMessage)
    {
    }
}

/// <summary>
/// Raised when a statement's result does not have the exact shape its definition requires — a
/// missing row, an unexpected extra row, an unexpected column count or an unexpected NULL.
/// </summary>
/// <remarks>
/// Carries a fixed message and never a returned value. The session runner treats this as a
/// verification failure rather than letting it escape, so it never crosses the boundary as-is.
/// </remarks>
internal sealed class PostgreSqlSqlResultShapeException : Exception
{
    private const string SanitizedMessage = "The PostgreSQL statement returned an unexpected result shape.";

    /// <summary>
    /// Creates the sanitized result-shape exception.
    /// </summary>
    internal PostgreSqlSqlResultShapeException()
        : base(SanitizedMessage)
    {
    }
}
