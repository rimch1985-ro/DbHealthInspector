using System.Collections.ObjectModel;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Indexes;

namespace DbHealthInspector.UnitTests.Indexes;

/// <summary>
/// The index-snapshot result contract (GC-DHI-04E §21): a defensive copy, deterministic ordinal
/// ordering, duplicate rejection, and a <see cref="object.ToString"/> that renders no customer
/// structure.
/// </summary>
public sealed class PostgreSqlIndexSnapshotQueryResultTests
{
    private const string LeakMessage = "Sensitive data was exposed.";

    private static IndexSnapshot Index(
        string schemaName = "public",
        string tableName = "orders",
        string indexName = "orders_a_idx") =>
        new(
            schemaName,
            tableName,
            indexName,
            "btree",
            [new IndexKeyPartSnapshot(1, "a", null, null, "\"pg_catalog\".\"text_ops\"", IndexSortDirection.Ascending, IndexNullsOrdering.Last)],
            [],
            null,
            isUnique: false,
            nullsNotDistinct: null,
            isPrimaryKey: false,
            backsConstraint: false,
            isValid: true,
            isReady: true,
            isLive: true,
            sizeBytes: 8192,
            scanCount: null);

    [Fact]
    public void AnEmptyResult_IsValid() =>
        Assert.Empty(new PostgreSqlIndexSnapshotQueryResult([]).Indexes);

    [Fact]
    public void ANullList_IsRejected() =>
        Assert.Throws<ArgumentNullException>(() => new PostgreSqlIndexSnapshotQueryResult(null!));

    [Fact]
    public void ANullElement_IsRejected() =>
        Assert.Throws<PostgreSqlIndexSnapshotMappingException>(
            () => new PostgreSqlIndexSnapshotQueryResult([null!]));

    [Fact]
    public void TheResultIsOrderedBySchemaThenTableThenIndex_Ordinally()
    {
        var result = new PostgreSqlIndexSnapshotQueryResult(
        [
            Index("public", "zebra", "i2"),
            Index("archive", "orders", "i1"),
            Index("public", "apple", "i9"),
            Index("public", "apple", "i1"),
        ]);

        Assert.Equal(
            [("archive", "orders", "i1"), ("public", "apple", "i1"), ("public", "apple", "i9"), ("public", "zebra", "i2")],
            result.Indexes.Select(index => (index.SchemaName, index.TableName, index.IndexName)).ToArray());
    }

    [Fact]
    public void OrderingIsOrdinal_NotCultureSensitive()
    {
        // Ordinal puts every uppercase letter before every lowercase one; a culture-aware compare
        // would interleave them.
        var result = new PostgreSqlIndexSnapshotQueryResult(
        [
            Index("public", "orders", "a_idx"),
            Index("public", "orders", "B_idx"),
        ]);

        Assert.Equal(["B_idx", "a_idx"], result.Indexes.Select(index => index.IndexName).ToArray());
    }

    [Fact]
    public void IdentitiesAreCaseSensitive()
    {
        // Two indexes differing only in case are different indexes, not a duplicate.
        var result = new PostgreSqlIndexSnapshotQueryResult(
        [
            Index(indexName: "orders_idx"),
            Index(indexName: "ORDERS_IDX"),
        ]);

        Assert.Equal(2, result.Indexes.Count);
    }

    [Fact]
    public void ADuplicateSchemaAndIndexName_IsRejected() =>
        Assert.Throws<PostgreSqlIndexSnapshotMappingException>(
            () => new PostgreSqlIndexSnapshotQueryResult([Index(), Index()]));

    [Fact]
    public void ADuplicateIndexNameIsRejectedEvenUnderADifferentTable()
    {
        // An index name is unique per schema in PostgreSQL, so the table name must not be able to
        // disguise a duplicate.
        Assert.Throws<PostgreSqlIndexSnapshotMappingException>(
            () => new PostgreSqlIndexSnapshotQueryResult(
            [
                Index(tableName: "orders", indexName: "shared_idx"),
                Index(tableName: "invoices", indexName: "shared_idx"),
            ]));
    }

    [Fact]
    public void TheSameIndexNameInAnotherSchema_IsAccepted()
    {
        var result = new PostgreSqlIndexSnapshotQueryResult(
        [
            Index(schemaName: "one", indexName: "same_idx"),
            Index(schemaName: "two", indexName: "same_idx"),
        ]);

        Assert.Equal(2, result.Indexes.Count);
    }

    [Fact]
    public void TheResultCopiesItsInput()
    {
        var source = new List<IndexSnapshot> { Index(indexName: "first") };
        var result = new PostgreSqlIndexSnapshotQueryResult(source);

        source.Add(Index(indexName: "second"));
        source.Clear();

        Assert.Single(result.Indexes);
        Assert.Equal("first", result.Indexes[0].IndexName);
    }

    [Fact]
    public void TheExposedCollectionIsReadOnly()
    {
        var result = new PostgreSqlIndexSnapshotQueryResult([Index()]);

        Assert.IsAssignableFrom<ReadOnlyCollection<IndexSnapshot>>(result.Indexes);
        Assert.True(((IList<IndexSnapshot>)result.Indexes).IsReadOnly);
    }

    [Fact]
    public void KeyPartsAndIncludedColumnsAreNotMutableThroughTheResult()
    {
        IndexSnapshot snapshot = Index();
        var result = new PostgreSqlIndexSnapshotQueryResult([snapshot]);

        Assert.True(((ICollection<IndexKeyPartSnapshot>)result.Indexes[0].KeyParts).IsReadOnly);
        Assert.True(((ICollection<string>)result.Indexes[0].IncludedColumns).IsReadOnly);
    }

    [Fact]
    public void ToStringRendersNoCustomerStructure()
    {
        const string marker = "sensitive-marker-04e-result";

        var result = new PostgreSqlIndexSnapshotQueryResult(
            [Index(schemaName: marker + "-s", tableName: marker + "-t", indexName: marker + "-i")]);

        Assert.False(result.ToString()!.Contains(marker, StringComparison.Ordinal), LeakMessage);
        Assert.Equal(typeof(PostgreSqlIndexSnapshotQueryResult).ToString(), result.ToString());
    }

    [Fact]
    public void TheResultIsNotARecord()
    {
        // A generated record ToString would print every schema, table, index and expression.
        Assert.Null(typeof(PostgreSqlIndexSnapshotQueryResult).GetMethod(
            "<Clone>$",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance));
    }
}
