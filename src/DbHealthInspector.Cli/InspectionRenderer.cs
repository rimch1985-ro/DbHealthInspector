using System.Globalization;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Cli;

/// <summary>
/// Renders an <see cref="InspectionResult"/> as deterministic plain text.
/// </summary>
/// <remarks>
/// No colour, no ANSI escapes, no cursor control and no box drawing, so the output is identical
/// in a Windows terminal, a Linux terminal and a redirected file. Ordering comes entirely from
/// Core; this renderer never re-sorts (§11, §13 of the gate definition).
/// </remarks>
internal static class InspectionRenderer
{
    internal static void Render(TextWriter writer, InspectionResult result)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(result);

        DatabaseSnapshot snapshot = result.Snapshot;

        writer.WriteLine("DbHealth Inspector");
        writer.WriteLine();

        writer.WriteLine("TARGET");
        writer.WriteLine($"  Database : {snapshot.Metadata.DatabaseName}");
        writer.WriteLine($"  Engine   : {snapshot.Metadata.Engine}");
        writer.WriteLine($"  Version  : {snapshot.Metadata.EngineVersion}");
        writer.WriteLine();

        writer.WriteLine("INSPECTION");
        writer.WriteLine($"  Schemas : {Count(snapshot.Schemas.Count)}");
        writer.WriteLine($"  Tables  : {Count(snapshot.Tables.Count)}");
        writer.WriteLine($"  Indexes : {Count(snapshot.Indexes.Count)}");
        writer.WriteLine();

        RenderCapabilities(writer, snapshot);
        RenderDiagnostics(writer, result);
        RenderFindings(writer, result);
        RenderSummary(writer, result);
    }

    private static void RenderCapabilities(TextWriter writer, DatabaseSnapshot snapshot)
    {
        writer.WriteLine("CAPABILITIES");
        foreach (CapabilityKind kind in Enum.GetValues<CapabilityKind>())
        {
            CapabilityState state = snapshot.Capabilities.GetState(kind);
            string reason = state.Reason is null ? string.Empty : $" - {state.Reason}";
            writer.WriteLine($"  {kind,-16} : {state.Status}{reason}");
        }

        writer.WriteLine();
    }

    private static void RenderDiagnostics(TextWriter writer, InspectionResult result)
    {
        writer.WriteLine("DIAGNOSTICS");
        foreach (DiagnosticExecution execution in result.DiagnosticExecutions)
        {
            // Status and finding count stay separate fields: a skipped diagnostic must never
            // read as one that ran and found nothing.
            writer.WriteLine(
                $"  {execution.Code.Value}  {execution.RuleName,-26}  {execution.Status,-30}  "
                + $"{Count(execution.FindingCount)} findings");

            if (execution.UnavailableCapabilities.Count > 0)
            {
                writer.WriteLine(
                    "      skipped: unavailable capability "
                    + string.Join(", ", execution.UnavailableCapabilities));
            }

            if (execution.Failure is { } failure)
            {
                writer.WriteLine($"      failed: {failure.Kind} - {failure.Message}");
            }
        }

        int skipped = result.Summary.SkippedDiagnostics;
        if (skipped > 0)
        {
            writer.WriteLine();
            writer.WriteLine(
                $"  WARNING: {Count(skipped)} diagnostic(s) were skipped because an optional "
                + "capability was unavailable.");
            writer.WriteLine(
                "  Their conditions were not evaluated. A skipped diagnostic is not a clean result.");
        }

        writer.WriteLine();
    }

    private static void RenderFindings(TextWriter writer, InspectionResult result)
    {
        writer.WriteLine("FINDINGS");

        if (result.Findings.Count == 0)
        {
            writer.WriteLine($"  {CliMessages.NoFindings}");
            writer.WriteLine($"  {CliMessages.NoFindingsCaveat}");
            writer.WriteLine();
            return;
        }

        foreach (Finding finding in result.Findings)
        {
            writer.WriteLine();
            writer.WriteLine($"  [{finding.Severity}] {finding.Code.Value} - {Identity(finding.ObjectReference)}");
            writer.WriteLine($"    Message        : {finding.Message}");
            writer.WriteLine($"    Confidence     : {finding.Confidence}");
            writer.WriteLine($"    Category       : {finding.Category}");

            if (finding.Evidence.Count > 0)
            {
                writer.WriteLine("    Evidence       :");
                foreach (EvidenceItem item in finding.Evidence)
                {
                    string unit = item.Unit is null ? string.Empty : $" {item.Unit}";
                    writer.WriteLine($"      {item.Key,-24} = {item.Value}{unit}");
                }
            }

            writer.WriteLine($"    Recommendation : {finding.Recommendation}");
            writer.WriteLine($"    Documentation  : {finding.DocumentationReference}");
        }

        writer.WriteLine();
    }

    private static void RenderSummary(TextWriter writer, InspectionResult result)
    {
        InspectionSummary summary = result.Summary;

        writer.WriteLine("SUMMARY");
        writer.WriteLine($"  Info         : {Count(summary.InfoFindings)}");
        writer.WriteLine($"  Warning      : {Count(summary.WarningFindings)}");
        writer.WriteLine($"  Critical     : {Count(summary.CriticalFindings)}");
        writer.WriteLine($"  Total        : {Count(summary.TotalFindings)}");
        writer.WriteLine($"  Overall risk : {result.OverallRisk}");

        if (result.HasErrors)
        {
            writer.WriteLine($"  {CliMessages.DiagnosticExecutionFailed}");
        }
    }

    /// <summary>
    /// Schema-qualified object identity, naming the parent relation for an index.
    /// </summary>
    private static string Identity(DatabaseObjectReference reference)
    {
        string qualified = reference.SchemaName is { } schema
            ? $"{schema}.{reference.ObjectName}"
            : reference.ObjectName;

        return reference.ParentObjectName is { } parent
            ? $"{qualified} (on {parent})"
            : qualified;
    }

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);
}
