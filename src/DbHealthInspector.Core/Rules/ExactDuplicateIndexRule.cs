using System.Globalization;
using System.Text;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Rules;

/// <summary>
/// DBH003 — reports groups of indexes on the same table that are structurally identical.
/// </summary>
/// <remarks>
/// <para>
/// This rule deliberately does <b>not</b> use <see cref="IndexSnapshot.Equals(IndexSnapshot)"/>.
/// That override also compares <see cref="IndexSnapshot.IndexName"/>,
/// <see cref="IndexSnapshot.SizeBytes"/> and <see cref="IndexSnapshot.ScanCount"/>, which is
/// correct for identity but returns <see langword="false"/> for exactly the duplicates this
/// rule must find: two duplicates necessarily have different names and usually different
/// sizes. The structural key below is therefore built here, and
/// <see cref="IndexSnapshot"/> is left untouched.
/// </para>
/// <para>
/// Pure and deterministic; performs no I/O. See docs/gates/GC-DHI-05A_DEFINITION.md §7.
/// </para>
/// </remarks>
public sealed class ExactDuplicateIndexRule : IInspectionRule
{
    /// <inheritdoc />
    public FindingCode Code => FindingCodes.ExactDuplicateIndex;

    /// <inheritdoc />
    public RuleVersion Version => RuleVersion.Initial;

    /// <inheritdoc />
    public string Name => "EXACT_DUPLICATE_INDEX";

    /// <inheritdoc />
    public FindingCategory Category => FindingCategory.Indexing;

    /// <inheritdoc />
    public IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        // Group by table and structure together: the key already carries schema and table, so
        // indexes on different tables can never share a group.
        var groups = new Dictionary<string, List<IndexSnapshot>>(StringComparer.Ordinal);
        foreach (IndexSnapshot index in snapshot.Indexes)
        {
            string key = BuildStructuralKey(index);
            if (!groups.TryGetValue(key, out List<IndexSnapshot>? members))
            {
                members = [];
                groups[key] = members;
            }

            members.Add(index);
        }

        var findings = new List<Finding>();
        foreach (List<IndexSnapshot> members in groups.Values)
        {
            if (members.Count < 2)
            {
                continue;
            }

            findings.Add(CreateFinding(snapshot, members));
        }

        return findings;
    }

    /// <summary>
    /// Builds the structural identity of an index: schema, table, access method, ordered key
    /// parts with every structural property of each part, ordered INCLUDE columns, partial
    /// predicate, uniqueness and null-distinctness.
    /// </summary>
    /// <remarks>
    /// Deliberately excluded, so that two indexes differing only in these remain duplicates:
    /// <c>IndexName</c>, <c>SizeBytes</c>, <c>ScanCount</c>, <c>IsValid</c>, <c>IsReady</c>,
    /// <c>IsLive</c>, <c>IsPrimaryKey</c> and <c>BacksConstraint</c>.
    /// <para>
    /// Every component is length-prefixed and every optional value carries an explicit
    /// presence marker, so no combination of field values can encode to the same key as a
    /// different combination — the same technique the finding fingerprint uses.
    /// </para>
    /// </remarks>
    private static string BuildStructuralKey(IndexSnapshot index)
    {
        var builder = new StringBuilder();

        AppendValue(builder, index.SchemaName);
        AppendValue(builder, index.TableName);
        AppendValue(builder, index.AccessMethod);

        AppendCount(builder, index.KeyParts.Count);
        foreach (IndexKeyPartSnapshot part in index.KeyParts)
        {
            AppendCount(builder, part.Position);
            AppendOptionalValue(builder, part.ColumnName);
            AppendOptionalValue(builder, part.Expression);
            AppendOptionalValue(builder, part.Collation);
            AppendOptionalValue(builder, part.OperatorClass);
            AppendValue(builder, part.SortDirection.ToString());
            AppendValue(builder, part.NullsOrdering.ToString());
        }

        AppendCount(builder, index.IncludedColumns.Count);
        foreach (string column in index.IncludedColumns)
        {
            AppendValue(builder, column);
        }

        AppendOptionalValue(builder, index.PartialPredicate);
        AppendValue(builder, index.IsUnique ? "u1" : "u0");
        AppendValue(
            builder,
            index.NullsNotDistinct switch { true => "n1", false => "n0", null => "n?" });

        return builder.ToString();
    }

    private static void AppendValue(StringBuilder builder, string value)
    {
        builder.Append(value.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(value).Append('|');
    }

    private static void AppendOptionalValue(StringBuilder builder, string? value)
    {
        if (value is null)
        {
            // A distinct marker for absence, so a null can never collide with any present
            // value — including the empty string.
            builder.Append("~|");
            return;
        }

        builder.Append('=');
        AppendValue(builder, value);
    }

    private static void AppendCount(StringBuilder builder, int value)
    {
        builder.Append('#').Append(value.ToString(CultureInfo.InvariantCulture)).Append('|');
    }

    private static Finding CreateFinding(DatabaseSnapshot snapshot, List<IndexSnapshot> members)
    {
        // Ordinal order makes both the anchor and the evidence deterministic.
        IndexSnapshot[] ordered = [.. members.OrderBy(index => index.IndexName, StringComparer.Ordinal)];
        IndexSnapshot anchor = ordered[0];

        string indexNames = string.Join(", ", ordered.Select(index => index.IndexName));
        string indexSizes = string.Join(
            ", ", ordered.Select(index => index.SizeBytes.ToString(CultureInfo.InvariantCulture)));

        var evidence = new List<EvidenceItem>
        {
            new("schema", anchor.SchemaName, FingerprintParticipation.Include),
            new("table", anchor.TableName, FingerprintParticipation.Include),
            new("duplicate_indexes", indexNames, FingerprintParticipation.Include),
            new(
                "duplicate_count",
                ordered.Length.ToString(CultureInfo.InvariantCulture),
                FingerprintParticipation.Include),
            new("access_method", anchor.AccessMethod, FingerprintParticipation.Include),

            // Sizes fluctuate, so they inform the reader without destabilizing the fingerprint.
            new("index_sizes_bytes", indexSizes, FingerprintParticipation.Exclude, "bytes"),
        };

        return new Finding(
            FindingCodes.ExactDuplicateIndex,
            RuleVersion.Initial,
            FindingCategory.Indexing,
            FindingSeverity.Warning,
            FindingConfidence.High,
            new DatabaseObjectReference(
                DatabaseObjectType.Index, anchor.SchemaName, anchor.IndexName, anchor.TableName),
            $"The table '{anchor.SchemaName}.{anchor.TableName}' has {ordered.Length} structurally "
                + $"identical indexes: {indexNames}.",
            "Structurally identical indexes duplicate write cost and storage without adding read "
                + "capability. Confirm which one to retain and remove the rest during a planned "
                + "change. Check first whether an index backs a constraint: such an index must be "
                + "removed through its constraint, not dropped directly. This tool does not modify "
                + "the database.",
            evidence,
            DiagnosticDocumentation.Reference,
            snapshot.Metadata.Engine);
    }
}
