using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.Core.Fingerprinting;

/// <summary>
/// The pure data a fingerprint is derived from, independent of any <see cref="Finding"/> instance.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="Finding"/> so <see cref="FindingFingerprintGenerator"/> can be
/// exercised directly in tests without constructing an entire finding for every scenario. The
/// full evidence collection is accepted here; <see cref="FindingFingerprintGenerator"/> is
/// responsible for keeping only the items marked
/// <see cref="Findings.FingerprintParticipation.Include"/>.
/// </remarks>
public sealed class FindingFingerprintInput
{
    /// <summary>
    /// The current fingerprint canonicalization format version.
    /// </summary>
    public const string CurrentFormatVersion = "fp1";

    /// <summary>
    /// The database engine the finding was produced against.
    /// </summary>
    public DatabaseEngine Engine { get; }

    /// <summary>
    /// The finding code.
    /// </summary>
    public FindingCode Code { get; }

    /// <summary>
    /// The database object the finding is about.
    /// </summary>
    public DatabaseObjectReference ObjectReference { get; }

    /// <summary>
    /// The finding's full evidence collection. Only items marked
    /// <see cref="Findings.FingerprintParticipation.Include"/> affect the resulting fingerprint.
    /// </summary>
    public IReadOnlyList<EvidenceItem> Evidence { get; }

    /// <summary>
    /// Creates fingerprint input data.
    /// </summary>
    public FindingFingerprintInput(
        DatabaseEngine engine,
        FindingCode code,
        DatabaseObjectReference objectReference,
        IReadOnlyCollection<EvidenceItem> evidence)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(objectReference);
        Engine = engine;
        Code = code;
        ObjectReference = objectReference;
        Evidence = Guard.CopyDefensivelyRejectingNullElements(evidence, nameof(evidence));
    }
}
