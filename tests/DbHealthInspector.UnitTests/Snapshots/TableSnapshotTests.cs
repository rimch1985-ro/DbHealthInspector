using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Snapshots;

public sealed class TableSnapshotTests
{
    [Fact]
    public void Constructor_AllowsAnOrdinaryTable()
    {
        TableSnapshot table = SampleSnapshots.OrdinaryTable();

        Assert.Equal(RelationKind.OrdinaryTable, table.RelationKind);
        Assert.False(table.IsPartitionedRoot);
        Assert.False(table.IsPartition);
    }

    [Fact]
    public void Constructor_AllowsAPartitionedRootTable()
    {
        var table = new TableSnapshot(
            "sales", "orders", RelationKind.PartitionedTable,
            isPartitionedRoot: true, isPartition: false,
            estimatedRowCount: 1_000_000, tableSizeBytes: 0, indexSizeBytes: 0, totalSizeBytes: 0,
            hasPrimaryKey: true);

        Assert.True(table.IsPartitionedRoot);
    }

    [Fact]
    public void Constructor_AllowsAnUnknownEstimatedRowCount()
    {
        var table = new TableSnapshot(
            "sales", "orders", RelationKind.OrdinaryTable,
            isPartitionedRoot: false, isPartition: false,
            estimatedRowCount: null, tableSizeBytes: 0, indexSizeBytes: 0, totalSizeBytes: 0,
            hasPrimaryKey: true);

        Assert.Null(table.EstimatedRowCount);
    }

    [Fact]
    public void Constructor_RejectsBeingBothPartitionedRootAndPartition()
    {
        Assert.Throws<ArgumentException>(() => new TableSnapshot(
            "sales", "orders", RelationKind.Partition,
            isPartitionedRoot: true, isPartition: true,
            estimatedRowCount: 0, tableSizeBytes: 0, indexSizeBytes: 0, totalSizeBytes: 0,
            hasPrimaryKey: true));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Constructor_RejectsNegativeEstimatedRowCount(long estimatedRowCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableSnapshot(
            "sales", "orders", RelationKind.OrdinaryTable,
            isPartitionedRoot: false, isPartition: false,
            estimatedRowCount, tableSizeBytes: 0, indexSizeBytes: 0, totalSizeBytes: 0,
            hasPrimaryKey: true));
    }

    [Fact]
    public void Constructor_RejectsNegativeSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableSnapshot(
            "sales", "orders", RelationKind.OrdinaryTable,
            isPartitionedRoot: false, isPartition: false,
            estimatedRowCount: 0, tableSizeBytes: -1, indexSizeBytes: 0, totalSizeBytes: 0,
            hasPrimaryKey: true));
    }

    [Fact]
    public void Constructor_RejectsUndefinedRelationKind()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new TableSnapshot(
            "sales", "orders", (RelationKind)999,
            isPartitionedRoot: false, isPartition: false,
            estimatedRowCount: 0, tableSizeBytes: 0, indexSizeBytes: 0, totalSizeBytes: 0,
            hasPrimaryKey: true));
    }
}
