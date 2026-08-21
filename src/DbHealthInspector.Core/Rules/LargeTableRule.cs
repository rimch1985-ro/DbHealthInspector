using System.Globalization;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Rules;

/// <summary>
/// DBH002 — reports tables whose estimated row count or total size has crossed a product
/// threshold.
/// </summary>
/// <remarks>
/// Pure and deterministic. It reads only snapshot fields and never executes
/// <c>COUNT(*)</c> or any other statement. See docs/gates/GC-DHI-05A_DEFINITION.md §6.
/// </remarks>
public sealed class LargeTableRule : IInspectionRule
{
    private const string RowsOnly = "rows";
    private const string SizeOnly = "size";
    private const string RowsAndSize = "rows_and_size";

    private readonly DiagnosticThresholds _thresholds;

    /// <summary>
    /// Creates the rule with the frozen default thresholds.
    /// </summary>
    public LargeTableRule()
        : this(DiagnosticThresholds.Default)
    {
    }

    /// <summary>
    /// Creates the rule with explicit thresholds.
    /// </summary>
    /// <param name="thresholds">The thresholds to compare against.</param>
    public LargeTableRule(DiagnosticThresholds thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        _thresholds = thresholds;
    }

    /// <inheritdoc />
    public FindingCode Code => FindingCodes.LargeTable;

    /// <inheritdoc />
    public RuleVersion Version => RuleVersion.Initial;

    /// <inheritdoc />
    public string Name => "LARGE_TABLE";

    /// <inheritdoc />
    public FindingCategory Category => FindingCategory.Capacity;

    /// <inheritdoc />
    public IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var findings = new List<Finding>();
        foreach (TableSnapshot table in snapshot.Tables)
        {
            if (!IsInScope(table))
            {
                continue;
            }

            // A null estimate is unknown, never zero: it disables only the row criterion and
            // never blocks the size criterion.
            bool rowsExceeded = table.EstimatedRowCount is { } rows
                && rows >= _thresholds.LargeTableRowThreshold;
            bool sizeExceeded = table.TotalSizeBytes >= _thresholds.LargeTableSizeThresholdBytes;

            if (!rowsExceeded && !sizeExceeded)
            {
                continue;
            }

            findings.Add(CreateFinding(snapshot, table, rowsExceeded, sizeExceeded));
        }

        return findings;
    }

    /// <summary>
    /// Ordinary tables and physical partitions are in scope: both hold real storage. A
    /// partitioned root is excluded because the snapshot reports physical relation sizes
    /// without descendant aggregation, so a root's own size is not the logical size of its
    /// partitions and must not be presented as such. Views and foreign tables report no local
    /// storage; materialized views, temporary tables and unclassified relations are out of
    /// scope for this gate.
    /// </summary>
    private static bool IsInScope(TableSnapshot table)
    {
        if (table.IsPartitionedRoot)
        {
            return false;
        }

        if (table.IsPartition)
        {
            return true;
        }

        return table.RelationKind is RelationKind.OrdinaryTable or RelationKind.Partition;
    }

    private Finding CreateFinding(
        DatabaseSnapshot snapshot, TableSnapshot table, bool rowsExceeded, bool sizeExceeded)
    {
        string exceeded = rowsExceeded && sizeExceeded
            ? RowsAndSize
            : rowsExceeded ? RowsOnly : SizeOnly;

        // Which *kind* of largeness this is belongs to the finding's identity; the
        // measurements themselves do not, so the finding survives ordinary growth.
        var evidence = new List<EvidenceItem>
        {
            new("schema", table.SchemaName, FingerprintParticipation.Include),
            new("table", table.TableName, FingerprintParticipation.Include),
            new("exceeded_threshold", exceeded, FingerprintParticipation.Include),
        };

        if (table.EstimatedRowCount is { } estimatedRows)
        {
            evidence.Add(new EvidenceItem(
                "estimated_rows",
                estimatedRows.ToString(CultureInfo.InvariantCulture),
                FingerprintParticipation.Exclude,
                "rows"));
            evidence.Add(new EvidenceItem(
                "row_threshold",
                _thresholds.LargeTableRowThreshold.ToString(CultureInfo.InvariantCulture),
                FingerprintParticipation.Exclude,
                "rows"));
        }

        evidence.Add(new EvidenceItem(
            "total_size_bytes",
            table.TotalSizeBytes.ToString(CultureInfo.InvariantCulture),
            FingerprintParticipation.Exclude,
            "bytes"));
        evidence.Add(new EvidenceItem(
            "size_threshold_bytes",
            _thresholds.LargeTableSizeThresholdBytes.ToString(CultureInfo.InvariantCulture),
            FingerprintParticipation.Exclude,
            "bytes"));

        return new Finding(
            Code,
            Version,
            Category,
            FindingSeverity.Info,
            FindingConfidence.Medium,
            new DatabaseObjectReference(DatabaseObjectType.Table, table.SchemaName, table.TableName),
            $"The table '{table.SchemaName}.{table.TableName}' has crossed a large-table threshold "
                + $"({exceeded}).",
            "Large tables are not a defect in themselves. Treat this as a prompt to confirm that "
                + "maintenance, backup and query plans still suit this table's current scale, and to "
                + "consider partitioning or archiving if growth continues. No action is required by "
                + "this finding alone.",
            evidence,
            DiagnosticDocumentation.Reference,
            snapshot.Metadata.Engine);
    }
}
