using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Rules;

/// <summary>
/// Exercises <see cref="IInspectionRule"/> using a private, test-only, deliberately neutral
/// implementation. This fake must never be mistaken for DBH001–DBH005: it uses an unofficial
/// code outside the approved catalog (<see cref="FindingCodes"/> stays limited to the five
/// official codes), evaluates no real diagnostic condition, and only demonstrates that the
/// contract is usable, pure and deterministic.
/// </summary>
public sealed class InspectionRuleContractTests
{
    /// <summary>
    /// A minimal, pure, semantically-neutral rule: returns one fixed finding per schema in the
    /// snapshot, regardless of table or index content. Exists only to prove the
    /// <see cref="IInspectionRule"/> contract; it does not evaluate any DBH001–DBH005 condition.
    /// </summary>
    private sealed class NeutralTestRule : IInspectionRule
    {
        public FindingCode Code { get; } = new("DBH900");

        public RuleVersion Version { get; } = RuleVersion.Initial;

        public string Name => "TEST_RULE";

        public FindingCategory Category => FindingCategory.Structure;

        public IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot)
        {
            ArgumentNullException.ThrowIfNull(snapshot);

            return [.. snapshot.Schemas.Select(schema => new Finding(
                Code,
                Version,
                Category,
                FindingSeverity.Info,
                FindingConfidence.Low,
                new DatabaseObjectReference(DatabaseObjectType.Schema, schemaName: null, objectName: schema.SchemaName),
                "Fixed test-rule message, not tied to any approved diagnostic.",
                "Fixed test-rule recommendation, not tied to any approved diagnostic.",
                [],
                "docs/design/core-domain-contracts.md",
                snapshot.Metadata.Engine))];
        }
    }

    [Fact]
    public void Evaluate_IsDeterministicForTheSameSnapshot()
    {
        IInspectionRule rule = new NeutralTestRule();
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot();

        IReadOnlyList<Finding> first = rule.Evaluate(snapshot);
        IReadOnlyList<Finding> second = rule.Evaluate(snapshot);

        Assert.Single(first);
        Assert.Single(second);
        Assert.Equal(first[0].Fingerprint, second[0].Fingerprint);
    }

    [Fact]
    public void Evaluate_ProducesOneFindingPerSchema()
    {
        IInspectionRule rule = new NeutralTestRule();
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(
            schemas: [new SchemaSnapshot("sales"), new SchemaSnapshot("operations")]);

        Assert.Equal(2, rule.Evaluate(snapshot).Count);
    }

    [Fact]
    public void Evaluate_ProducesNoFindingsWhenThereAreNoSchemas()
    {
        IInspectionRule rule = new NeutralTestRule();
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot(schemas: []);

        Assert.Empty(rule.Evaluate(snapshot));
    }

    [Fact]
    public void Rule_ExposesExplicitIdentity()
    {
        IInspectionRule rule = new NeutralTestRule();

        Assert.Equal("DBH900", rule.Code.Value);
        Assert.Equal(RuleVersion.Initial, rule.Version);
        Assert.Equal(FindingCategory.Structure, rule.Category);
        Assert.Equal("TEST_RULE", rule.Name);
    }

    [Fact]
    public void Rule_UsesACodeOutsideTheApprovedCatalog()
    {
        FindingCode[] approvedCodes =
        [
            FindingCodes.TableWithoutPrimaryKey,
            FindingCodes.LargeTable,
            FindingCodes.ExactDuplicateIndex,
            FindingCodes.UnusedIndexCandidate,
            FindingCodes.InvalidIndex,
        ];

        IInspectionRule rule = new NeutralTestRule();

        Assert.DoesNotContain(rule.Code, approvedCodes);
    }

    [Fact]
    public void Evaluate_IsCompatibleWithADatabaseSnapshot()
    {
        IInspectionRule rule = new NeutralTestRule();
        DatabaseSnapshot snapshot = SampleSnapshots.Snapshot();

        IReadOnlyList<Finding> findings = rule.Evaluate(snapshot);

        Assert.NotNull(findings);
    }

    [Fact]
    public void Evaluate_RejectsANullSnapshot()
    {
        IInspectionRule rule = new NeutralTestRule();

        Assert.Throws<ArgumentNullException>(() => rule.Evaluate(null!));
    }
}
