namespace DbHealthInspector.Core.Findings;

/// <summary>
/// Indicates whether an <see cref="EvidenceItem"/> is part of a finding's stable logical
/// identity or is purely informational.
/// </summary>
public enum FingerprintParticipation
{
    /// <summary>
    /// The evidence value participates in fingerprint computation. Changing it produces a
    /// different fingerprint because it changes what the finding logically identifies.
    /// </summary>
    Include,

    /// <summary>
    /// The evidence value does not participate in fingerprint computation. It may change freely
    /// between inspections (for example current size or row estimates) without affecting the
    /// finding's identity.
    /// </summary>
    Exclude,
}
