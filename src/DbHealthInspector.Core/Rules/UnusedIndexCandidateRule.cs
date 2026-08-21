using System.Globalization;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Rules;

/// <summary>
/// DBH004 — reports sizeable indexes the server has recorded no scans against.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately conservative. The rule is registered as requiring
/// <see cref="CapabilityKind.UsageStatistics"/>, so when statistics are unavailable the
/// orchestrator skips it and it never runs — absence of statistics can therefore never be
/// read as zero. A null <see cref="IndexSnapshot.ScanCount"/> is rejected inside the rule as
/// a second, independent line of defence.
/// </para>
/// <para>
/// Pure and deterministic: it inspects whether a statistics reset timestamp is present, and
/// never reads a clock or computes elapsed time. See
/// docs/gates/GC-DHI-05A_DEFINITION.md §8.
/// </para>
/// </remarks>
public sealed class UnusedIndexCandidateRule : IInspectionRule
{
    private readonly DiagnosticThresholds _thresholds;

    /// <summary>
    /// Creates the rule with the frozen default thresholds.
    /// </summary>
    public UnusedIndexCandidateRule()
        : this(DiagnosticThresholds.Default)
    {
    }

    /// <summary>
    /// Creates the rule with explicit thresholds.
    /// </summary>
    /// <param name="thresholds">The thresholds to compare against.</param>
    public UnusedIndexCandidateRule(DiagnosticThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        _thresholds = thresholds;
    }

    /// <inheritdoc />
    public FindingCode Code => FindingCodes.UnusedIndexCandidate;

    /// <inheritdoc />
    public RuleVersion Version => RuleVersion.Initial;

    /// <inheritdoc />
    public string Name => "UNUSED_INDEX_CANDIDATE";

    /// <inheritdoc />
    public FindingCategory Category => FindingCategory.Statistics;

    /// <inheritdoc />
    public IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var findings = new List<Finding>();
        foreach (IndexSnapshot index in snapshot.Indexes)
        {
            if (!IsCandidate(index))
            {
                continue;
            }

            findings.Add(CreateFinding(snapshot, index));
        }

        return findings;
    }

    /// <summary>
    /// Every condition must hold. A unique or primary-key index enforces a constraint
    /// regardless of scans; a constraint-backing index does too, even when it is neither
    /// unique nor a primary key — a PostgreSQL exclusion-constraint index is exactly that
    /// shape. An index that is not valid, ready and live has no comparable scan history.
    /// </summary>
    private bool IsCandidate(IndexSnapshot index) =>
        index.ScanCount == 0
        && index.SizeBytes >= _thresholds.UnusedIndexSizeThresholdBytes
        && !index.IsPrimaryKey
        && !index.IsUnique
        && !index.BacksConstraint
        && index.IsValid
        && index.IsReady
        && index.IsLive;

    private Finding CreateFinding(DatabaseSnapshot snapshot, IndexSnapshot index)
    {
        DateTimeOffset? resetAt = snapshot.Statistics.StatisticsResetAtUtc;

        // Presence only. Computing elapsed time would make the rule impure and would make the
        // same snapshot yield different findings on different runs.
        FindingConfidence confidence = resetAt is null
            ? FindingConfidence.Low
            : FindingConfidence.Medium;

        var evidence = new List<EvidenceItem>
        {
            new("schema", index.SchemaName, FingerprintParticipation.Include),
            new("table", index.TableName, FingerprintParticipation.Include),
            new("index", index.IndexName, FingerprintParticipation.Include),
            new("access_method", index.AccessMethod, FingerprintParticipation.Include),
            new("scan_count", "0", FingerprintParticipation.Exclude, "scans"),
            new(
                "index_size_bytes",
                index.SizeBytes.ToString(CultureInfo.InvariantCulture),
                FingerprintParticipation.Exclude,
                "bytes"),
            new(
                "size_threshold_bytes",
                _thresholds.UnusedIndexSizeThresholdBytes.ToString(CultureInfo.InvariantCulture),
                FingerprintParticipation.Exclude,
                "bytes"),
        };

        if (resetAt is { } reset)
        {
            evidence.Add(new EvidenceItem(
                "statistics_reset_at_utc",
                reset.ToString("O", CultureInfo.InvariantCulture),
                FingerprintParticipation.Exclude));
        }

        return new Finding(
            Code,
            Version,
            Category,
            FindingSeverity.Info,
            confidence,
            new DatabaseObjectReference(
                DatabaseObjectType.Index, index.SchemaName, index.IndexName, index.TableName),
            $"The index '{index.SchemaName}.{index.IndexName}' on "
                + $"'{index.SchemaName}.{index.TableName}' has recorded no scans.",
            "This is a candidate for human review, not an instruction. Zero recorded scans only "
                + "describes the window the server's statistics cover, which may be shorter than a "
                + "full business cycle and may exclude periodic or seasonal workloads. Confirm the "
                + "index is genuinely unused across a representative period before considering "
                + "removal. Do not drop it automatically.",
            evidence,
            DiagnosticDocumentation.Reference,
            snapshot.Metadata.Engine);
    }
}
