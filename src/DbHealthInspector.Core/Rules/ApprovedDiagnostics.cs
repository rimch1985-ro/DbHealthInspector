using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Rules;

/// <summary>
/// Composes the five approved v0.1.0 diagnostics into rule registrations for
/// <see cref="InspectionOrchestrator"/>.
/// </summary>
/// <remarks>
/// Deliberately a plain factory: the rules are named explicitly, in one place. There is no
/// reflection-based discovery, no dependency-injection container, no plugin mechanism and no
/// service locator. See docs/gates/GC-DHI-05A_DEFINITION.md §10.
/// </remarks>
public static class ApprovedDiagnostics
{
    /// <summary>
    /// Creates registrations for DBH001 through DBH005 using the frozen default thresholds.
    /// </summary>
    public static IReadOnlyList<InspectionRuleRegistration> CreateRegistrations() =>
        CreateRegistrations(DiagnosticThresholds.Default);

    /// <summary>
    /// Creates registrations for DBH001 through DBH005 using explicit thresholds.
    /// </summary>
    /// <param name="thresholds">The thresholds DBH002 and DBH004 compare against.</param>
    /// <remarks>
    /// Only DBH004 declares a required capability. When
    /// <see cref="CapabilityKind.UsageStatistics"/> is unavailable the orchestrator records a
    /// skipped execution and never invokes the rule, which is what keeps missing statistics
    /// from being read as zero scans.
    /// </remarks>
    public static IReadOnlyList<InspectionRuleRegistration> CreateRegistrations(
        DiagnosticThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);

        return
        [
            new InspectionRuleRegistration(new TableWithoutPrimaryKeyRule(), []),
            new InspectionRuleRegistration(new LargeTableRule(thresholds), []),
            new InspectionRuleRegistration(new ExactDuplicateIndexRule(), []),
            new InspectionRuleRegistration(
                new UnusedIndexCandidateRule(thresholds), [CapabilityKind.UsageStatistics]),
            new InspectionRuleRegistration(new InvalidIndexRule(), []),
        ];
    }
}
