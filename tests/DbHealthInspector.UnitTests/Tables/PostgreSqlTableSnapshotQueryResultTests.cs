using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.UnitTests.Tables;

/// <summary>
/// The internal result contract (GC-DHI-04D §4 and §16): defensively copied, canonically ordered,
/// duplicate-free, read-only and non-disclosing.
/// </summary>
public sealed class PostgreSqlTableSnapshotQueryResultTests
{
    private static TableSnapshot Table(string schema, string name) =>
        new(schema, name, RelationKind.OrdinaryTable, false, false, 0, 0, 0, 0, false);

    private static (string Schema, string Table)[] NamesOf(PostgreSqlTableSnapshotQueryResult result) =>
        [.. result.Tables.Select(table => (table.SchemaName, table.TableName))];

    [Fact]
    public void AnEmptyResultIsValid()
    {
        var result = new PostgreSqlTableSnapshotQueryResult([]);

        Assert.Empty(result.Tables);
    }

    [Fact]
    public void TablesAreSortedBySchemaThenName_Ordinally()
    {
        var result = new PostgreSqlTableSnapshotQueryResult(
        [
            Table("public", "zebra"),
            Table("archive", "orders"),
            Table("public", "Apple"),
            Table("archive", "Invoices"),
        ]);

        // Ordinal puts every uppercase letter before every lowercase one, within each schema.
        Assert.Equal(
            [("archive", "Invoices"), ("archive", "orders"), ("public", "Apple"), ("public", "zebra")],
            NamesOf(result));
    }

    [Fact]
    public void OrderingDoesNotDependOnCulture()
    {
        // A culture-aware sort would not agree with ordinal on these.
        var result = new PostgreSqlTableSnapshotQueryResult(
        [
            Table("a", "b"),
            Table("A", "b"),
            Table("a", "B"),
            Table("A", "B"),
        ]);

        Assert.Equal([("A", "B"), ("A", "b"), ("a", "B"), ("a", "b")], NamesOf(result));
    }

    [Fact]
    public void AlreadyOrderedInputIsPreserved()
    {
        var result = new PostgreSqlTableSnapshotQueryResult(
        [
            Table("archive", "orders"),
            Table("public", "customers"),
        ]);

        Assert.Equal([("archive", "orders"), ("public", "customers")], NamesOf(result));
    }

    [Fact]
    public void MutatingTheCallerListCannotChangeTheResult()
    {
        TableSnapshot[] source = [Table("public", "orders")];

        var result = new PostgreSqlTableSnapshotQueryResult(source);
        source[0] = Table("hijacked", "hijacked");

        Assert.Equal([("public", "orders")], NamesOf(result));
    }

    [Fact]
    public void TheExposedCollectionIsReadOnly()
    {
        var result = new PostgreSqlTableSnapshotQueryResult([Table("public", "orders")]);

        Assert.True(((IList<TableSnapshot>)result.Tables).IsReadOnly);
        Assert.Throws<NotSupportedException>(() => ((IList<TableSnapshot>)result.Tables).Clear());
    }

    [Fact]
    public void ADuplicateSchemaAndTablePair_IsRejected()
    {
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => new PostgreSqlTableSnapshotQueryResult(
        [
            Table("public", "orders"),
            Table("public", "orders"),
        ]));
    }

    [Fact]
    public void ADuplicateSeparatedByOtherRows_IsStillRejected()
    {
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => new PostgreSqlTableSnapshotQueryResult(
        [
            Table("public", "orders"),
            Table("archive", "invoices"),
            Table("public", "orders"),
        ]));
    }

    [Fact]
    public void NamesDifferingOnlyByCase_AreNotDuplicates()
    {
        var result = new PostgreSqlTableSnapshotQueryResult(
        [
            Table("public", "Orders"),
            Table("public", "orders"),
        ]);

        Assert.Equal(2, result.Tables.Count);
    }

    [Fact]
    public void TheSameTableNameInDifferentSchemas_IsNotADuplicate()
    {
        var result = new PostgreSqlTableSnapshotQueryResult(
        [
            Table("public", "orders"),
            Table("archive", "orders"),
        ]);

        Assert.Equal(2, result.Tables.Count);
    }

    [Fact]
    public void ANullCollection_IsRejected() =>
        Assert.Throws<ArgumentNullException>(() => new PostgreSqlTableSnapshotQueryResult(null!));

    [Fact]
    public void ANullElement_IsRejected() =>
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(
            () => new PostgreSqlTableSnapshotQueryResult([Table("public", "orders"), null!]));

    [Fact]
    public void ToStringExposesNoSchemaOrTableName()
    {
        const string schemaMarker = "marker-schema-04d";
        const string tableMarker = "marker-table-04d";

        var result = new PostgreSqlTableSnapshotQueryResult([Table(schemaMarker, tableMarker)]);

        string rendered = result.ToString()!;

        foreach (string marker in new[] { schemaMarker, tableMarker })
        {
            bool leaked = rendered.Contains(marker, StringComparison.Ordinal);
            Assert.False(leaked, "Sensitive data was exposed.");
        }

        Assert.Equal(typeof(PostgreSqlTableSnapshotQueryResult).ToString(), rendered);
    }

    [Fact]
    public void TheResultHoldsExactlyOneFieldAndNoInfrastructure()
    {
        System.Reflection.FieldInfo[] fields = typeof(PostgreSqlTableSnapshotQueryResult).GetFields(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        System.Reflection.FieldInfo only = Assert.Single(fields);
        Assert.Equal(typeof(System.Collections.ObjectModel.ReadOnlyCollection<TableSnapshot>), only.FieldType);

        // No static mutable state backs the type either.
        Assert.Empty(typeof(PostgreSqlTableSnapshotQueryResult).GetFields(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
    }
}
