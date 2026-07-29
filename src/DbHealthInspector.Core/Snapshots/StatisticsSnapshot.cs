namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// Server-wide statistics context for an inspection.
/// </summary>
public sealed record StatisticsSnapshot
{
    /// <summary>
    /// The UTC timestamp statistics counters were last reset, when reported by the server.
    /// </summary>
    /// <remarks>
    /// Core never computes this value; the adapter supplies it from server-reported data in a
    /// future gate.
    /// </remarks>
    public DateTimeOffset? StatisticsResetAtUtc { get; }

    /// <summary>
    /// Creates a statistics snapshot.
    /// </summary>
    /// <param name="statisticsResetAtUtc">
    /// The UTC statistics reset timestamp, when available. When provided, its offset must be
    /// <see cref="TimeSpan.Zero"/>.
    /// </param>
    public StatisticsSnapshot(DateTimeOffset? statisticsResetAtUtc)
    {
        if (statisticsResetAtUtc is { Offset: var offset } && offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Statistics reset timestamp must be expressed in UTC (zero offset).",
                nameof(statisticsResetAtUtc));
        }

        StatisticsResetAtUtc = statisticsResetAtUtc;
    }
}
