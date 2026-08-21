using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.Rules.TestSupport;

namespace DbHealthInspector.UnitTests.Rules;

public sealed class UnusedIndexCandidateRuleTests
{
    private const long Threshold = 10_485_760;

    private static readonly UnusedIndexCandidateRule Rule = new();

    /// <summary>
    /// The single qualifying shape every negative test varies by exactly one condition.
    /// </summary>
    private static IndexSnapshot Candidate(
        bool isUnique = false,
        bool isPrimaryKey = false,
        bool backsConstraint = false,
        bool isValid = true,
        bool isReady = true,
        bool isLive = true,
        long sizeBytes = Threshold,
        long? scanCount = 0) =>
        DiagnosticSnapshotBuilder.Index(
            "idx_cold",
            isUnique: isUnique,
            isPrimaryKey: isPrimaryKey,
            backsConstraint: backsConstraint,
            isValid: isValid,
            isReady: isReady,
            isLive: isLive,
            sizeBytes: sizeBytes,
            scanCount: scanCount);

    [Fact]
    public void Identity_MatchesTheApprovedCatalog()
    {
        Assert.Equal("DBH004", Rule.Code.Value);
        Assert.Equal("UNUSED_INDEX_CANDIDATE", Rule.Name);
        Assert.Equal(FindingCategory.Statistics, Rule.Category);
        Assert.Equal(1, Rule.Version.Value);
    }

    [Fact]
    public void FullyQualifyingIndexAtExactlyTheThreshold_IsReported()
    {
        // Zero scans, size exactly at the threshold (inclusive), non-PK, non-unique, not
        // constraint-backed, valid, ready and live.
        Finding finding = Assert.Single(
            Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate()])));

        Assert.Equal(FindingSeverity.Info, finding.Severity);
        Assert.Equal(DatabaseObjectType.Index, finding.ObjectReference.ObjectType);
        Assert.Equal("idx_cold", finding.ObjectReference.ObjectName);
        Assert.Equal("orders", finding.ObjectReference.ParentObjectName);
    }

    [Fact]
    public void NullScanCount_IsNeverTreatedAsZero()
    {
        // The decisive false-positive guard: unknown is not zero.
        Assert.Empty(Rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(scanCount: null)])));
    }

    [Fact]
    public void NonZeroScanCount_IsNotReported()
    {
        Assert.Empty(Rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(scanCount: 1)])));
    }

    [Fact]
    public void SizeBelowTheThreshold_IsNotReported()
    {
        Assert.Empty(Rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(sizeBytes: Threshold - 1)])));
    }

    [Fact]
    public void PrimaryKeyIndex_IsNotReported()
    {
        // IndexSnapshot requires a primary-key index to be unique and constraint-backing, so
        // this case necessarily exercises all three exclusions together.
        Assert.Empty(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes: [Candidate(isPrimaryKey: true, isUnique: true, backsConstraint: true)])));
    }

    [Fact]
    public void UniqueIndex_IsNotReported()
    {
        Assert.Empty(Rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(isUnique: true)])));
    }

    [Fact]
    public void ConstraintBackingIndex_IsNotReported()
    {
        // A non-unique index can still enforce a constraint — a PostgreSQL exclusion
        // constraint is exactly that shape — and dropping it would destroy the constraint.
        Assert.Empty(Rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(backsConstraint: true)])));
    }

    [Fact]
    public void InvalidIndex_IsNotReported()
    {
        Assert.Empty(Rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(isValid: false)])));
    }

    [Fact]
    public void NotReadyIndex_IsNotReported()
    {
        Assert.Empty(Rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(isReady: false)])));
    }

    [Fact]
    public void NotLiveIndex_IsNotReported()
    {
        Assert.Empty(Rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(isLive: false)])));
    }

    [Fact]
    public void StatisticsResetTimestampPresent_YieldsMediumConfidenceAndEvidence()
    {
        DateTimeOffset reset = new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes: [Candidate()], statisticsResetAtUtc: reset)));

        Assert.Equal(FindingConfidence.Medium, finding.Confidence);
        EvidenceItem item = Assert.Single(
            finding.Evidence, evidence => evidence.Key == "statistics_reset_at_utc");
        Assert.Equal(FingerprintParticipation.Exclude, item.FingerprintParticipation);
    }

    [Fact]
    public void StatisticsResetTimestampAbsent_YieldsLowConfidenceAndNoEvidenceKey()
    {
        Finding finding = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes: [Candidate()], statisticsResetAtUtc: null)));

        Assert.Equal(FindingConfidence.Low, finding.Confidence);
        Assert.DoesNotContain(finding.Evidence, item => item.Key == "statistics_reset_at_utc");
    }

    [Fact]
    public void Recommendation_NeverInstructsAnAutomaticDrop()
    {
        Finding finding = Assert.Single(
            Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate()])));

        Assert.Contains("candidate for human review", finding.Recommendation, StringComparison.Ordinal);
        Assert.Contains("Do not drop it automatically", finding.Recommendation, StringComparison.Ordinal);
        Assert.DoesNotContain("DROP INDEX", finding.Recommendation, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Evaluation_IsDeterministicAndReadsNoClock()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate()]);

        Finding first = Assert.Single(Rule.Evaluate(snapshot));
        Finding second = Assert.Single(Rule.Evaluate(snapshot));

        Assert.Equal(first.Fingerprint.Value, second.Fingerprint.Value);
        Assert.Equal(first.Confidence, second.Confidence);
        Assert.Equal(
            first.Evidence.Select(item => (item.Key, item.Value)),
            second.Evidence.Select(item => (item.Key, item.Value)));
    }

    [Fact]
    public void ExplicitThreshold_IsHonored()
    {
        var rule = new UnusedIndexCandidateRule(new DiagnosticThresholds(10, 20, 4096));

        Assert.Single(rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(sizeBytes: 4096)])));
        Assert.Empty(rule.Evaluate(
            DiagnosticSnapshotBuilder.Snapshot(indexes: [Candidate(sizeBytes: 4095)])));
    }
}
