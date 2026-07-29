using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.UnitTests.Snapshots;

public sealed class StatisticsSnapshotTests
{
    [Fact]
    public void Constructor_AllowsAnUnknownResetTimestamp()
    {
        var statistics = new StatisticsSnapshot(null);

        Assert.Null(statistics.StatisticsResetAtUtc);
    }

    [Fact]
    public void Constructor_AllowsAUtcResetTimestamp()
    {
        var resetAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var statistics = new StatisticsSnapshot(resetAt);

        Assert.Equal(resetAt, statistics.StatisticsResetAtUtc);
    }

    [Fact]
    public void Constructor_RejectsANonUtcOffset()
    {
        var nonUtc = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.FromHours(-3));

        Assert.Throws<ArgumentException>(() => new StatisticsSnapshot(nonUtc));
    }
}
