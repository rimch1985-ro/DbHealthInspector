using System.Collections.ObjectModel;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.PostgreSql.Indexes;

/// <summary>
/// The complete, immutable outcome of one index-snapshot operation — E001, plus E002 when the
/// usage-statistics capability was available.
/// </summary>
/// <remarks>
/// <para>
/// Everything it exposes is an existing Core model. No Npgsql type, OID, SQL text, connection,
/// transaction, command, reader or stored exception crosses this boundary, and no static mutable
/// state backs it.
/// </para>
/// <para>
/// The constructor copies its input, so a caller that keeps and mutates the source list cannot
/// change a result that already exists, and sorts the copy by <c>SchemaName</c>, <c>TableName</c>
/// then <c>IndexName</c> with <see cref="StringComparer.Ordinal"/> — deliberately repeating what
/// E001's <c>ORDER BY</c> already did, because database collation and process culture are not
/// things this adapter is willing to depend on for a deterministic result.
/// </para>
/// <para>
/// Deliberately not a <see langword="record"/>: a record's generated
/// <see cref="object.ToString"/> would render every schema, table, index, expression and predicate
/// structurally. Those are authorized <i>result</i> values, reachable through
/// <see cref="Indexes"/>, but they must never leak into an exception, a log or a test display name
/// — so the inherited <see cref="object.ToString"/>, which returns only the type name, is the
/// safer default.
/// </para>
/// </remarks>
internal sealed class PostgreSqlIndexSnapshotQueryResult
{
    /// <summary>
    /// Every mapped index, ordered by schema name, table name then index name, all ordinally.
    /// Empty is a valid result.
    /// </summary>
    internal ReadOnlyCollection<IndexSnapshot> Indexes { get; }

    /// <summary>
    /// Creates the result from already-mapped snapshots.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="indexes"/> is <see langword="null"/>.</exception>
    /// <exception cref="PostgreSqlIndexSnapshotMappingException">
    /// An element is null, or two elements share the same schema and index name.
    /// </exception>
    internal PostgreSqlIndexSnapshotQueryResult(IReadOnlyList<IndexSnapshot> indexes)
    {
        ArgumentNullException.ThrowIfNull(indexes);

        var copied = new IndexSnapshot[indexes.Count];
        for (var index = 0; index < indexes.Count; index++)
        {
            copied[index] = indexes[index] ?? throw new PostgreSqlIndexSnapshotMappingException();
        }

        Array.Sort(copied, CompareCanonically);

        // An index name is unique per schema in PostgreSQL, so two snapshots sharing one is a
        // contract violation rather than something to silently collapse. The table name is
        // deliberately excluded from this test: it must not be able to disguise a duplicate.
        //
        // Compared globally rather than pairwise. The sort above orders by schema, *table*, then
        // index, so two entries sharing a schema and index name are only neighbours when nothing
        // sorts between them — a third index on a table whose name falls in between separates them
        // and a neighbour-only scan would miss the collision entirely (GC-DHI-04E-C1, R1-01).
        var seen = new HashSet<(string SchemaName, string IndexName)>(copied.Length);
        foreach (IndexSnapshot snapshot in copied)
        {
            if (!seen.Add((snapshot.SchemaName, snapshot.IndexName)))
            {
                throw new PostgreSqlIndexSnapshotMappingException();
            }
        }

        Indexes = Array.AsReadOnly(copied);
    }

    private static int CompareCanonically(IndexSnapshot left, IndexSnapshot right)
    {
        int bySchema = string.CompareOrdinal(left.SchemaName, right.SchemaName);
        if (bySchema != 0)
        {
            return bySchema;
        }

        int byTable = string.CompareOrdinal(left.TableName, right.TableName);
        return byTable != 0 ? byTable : string.CompareOrdinal(left.IndexName, right.IndexName);
    }
}
