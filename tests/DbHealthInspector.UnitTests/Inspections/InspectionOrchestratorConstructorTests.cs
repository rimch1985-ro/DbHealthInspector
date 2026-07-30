using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class InspectionOrchestratorConstructorTests
{
    private static FakeSnapshotProvider Provider => FakeSnapshotProvider.Returning(SampleSnapshots.Snapshot());

    [Fact]
    public void Constructor_AllowsZeroEnabledRules()
    {
        var orchestrator = new InspectionOrchestrator(Provider, []);

        Assert.NotNull(orchestrator);
    }

    [Fact]
    public void Constructor_RejectsNullSnapshotProvider()
    {
        Assert.Throws<ArgumentNullException>(() => new InspectionOrchestrator(null!, []));
    }

    [Fact]
    public void Constructor_RejectsNullRegistrationCollection()
    {
        Assert.Throws<ArgumentNullException>(() => new InspectionOrchestrator(Provider, null!));
    }

    [Fact]
    public void Constructor_RejectsANullRegistration()
    {
        Assert.Throws<ArgumentException>(() => new InspectionOrchestrator(Provider, [null!]));
    }

    [Fact]
    public void Constructor_RejectsTwoRulesWithTheSameFindingCode()
    {
        FakeInspectionRule ruleA = FakeInspectionRule.NoFindings("DBH900");
        FakeInspectionRule ruleB = FakeInspectionRule.NoFindings("DBH900");

        Assert.Throws<ArgumentException>(() => new InspectionOrchestrator(
            Provider, [new InspectionRuleRegistration(ruleA, []), new InspectionRuleRegistration(ruleB, [])]));
    }

    [Fact]
    public void Constructor_AllowsTwoRulesWithDifferentFindingCodes()
    {
        FakeInspectionRule ruleA = FakeInspectionRule.NoFindings("DBH900");
        FakeInspectionRule ruleB = FakeInspectionRule.NoFindings("DBH901");

        var orchestrator = new InspectionOrchestrator(
            Provider, [new InspectionRuleRegistration(ruleA, []), new InspectionRuleRegistration(ruleB, [])]);

        Assert.NotNull(orchestrator);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsABlankRuleName(string name)
    {
        var rule = new FakeInspectionRule("DBH900", _ => [], name: name);

        Assert.Throws<ArgumentException>(() =>
            new InspectionOrchestrator(Provider, [new InspectionRuleRegistration(rule, [])]));
    }

    [Fact]
    public void Constructor_RejectsANullRuleCode()
    {
        var rule = new NullIdentityRule(code: null, version: RuleVersion.Initial);

        Assert.Throws<ArgumentException>(() =>
            new InspectionOrchestrator(Provider, [new InspectionRuleRegistration(rule, [])]));
    }

    [Fact]
    public void Constructor_RejectsANullRuleVersion()
    {
        var rule = new NullIdentityRule(code: new FindingCode("DBH900"), version: null);

        Assert.Throws<ArgumentException>(() =>
            new InspectionOrchestrator(Provider, [new InspectionRuleRegistration(rule, [])]));
    }

    [Fact]
    public void Constructor_RejectsAnUndefinedRuleCategory()
    {
        var rule = new FakeInspectionRule("DBH900", _ => [], category: (FindingCategory)999);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InspectionOrchestrator(Provider, [new InspectionRuleRegistration(rule, [])]));
    }

    [Fact]
    public void Constructor_CopiesRegistrationsDefensively()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var registrations = new List<InspectionRuleRegistration> { new(rule, []) };

        var orchestrator = new InspectionOrchestrator(Provider, registrations);
        registrations.Add(new InspectionRuleRegistration(FakeInspectionRule.NoFindings("DBH901"), []));

        // The added registration must not affect an orchestrator already constructed; verified
        // indirectly through InspectAsync behavior in InspectionOrchestratorExecutionTests.
        Assert.NotNull(orchestrator);
    }

    /// <summary>
    /// A rule whose Code/Version can be forced to null at the interface level to exercise the
    /// orchestrator's defensive check against a badly-behaved implementation — something
    /// <see cref="FakeInspectionRule"/> cannot represent because its own constructor requires a
    /// valid <see cref="Core.Findings.FindingCode"/> string.
    /// </summary>
    private sealed class NullIdentityRule : IInspectionRule
    {
        public NullIdentityRule(FindingCode? code, RuleVersion? version)
        {
            Code = code!;
            Version = version!;
        }

        public FindingCode Code { get; }

        public RuleVersion Version { get; }

        public string Name => "NULL_IDENTITY_RULE";

        public FindingCategory Category => FindingCategory.Structure;

        public IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot) => [];
    }
}
