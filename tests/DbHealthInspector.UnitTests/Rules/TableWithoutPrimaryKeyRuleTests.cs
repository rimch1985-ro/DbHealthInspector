using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.Rules.TestSupport;

namespace DbHealthInspector.UnitTests.Rules;

public sealed class TableWithoutPrimaryKeyRuleTests
{
    private static readonly TableWithoutPrimaryKeyRule Rule = new();

    [Fact]
    public void Identity_MatchesTheApprovedCatalog()
    {
        Assert.Equal("DBH001", Rule.Code.Value);
        Assert.Equal("TABLE_WITHOUT_PRIMARY_KEY", Rule.Name);
        Assert.Equal(FindingCategory.Structure, Rule.Category);
        Assert.Equal(1, Rule.Version.Value);
    }

    [Fact]
    public void OrdinaryTableWithoutPrimaryKey_IsReported()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables: [DiagnosticSnapshotBuilder.Table("orders", hasPrimaryKey: false)]);

        Finding finding = Assert.Single(Rule.Evaluate(snapshot));

        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Equal(DatabaseObjectType.Table, finding.ObjectReference.ObjectType);
        Assert.Equal("orders", finding.ObjectReference.ObjectName);
        Assert.Equal(DiagnosticSnapshotBuilder.Schema, finding.ObjectReference.SchemaName);
    }

    [Fact]
    public void OrdinaryTableWithPrimaryKey_IsNotReported()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables: [DiagnosticSnapshotBuilder.Table("orders", hasPrimaryKey: true)]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void PartitionedRootWithoutPrimaryKey_IsReported()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "events",
                    RelationKind.PartitionedTable,
                    hasPrimaryKey: false,
                    isPartitionedRoot: true),
            ]);

        Finding finding = Assert.Single(Rule.Evaluate(snapshot));
        Assert.Equal("events", finding.ObjectReference.ObjectName);
    }

    [Fact]
    public void PhysicalPartitionWithoutPrimaryKey_IsNotReportedIndependently()
    {
        // The key belongs to the root; reporting each partition would multiply one root
        // defect across every partition.
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "events_2026",
                    RelationKind.Partition,
                    hasPrimaryKey: false,
                    isPartition: true),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Theory]
    [InlineData(RelationKind.View)]
    [InlineData(RelationKind.MaterializedView)]
    [InlineData(RelationKind.ForeignTable)]
    [InlineData(RelationKind.TemporaryTable)]
    [InlineData(RelationKind.Unknown)]
    public void ExcludedRelationKinds_AreNeverReported(RelationKind relationKind)
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables: [DiagnosticSnapshotBuilder.Table("thing", relationKind, hasPrimaryKey: false)]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void Evidence_IdentifiesTheTableAndCarriesMeasurementsOutsideTheFingerprint()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "orders", hasPrimaryKey: false, estimatedRowCount: 42, totalSizeBytes: 4096),
            ]);

        Finding finding = Assert.Single(Rule.Evaluate(snapshot));

        Assert.Equal(
            FingerprintParticipation.Include,
            Find(finding, "schema").FingerprintParticipation);
        Assert.Equal("OrdinaryTable", Find(finding, "relation_kind").Value);
        Assert.Equal("42", Find(finding, "estimated_rows").Value);
        Assert.Equal(
            FingerprintParticipation.Exclude,
            Find(finding, "estimated_rows").FingerprintParticipation);
        Assert.Equal("4096", Find(finding, "total_size_bytes").Value);
        Assert.Equal(
            FingerprintParticipation.Exclude,
            Find(finding, "total_size_bytes").FingerprintParticipation);
    }

    [Fact]
    public void Evidence_OmitsTheRowEstimateWhenItIsUnknown()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table("orders", hasPrimaryKey: false, estimatedRowCount: null),
            ]);

        Finding finding = Assert.Single(Rule.Evaluate(snapshot));

        Assert.DoesNotContain(finding.Evidence, item => item.Key == "estimated_rows");
    }

    [Fact]
    public void Fingerprint_SurvivesGrowthOfTheSameTable()
    {
        Finding small = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "orders", hasPrimaryKey: false, estimatedRowCount: 10, totalSizeBytes: 1024),
            ])));

        Finding grown = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "orders", hasPrimaryKey: false, estimatedRowCount: 9_000, totalSizeBytes: 999_999),
            ])));

        Assert.Equal(small.Fingerprint.Value, grown.Fingerprint.Value);
    }

    private static EvidenceItem Find(Finding finding, string key) =>
        Assert.Single(finding.Evidence, item => item.Key == key);
}
