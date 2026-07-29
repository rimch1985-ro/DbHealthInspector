namespace DbHealthInspector.Core.Findings;

/// <summary>
/// Classifies the technical area a finding belongs to.
/// </summary>
public enum FindingCategory
{
    /// <summary>
    /// Structural risks, such as missing keys or unsafe table shapes.
    /// </summary>
    Structure,

    /// <summary>
    /// Capacity signals, such as table or index growth.
    /// </summary>
    Capacity,

    /// <summary>
    /// Indexing risks, such as duplicated or invalid indexes.
    /// </summary>
    Indexing,

    /// <summary>
    /// Signals derived from server-reported usage statistics.
    /// </summary>
    Statistics,
}
