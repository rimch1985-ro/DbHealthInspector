using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Snapshots;

public sealed class DatabaseSnapshotTests
{
    [Fact]
    public void Constructor_ExposesEveryAggregatedComponent()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot();

        Assert.NotNull(snapshot.Metadata);
        Assert.Single(snapshot.Schemas);
        Assert.Single(snapshot.Tables);
        Assert.Single(snapshot.Indexes);
        Assert.NotNull(snapshot.Capabilities);
        Assert.NotNull(snapshot.Statistics);
    }

    [Fact]
    public void Constructor_CopiesCollectionsDefensively()
    {
        var tables = new List<TableSnapshot> { SampleSnapshots.OrdinaryTable() };

        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(tables: tables);
        tables.Add(SampleSnapshots.OrdinaryTable(tableName: "second_table"));

        Assert.Single(snapshot.Tables);
    }

    [Fact]
    public void Constructor_AllowsEmptySchemaTableAndIndexCollections()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(schemas: [], tables: [], indexes: []);

        Assert.Empty(snapshot.Schemas);
        Assert.Empty(snapshot.Tables);
        Assert.Empty(snapshot.Indexes);
    }

    [Fact]
    public void Constructor_RejectsNullMetadata()
    {
        Assert.Throws<ArgumentNullException>(() => new DatabaseSnapshot(
            null!, [], [], [], SampleSnapshots.AllCapabilitiesAvailable(), SampleSnapshots.StatisticsWithReset()));
    }

    [Fact]
    public void Constructor_RejectsNullCapabilities()
    {
        Assert.Throws<ArgumentNullException>(() => new DatabaseSnapshot(
            SampleSnapshots.Metadata(), [], [], [], null!, SampleSnapshots.StatisticsWithReset()));
    }

    [Fact]
    public void Constructor_RejectsANullSchemaElement()
    {
        Assert.Throws<ArgumentException>(() => SampleSnapshots.Snapshot(schemas: [null!]));
    }

    [Fact]
    public void Constructor_RejectsADuplicateSchema()
    {
        Assert.Throws<ArgumentException>(() => SampleSnapshots.Snapshot(
            schemas: [new SchemaSnapshot("operations"), new SchemaSnapshot("operations")]));
    }

    [Fact]
    public void Constructor_RejectsANullTableElement()
    {
        Assert.Throws<ArgumentException>(() => SampleSnapshots.Snapshot(tables: [null!]));
    }

    [Fact]
    public void Constructor_RejectsADuplicateTable()
    {
        Assert.Throws<ArgumentException>(() => SampleSnapshots.Snapshot(
            tables: [SampleSnapshots.OrdinaryTable(), SampleSnapshots.OrdinaryTable()]));
    }

    [Fact]
    public void Constructor_RejectsANullIndexElement()
    {
        Assert.Throws<ArgumentException>(() => SampleSnapshots.Snapshot(indexes: [null!]));
    }

    [Fact]
    public void Constructor_RejectsADuplicateIndex()
    {
        Assert.Throws<ArgumentException>(() => SampleSnapshots.Snapshot(
            indexes: [SampleSnapshots.Index(), SampleSnapshots.Index()]));
    }
}
