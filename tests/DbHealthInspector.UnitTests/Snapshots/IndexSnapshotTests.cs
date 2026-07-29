using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Snapshots;

public sealed class IndexSnapshotTests
{
    [Fact]
    public void Constructor_AllowsAValidNonUniqueIndex()
    {
        IndexSnapshot index = SampleSnapshots.Index();

        Assert.Equal("sales", index.SchemaName);
        Assert.Equal("orders", index.TableName);
        Assert.Equal("ix_orders_customer_id", index.IndexName);
        Assert.False(index.IsUnique);
        Assert.False(index.IsPrimaryKey);
        Assert.False(index.BacksConstraint);
    }

    [Fact]
    public void Constructor_CopiesKeyPartsAndIncludedColumnsDefensively()
    {
        var keyParts = new List<IndexKeyPartSnapshot> { SampleSnapshots.KeyPartOnColumn() };
        var included = new List<string> { "created_at" };

        var index = new IndexSnapshot(
            "sales", "orders", "ix_orders_customer_id", "btree", keyParts, included,
            partialPredicate: null, isUnique: false, nullsNotDistinct: null, isPrimaryKey: false,
            backsConstraint: false, isValid: true, isReady: true, isLive: true, sizeBytes: 100, scanCount: 0);

        keyParts.Add(SampleSnapshots.KeyPartOnColumn(position: 2, columnName: "order_date"));
        included.Add("region");

        Assert.Single(index.KeyParts);
        Assert.Single(index.IncludedColumns);
    }

    [Fact]
    public void Constructor_RequiresAtLeastOneKeyPart()
    {
        Assert.Throws<ArgumentException>(() => SampleSnapshots.Index(keyParts: []));
    }

    [Fact]
    public void Constructor_RejectsANullKeyPartElement()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleSnapshots.Index(keyParts: [SampleSnapshots.KeyPartOnColumn(), null!]));
    }

    [Fact]
    public void Constructor_RejectsDuplicateKeyPartPositions()
    {
        IndexKeyPartSnapshot[] duplicated =
        [
            SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "a"),
            SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "b"),
        ];

        Assert.Throws<ArgumentException>(() => SampleSnapshots.Index(keyParts: duplicated));
    }

    [Fact]
    public void Constructor_AllowsColumnsWithExpressionsWithinTheSameIndex()
    {
        IndexKeyPartSnapshot[] mixed =
        [
            SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "customer_id"),
            SampleSnapshots.KeyPartOnExpression(position: 2, expression: "lower(email)"),
        ];

        IndexSnapshot index = SampleSnapshots.Index(keyParts: mixed);

        Assert.Equal(2, index.KeyParts.Count);
    }

    [Fact]
    public void Constructor_AllowsIncludedColumns()
    {
        IndexSnapshot index = SampleSnapshots.Index(includedColumns: ["created_at", "region"]);

        Assert.Equal(["created_at", "region"], index.IncludedColumns);
    }

    [Fact]
    public void Constructor_RejectsANullIncludedColumnElement()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleSnapshots.Index(includedColumns: ["created_at", null!]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsABlankIncludedColumnElement(string blank)
    {
        Assert.Throws<ArgumentException>(() =>
            SampleSnapshots.Index(includedColumns: ["created_at", blank]));
    }

    [Fact]
    public void Constructor_RejectsADuplicateIncludedColumn()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleSnapshots.Index(includedColumns: ["created_at", "created_at"]));
    }

    [Fact]
    public void Constructor_AllowsAPartialIndexPredicate()
    {
        var index = new IndexSnapshot(
            "sales", "orders", "ix_orders_open", "btree", [SampleSnapshots.KeyPartOnColumn()], [],
            partialPredicate: "status = 'open'", isUnique: false, nullsNotDistinct: null, isPrimaryKey: false,
            backsConstraint: false, isValid: true, isReady: true, isLive: true, sizeBytes: 100, scanCount: 0);

        Assert.Equal("status = 'open'", index.PartialPredicate);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsABlankPartialPredicate(string partialPredicate)
    {
        Assert.Throws<ArgumentException>(() => new IndexSnapshot(
            "sales", "orders", "ix_orders_open", "btree", [SampleSnapshots.KeyPartOnColumn()], [],
            partialPredicate, isUnique: false, nullsNotDistinct: null, isPrimaryKey: false,
            backsConstraint: false, isValid: true, isReady: true, isLive: true, sizeBytes: 100, scanCount: 0));
    }

    [Fact]
    public void Constructor_AllowsAnInvalidIndex()
    {
        IndexSnapshot index = SampleSnapshots.Index(isValid: false, isReady: false, isLive: true);

        Assert.False(index.IsValid);
        Assert.False(index.IsReady);
        Assert.True(index.IsLive);
    }

    [Fact]
    public void Constructor_AllowsAnUnknownScanCount()
    {
        IndexSnapshot index = SampleSnapshots.Index(scanCount: null);

        Assert.Null(index.ScanCount);
    }

    [Fact]
    public void Constructor_RejectsANegativeScanCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SampleSnapshots.Index(scanCount: -1));
    }

    [Fact]
    public void Constructor_RejectsANegativeSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SampleSnapshots.Index(sizeBytes: -1));
    }

    [Fact]
    public void Constructor_AllowsAValidPrimaryKey()
    {
        IndexSnapshot index = SampleSnapshots.Index(isPrimaryKey: true, isUnique: true, backsConstraint: true);

        Assert.True(index.IsPrimaryKey);
        Assert.True(index.IsUnique);
        Assert.True(index.BacksConstraint);
    }

    [Fact]
    public void Constructor_RejectsAPrimaryKeyThatIsNotUnique()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleSnapshots.Index(isPrimaryKey: true, isUnique: false, backsConstraint: true));
    }

    [Fact]
    public void Constructor_RejectsAPrimaryKeyThatDoesNotBackAConstraint()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleSnapshots.Index(isPrimaryKey: true, isUnique: true, backsConstraint: false));
    }

    [Fact]
    public void Constructor_AllowsAUniqueConstraintThatIsNotAPrimaryKey()
    {
        IndexSnapshot index = SampleSnapshots.Index(isPrimaryKey: false, isUnique: true, backsConstraint: true);

        Assert.False(index.IsPrimaryKey);
        Assert.True(index.IsUnique);
        Assert.True(index.BacksConstraint);
    }

    [Fact]
    public void Constructor_AllowsAnOrdinaryIndexThatBacksNoConstraint()
    {
        IndexSnapshot index = SampleSnapshots.Index(isPrimaryKey: false, isUnique: false, backsConstraint: false);

        Assert.False(index.IsPrimaryKey);
        Assert.False(index.IsUnique);
        Assert.False(index.BacksConstraint);
    }

    [Fact]
    public void Constructor_RejectsNullSchemaName()
    {
        Assert.Throws<ArgumentNullException>(() => new IndexSnapshot(
            null!, "orders", "ix_orders_customer_id", "btree", [SampleSnapshots.KeyPartOnColumn()], [],
            partialPredicate: null, isUnique: false, nullsNotDistinct: null, isPrimaryKey: false,
            backsConstraint: false, isValid: true, isReady: true, isLive: true, sizeBytes: 0, scanCount: null));
    }

    [Fact]
    public void Constructor_RejectsNullTableName()
    {
        Assert.Throws<ArgumentNullException>(() => new IndexSnapshot(
            "sales", null!, "ix_orders_customer_id", "btree", [SampleSnapshots.KeyPartOnColumn()], [],
            partialPredicate: null, isUnique: false, nullsNotDistinct: null, isPrimaryKey: false,
            backsConstraint: false, isValid: true, isReady: true, isLive: true, sizeBytes: 0, scanCount: null));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankTableName(string tableName)
    {
        Assert.Throws<ArgumentException>(() => new IndexSnapshot(
            "sales", tableName, "ix_orders_customer_id", "btree", [SampleSnapshots.KeyPartOnColumn()], [],
            partialPredicate: null, isUnique: false, nullsNotDistinct: null, isPrimaryKey: false,
            backsConstraint: false, isValid: true, isReady: true, isLive: true, sizeBytes: 0, scanCount: null));
    }
}
