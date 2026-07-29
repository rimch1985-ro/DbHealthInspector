namespace DbHealthInspector.Core.Findings;

/// <summary>
/// Represents how urgently a finding deserves attention.
/// </summary>
/// <remarks>
/// The word "Error" is intentionally not a member of this enumeration; it is reserved for tool
/// execution failures, not for findings about the inspected database.
/// </remarks>
public enum FindingSeverity
{
    /// <summary>
    /// Useful information or a candidate that requires context before acting.
    /// </summary>
    Info,

    /// <summary>
    /// A technical risk that deserves review.
    /// </summary>
    Warning,

    /// <summary>
    /// A serious structural condition that requires priority attention.
    /// </summary>
    Critical,
}
