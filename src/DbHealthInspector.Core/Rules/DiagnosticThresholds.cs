namespace DbHealthInspector.Core.Rules;

/// <summary>
/// The product thresholds DBH002 and DBH004 compare against.
/// </summary>
/// <remarks>
/// <para>
/// These are <b>product defaults, not database facts</b>. They describe no property of any
/// database engine; they encode the tool's opinion about when size becomes worth a human's
/// attention. See docs/gates/GC-DHI-05A_DEFINITION.md §6.3 and §8.3.
/// </para>
/// <para>
/// This is deliberately one validated value object rather than a configuration subsystem:
/// there is no provider, no binding, no file access, no environment access and no CLI
/// parsing. GC-DHI-05B maps the approved CLI threshold options onto it.
/// </para>
/// <para>
/// Every comparison that uses these values is <b>inclusive</b>: a measurement exactly equal
/// to its threshold qualifies.
/// </para>
/// </remarks>
public sealed record DiagnosticThresholds
{
    /// <summary>
    /// The estimated row count at or above which DBH002 reports a table as large.
    /// </summary>
    public long LargeTableRowThreshold { get; }

    /// <summary>
    /// The total size in bytes at or above which DBH002 reports a table as large.
    /// </summary>
    public long LargeTableSizeThresholdBytes { get; }

    /// <summary>
    /// The index size in bytes at or above which DBH004 considers an unscanned index worth
    /// reporting as a candidate.
    /// </summary>
    public long UnusedIndexSizeThresholdBytes { get; }

    /// <summary>
    /// Creates a threshold set.
    /// </summary>
    /// <param name="largeTableRowThreshold">Must be positive.</param>
    /// <param name="largeTableSizeThresholdBytes">Must be positive.</param>
    /// <param name="unusedIndexSizeThresholdBytes">Must be positive.</param>
    /// <remarks>
    /// Positivity is the only invariant. There is deliberately no minimum, maximum, range,
    /// relative-ordering, preset or profile policy: a caller may choose any positive value.
    /// </remarks>
    public DiagnosticThresholds(
        long largeTableRowThreshold,
        long largeTableSizeThresholdBytes,
        long unusedIndexSizeThresholdBytes)
    {
        LargeTableRowThreshold = RequirePositive(largeTableRowThreshold, nameof(largeTableRowThreshold));
        LargeTableSizeThresholdBytes =
            RequirePositive(largeTableSizeThresholdBytes, nameof(largeTableSizeThresholdBytes));
        UnusedIndexSizeThresholdBytes =
            RequirePositive(unusedIndexSizeThresholdBytes, nameof(unusedIndexSizeThresholdBytes));
    }

    /// <summary>
    /// The frozen v0.1.0 defaults: one million rows, one gibibyte of table storage, and ten
    /// mebibytes of index storage.
    /// </summary>
    public static DiagnosticThresholds Default { get; } = new(
        largeTableRowThreshold: 1_000_000,
        largeTableSizeThresholdBytes: 1_073_741_824,
        unusedIndexSizeThresholdBytes: 10_485_760);

    private static long RequirePositive(long value, string paramName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName, value, "Threshold must be a positive value; zero and negative values are rejected.");
        }

        return value;
    }
}
