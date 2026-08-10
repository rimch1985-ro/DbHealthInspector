using System.Collections.ObjectModel;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.PostgreSql.Tables;

/// <summary>
/// The complete, immutable outcome of one D001 execution: every eligible relation the filter
/// admitted, in canonical order.
/// </summary>
/// <remarks>
/// <para>
/// Everything it exposes is an existing Core model. No Npgsql type, OID, SQL text, connection,
/// transaction, command, reader or stored exception crosses this boundary, and no static mutable
/// state backs it.
/// </para>
/// <para>
/// The constructor copies its input, so a caller that keeps and mutates the source list cannot
/// change a result that already exists, and sorts the copy by <c>SchemaName</c> then
/// <c>TableName</c> with <see cref="StringComparer.Ordinal"/> — deliberately repeating what D001's
/// <c>ORDER BY</c> already did, because database collation and process culture are not things this
/// adapter is willing to depend on for a deterministic result.
/// </para>
/// <para>
/// Deliberately not a <see langword="record"/>: a record's generated
/// <see cref="object.ToString"/> would render every schema and table name structurally. Those are
/// authorized <i>result</i> values, reachable through <see cref="Tables"/>, but they must never
/// leak into an exception, a log or a test display name — so the inherited
/// <see cref="object.ToString"/>, which returns only the type name, is the safer default.
/// </para>
/// </remarks>
internal sealed class PostgreSqlTableSnapshotQueryResult
{
    /// <summary>
    /// Every mapped relation, ordered by schema name then table name, both ordinally. Empty is a
    /// valid result.
    /// </summary>
    internal ReadOnlyCollection<TableSnapshot> Tables { get; }

    /// <summary>
    /// Creates the result from already-mapped snapshots.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="tables"/> is <see langword="null"/>.</exception>
    /// <exception cref="PostgreSqlTableSnapshotMappingException">
    /// An element is null, or two elements share the same schema and table name.
    /// </exception>
    internal PostgreSqlTableSnapshotQueryResult(IReadOnlyList<TableSnapshot> tables)
    {
        ArgumentNullException.ThrowIfNull(tables);

        var copied = new TableSnapshot[tables.Count];
        for (var index = 0; index < tables.Count; index++)
        {
            copied[index] = tables[index] ?? throw new PostgreSqlTableSnapshotMappingException();
        }

        Array.Sort(copied, CompareCanonically);

        // Duplicates are a contract violation, not something to silently collapse: after the sort
        // any pair is adjacent, so one pass is enough.
        for (var index = 1; index < copied.Length; index++)
        {
            if (CompareCanonically(copied[index - 1], copied[index]) == 0)
            {
                throw new PostgreSqlTableSnapshotMappingException();
            }
        }

        Tables = Array.AsReadOnly(copied);
    }

    private static int CompareCanonically(TableSnapshot left, TableSnapshot right)
    {
        int bySchema = string.CompareOrdinal(left.SchemaName, right.SchemaName);
        return bySchema != 0 ? bySchema : string.CompareOrdinal(left.TableName, right.TableName);
    }
}
