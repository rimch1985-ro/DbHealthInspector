namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// An immutable, engine-neutral point-in-time view of a database, sufficient to evaluate every
/// diagnostic rule.
/// </summary>
/// <remarks>
/// A snapshot carries only observed facts. It never carries findings, recommendations or risk
/// conclusions; those are produced by rules that consume a snapshot.
/// </remarks>
public sealed class DatabaseSnapshot
{
    /// <summary>
    /// Identity information about the inspected database.
    /// </summary>
    public DatabaseMetadata Metadata { get; }

    /// <summary>
    /// The schemas included in this inspection. No two schemas share the same
    /// <see cref="SchemaSnapshot.SchemaName"/>.
    /// </summary>
    public IReadOnlyList<SchemaSnapshot> Schemas { get; }

    /// <summary>
    /// The tables observed in this inspection. No two tables share the same
    /// (<see cref="TableSnapshot.SchemaName"/>, <see cref="TableSnapshot.TableName"/>) pair.
    /// </summary>
    public IReadOnlyList<TableSnapshot> Tables { get; }

    /// <summary>
    /// The indexes observed in this inspection. No two indexes share the same
    /// (<see cref="IndexSnapshot.SchemaName"/>, <see cref="IndexSnapshot.IndexName"/>) pair,
    /// matching PostgreSQL's per-schema index name uniqueness.
    /// </summary>
    public IReadOnlyList<IndexSnapshot> Indexes { get; }

    /// <summary>
    /// The capability state reported for this inspection.
    /// </summary>
    public CapabilitySnapshot Capabilities { get; }

    /// <summary>
    /// Server-wide statistics context for this inspection.
    /// </summary>
    public StatisticsSnapshot Statistics { get; }

    /// <summary>
    /// Creates a database snapshot. All collections are copied defensively.
    /// </summary>
    public DatabaseSnapshot(
        DatabaseMetadata metadata,
        IReadOnlyCollection<SchemaSnapshot> schemas,
        IReadOnlyCollection<TableSnapshot> tables,
        IReadOnlyCollection<IndexSnapshot> indexes,
        CapabilitySnapshot capabilities,
        StatisticsSnapshot statistics)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(statistics);

        Metadata = metadata;

        IReadOnlyList<SchemaSnapshot> schemasCopy =
            Guard.CopyDefensivelyRejectingNullElements(schemas, nameof(schemas));
        var seenSchemaNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (SchemaSnapshot schema in schemasCopy)
        {
            if (!seenSchemaNames.Add(schema.SchemaName))
            {
                throw new ArgumentException($"Duplicate schema '{schema.SchemaName}'.", nameof(schemas));
            }
        }

        Schemas = schemasCopy;

        IReadOnlyList<TableSnapshot> tablesCopy =
            Guard.CopyDefensivelyRejectingNullElements(tables, nameof(tables));
        var seenTables = new HashSet<(string Schema, string Table)>();
        foreach (TableSnapshot table in tablesCopy)
        {
            if (!seenTables.Add((table.SchemaName, table.TableName)))
            {
                throw new ArgumentException(
                    $"Duplicate table '{table.SchemaName}.{table.TableName}'.", nameof(tables));
            }
        }

        Tables = tablesCopy;

        IReadOnlyList<IndexSnapshot> indexesCopy =
            Guard.CopyDefensivelyRejectingNullElements(indexes, nameof(indexes));
        var seenIndexes = new HashSet<(string Schema, string Index)>();
        foreach (IndexSnapshot index in indexesCopy)
        {
            if (!seenIndexes.Add((index.SchemaName, index.IndexName)))
            {
                throw new ArgumentException(
                    $"Duplicate index '{index.SchemaName}.{index.IndexName}'.", nameof(indexes));
            }
        }

        Indexes = indexesCopy;

        Capabilities = capabilities;
        Statistics = statistics;
    }
}
