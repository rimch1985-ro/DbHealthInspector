namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// A capability whose availability the adapter reports for the current inspection.
/// </summary>
public enum CapabilityKind
{
    /// <summary>
    /// Access to catalog metadata (schemas, tables, indexes and their structural properties).
    /// </summary>
    CatalogMetadata,

    /// <summary>
    /// Access to server-reported usage statistics (for example index scan counters).
    /// </summary>
    UsageStatistics,

    /// <summary>
    /// Access to business-row data profiling. Core represents this capability's state in an
    /// engine-neutral way and permits any <see cref="CapabilityStatus"/> value for it; Core
    /// itself enforces no policy about which value is correct. The v0.1.0 product policy — that
    /// composition sets this to <see cref="CapabilityStatus.Disabled"/>, per ADR-0002 — is
    /// applied by the CLI/composition layer in a later gate, not by Core, and will be validated
    /// there.
    /// </summary>
    DataProfiling,
}
