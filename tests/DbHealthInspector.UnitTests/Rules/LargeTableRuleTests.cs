using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.Rules.TestSupport;

namespace DbHealthInspector.UnitTests.Rules;

public sealed class LargeTableRuleTests
{
    private const long Rows = 1_000_000;
    private const long SizeBytes = 1_073_741_824;

    private static readonly LargeTableRule Rule = new();

    [Fact]
    public void Identity_MatchesTheApprovedCatalog()
    {
        Assert.Equal("DBH002", Rule.Code.Value);
        Assert.Equal("LARGE_TABLE", Rule.Name);
        Assert.Equal(FindingCategory.Capacity, Rule.Category);
        Assert.Equal(1, Rule.Version.Value);
    }

    [Fact]
    public void BelowBothThresholds_IsNotReported()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "orders", estimatedRowCount: Rows - 1, totalSizeBytes: SizeBytes - 1),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void RowCountExactlyAtTheThreshold_IsReportedAsRows()
    {
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table("orders", estimatedRowCount: Rows, totalSizeBytes: 0),
            ])));

        Assert.Equal("rows", Find(finding, "exceeded_threshold").Value);
        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
    }

    [Fact]
    public void SizeExactlyAtTheThreshold_IsReportedAsSize()
    {
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table("orders", estimatedRowCount: 0, totalSizeBytes: SizeBytes),
            ])));

        Assert.Equal("size", Find(finding, "exceeded_threshold").Value);
    }

    [Fact]
    public void BothThresholdsExceeded_ProducesOneFindingIdentifyingBoth()
    {
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "orders", estimatedRowCount: Rows + 5, totalSizeBytes: SizeBytes + 5),
            ])));

        Assert.Equal("rows_and_size", Find(finding, "exceeded_threshold").Value);
    }

    [Fact]
    public void NullRowEstimateWithLargeSize_StillFiresOnSize()
    {
        // A null estimate is unknown, never zero, and must not block the size criterion.
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "orders", estimatedRowCount: null, totalSizeBytes: SizeBytes),
            ])));

        Assert.Equal("size", Find(finding, "exceeded_threshold").Value);
        Assert.DoesNotContain(finding.Evidence, item => item.Key == "estimated_rows");
        Assert.DoesNotContain(finding.Evidence, item => item.Key == "row_threshold");
    }

    [Fact]
    public void NullRowEstimateWithSmallSize_IsNotReported()
    {
        // The decisive guard: a missing statistic must never become a finding.
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table("orders", estimatedRowCount: null, totalSizeBytes: 1024),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void PhysicalPartition_IsEvaluated()
    {
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "events_2026",
                    RelationKind.Partition,
                    estimatedRowCount: Rows,
                    isPartition: true),
            ])));

        Assert.Equal("events_2026", finding.ObjectReference.ObjectName);
    }

    [Fact]
    public void PartitionedRoot_IsExcluded()
    {
        // The snapshot reports physical sizes without descendant aggregation, so a root's own
        // size is not the logical size of its partitions and must not be presented as such.
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "events",
                    RelationKind.PartitionedTable,
                    estimatedRowCount: Rows * 10,
                    totalSizeBytes: SizeBytes * 10,
                    isPartitionedRoot: true),
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
            tables:
            [
                DiagnosticSnapshotBuilder.Table(
                    "thing", relationKind, estimatedRowCount: Rows, totalSizeBytes: SizeBytes),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void ExplicitThresholds_AreHonored()
    {
        var rule = new LargeTableRule(new DiagnosticThresholds(10, 20, 30));

        Assert.Single(rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables: [DiagnosticSnapshotBuilder.Table("orders", estimatedRowCount: 10)])));
        Assert.Empty(rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables: [DiagnosticSnapshotBuilder.Table("orders", estimatedRowCount: 9)])));
    }

    [Fact]
    public void Fingerprint_SurvivesFurtherGrowthOfTheSameTable()
    {
        Finding first = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables: [DiagnosticSnapshotBuilder.Table("orders", estimatedRowCount: Rows)])));
        Finding later = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            tables: [DiagnosticSnapshotBuilder.Table("orders", estimatedRowCount: Rows * 3)])));

        Assert.Equal(first.Fingerprint.Value, later.Fingerprint.Value);
    }

    private static EvidenceItem Find(Finding finding, string key) =>
        Assert.Single(finding.Evidence, item => item.Key == key);
}
