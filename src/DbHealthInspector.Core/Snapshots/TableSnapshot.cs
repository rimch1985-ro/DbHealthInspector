namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// A single table (or table-like relation) observed during an inspection.
/// </summary>
public sealed record TableSnapshot
{
    /// <summary>
    /// The owning schema name.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// The table name.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// The relation kind.
    /// </summary>
    public RelationKind RelationKind { get; }

    /// <summary>
    /// Whether this table is the root of a partitioned table.
    /// </summary>
    public bool IsPartitionedRoot { get; }

    /// <summary>
    /// Whether this table is an individual partition of a partitioned table.
    /// </summary>
    public bool IsPartition { get; }

    /// <summary>
    /// The estimated row count, when known. Estimates come from catalog statistics, never from
    /// <c>COUNT(*)</c>.
    /// </summary>
    public long? EstimatedRowCount { get; }

    /// <summary>
    /// The table's own storage size, in bytes.
    /// </summary>
    public long TableSizeBytes { get; }

    /// <summary>
    /// The combined size of the table's indexes, in bytes.
    /// </summary>
    public long IndexSizeBytes { get; }

    /// <summary>
    /// The total storage size attributable to the table, in bytes.
    /// </summary>
    public long TotalSizeBytes { get; }

    /// <summary>
    /// Whether the table defines a primary key.
    /// </summary>
    public bool HasPrimaryKey { get; }

    /// <summary>
    /// Creates a table snapshot.
    /// </summary>
    public TableSnapshot(
        string schemaName,
        string tableName,
        RelationKind relationKind,
        bool isPartitionedRoot,
        bool isPartition,
        long? estimatedRowCount,
        long tableSizeBytes,
        long indexSizeBytes,
        long totalSizeBytes,
        bool hasPrimaryKey)
    {
        SchemaName = Guard.AgainstNullOrWhiteSpace(schemaName, nameof(schemaName));
        TableName = Guard.AgainstNullOrWhiteSpace(tableName, nameof(tableName));
        Guard.AgainstUndefinedEnum(relationKind, nameof(relationKind));
        RelationKind = relationKind;

        if (isPartitionedRoot && isPartition)
        {
            throw new ArgumentException(
                "A table cannot be both a partitioned root and an individual partition.");
        }

        IsPartitionedRoot = isPartitionedRoot;
        IsPartition = isPartition;

        if (estimatedRowCount is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(estimatedRowCount), estimatedRowCount, "Estimated row count cannot be negative.");
        }

        EstimatedRowCount = estimatedRowCount;
        TableSizeBytes = Guard.AgainstNegative(tableSizeBytes, nameof(tableSizeBytes));
        IndexSizeBytes = Guard.AgainstNegative(indexSizeBytes, nameof(indexSizeBytes));
        TotalSizeBytes = Guard.AgainstNegative(totalSizeBytes, nameof(totalSizeBytes));
        HasPrimaryKey = hasPrimaryKey;
    }
}
