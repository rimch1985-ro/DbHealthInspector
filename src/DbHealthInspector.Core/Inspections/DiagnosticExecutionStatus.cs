namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// The outcome of running a single registered rule during one inspection.
/// </summary>
public enum DiagnosticExecutionStatus
{
    /// <summary>
    /// The rule ran and its output passed contract validation.
    /// </summary>
    Completed,

    /// <summary>
    /// The rule did not run because at least one of its required capabilities was not
    /// <see cref="Snapshots.CapabilityStatus.Available"/>. Not treated as an error.
    /// </summary>
    SkippedUnavailableCapability,

    /// <summary>
    /// The rule threw an unhandled exception, or its output violated the rule contract. Its
    /// findings, if any were produced, are discarded.
    /// </summary>
    Failed,
}
