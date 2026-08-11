using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.PostgreSql.Indexes;

/// <summary>
/// Maps one complete group of E001 rows — every attribute of a single index — to one
/// <see cref="IndexSnapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every value is validated <b>before</b> an <see cref="IndexSnapshot"/> is constructed. That
/// ordering is deliberate: Core's own guards are correct but they name the offending parameter and
/// sometimes the offending value, and those exceptions would escape through the session boundary.
/// Pre-validating means a bad group always surfaces as the fixed, valueless
/// <see cref="PostgreSqlIndexSnapshotMappingException"/> instead.
/// </para>
/// <para>
/// It reads no rows itself, holds no state, parses no DDL and reconstructs nothing. A column key,
/// an expression key, a collation, an operator class and an ordering are all separate server-supplied
/// fields; none is recovered by picking apart <c>CREATE INDEX</c> text.
/// </para>
/// </remarks>
internal static class PostgreSqlIndexSnapshotMapper
{
    private const string PhysicalIndexKind = "i";
    private const string PartitionedIndexKind = "I";

    /// <summary>
    /// Applies every rule <see cref="Map"/> applies, without keeping the result.
    /// </summary>
    /// <remarks>
    /// Exists so the executor can reject a malformed group <b>while its reader is still open</b>.
    /// The scan count only ever reaches the constructed snapshot and can never change whether a
    /// group is valid, so validating with <see langword="null"/> here is equivalent to validating
    /// with the real counter. Deliberately implemented by calling <see cref="Map"/> rather than by
    /// a parallel copy of the rules: a second implementation could drift, and this one cannot.
    /// </remarks>
    /// <exception cref="PostgreSqlIndexSnapshotMappingException">The group is not mappable.</exception>
    internal static void ValidateGroup(IReadOnlyList<PostgreSqlIndexMetadataRow> rows) => _ = Map(rows, null);

    /// <summary>
    /// Maps one already-grouped index: all rows of a single (schema, table, index), in the order
    /// E001 returned them.
    /// </summary>
    /// <param name="rows">Every attribute row of exactly one index.</param>
    /// <param name="scanCount">
    /// The merged E002 counter, or <see langword="null"/> when statistics were unavailable, the
    /// index is virtual, or no statistics row matched. Never zero as a stand-in for unknown.
    /// </param>
    /// <exception cref="PostgreSqlIndexSnapshotMappingException">
    /// Any value is missing, contradictory, out of range, or a combination this adapter does not
    /// recognise.
    /// </exception>
    internal static IndexSnapshot Map(IReadOnlyList<PostgreSqlIndexMetadataRow> rows, long? scanCount)
    {
        if (rows is null || rows.Count == 0)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        PostgreSqlIndexMetadataRow header = rows[0] ?? throw new PostgreSqlIndexSnapshotMappingException();

        ValidateHeader(header);
        ValidateGroupConsistency(rows, header);

        var keyParts = new List<IndexKeyPartSnapshot>(header.KeyAttributeCount);
        var includedColumns = new List<string>(header.AttributeCount - header.KeyAttributeCount);
        var seenIncluded = new HashSet<string>(StringComparer.Ordinal);

        // Positions were already proven to be exactly 1..AttributeCount with no gap or duplicate,
        // so indexing by position - 1 after sorting is safe.
        foreach (PostgreSqlIndexMetadataRow row in Ordered(rows))
        {
            if (row.AttributePosition <= header.KeyAttributeCount)
            {
                if (!row.IsKey)
                {
                    throw new PostgreSqlIndexSnapshotMappingException();
                }

                keyParts.Add(MapKeyPart(row));
                continue;
            }

            if (row.IsKey)
            {
                throw new PostgreSqlIndexSnapshotMappingException();
            }

            string included = MapIncludedColumn(row);
            if (!seenIncluded.Add(included))
            {
                throw new PostgreSqlIndexSnapshotMappingException();
            }

            includedColumns.Add(included);
        }

        if (keyParts.Count != header.KeyAttributeCount
            || includedColumns.Count != header.AttributeCount - header.KeyAttributeCount)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        ValidateIndexWideFacts(header);

        try
        {
            return new IndexSnapshot(
                header.SchemaName,
                header.TableName,
                header.IndexName,
                header.AccessMethod,
                keyParts,
                includedColumns,
                header.PartialPredicate,
                header.IsUnique,
                header.NullsNotDistinct,
                header.IsPrimaryKey,
                header.BacksConstraint,
                header.IsValid,
                header.IsReady,
                header.IsLive,
                header.SizeBytes,
                scanCount);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            // Core's guards are correct but name their parameter and sometimes their value. Any
            // state that reaches them here is one this mapper failed to reject first, and it must
            // still surface as the fixed, valueless failure.
            throw new PostgreSqlIndexSnapshotMappingException();
        }
    }

    // --- Header and group consistency ---------------------------------------------------------

    private static void ValidateHeader(PostgreSqlIndexMetadataRow header)
    {
        // Contractually active identifiers: each must be present and carry something usable.
        if (!IsActive(header.SchemaName)
            || !IsActive(header.TableName)
            || !IsActive(header.IndexName)
            || !IsActive(header.AccessMethod))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        // Only a physical index or a partitioned (virtual) index root may appear. E001's WHERE
        // clause already restricts relkind, so anything else means the shape contract was broken.
        if (header.IndexRelationKind is not (PhysicalIndexKind or PartitionedIndexKind))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        if (header.AttributeCount < 1
            || header.KeyAttributeCount < 1
            || header.KeyAttributeCount > header.AttributeCount)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        if (header.SizeBytes < 0)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        // A virtual index has no storage of its own and never aggregates its partitions'.
        if (header.IndexRelationKind == PartitionedIndexKind && header.SizeBytes != 0)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        // Optional field: SQL NULL means the index is not partial. A present predicate must be
        // usable — a blank one is a broken row, never a second way of saying "not partial".
        if (header.PartialPredicate is not null && !IsActive(header.PartialPredicate))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }
    }

    /// <summary>
    /// Requires the group to describe exactly one index: every repeated header column identical,
    /// and positions forming exactly <c>1..AttributeCount</c>.
    /// </summary>
    private static void ValidateGroupConsistency(
        IReadOnlyList<PostgreSqlIndexMetadataRow> rows,
        PostgreSqlIndexMetadataRow header)
    {
        if (rows.Count != header.AttributeCount)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        var seenPositions = new HashSet<int>(rows.Count);

        foreach (PostgreSqlIndexMetadataRow row in rows)
        {
            if (row is null)
            {
                throw new PostgreSqlIndexSnapshotMappingException();
            }

            // Every column that describes the index rather than the attribute must be identical
            // across the group; a mismatch means two indexes were folded together.
            bool sameHeader =
                string.Equals(row.SchemaName, header.SchemaName, StringComparison.Ordinal)
                && string.Equals(row.TableName, header.TableName, StringComparison.Ordinal)
                && string.Equals(row.IndexName, header.IndexName, StringComparison.Ordinal)
                && string.Equals(row.AccessMethod, header.AccessMethod, StringComparison.Ordinal)
                && string.Equals(row.IndexRelationKind, header.IndexRelationKind, StringComparison.Ordinal)
                && row.IsIndexPartition == header.IsIndexPartition
                && row.AttributeCount == header.AttributeCount
                && row.KeyAttributeCount == header.KeyAttributeCount
                && string.Equals(row.PartialPredicate, header.PartialPredicate, StringComparison.Ordinal)
                && row.IsUnique == header.IsUnique
                && row.NullsNotDistinct == header.NullsNotDistinct
                && row.IsPrimaryKey == header.IsPrimaryKey
                && row.BacksConstraint == header.BacksConstraint
                && row.IsValid == header.IsValid
                && row.IsReady == header.IsReady
                && row.IsLive == header.IsLive
                && row.SizeBytes == header.SizeBytes;

            if (!sameHeader)
            {
                throw new PostgreSqlIndexSnapshotMappingException();
            }

            if (row.AttributePosition < 1 || row.AttributePosition > header.AttributeCount)
            {
                throw new PostgreSqlIndexSnapshotMappingException();
            }

            if (!seenPositions.Add(row.AttributePosition))
            {
                throw new PostgreSqlIndexSnapshotMappingException();
            }
        }

        // Exactly AttributeCount distinct positions within 1..AttributeCount leaves no gap.
        if (seenPositions.Count != header.AttributeCount)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }
    }

    private static void ValidateIndexWideFacts(PostgreSqlIndexMetadataRow header)
    {
        // NullsNotDistinct is meaningful only for a unique index; for a non-unique one the server
        // reports NULL and inventing false would assert something it never said.
        if (header.IsUnique == (header.NullsNotDistinct is null))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        // A primary key is by construction a unique index backed by a constraint.
        if (header.IsPrimaryKey && (!header.IsUnique || !header.BacksConstraint))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }
    }

    private static PostgreSqlIndexMetadataRow[] Ordered(IReadOnlyList<PostgreSqlIndexMetadataRow> rows)
    {
        var ordered = new PostgreSqlIndexMetadataRow[rows.Count];
        for (var index = 0; index < rows.Count; index++)
        {
            ordered[index] = rows[index];
        }

        Array.Sort(ordered, static (left, right) => left.AttributePosition.CompareTo(right.AttributePosition));
        return ordered;
    }

    // --- Key parts ----------------------------------------------------------------------------

    private static IndexKeyPartSnapshot MapKeyPart(PostgreSqlIndexMetadataRow row)
    {
        // SQL NULL is the only way to say "this field does not apply". A present-but-blank string
        // is a broken row, not an absent value, so each field is checked for usability *before*
        // presence decides which kind of key this is. Testing blankness alone would silently accept
        // a simple key carrying a blank expression — the field is populated, so the row contradicts
        // itself — by reading that blank as absence.
        if ((row.ColumnName is not null && !IsActive(row.ColumnName))
            || (row.Expression is not null && !IsActive(row.Expression)))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        bool hasColumn = row.ColumnName is not null;
        bool hasExpression = row.Expression is not null;

        // Exactly one of the two, never both and never neither. A simple column key is never
        // reconstructed from DDL, and an expression key never carries a column name.
        if (hasColumn == hasExpression)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        // A key part must always name its operator class; the server supplies both halves or the
        // row is not describing a key this adapter understands.
        string operatorClass = MapOperatorClass(row);
        string? collation = MapCollation(row);
        (IndexSortDirection direction, IndexNullsOrdering nulls) = MapOrdering(row);

        return new IndexKeyPartSnapshot(
            row.AttributePosition,
            hasColumn ? row.ColumnName : null,
            hasExpression ? row.Expression : null,
            collation,
            operatorClass,
            direction,
            nulls);
    }

    private static string MapIncludedColumn(PostgreSqlIndexMetadataRow row)
    {
        // An INCLUDE attribute is a plain stored column and carries none of the key-only metadata.
        // Every one of these being SQL NULL — not merely blank — is what distinguishes it from a
        // key, so a populated field here means the row was misclassified. A blank string is a
        // populated field: it fails these checks exactly as a real value would.
        if (!IsActive(row.ColumnName)
            || row.Expression is not null
            || row.CollationSchema is not null
            || row.CollationName is not null
            || row.OperatorClassSchema is not null
            || row.OperatorClassName is not null
            || row.OperatorClassOptions is not null
            || row.IsOrderable is not null
            || row.IsAscending is not null
            || row.IsDescending is not null
            || row.NullsFirst is not null
            || row.NullsLast is not null)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        return row.ColumnName!;
    }

    // --- Structural identity ------------------------------------------------------------------

    private static string? MapCollation(PostgreSqlIndexMetadataRow row)
    {
        // Three states, not two. Absence is both halves being SQL NULL; presence is both halves
        // being non-null and non-blank. Everything else — half present, or present but blank — is
        // a broken row. Collapsing blank into absence here would silently drop a collation the
        // server did report, and collapsing it into presence would build an identity around an
        // empty name.
        if (row.CollationSchema is null && row.CollationName is null)
        {
            // No collation applies to this key part, which is normal for a non-collatable type.
            return null;
        }

        // Half-present is never completed with a guess: pg_catalog is not assumed, and an
        // unqualified name would depend on search_path.
        if (!IsActive(row.CollationSchema) || !IsActive(row.CollationName))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        return Qualify(row.CollationSchema, row.CollationName);
    }

    private static string MapOperatorClass(PostgreSqlIndexMetadataRow row)
    {
        // Required for every key part, so both halves must be present and non-blank; there is no
        // absent state to distinguish here.
        if (!IsActive(row.OperatorClassSchema) || !IsActive(row.OperatorClassName))
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        string qualified = Qualify(row.OperatorClassSchema, row.OperatorClassName);

        // SQL NULL means "no options", which is a different state from an options array that
        // happens to be empty. Only the latter gets an options suffix.
        return row.OperatorClassOptions is null
            ? qualified
            : qualified + EncodeOperatorClassOptions(row.OperatorClassOptions);
    }

    /// <summary>
    /// Renders a schema-qualified, double-quoted identity: <c>"schema"."name"</c>.
    /// </summary>
    /// <remarks>
    /// A structural identity, never SQL to execute. Embedded double quotes are doubled so the two
    /// halves cannot be confused with one another, the comparison stays ordinal, and the result is
    /// independent of <c>search_path</c>.
    /// </remarks>
    private static string Qualify(string schema, string name) =>
        "\"" + schema.Replace("\"", "\"\"", StringComparison.Ordinal)
        + "\".\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>
    /// Encodes ordered operator-class options as
    /// <c>|options[&lt;count&gt;;&lt;length&gt;:&lt;value&gt;...]</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Length-prefixing every element is what makes the encoding injective: because each value's
    /// extent is known before it is read, no option containing <c>:</c>, <c>;</c>, <c>]</c> or even
    /// the literal <c>|options[</c> can be confused with the structure around it, and two different
    /// option lists can never render identically.
    /// </para>
    /// <para>
    /// Lengths are .NET <see cref="string.Length"/> — UTF-16 code units — written in the invariant
    /// culture with no leading zero. Values are copied verbatim: no trimming, no Unicode
    /// normalization, no case folding, no semantic parsing and no sorting, because the stored order
    /// is itself part of the structural identity.
    /// </para>
    /// </remarks>
    private static string EncodeOperatorClassOptions(string[] options)
    {
        var builder = new StringBuilder("|options[");
        builder.Append(options.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(';');

        foreach (string? option in options)
        {
            // A null element would otherwise become indistinguishable from an empty option.
            if (option is null)
            {
                throw new PostgreSqlIndexSnapshotMappingException();
            }

            builder.Append(option.Length.ToString(CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(option);
        }

        builder.Append(']');
        return builder.ToString();
    }

    // --- Ordering -----------------------------------------------------------------------------

    /// <summary>
    /// Maps the five ordering properties to Core's direction and nulls placement.
    /// </summary>
    /// <remarks>
    /// <para>
    /// All five must be non-null for a key part. An orderable key must report exactly one direction
    /// and exactly one nulls placement; anything else is contradictory.
    /// </para>
    /// <para>
    /// A non-orderable key — hash, GIN, BRIN and the like — is accepted in exactly one shape: all
    /// five false. It maps to ascending/nulls-last as <b>normalization tokens</b>, because Core's
    /// enums have no "not applicable" member. That is the only place a value is synthesised, and it
    /// is reached only when the server positively reported non-orderability. An unknown or null
    /// property is never turned into a token.
    /// </para>
    /// </remarks>
    private static (IndexSortDirection Direction, IndexNullsOrdering Nulls) MapOrdering(
        PostgreSqlIndexMetadataRow row)
    {
        if (row.IsOrderable is not bool orderable
            || row.IsAscending is not bool ascending
            || row.IsDescending is not bool descending
            || row.NullsFirst is not bool nullsFirst
            || row.NullsLast is not bool nullsLast)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        if (!orderable)
        {
            // The only admitted non-orderable shape.
            if (ascending || descending || nullsFirst || nullsLast)
            {
                throw new PostgreSqlIndexSnapshotMappingException();
            }

            return (IndexSortDirection.Ascending, IndexNullsOrdering.Last);
        }

        // Exactly one direction and exactly one nulls placement.
        if (ascending == descending || nullsFirst == nullsLast)
        {
            throw new PostgreSqlIndexSnapshotMappingException();
        }

        return (
            ascending ? IndexSortDirection.Ascending : IndexSortDirection.Descending,
            nullsFirst ? IndexNullsOrdering.First : IndexNullsOrdering.Last);
    }

    /// <summary>
    /// Whether <paramref name="value"/> is a usable value: present (not SQL NULL) and carrying
    /// something other than whitespace.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately <b>not</b> a synonym for "not blank". SQL NULL and a present-but-blank string
    /// are different facts: NULL means the server said the field does not apply, while <c>""</c> or
    /// <c>"   "</c> means it said the field applies and then supplied nothing usable. Only the
    /// first is ever a valid absence; the second is always a broken row. Every caller therefore
    /// tests <c>is null</c> separately when absence is legal, and uses this only to decide whether
    /// a <i>present</i> value is usable.
    /// </para>
    /// <para>
    /// The value itself is never trimmed or rewritten — whitespace is detected, not removed — so a
    /// legitimate value keeps exactly the bytes the server sent.
    /// </para>
    /// </remarks>
    private static bool IsActive([NotNullWhen(true)] string? value) => !string.IsNullOrWhiteSpace(value);
}
