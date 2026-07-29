namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// Where null values sort within a single index key part.
/// </summary>
public enum IndexNullsOrdering
{
    /// <summary>
    /// Nulls sort first.
    /// </summary>
    First,

    /// <summary>
    /// Nulls sort last.
    /// </summary>
    Last,
}
