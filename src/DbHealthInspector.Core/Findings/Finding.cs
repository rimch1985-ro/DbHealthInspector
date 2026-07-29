using DbHealthInspector.Core.Fingerprinting;

namespace DbHealthInspector.Core.Findings;

/// <summary>
/// An immutable, evidence-based diagnostic result produced by a rule.
/// </summary>
/// <remarks>
/// <para>
/// A finding never carries a recommendation to apply an automatic change; recommendations are
/// always framed as something a human should review. See ADR-0002.
/// </para>
/// <para>
/// <see cref="Finding"/> intentionally does not implement value equality (it is a plain
/// <see langword="class"/>, not a <see langword="record"/>): <see cref="object.Equals(object?)"/>
/// and <see cref="object.GetHashCode"/> use reference identity. Two independently constructed
/// findings with identical field values are two distinct objects, not "the same finding" in the
/// C# equality sense — "the same finding" across separate inspections is expressed by
/// <see cref="Fingerprint"/> equality, not by <see cref="Finding"/> equality. This mirrors why
/// <see cref="Fingerprint"/>, not object identity or structural equality, is what a future report
/// comparison (v0.4.0 per the roadmap) will key on.
/// </para>
/// </remarks>
public sealed class Finding
{
    /// <summary>
    /// The stable finding code.
    /// </summary>
    public FindingCode Code { get; }

    /// <summary>
    /// The version of the rule implementation that produced this finding.
    /// </summary>
    public RuleVersion RuleVersion { get; }

    /// <summary>
    /// The technical category this finding belongs to.
    /// </summary>
    public FindingCategory Category { get; }

    /// <summary>
    /// How urgently this finding deserves attention.
    /// </summary>
    public FindingSeverity Severity { get; }

    /// <summary>
    /// How directly the evidence demonstrates the reported condition.
    /// </summary>
    public FindingConfidence Confidence { get; }

    /// <summary>
    /// The database object this finding is about.
    /// </summary>
    public DatabaseObjectReference ObjectReference { get; }

    /// <summary>
    /// The database engine this finding was produced against.
    /// </summary>
    public DatabaseEngine Engine { get; }

    /// <summary>
    /// A human-readable description of the condition found.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// A non-destructive, human-reviewable recommendation. Never an instruction that the tool
    /// itself, or an automated process, should apply a change.
    /// </summary>
    public string Recommendation { get; }

    /// <summary>
    /// The deterministic facts supporting this finding. No two items share the same
    /// <see cref="EvidenceItem.Key"/>.
    /// </summary>
    public IReadOnlyList<EvidenceItem> Evidence { get; }

    /// <summary>
    /// A reference to the finding's published documentation (for example a relative doc path).
    /// </summary>
    public string DocumentationReference { get; }

    /// <summary>
    /// The finding's stable logical identity, derived from <see cref="Engine"/>,
    /// <see cref="Code"/>, <see cref="ObjectReference"/> and the evidence marked
    /// <see cref="Findings.FingerprintParticipation.Include"/>. Computed from exactly the
    /// properties stored on this instance, so it can always be independently recomputed by
    /// passing <see cref="Engine"/>, <see cref="Code"/>, <see cref="ObjectReference"/> and
    /// <see cref="Evidence"/> into a fresh <see cref="FindingFingerprintInput"/> and
    /// <see cref="FindingFingerprintGenerator.Generate"/>.
    /// </summary>
    public FindingFingerprint Fingerprint { get; }

    /// <summary>
    /// Creates a finding.
    /// </summary>
    /// <param name="code">The stable finding code.</param>
    /// <param name="ruleVersion">The version of the producing rule implementation.</param>
    /// <param name="category">The technical category.</param>
    /// <param name="severity">The severity.</param>
    /// <param name="confidence">The confidence.</param>
    /// <param name="objectReference">The database object this finding is about.</param>
    /// <param name="message">A human-readable description. Cannot be null, empty or whitespace.</param>
    /// <param name="recommendation">A non-destructive recommendation. Cannot be null, empty or whitespace.</param>
    /// <param name="evidence">
    /// The supporting evidence. Copied defensively. Cannot contain a null element or two items
    /// sharing the same <see cref="EvidenceItem.Key"/>.
    /// </param>
    /// <param name="documentationReference">A reference to published documentation. Cannot be null, empty or whitespace.</param>
    /// <param name="engine">The database engine this finding was produced against.</param>
    public Finding(
        FindingCode code,
        RuleVersion ruleVersion,
        FindingCategory category,
        FindingSeverity severity,
        FindingConfidence confidence,
        DatabaseObjectReference objectReference,
        string message,
        string recommendation,
        IReadOnlyCollection<EvidenceItem> evidence,
        string documentationReference,
        DatabaseEngine engine)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(ruleVersion);
        ArgumentNullException.ThrowIfNull(objectReference);
        ArgumentNullException.ThrowIfNull(engine);
        Guard.AgainstUndefinedEnum(category, nameof(category));
        Guard.AgainstUndefinedEnum(severity, nameof(severity));
        Guard.AgainstUndefinedEnum(confidence, nameof(confidence));

        Code = code;
        RuleVersion = ruleVersion;
        Category = category;
        Severity = severity;
        Confidence = confidence;
        ObjectReference = objectReference;
        Engine = engine;
        Message = Guard.AgainstNullOrWhiteSpace(message, nameof(message));
        Recommendation = Guard.AgainstNullOrWhiteSpace(recommendation, nameof(recommendation));
        DocumentationReference = Guard.AgainstNullOrWhiteSpace(documentationReference, nameof(documentationReference));

        IReadOnlyList<EvidenceItem> evidenceCopy =
            Guard.CopyDefensivelyRejectingNullElements(evidence, nameof(evidence));
        var seenEvidenceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (EvidenceItem item in evidenceCopy)
        {
            if (!seenEvidenceKeys.Add(item.Key))
            {
                throw new ArgumentException($"Duplicate evidence key '{item.Key}'.", nameof(evidence));
            }
        }

        Evidence = evidenceCopy;

        var fingerprintInput = new FindingFingerprintInput(Engine, Code, ObjectReference, Evidence);
        Fingerprint = FindingFingerprintGenerator.Generate(fingerprintInput);
    }
}
