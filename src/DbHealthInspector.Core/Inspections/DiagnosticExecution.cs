using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// A record of what happened when <see cref="InspectionOrchestrator"/> tried to run one
/// registered rule during one inspection.
/// </summary>
/// <remarks>
/// <para>
/// Instances are only ever produced by <see cref="Completed"/>, <see cref="SkippedUnavailableCapability"/>
/// or <see cref="Failed"/> — there is no public constructor — so the invariants tying
/// <see cref="Status"/> to <see cref="FindingCount"/>, <see cref="UnavailableCapabilities"/> and
/// <see cref="Failure"/> cannot be violated through the API:
/// </para>
/// <list type="bullet">
/// <item><description>
/// <see cref="DiagnosticExecutionStatus.Completed"/>: <see cref="Failure"/> is
/// <see langword="null"/>, <see cref="UnavailableCapabilities"/> is empty,
/// <see cref="FindingCount"/> is zero or greater.
/// </description></item>
/// <item><description>
/// <see cref="DiagnosticExecutionStatus.SkippedUnavailableCapability"/>: <see cref="Failure"/> is
/// <see langword="null"/>, <see cref="UnavailableCapabilities"/> is non-empty,
/// <see cref="FindingCount"/> is zero.
/// </description></item>
/// <item><description>
/// <see cref="DiagnosticExecutionStatus.Failed"/>: <see cref="Failure"/> is non-null,
/// <see cref="UnavailableCapabilities"/> is empty, <see cref="FindingCount"/> is zero.
/// </description></item>
/// </list>
/// </remarks>
public sealed class DiagnosticExecution
{
    private static readonly IReadOnlyList<CapabilityKind> NoUnavailableCapabilities =
        Array.AsReadOnly(Array.Empty<CapabilityKind>());

    /// <summary>
    /// The finding code of the rule this execution record is about.
    /// </summary>
    public FindingCode Code { get; }

    /// <summary>
    /// The version of the rule implementation that ran (or would have run).
    /// </summary>
    public RuleVersion RuleVersion { get; }

    /// <summary>
    /// The rule's human-readable name.
    /// </summary>
    public string RuleName { get; }

    /// <summary>
    /// The rule's technical category.
    /// </summary>
    public FindingCategory Category { get; }

    /// <summary>
    /// The outcome of this execution.
    /// </summary>
    public DiagnosticExecutionStatus Status { get; }

    /// <summary>
    /// The number of findings this execution contributed. Zero unless <see cref="Status"/> is
    /// <see cref="DiagnosticExecutionStatus.Completed"/>.
    /// </summary>
    public int FindingCount { get; }

    /// <summary>
    /// The capabilities that were not <see cref="CapabilityStatus.Available"/> and prevented this
    /// rule from running. Empty unless <see cref="Status"/> is
    /// <see cref="DiagnosticExecutionStatus.SkippedUnavailableCapability"/>. Ordered by ascending
    /// <see cref="CapabilityKind"/> numeric value — a canonical order independent of the order
    /// the owning <see cref="Inspections.InspectionRuleRegistration.RequiredCapabilities"/> were
    /// declared in — so two logically equivalent registrations always produce identical output.
    /// </summary>
    public IReadOnlyList<CapabilityKind> UnavailableCapabilities { get; }

    /// <summary>
    /// Why this execution failed. <see langword="null"/> unless <see cref="Status"/> is
    /// <see cref="DiagnosticExecutionStatus.Failed"/>.
    /// </summary>
    public DiagnosticExecutionFailure? Failure { get; }

    private DiagnosticExecution(
        FindingCode code,
        RuleVersion ruleVersion,
        string ruleName,
        FindingCategory category,
        DiagnosticExecutionStatus status,
        int findingCount,
        IReadOnlyList<CapabilityKind> unavailableCapabilities,
        DiagnosticExecutionFailure? failure)
    {
        Code = code;
        RuleVersion = ruleVersion;
        RuleName = ruleName;
        Category = category;
        Status = status;
        FindingCount = findingCount;
        UnavailableCapabilities = unavailableCapabilities;
        Failure = failure;
    }

    /// <summary>
    /// Creates a <see cref="DiagnosticExecutionStatus.Completed"/> execution record.
    /// </summary>
    internal static DiagnosticExecution Completed(
        FindingCode code, RuleVersion ruleVersion, string ruleName, FindingCategory category, int findingCount)
    {
        ValidateIdentity(code, ruleVersion, ruleName, category);
        if (findingCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(findingCount), findingCount, "Finding count cannot be negative.");
        }

        return new DiagnosticExecution(
            code, ruleVersion, ruleName, category,
            DiagnosticExecutionStatus.Completed, findingCount, NoUnavailableCapabilities, failure: null);
    }

    /// <summary>
    /// Creates a <see cref="DiagnosticExecutionStatus.SkippedUnavailableCapability"/> execution
    /// record.
    /// </summary>
    internal static DiagnosticExecution SkippedUnavailableCapability(
        FindingCode code,
        RuleVersion ruleVersion,
        string ruleName,
        FindingCategory category,
        IReadOnlyCollection<CapabilityKind> unavailableCapabilities)
    {
        ValidateIdentity(code, ruleVersion, ruleName, category);
        IReadOnlyList<CapabilityKind> copy = Guard.CopyDefensivelyRejectingUndefinedOrDuplicateEnumValues(
            unavailableCapabilities, nameof(unavailableCapabilities));
        if (copy.Count == 0)
        {
            throw new ArgumentException(
                "A skipped execution must record at least one unavailable capability.",
                nameof(unavailableCapabilities));
        }

        // Canonical order (ascending CapabilityKind numeric value), not input order, so two
        // logically equivalent registrations (differing only in the order their required
        // capabilities were declared) produce identical observable output.
        CapabilityKind[] canonicalCapabilities = [.. copy.OrderBy(static capability => (int)capability)];

        return new DiagnosticExecution(
            code, ruleVersion, ruleName, category,
            DiagnosticExecutionStatus.SkippedUnavailableCapability, 0, Array.AsReadOnly(canonicalCapabilities), failure: null);
    }

    /// <summary>
    /// Creates a <see cref="DiagnosticExecutionStatus.Failed"/> execution record.
    /// </summary>
    internal static DiagnosticExecution Failed(
        FindingCode code,
        RuleVersion ruleVersion,
        string ruleName,
        FindingCategory category,
        DiagnosticExecutionFailure failure)
    {
        ValidateIdentity(code, ruleVersion, ruleName, category);
        ArgumentNullException.ThrowIfNull(failure);

        return new DiagnosticExecution(
            code, ruleVersion, ruleName, category,
            DiagnosticExecutionStatus.Failed, 0, NoUnavailableCapabilities, failure);
    }

    private static void ValidateIdentity(
        FindingCode code, RuleVersion ruleVersion, string ruleName, FindingCategory category)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(ruleVersion);
        Guard.AgainstNullOrWhiteSpace(ruleName, nameof(ruleName));
        Guard.AgainstUndefinedEnum(category, nameof(category));
    }
}
