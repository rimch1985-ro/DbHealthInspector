using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Snapshots;

public sealed class IndexKeyPartSnapshotTests
{
    [Fact]
    public void Constructor_AllowsAColumnKeyPart()
    {
        IndexKeyPartSnapshot keyPart = SampleSnapshots.KeyPartOnColumn();

        Assert.Equal("customer_id", keyPart.ColumnName);
        Assert.Null(keyPart.Expression);
    }

    [Fact]
    public void Constructor_AllowsAnExpressionKeyPart()
    {
        IndexKeyPartSnapshot keyPart = SampleSnapshots.KeyPartOnExpression();

        Assert.Null(keyPart.ColumnName);
        Assert.Equal("lower(email)", keyPart.Expression);
    }

    [Fact]
    public void Constructor_RejectsNeitherColumnNorExpression()
    {
        Assert.Throws<ArgumentException>(() => new IndexKeyPartSnapshot(
            1, columnName: null, expression: null, collation: null, operatorClass: null,
            IndexSortDirection.Ascending, IndexNullsOrdering.Last));
    }

    [Fact]
    public void Constructor_RejectsBothColumnAndExpression()
    {
        Assert.Throws<ArgumentException>(() => new IndexKeyPartSnapshot(
            1, columnName: "customer_id", expression: "lower(email)", collation: null, operatorClass: null,
            IndexSortDirection.Ascending, IndexNullsOrdering.Last));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsInvalidPosition(int position)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexKeyPartSnapshot(
            position, columnName: "customer_id", expression: null, collation: null, operatorClass: null,
            IndexSortDirection.Ascending, IndexNullsOrdering.Last));
    }

    [Fact]
    public void Constructor_AllowsAnExplicitCollationAndOperatorClass()
    {
        var keyPart = new IndexKeyPartSnapshot(
            1, "email", expression: null, collation: "en_US.utf8", operatorClass: "text_pattern_ops",
            IndexSortDirection.Descending, IndexNullsOrdering.First);

        Assert.Equal("en_US.utf8", keyPart.Collation);
        Assert.Equal("text_pattern_ops", keyPart.OperatorClass);
        Assert.Equal(IndexSortDirection.Descending, keyPart.SortDirection);
        Assert.Equal(IndexNullsOrdering.First, keyPart.NullsOrdering);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankColumnName(string columnName)
    {
        Assert.Throws<ArgumentException>(() => new IndexKeyPartSnapshot(
            1, columnName, expression: null, collation: null, operatorClass: null,
            IndexSortDirection.Ascending, IndexNullsOrdering.Last));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankExpression(string expression)
    {
        Assert.Throws<ArgumentException>(() => new IndexKeyPartSnapshot(
            1, columnName: null, expression, collation: null, operatorClass: null,
            IndexSortDirection.Ascending, IndexNullsOrdering.Last));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankCollation(string collation)
    {
        Assert.Throws<ArgumentException>(() => new IndexKeyPartSnapshot(
            1, columnName: "customer_id", expression: null, collation, operatorClass: null,
            IndexSortDirection.Ascending, IndexNullsOrdering.Last));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankOperatorClass(string operatorClass)
    {
        Assert.Throws<ArgumentException>(() => new IndexKeyPartSnapshot(
            1, columnName: "customer_id", expression: null, collation: null, operatorClass,
            IndexSortDirection.Ascending, IndexNullsOrdering.Last));
    }

    [Fact]
    public void Constructor_RejectsUndefinedSortDirection()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexKeyPartSnapshot(
            1, columnName: "customer_id", expression: null, collation: null, operatorClass: null,
            (IndexSortDirection)999, IndexNullsOrdering.Last));
    }

    [Fact]
    public void Constructor_RejectsUndefinedNullsOrdering()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IndexKeyPartSnapshot(
            1, columnName: "customer_id", expression: null, collation: null, operatorClass: null,
            IndexSortDirection.Ascending, (IndexNullsOrdering)999));
    }
}
