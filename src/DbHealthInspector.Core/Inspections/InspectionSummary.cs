using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// Deterministic counts describing one inspection's findings and diagnostic executions.
/// </summary>
/// <remarks>
/// Every count is derived directly from the final findings and diagnostic-execution collections
/// at construction time — never received as independently trusted data from a caller — so
/// <c>TotalFindings = InfoFindings + WarningFindings + CriticalFindings</c> and
/// <c>TotalDiagnostics = CompletedDiagnostics + SkippedDiagnostics + FailedDiagnostics</c> hold
/// by construction, not by a separate validation step. This gate does not add a per-confidence
/// breakdown; see docs/design/inspection-orchestration.md.
/// </remarks>
public sealed class InspectionSummary
{
    /// <summary>
    /// The total number of findings, across every severity.
    /// </summary>
    public int TotalFindings { get; }

    /// <summary>
    /// The number of <see cref="FindingSeverity.Info"/> findings.
    /// </summary>
    public int InfoFindings { get; }

    /// <summary>
    /// The number of <see cref="FindingSeverity.Warning"/> findings.
    /// </summary>
    public int WarningFindings { get; }

    /// <summary>
    /// The number of <see cref="FindingSeverity.Critical"/> findings.
    /// </summary>
    public int CriticalFindings { get; }

    /// <summary>
    /// The total number of diagnostic executions, across every status.
    /// </summary>
    public int TotalDiagnostics { get; }

    /// <summary>
    /// The number of executions with <see cref="DiagnosticExecutionStatus.Completed"/>.
    /// </summary>
    public int CompletedDiagnostics { get; }

    /// <summary>
    /// The number of executions with <see cref="DiagnosticExecutionStatus.SkippedUnavailableCapability"/>.
    /// </summary>
    public int SkippedDiagnostics { get; }

    /// <summary>
    /// The number of executions with <see cref="DiagnosticExecutionStatus.Failed"/>.
    /// </summary>
    public int FailedDiagnostics { get; }

    /// <summary>
    /// Derives a summary from the final findings and diagnostic executions of an inspection.
    /// </summary>
    internal InspectionSummary(
        IReadOnlyCollection<Finding> findings, IReadOnlyCollection<DiagnosticExecution> diagnosticExecutions)
    {
        ArgumentNullException.ThrowIfNull(findings);
        ArgumentNullException.ThrowIfNull(diagnosticExecutions);

        foreach (Finding finding in findings)
        {
            switch (finding.Severity)
            {
                case FindingSeverity.Info:
                    InfoFindings++;
                    break;
                case FindingSeverity.Warning:
                    WarningFindings++;
                    break;
                case FindingSeverity.Critical:
                    CriticalFindings++;
                    break;
            }
        }

        TotalFindings = InfoFindings + WarningFindings + CriticalFindings;

        foreach (DiagnosticExecution execution in diagnosticExecutions)
        {
            switch (execution.Status)
            {
                case DiagnosticExecutionStatus.Completed:
                    CompletedDiagnostics++;
                    break;
                case DiagnosticExecutionStatus.SkippedUnavailableCapability:
                    SkippedDiagnostics++;
                    break;
                case DiagnosticExecutionStatus.Failed:
                    FailedDiagnostics++;
                    break;
            }
        }

        TotalDiagnostics = CompletedDiagnostics + SkippedDiagnostics + FailedDiagnostics;
    }
}
