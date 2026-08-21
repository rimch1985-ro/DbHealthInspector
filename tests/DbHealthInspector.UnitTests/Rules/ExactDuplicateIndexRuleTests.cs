using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.Rules.TestSupport;

namespace DbHealthInspector.UnitTests.Rules;

public sealed class ExactDuplicateIndexRuleTests
{
    private static readonly ExactDuplicateIndexRule Rule = new();

    [Fact]
    public void Identity_MatchesTheApprovedCatalog()
    {
        Assert.Equal("DBH003", Rule.Code.Value);
        Assert.Equal("EXACT_DUPLICATE_INDEX", Rule.Name);
        Assert.Equal(FindingCategory.Indexing, Rule.Category);
        Assert.Equal(1, Rule.Version.Value);
    }

    [Fact]
    public void TwoStructurallyIdenticalIndexes_ProduceOneFindingNamingBoth()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes: [DiagnosticSnapshotBuilder.Index("idx_b"), DiagnosticSnapshotBuilder.Index("idx_a")]);

        Finding finding = Assert.Single(Rule.Evaluate(snapshot));

        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Equal("idx_a, idx_b", Find(finding, "duplicate_indexes").Value);
        Assert.Equal("2", Find(finding, "duplicate_count").Value);

        // Anchored on the ordinally-first member, with the table as its parent.
        Assert.Equal(DatabaseObjectType.Index, finding.ObjectReference.ObjectType);
        Assert.Equal("idx_a", finding.ObjectReference.ObjectName);
        Assert.Equal("orders", finding.ObjectReference.ParentObjectName);
    }

    [Fact]
    public void ThreeStructurallyIdenticalIndexes_ProduceOneFindingNotThreePairs()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_c"),
                DiagnosticSnapshotBuilder.Index("idx_a"),
                DiagnosticSnapshotBuilder.Index("idx_b"),
            ]);

        Finding finding = Assert.Single(Rule.Evaluate(snapshot));

        Assert.Equal("3", Find(finding, "duplicate_count").Value);
        Assert.Equal("idx_a, idx_b, idx_c", Find(finding, "duplicate_indexes").Value);
    }

    [Fact]
    public void DifferingOnlyInNameSizeScanCountAndState_StillCountsAsDuplicate()
    {
        // This is the case IndexSnapshot.Equals cannot detect, because that override also
        // compares name, size and scan count.
        IndexSnapshot first = DiagnosticSnapshotBuilder.Index(
            "idx_a", sizeBytes: 8192, scanCount: 0, isValid: true, isReady: true, isLive: true);
        IndexSnapshot second = DiagnosticSnapshotBuilder.Index(
            "idx_b", sizeBytes: 999_999, scanCount: 4_321, isValid: false, isReady: false, isLive: false);

        Assert.NotEqual(first, second);

        Finding finding = Assert.Single(
            Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(indexes: [first, second])));
        Assert.Equal("idx_a, idx_b", Find(finding, "duplicate_indexes").Value);
    }

    [Fact]
    public void PrimaryKeyOrConstraintBackingDifference_DoesNotPreventStructuralMatch()
    {
        IndexSnapshot backing = DiagnosticSnapshotBuilder.Index(
            "idx_a", isUnique: true, backsConstraint: true);
        IndexSnapshot plain = DiagnosticSnapshotBuilder.Index("idx_b", isUnique: true);

        Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(indexes: [backing, plain])));
    }

    [Fact]
    public void IndexesOnDifferentTables_AreNeverDuplicates()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", tableName: "orders"),
                DiagnosticSnapshotBuilder.Index("idx_b", tableName: "invoices"),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void DifferentAccessMethod_IsNotADuplicate()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", accessMethod: "btree"),
                DiagnosticSnapshotBuilder.Index("idx_b", accessMethod: "hash"),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void PrefixIndex_IsNotADuplicateOfTheLongerIndex()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index(
                    "idx_a", keyParts: [DiagnosticSnapshotBuilder.KeyPart(1, "a")]),
                DiagnosticSnapshotBuilder.Index(
                    "idx_b",
                    keyParts:
                    [
                        DiagnosticSnapshotBuilder.KeyPart(1, "a"),
                        DiagnosticSnapshotBuilder.KeyPart(2, "b"),
                    ]),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void ReorderedKeys_AreNotDuplicates()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index(
                    "idx_a",
                    keyParts:
                    [
                        DiagnosticSnapshotBuilder.KeyPart(1, "a"),
                        DiagnosticSnapshotBuilder.KeyPart(2, "b"),
                    ]),
                DiagnosticSnapshotBuilder.Index(
                    "idx_b",
                    keyParts:
                    [
                        DiagnosticSnapshotBuilder.KeyPart(1, "b"),
                        DiagnosticSnapshotBuilder.KeyPart(2, "a"),
                    ]),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Theory]
    [InlineData("sort")]
    [InlineData("nulls")]
    [InlineData("collation")]
    [InlineData("opclass")]
    [InlineData("expression")]
    public void DifferingKeyPartProperty_IsNotADuplicate(string differingProperty)
    {
        IndexKeyPartSnapshot baseline = DiagnosticSnapshotBuilder.KeyPart();
        IndexKeyPartSnapshot varied = differingProperty switch
        {
            "sort" => DiagnosticSnapshotBuilder.KeyPart(sortDirection: IndexSortDirection.Descending),
            "nulls" => DiagnosticSnapshotBuilder.KeyPart(nullsOrdering: IndexNullsOrdering.First),
            "collation" => DiagnosticSnapshotBuilder.KeyPart(collation: "\"pg_catalog\".\"C\""),
            "opclass" => DiagnosticSnapshotBuilder.KeyPart(operatorClass: "\"pg_catalog\".\"text_ops\""),
            _ => DiagnosticSnapshotBuilder.KeyPart(columnName: null, expression: "lower(id)"),
        };

        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", keyParts: [baseline]),
                DiagnosticSnapshotBuilder.Index("idx_b", keyParts: [varied]),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void DifferingOperatorClassOptions_AreNotDuplicates()
    {
        // The 04E encoding embeds ordered operator-class options in the identity string, so
        // two BRIN indexes differing only in stored options must not collapse into one group.
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index(
                    "idx_a",
                    keyParts:
                    [
                        DiagnosticSnapshotBuilder.KeyPart(
                            operatorClass:
                            "\"pg_catalog\".\"int4_bloom_ops\"|options[1;23:n_distinct_per_range=16]"),
                    ]),
                DiagnosticSnapshotBuilder.Index(
                    "idx_b",
                    keyParts:
                    [
                        DiagnosticSnapshotBuilder.KeyPart(
                            operatorClass:
                            "\"pg_catalog\".\"int4_bloom_ops\"|options[1;23:n_distinct_per_range=32]"),
                    ]),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void DifferingIncludedColumns_AreNotDuplicates()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", includedColumns: ["x"]),
                DiagnosticSnapshotBuilder.Index("idx_b", includedColumns: ["x", "y"]),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void ReorderedIncludedColumns_AreNotDuplicates()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", includedColumns: ["x", "y"]),
                DiagnosticSnapshotBuilder.Index("idx_b", includedColumns: ["y", "x"]),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void DifferingPredicate_IsNotADuplicate()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", partialPredicate: "(active = true)"),
                DiagnosticSnapshotBuilder.Index("idx_b", partialPredicate: "(active = false)"),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void PartialVersusTotalIndex_IsNotADuplicate()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", partialPredicate: "(active = true)"),
                DiagnosticSnapshotBuilder.Index("idx_b", partialPredicate: null),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void DifferingUniqueness_IsNotADuplicate()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", isUnique: true),
                DiagnosticSnapshotBuilder.Index("idx_b", isUnique: false),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void DifferingNullsNotDistinct_IsNotADuplicate()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", isUnique: true, nullsNotDistinct: true),
                DiagnosticSnapshotBuilder.Index("idx_b", isUnique: true, nullsNotDistinct: false),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void NullNullsNotDistinct_IsDistinguishedFromFalse()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a", nullsNotDistinct: null),
                DiagnosticSnapshotBuilder.Index("idx_b", nullsNotDistinct: false),
            ]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void TwoDisjointGroupsOnOneTable_ProduceTwoDistinctFindings()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index(
                    "idx_a1", keyParts: [DiagnosticSnapshotBuilder.KeyPart(1, "a")]),
                DiagnosticSnapshotBuilder.Index(
                    "idx_a2", keyParts: [DiagnosticSnapshotBuilder.KeyPart(1, "a")]),
                DiagnosticSnapshotBuilder.Index(
                    "idx_b1", keyParts: [DiagnosticSnapshotBuilder.KeyPart(1, "b")]),
                DiagnosticSnapshotBuilder.Index(
                    "idx_b2", keyParts: [DiagnosticSnapshotBuilder.KeyPart(1, "b")]),
            ]);

        IReadOnlyList<Finding> findings = Rule.Evaluate(snapshot);

        Assert.Equal(2, findings.Count);
        Assert.Equal(2, findings.Select(finding => finding.Fingerprint.Value).Distinct().Count());
    }

    [Fact]
    public void SingleIndex_IsNotAGroup()
    {
        DatabaseSnapshot snapshot = DiagnosticSnapshotBuilder.Snapshot(
            indexes: [DiagnosticSnapshotBuilder.Index("idx_a")]);

        Assert.Empty(Rule.Evaluate(snapshot));
    }

    [Fact]
    public void EvidenceOrdering_IsDeterministicRegardlessOfSnapshotOrder()
    {
        Finding forward = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_a"),
                DiagnosticSnapshotBuilder.Index("idx_b"),
                DiagnosticSnapshotBuilder.Index("idx_c"),
            ])));
        Finding reversed = Assert.Single(Rule.Evaluate(DiagnosticSnapshotBuilder.Snapshot(
            indexes:
            [
                DiagnosticSnapshotBuilder.Index("idx_c"),
                DiagnosticSnapshotBuilder.Index("idx_b"),
                DiagnosticSnapshotBuilder.Index("idx_a"),
            ])));

        Assert.Equal("idx_a, idx_b, idx_c", Find(forward, "duplicate_indexes").Value);
        Assert.Equal(Find(forward, "duplicate_indexes").Value, Find(reversed, "duplicate_indexes").Value);
        Assert.Equal(forward.Fingerprint.Value, reversed.Fingerprint.Value);
    }

    private static EvidenceItem Find(Finding finding, string key) =>
        Assert.Single(finding.Evidence, item => item.Key == key);
}
