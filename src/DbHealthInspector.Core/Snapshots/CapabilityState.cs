namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// The reported state of a single <see cref="CapabilityKind"/>.
/// </summary>
public sealed record CapabilityState
{
    /// <summary>
    /// The capability this state describes.
    /// </summary>
    public CapabilityKind Kind { get; }

    /// <summary>
    /// The capability's status for the current inspection.
    /// </summary>
    public CapabilityStatus Status { get; }

    /// <summary>
    /// Why the capability is <see cref="CapabilityStatus.Unavailable"/> or
    /// <see cref="CapabilityStatus.Disabled"/> — the cause of its absence or deactivation, not a
    /// description of an available capability. Always <see langword="null"/> when
    /// <see cref="Status"/> is <see cref="CapabilityStatus.Available"/>: a working capability has
    /// nothing to explain. Optional (may be <see langword="null"/>) when <see cref="Status"/> is
    /// <see cref="CapabilityStatus.Unavailable"/> or <see cref="CapabilityStatus.Disabled"/>, but
    /// when provided in that case, cannot be empty or whitespace-only.
    /// </summary>
    public string? Reason { get; }

    /// <summary>
    /// Creates a capability state.
    /// </summary>
    /// <param name="kind">The capability this state describes.</param>
    /// <param name="status">The capability's status.</param>
    /// <param name="reason">
    /// Must be <see langword="null"/> when <paramref name="status"/> is
    /// <see cref="CapabilityStatus.Available"/> (a working capability needs no explanation).
    /// When <paramref name="status"/> is <see cref="CapabilityStatus.Unavailable"/> or
    /// <see cref="CapabilityStatus.Disabled"/>, <see langword="null"/> is allowed, but a
    /// provided value cannot be empty or whitespace-only.
    /// </param>
    public CapabilityState(CapabilityKind kind, CapabilityStatus status, string? reason = null)
    {
        Guard.AgainstUndefinedEnum(kind, nameof(kind));
        Guard.AgainstUndefinedEnum(status, nameof(status));
        Kind = kind;
        Status = status;

        if (status == CapabilityStatus.Available)
        {
            if (reason is not null)
            {
                throw new ArgumentException(
                    "Reason must be null when status is Available: a working capability has nothing to explain.",
                    nameof(reason));
            }

            Reason = null;
        }
        else
        {
            Reason = Guard.AgainstEmptyOrWhiteSpace(reason, nameof(reason));
        }
    }
}
