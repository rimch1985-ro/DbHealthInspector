namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// The kind of relation a <see cref="TableSnapshot"/> represents.
/// </summary>
public enum RelationKind
{
    /// <summary>
    /// A regular, non-partitioned table.
    /// </summary>
    OrdinaryTable,

    /// <summary>
    /// The root of a partitioned table.
    /// </summary>
    PartitionedTable,

    /// <summary>
    /// An individual partition of a partitioned table.
    /// </summary>
    Partition,

    /// <summary>
    /// A view.
    /// </summary>
    View,

    /// <summary>
    /// A materialized view.
    /// </summary>
    MaterializedView,

    /// <summary>
    /// A foreign table.
    /// </summary>
    ForeignTable,

    /// <summary>
    /// A temporary table.
    /// </summary>
    TemporaryTable,

    /// <summary>
    /// A relation kind the adapter could not classify into one of the other members.
    /// </summary>
    Unknown,
}
