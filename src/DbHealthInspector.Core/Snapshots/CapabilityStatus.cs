namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// Whether a <see cref="CapabilityKind"/> could be used during an inspection.
/// </summary>
public enum CapabilityStatus
{
    /// <summary>
    /// The capability was available and used.
    /// </summary>
    Available,

    /// <summary>
    /// The capability was not available, for example because of insufficient permissions or an
    /// unsupported server feature.
    /// </summary>
    Unavailable,

    /// <summary>
    /// The capability is disabled by product design, regardless of server support or permissions.
    /// </summary>
    Disabled,
}
