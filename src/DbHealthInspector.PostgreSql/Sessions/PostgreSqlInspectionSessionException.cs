namespace DbHealthInspector.PostgreSql.Sessions;

/// <summary>
/// A sanitized inspection-session failure. Carries only a
/// <see cref="PostgreSqlInspectionSessionFailureKind"/> and the fixed message that kind maps to.
/// </summary>
/// <remarks>
/// <para>
/// The only constructor takes a failure kind, and the message is derived from it — there is no
/// code path, anywhere in this assembly, that can attach a caller-supplied message, an inner
/// exception or extra <see cref="Exception.Data"/>. That makes "no server detail can leak
/// through this type" true by construction rather than by convention: no SQLSTATE, no
/// <c>Detail</c>/<c>Hint</c>, no schema, table, column or constraint name, no SQL text, no bound
/// parameter value, no connection metadata and no original stack trace.
/// </para>
/// <para>
/// <see cref="object.ToString"/> is intentionally not overridden; the base implementation renders
/// the type name, the fixed message and this exception's own stack trace, none of which is
/// derived from the failure it replaced.
/// </para>
/// </remarks>
internal sealed class PostgreSqlInspectionSessionException : Exception
{
    private const string InitializationMessage = "The PostgreSQL inspection session could not be initialized.";
    private const string ExecutionMessage = "The PostgreSQL inspection operation failed.";
    private const string CleanupMessage = "The PostgreSQL inspection session could not be closed safely.";

    /// <summary>
    /// Which stage failed.
    /// </summary>
    internal PostgreSqlInspectionSessionFailureKind FailureKind { get; }

    /// <summary>
    /// Creates a sanitized session exception for <paramref name="failureKind"/>.
    /// </summary>
    internal PostgreSqlInspectionSessionException(PostgreSqlInspectionSessionFailureKind failureKind)
        : base(MessageFor(failureKind))
    {
        FailureKind = failureKind;
    }

    /// <summary>
    /// The fixed message for a failure kind. Public-by-assembly so tests can assert the exact
    /// contract text without duplicating string literals that could drift apart.
    /// </summary>
    internal static string MessageFor(PostgreSqlInspectionSessionFailureKind failureKind) => failureKind switch
    {
        PostgreSqlInspectionSessionFailureKind.InitializationFailed => InitializationMessage,

        // Canonical contract (GC-DHI-04B-C1, F-07): a verification failure deliberately reuses the
        // initialization message. The distinct FailureKind stays available to internal callers,
        // but the text a caller sees must not reveal that the session got as far as reading back
        // its own state — that is a detail about the server interaction, not about the caller.
        PostgreSqlInspectionSessionFailureKind.VerificationFailed => InitializationMessage,
        PostgreSqlInspectionSessionFailureKind.ExecutionFailed => ExecutionMessage,
        PostgreSqlInspectionSessionFailureKind.CleanupFailed => CleanupMessage,
        _ => throw new ArgumentOutOfRangeException(nameof(failureKind), failureKind, "Undefined failure kind."),
    };
}
