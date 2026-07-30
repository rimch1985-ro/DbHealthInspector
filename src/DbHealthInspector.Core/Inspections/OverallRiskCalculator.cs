using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// Computes <see cref="OverallRisk"/> from a set of findings.
/// </summary>
/// <remarks>
/// Pure and deterministic: the same findings always produce the same risk. The matrix considers
/// <see cref="FindingSeverity"/> only — never <see cref="FindingConfidence"/>, weighting,
/// percentages, object counts or sizes, rule-failure counts, or skipped-diagnostic counts. Kept
/// <see langword="internal"/> to keep the public Core API small; the assembly already exposes
/// internals to <c>DbHealthInspector.UnitTests</c> for testing.
/// </remarks>
internal static class OverallRiskCalculator
{
    /// <summary>
    /// Computes the overall risk for <paramref name="findings"/>.
    /// </summary>
    /// <returns>
    /// <see cref="OverallRisk.High"/> when at least one finding is <c>Critical</c>;
    /// otherwise <see cref="OverallRisk.Medium"/> when at least one finding is <c>Warning</c>;
    /// otherwise <see cref="OverallRisk.Low"/> when <paramref name="findings"/> is non-empty
    /// (every remaining finding is <c>Info</c>); otherwise <see cref="OverallRisk.None"/>.
    /// </returns>
    public static OverallRisk Calculate(IReadOnlyCollection<Finding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        if (findings.Count == 0)
        {
            return OverallRisk.None;
        }

        bool hasWarning = false;
        foreach (Finding finding in findings)
        {
            if (finding.Severity == FindingSeverity.Critical)
            {
                return OverallRisk.High;
            }

            if (finding.Severity == FindingSeverity.Warning)
            {
                hasWarning = true;
            }
        }

        return hasWarning ? OverallRisk.Medium : OverallRisk.Low;
    }
}
