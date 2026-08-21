using System.Globalization;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Rules;

/// <summary>
/// DBH001 — reports user tables and partitioned roots that have no primary key.
/// </summary>
/// <remarks>
/// Pure and deterministic; reads <see cref="TableSnapshot.HasPrimaryKey"/> and performs no
/// I/O. See docs/gates/GC-DHI-05A_DEFINITION.md §5.
/// </remarks>
public sealed class TableWithoutPrimaryKeyRule : IInspectionRule
{
    /// <inheritdoc />
    public FindingCode Code => FindingCodes.TableWithoutPrimaryKey;

    /// <inheritdoc />
    public RuleVersion Version => RuleVersion.Initial;

    /// <inheritdoc />
    public string Name => "TABLE_WITHOUT_PRIMARY_KEY";

    /// <inheritdoc />
    public FindingCategory Category => FindingCategory.Structure;

    /// <inheritdoc />
    public IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var findings = new List<Finding>();
        foreach (TableSnapshot table in snapshot.Tables)
        {
            if (!IsInScope(table) || table.HasPrimaryKey)
            {
                continue;
            }

            findings.Add(CreateFinding(snapshot, table));
        }

        return findings;
    }

    /// <summary>
    /// Ordinary tables and partitioned roots are in scope. A physical partition is excluded
    /// because its key is defined at the root, so reporting it would multiply one root defect
    /// across every partition. Views, materialized views and foreign tables cannot carry a
    /// primary key; temporary tables are session-scoped; unclassified relations are never
    /// reported.
    /// </summary>
    private static bool IsInScope(TableSnapshot table)
    {
        if (table.IsPartition)
        {
            return false;
        }

        return table.RelationKind is RelationKind.OrdinaryTable or RelationKind.PartitionedTable;
    }

    private Finding CreateFinding(DatabaseSnapshot snapshot, TableSnapshot table)
    {
        var evidence = new List<EvidenceItem>
        {
            new("schema", table.SchemaName, FingerprintParticipation.Include),
            new("table", table.TableName, FingerprintParticipation.Include),
            new("relation_kind", table.RelationKind.ToString(), FingerprintParticipation.Include),
        };

        // Measurements never participate in the fingerprint: a table's identity as "has no
        // primary key" must survive its own growth between two inspections.
        if (table.EstimatedRowCount is { } estimatedRows)
        {
            evidence.Add(new EvidenceItem(
                "estimated_rows",
                estimatedRows.ToString(CultureInfo.InvariantCulture),
                FingerprintParticipation.Exclude,
                "rows"));
        }

        evidence.Add(new EvidenceItem(
            "total_size_bytes",
            table.TotalSizeBytes.ToString(CultureInfo.InvariantCulture),
            FingerprintParticipation.Exclude,
            "bytes"));

        return new Finding(
            Code,
            Version,
            Category,
            FindingSeverity.Warning,
            FindingConfidence.High,
            new DatabaseObjectReference(DatabaseObjectType.Table, table.SchemaName, table.TableName),
            $"The table '{table.SchemaName}.{table.TableName}' has no primary key.",
            "Review whether this table should declare a primary key. A primary key gives rows a "
                + "stable identity, which replication, incremental tooling and many client libraries "
                + "depend on. Choose the key with the owning team and apply it during a planned "
                + "change; this tool does not modify the database.",
            evidence,
            DiagnosticDocumentation.Reference,
            snapshot.Metadata.Engine);
    }
}
