namespace DbHealthInspector.Core.Findings;

/// <summary>
/// Identifies the kind of database object a <see cref="DatabaseObjectReference"/> points to.
/// </summary>
public enum DatabaseObjectType
{
    /// <summary>
    /// The database itself.
    /// </summary>
    Database,

    /// <summary>
    /// A schema.
    /// </summary>
    Schema,

    /// <summary>
    /// A table, including partitioned tables and individual partitions.
    /// </summary>
    Table,

    /// <summary>
    /// A column.
    /// </summary>
    Column,

    /// <summary>
    /// An index.
    /// </summary>
    Index,

    /// <summary>
    /// A constraint.
    /// </summary>
    Constraint,
}
