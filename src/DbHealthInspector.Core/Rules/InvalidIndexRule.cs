using System.Globalization;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Rules;

/// <summary>
/// DBH005 — reports indexes the engine has marked invalid.
/// </summary>
/// <remarks>
/// <see cref="IndexSnapshot.IsValid"/> is the sole trigger.
/// <see cref="IndexSnapshot.IsReady"/> and <see cref="IndexSnapshot.IsLive"/> are recorded as
/// evidence but never filter: an index can be invalid while still ready and live — the
/// empirically verified triple for <c>CREATE INDEX … ON ONLY</c> against a partitioned table
/// on both supported majors — so filtering on them would hide real invalid indexes. Pure and
/// deterministic; performs no I/O. See docs/gates/GC-DHI-05A_DEFINITION.md §9.
/// </remarks>
public sealed class InvalidIndexRule : IInspectionRule
{
    /// <inheritdoc />
    public FindingCode Code => FindingCodes.InvalidIndex;

    /// <inheritdoc />
    public RuleVersion Version => RuleVersion.Initial;

    /// <inheritdoc />
    public string Name => "INVALID_INDEX";

    /// <inheritdoc />
    public FindingCategory Category => FindingCategory.Indexing;

    /// <inheritdoc />
    public IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var findings = new List<Finding>();
        foreach (IndexSnapshot index in snapshot.Indexes)
        {
            if (index.IsValid)
            {
                continue;
            }

            findings.Add(CreateFinding(snapshot, index));
        }

        return findings;
    }

    private Finding CreateFinding(DatabaseSnapshot snapshot, IndexSnapshot index)
    {
        // The three state flags describe *which kind* of invalidity this is — a stable
        // property of the condition rather than a fluctuating measurement — so they belong in
        // the fingerprint.
        var evidence = new List<EvidenceItem>
        {
            new("schema", index.SchemaName, FingerprintParticipation.Include),
            new("table", index.TableName, FingerprintParticipation.Include),
            new("index", index.IndexName, FingerprintParticipation.Include),
            new("is_valid", "false", FingerprintParticipation.Include),
            new(
                "is_ready",
                index.IsReady ? "true" : "false",
                FingerprintParticipation.Include),
            new("is_live", index.IsLive ? "true" : "false", FingerprintParticipation.Include),
            new("access_method", index.AccessMethod, FingerprintParticipation.Include),
            new(
                "index_size_bytes",
                index.SizeBytes.ToString(CultureInfo.InvariantCulture),
                FingerprintParticipation.Exclude,
                "bytes"),
        };

        return new Finding(
            Code,
            Version,
            Category,
            FindingSeverity.Critical,
            FindingConfidence.High,
            new DatabaseObjectReference(
                DatabaseObjectType.Index, index.SchemaName, index.IndexName, index.TableName),
            $"The index '{index.SchemaName}.{index.IndexName}' on "
                + $"'{index.SchemaName}.{index.TableName}' is marked invalid by the engine.",
            "An invalid index is not used by the query planner, so queries that depend on it fall "
                + "back to slower plans while the index still costs storage and write time. It "
                + "usually results from an interrupted concurrent build. Plan a supervised rebuild "
                + "with the owning team. This tool performs no DDL and does not rebuild or drop "
                + "anything.",
            evidence,
            DiagnosticDocumentation.Reference,
            snapshot.Metadata.Engine);
    }
}
