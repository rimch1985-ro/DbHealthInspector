namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// A small, safe description of why a rule's execution failed.
/// </summary>
/// <remarks>
/// Deliberately carries no potentially sensitive detail: never the original exception's message
/// or stack trace, never a connection string, never SQL, never user or business data, and never
/// the <see cref="Exception"/> instance itself. <see cref="Message"/> is one of a small, fixed
/// set of generic, deterministic strings — see
/// <see cref="Inspections.InspectionOrchestrator"/> for exactly which message is used for each
/// <see cref="DiagnosticFailureKind"/>.
/// </remarks>
public sealed record DiagnosticExecutionFailure
{
    /// <summary>
    /// The failure classification.
    /// </summary>
    public DiagnosticFailureKind Kind { get; }

    /// <summary>
    /// A generic, deterministic description of the failure. Never the original exception
    /// message.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Creates a diagnostic execution failure.
    /// </summary>
    /// <param name="kind">The failure classification.</param>
    /// <param name="message">A generic, deterministic message. Cannot be null, empty or whitespace.</param>
    public DiagnosticExecutionFailure(DiagnosticFailureKind kind, string message)
    {
        Guard.AgainstUndefinedEnum(kind, nameof(kind));
        Kind = kind;
        Message = Guard.AgainstNullOrWhiteSpace(message, nameof(message));
    }
}
