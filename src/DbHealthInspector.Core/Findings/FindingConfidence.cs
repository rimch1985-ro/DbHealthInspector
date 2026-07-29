namespace DbHealthInspector.Core.Findings;

/// <summary>
/// Represents how directly the recorded evidence demonstrates the reported condition.
/// </summary>
public enum FindingConfidence
{
    /// <summary>
    /// The signal depends on statistics or an observation window of unknown length.
    /// </summary>
    Low,

    /// <summary>
    /// The condition is real, but its impact depends on context.
    /// </summary>
    Medium,

    /// <summary>
    /// The evidence directly demonstrates the condition.
    /// </summary>
    High,
}
