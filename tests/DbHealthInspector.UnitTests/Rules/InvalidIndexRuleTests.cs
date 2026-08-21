using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.UnitTests.Rules.TestSupport;

namespace DbHealthInspector.UnitTests.Rules;

public sealed class InvalidIndexRuleTests
{
    private static readonly InvalidIndexRule Rule = new();

    [Fact]
    public void Identity_MatchesTheApprovedCatalog()
    {
        Assert.Equal("DBH005", Rule.Code.Value);
        Assert.Equal("INVALID_INDEX", Rule.Name);
        Assert.Equal(FindingCategory.Indexing, Rule.Category);
        Assert.Equal(1, Rule.Version.Value);
    }

    [Fact]
    public void InvalidIndex_IsReportedAsCritical()
    {
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes: [DiagnosticSnapshotBuilder.Index("idx_broken", isValid: false)])));

        Assert.Equal(FindingSeverity.Critical, finding.Severity);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Equal("idx_broken", finding.ObjectReference.ObjectName);
        Assert.Equal("orders", finding.ObjectReference.ParentObjectName);
    }

    [Fact]
    public void ValidIndex_IsNotReported()
    {
        Assert.Empty(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes: [DiagnosticSnapshotBuilder.Index("idx_fine", isValid: true)])));
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void ReadinessAndLivenessNeverSuppressAnInvalidIndex(bool isReady, bool isLive)
    {
        // An index can be invalid while still ready and live — the verified triple for
        // CREATE INDEX ... ON ONLY a partitioned table — so neither flag may filter.
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index(
                    "idx_broken", isValid: false, isReady: isReady, isLive: isLive),
            ])));

        Assert.Equal(isReady ? "true" : "false", Find(finding, "is_ready").Value);
        Assert.Equal(isLive ? "true" : "false", Find(finding, "is_live").Value);
    }

    [Fact]
    public void Evidence_RecordsTheCompleteStateTripleInsideTheFingerprint()
    {
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index(
                    "idx_broken", isValid: false, isReady: true, isLive: true, sizeBytes: 2048),
            ])));

        Assert.Equal("false", Find(finding, "is_valid").Value);
        foreach (string key in new[] { "is_valid", "is_ready", "is_live" })
        {
            Assert.Equal(FingerprintParticipation.Include, Find(finding, key).FingerprintParticipation);
        }

        Assert.Equal("2048", Find(finding, "index_size_bytes").Value);
        Assert.Equal(
            FingerprintParticipation.Exclude,
            Find(finding, "index_size_bytes").FingerprintParticipation);
    }

    [Fact]
    public void Recommendation_IsNonDestructive()
    {
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes: [DiagnosticSnapshotBuilder.Index("idx_broken", isValid: false)])));

        Assert.Contains("supervised rebuild", finding.Recommendation, StringComparison.Ordinal);
        Assert.Contains("performs no DDL", finding.Recommendation, StringComparison.Ordinal);
    }

    private static EvidenceItem Find(Finding finding, string key) =>
        Assert.Single(finding.Evidence, item => item.Key == key);
}
