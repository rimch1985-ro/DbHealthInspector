using DbHealthInspector.Core;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Fingerprinting;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests;

/// <summary>
/// Covers DHI-R2-001: every public collection exposed by the Core domain model must reject
/// mutation not just through <c>Add</c> (which a plain array-backed <see cref="IReadOnlyList{T}"/>
/// already rejects) but also through index assignment and <c>RemoveAt</c>/<c>Remove</c>, which a
/// plain array does not reject. See <c>Guard.CopyDefensively*</c> and
/// docs/design/core-domain-contracts.md.
/// </summary>
public sealed class ReadOnlyCollectionMutationTests
{
    private static void AssertRejectsAllMutation<T>(IEnumerable<T> collection, T replacement)
    {
        var list = Assert.IsAssignableFrom<IList<T>>(collection);

        Assert.Throws<NotSupportedException>(() => list.Add(replacement));
        Assert.Throws<NotSupportedException>(() => list[0] = replacement);
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => list.Remove(list[0]));
        Assert.Throws<NotSupportedException>(() => list.Insert(0, replacement));
        Assert.Throws<NotSupportedException>(() => list.Clear());

        var collectionInterface = Assert.IsAssignableFrom<ICollection<T>>(collection);
        Assert.True(collectionInterface.IsReadOnly);
    }

    [Fact]
    public void FindingEvidence_RejectsAllMutation()
    {
        Finding finding = SampleData.SampleFinding(evidence: [SampleData.ExcludedEvidence()]);

        AssertRejectsAllMutation(finding.Evidence, SampleData.IncludedEvidence());
    }

    [Fact]
    public void FindingEvidence_SourceMutationAfterConstructionDoesNotAffectFinding()
    {
        var source = new List<EvidenceItem> { SampleData.ExcludedEvidence() };
        Finding finding = SampleData.SampleFinding(evidence: source);

        source.Add(SampleData.IncludedEvidence());

        Assert.Single(finding.Evidence);
    }

    [Fact]
    public void FindingFingerprint_IsUnaffectedByFailedMutationAttempts()
    {
        Finding finding = SampleData.SampleFinding(
            evidence: [SampleData.IncludedEvidence(), SampleData.ExcludedEvidence()]);
        FindingFingerprint before = finding.Fingerprint;

        var list = Assert.IsAssignableFrom<IList<EvidenceItem>>(finding.Evidence);
        Assert.Throws<NotSupportedException>(() => list[0] = SampleData.IncludedEvidence(value: "tampered"));
        Assert.Throws<NotSupportedException>(() => list.Add(SampleData.IncludedEvidence(value: "tampered")));
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));

        Assert.Equal(before, finding.Fingerprint);
    }

    [Fact]
    public void FindingFingerprintInputEvidence_RejectsAllMutation()
    {
        var input = new FindingFingerprintInput(
            DatabaseEngine.PostgreSql,
            FindingCodes.TableWithoutPrimaryKey,
            SampleData.TableReference(),
            [SampleData.ExcludedEvidence()]);

        AssertRejectsAllMutation(input.Evidence, SampleData.IncludedEvidence());
    }

    [Fact]
    public void DatabaseSnapshotSchemas_RejectsAllMutation()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(
            schemas: [new SchemaSnapshot("operations"), new SchemaSnapshot("sales")]);

        AssertRejectsAllMutation(snapshot.Schemas, new SchemaSnapshot("reporting"));
    }

    [Fact]
    public void DatabaseSnapshotSchemas_EmptyCollectionStillRejectsMutation()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(schemas: []);

        var list = Assert.IsAssignableFrom<IList<SchemaSnapshot>>(snapshot.Schemas);
        Assert.Empty(list);
        Assert.Throws<NotSupportedException>(() => list.Add(new SchemaSnapshot("reporting")));
    }

    [Fact]
    public void DatabaseSnapshotTables_RejectsAllMutation()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(tables: [SampleSnapshots.OrdinaryTable()]);

        AssertRejectsAllMutation(snapshot.Tables, SampleSnapshots.OrdinaryTable(tableName: "other"));
    }

    [Fact]
    public void DatabaseSnapshotTables_SourceMutationAfterConstructionDoesNotAffectSnapshot()
    {
        var source = new List<TableSnapshot> { SampleSnapshots.OrdinaryTable() };
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(tables: source);

        source.Add(SampleSnapshots.OrdinaryTable(tableName: "second_table"));

        Assert.Single(snapshot.Tables);
    }

    [Fact]
    public void DatabaseSnapshotIndexes_RejectsAllMutation()
    {
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(indexes: [SampleSnapshots.Index()]);

        AssertRejectsAllMutation(snapshot.Indexes, SampleSnapshots.Index(indexName: "ix_other"));
    }

    [Fact]
    public void CapabilitySnapshotStates_RejectsAllMutation()
    {
        CapabilitySnapshot snapshot = SampleSnapshots.AllCapabilitiesAvailable();

        AssertRejectsAllMutation(
            snapshot.States, new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available));
    }

    [Fact]
    public void IndexSnapshotKeyParts_RejectsAllMutation()
    {
        IndexSnapshot index = SampleSnapshots.Index(keyParts:
        [
            SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "customer_id"),
            SampleSnapshots.KeyPartOnColumn(position: 2, columnName: "order_date"),
        ]);

        AssertRejectsAllMutation(index.KeyParts, SampleSnapshots.KeyPartOnColumn(position: 3, columnName: "region"));
    }

    [Fact]
    public void IndexSnapshotKeyParts_OrderIsPreserved()
    {
        IndexSnapshot index = SampleSnapshots.Index(keyParts:
        [
            SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "customer_id"),
            SampleSnapshots.KeyPartOnColumn(position: 2, columnName: "order_date"),
        ]);

        Assert.Equal("customer_id", index.KeyParts[0].ColumnName);
        Assert.Equal("order_date", index.KeyParts[1].ColumnName);
    }

    [Fact]
    public void IndexSnapshotHashCode_IsUnaffectedByFailedMutationAttempts()
    {
        IndexSnapshot index = SampleSnapshots.Index(keyParts:
        [
            SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "customer_id"),
            SampleSnapshots.KeyPartOnColumn(position: 2, columnName: "order_date"),
        ]);
        int hashBefore = index.GetHashCode();

        var keyPartsList = Assert.IsAssignableFrom<IList<IndexKeyPartSnapshot>>(index.KeyParts);
        Assert.Throws<NotSupportedException>(() =>
            keyPartsList[0] = SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "tampered"));
        Assert.Throws<NotSupportedException>(() => keyPartsList.RemoveAt(0));

        var includedColumnsList = Assert.IsAssignableFrom<IList<string>>(index.IncludedColumns);
        Assert.Throws<NotSupportedException>(() => includedColumnsList.Add("tampered"));

        Assert.Equal(hashBefore, index.GetHashCode());
    }

    [Fact]
    public void IndexSnapshotIncludedColumns_RejectsAllMutation()
    {
        IndexSnapshot index = SampleSnapshots.Index(includedColumns: ["region", "created_at"]);

        AssertRejectsAllMutation(index.IncludedColumns, "tampered");
    }

    [Fact]
    public void IndexSnapshotIncludedColumns_OrderIsPreserved()
    {
        IndexSnapshot index = SampleSnapshots.Index(includedColumns: ["region", "created_at", "notes"]);

        Assert.Equal(["region", "created_at", "notes"], index.IncludedColumns);
    }

    [Fact]
    public void IndexSnapshotIncludedColumns_EmptyCollectionStillRejectsMutation()
    {
        IndexSnapshot index = SampleSnapshots.Index(includedColumns: []);

        var list = Assert.IsAssignableFrom<IList<string>>(index.IncludedColumns);
        Assert.Empty(list);
        Assert.Throws<NotSupportedException>(() => list.Add("tampered"));
    }
}
