using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// The complete, immutable outcome of one inspection.
/// </summary>
/// <remarks>
/// The constructor is <see langword="internal"/>: only <see cref="InspectionOrchestrator"/> (and
/// tests, via the assembly's <c>InternalsVisibleTo</c>) can build a result, which guarantees
/// <see cref="Summary"/>, <see cref="OverallRisk"/> and <see cref="HasErrors"/> are always
/// derived from — and therefore can never contradict — <see cref="DiagnosticExecutions"/> and
/// <see cref="Findings"/>: they are computed here, not supplied independently. This gate adds no
/// timestamp or tool metadata; that belongs to the future report model.
/// </remarks>
public sealed class InspectionResult
{
    /// <summary>
    /// The snapshot this inspection evaluated.
    /// </summary>
    public DatabaseSnapshot Snapshot { get; }

    /// <summary>
    /// One execution record per enabled rule, ordered by <see cref="Findings.FindingCode"/> value
    /// (ordinal).
    /// </summary>
    public IReadOnlyList<DiagnosticExecution> DiagnosticExecutions { get; }

    /// <summary>
    /// Every accepted finding from every completed rule, ordered by finding code then by
    /// fingerprint (both ordinal).
    /// </summary>
    public IReadOnlyList<Finding> Findings { get; }

    /// <summary>
    /// Deterministic counts derived from <see cref="Findings"/> and
    /// <see cref="DiagnosticExecutions"/>.
    /// </summary>
    public InspectionSummary Summary { get; }

    /// <summary>
    /// The overall risk, derived solely from <see cref="Findings"/> severities.
    /// </summary>
    public OverallRisk OverallRisk { get; }

    /// <summary>
    /// <see langword="true"/> when at least one <see cref="DiagnosticExecutions"/> entry has
    /// <see cref="DiagnosticExecutionStatus.Failed"/>. A
    /// <see cref="DiagnosticExecutionStatus.SkippedUnavailableCapability"/> entry never sets this;
    /// it is independent of <see cref="OverallRisk"/>, which can be <see cref="Inspections.OverallRisk.None"/>
    /// while this is <see langword="true"/> when no findings were produced but a rule failed.
    /// </summary>
    public bool HasErrors { get; }

    /// <summary>
    /// Builds a coherent inspection result. <paramref name="diagnosticExecutions"/> and
    /// <paramref name="findings"/> are expected to already be in final, deterministic order;
    /// they are still copied defensively into genuinely non-modifiable collections.
    /// </summary>
    internal InspectionResult(
        DatabaseSnapshot snapshot,
        IReadOnlyCollection<DiagnosticExecution> diagnosticExecutions,
        IReadOnlyCollection<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        Snapshot = snapshot;
        DiagnosticExecutions = Guard.CopyDefensivelyRejectingNullElements(
            diagnosticExecutions, nameof(diagnosticExecutions));
        Findings = Guard.CopyDefensivelyRejectingNullElements(findings, nameof(findings));
        Summary = new InspectionSummary(Findings, DiagnosticExecutions);
        OverallRisk = OverallRiskCalculator.Calculate(Findings);

        bool hasErrors = false;
        foreach (DiagnosticExecution execution in DiagnosticExecutions)
        {
            if (execution.Status == DiagnosticExecutionStatus.Failed)
            {
                hasErrors = true;
                break;
            }
        }

        HasErrors = hasErrors;
    }
}
