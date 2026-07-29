namespace DbHealthInspector.Core.Findings;

/// <summary>
/// The catalog of finding codes approved for v0.1.0.
/// </summary>
/// <remarks>
/// This catalog defines identity only. It does not implement the corresponding diagnostic
/// rules; rule implementations are out of scope for this gate.
/// </remarks>
public static class FindingCodes
{
    /// <summary>
    /// DBH001 — a user table has no primary key.
    /// </summary>
    public static FindingCode TableWithoutPrimaryKey { get; } = new("DBH001");

    /// <summary>
    /// DBH002 — a table exceeds a configured row or size threshold.
    /// </summary>
    public static FindingCode LargeTable { get; } = new("DBH002");

    /// <summary>
    /// DBH003 — two indexes on the same table are structurally equivalent.
    /// </summary>
    public static FindingCode ExactDuplicateIndex { get; } = new("DBH003");

    /// <summary>
    /// DBH004 — an index shows no recorded scans and exceeds a configured size threshold.
    /// </summary>
    public static FindingCode UnusedIndexCandidate { get; } = new("DBH004");

    /// <summary>
    /// DBH005 — an index is marked invalid by the database engine.
    /// </summary>
    public static FindingCode InvalidIndex { get; } = new("DBH005");
}
