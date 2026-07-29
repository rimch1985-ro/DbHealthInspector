namespace DbHealthInspector.Core.Findings;

/// <summary>
/// A single deterministic fact supporting a <see cref="Finding"/>.
/// </summary>
/// <remarks>
/// Evidence must never carry secrets or sensitive business data. Marking an item
/// <see cref="Findings.FingerprintParticipation.Exclude"/> only removes it from fingerprint
/// computation; it does not authorize including sensitive content.
/// </remarks>
public sealed record EvidenceItem
{
    /// <summary>
    /// The stable key identifying what this evidence represents (for example <c>"estimatedRows"</c>).
    /// </summary>
    public string Key { get; }

    /// <summary>
    /// The deterministic textual value (for example <c>"25000"</c>).
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// An optional unit for <see cref="Value"/> (for example <c>"bytes"</c> or <c>"rows"</c>).
    /// When provided, cannot be empty or whitespace-only.
    /// </summary>
    public string? Unit { get; }

    /// <summary>
    /// Whether this item participates in the owning finding's fingerprint.
    /// </summary>
    public FingerprintParticipation FingerprintParticipation { get; }

    /// <summary>
    /// Creates an evidence item.
    /// </summary>
    /// <param name="key">Stable key. Cannot be null, empty or whitespace.</param>
    /// <param name="value">Deterministic textual value. Cannot be null, empty or whitespace.</param>
    /// <param name="fingerprintParticipation">Whether this item is part of the finding's logical identity.</param>
    /// <param name="unit">
    /// An optional unit for <paramref name="value"/>. When provided, cannot be empty or whitespace-only.
    /// </param>
    public EvidenceItem(
        string key,
        string value,
        FingerprintParticipation fingerprintParticipation,
        string? unit = null)
    {
        Key = Guard.AgainstNullOrWhiteSpace(key, nameof(key));
        Value = Guard.AgainstNullOrWhiteSpace(value, nameof(value));
        Guard.AgainstUndefinedEnum(fingerprintParticipation, nameof(fingerprintParticipation));
        FingerprintParticipation = fingerprintParticipation;
        Unit = Guard.AgainstEmptyOrWhiteSpace(unit, nameof(unit));
    }
}
