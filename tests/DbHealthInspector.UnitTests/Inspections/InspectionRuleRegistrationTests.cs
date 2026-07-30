using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class InspectionRuleRegistrationTests
{
    [Fact]
    public void Constructor_AllowsAValidRuleWithoutRequiredCapabilities()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");

        var registration = new InspectionRuleRegistration(rule, []);

        Assert.Same(rule, registration.Rule);
        Assert.Empty(registration.RequiredCapabilities);
    }

    [Fact]
    public void Constructor_AllowsRequiredCapabilitiesInSuppliedOrder()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");

        var registration = new InspectionRuleRegistration(
            rule, [CapabilityKind.UsageStatistics, CapabilityKind.CatalogMetadata]);

        Assert.Equal(
            [CapabilityKind.UsageStatistics, CapabilityKind.CatalogMetadata], registration.RequiredCapabilities);
    }

    [Fact]
    public void Constructor_RejectsNullRule()
    {
        Assert.Throws<ArgumentNullException>(() => new InspectionRuleRegistration(null!, []));
    }

    [Fact]
    public void Constructor_RejectsNullRequiredCapabilities()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");

        Assert.Throws<ArgumentNullException>(() => new InspectionRuleRegistration(rule, null!));
    }

    [Fact]
    public void Constructor_RejectsUndefinedCapability()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InspectionRuleRegistration(rule, [(CapabilityKind)999]));
    }

    [Fact]
    public void Constructor_RejectsDuplicateCapabilities()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");

        Assert.Throws<ArgumentException>(() => new InspectionRuleRegistration(
            rule, [CapabilityKind.CatalogMetadata, CapabilityKind.CatalogMetadata]));
    }

    [Fact]
    public void RequiredCapabilities_CopiesSourceDefensively()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var source = new List<CapabilityKind> { CapabilityKind.CatalogMetadata };

        var registration = new InspectionRuleRegistration(rule, source);
        source.Add(CapabilityKind.UsageStatistics);

        Assert.Single(registration.RequiredCapabilities);
    }

    [Fact]
    public void RequiredCapabilities_RejectsAllMutation()
    {
        FakeInspectionRule rule = FakeInspectionRule.NoFindings("DBH900");
        var registration = new InspectionRuleRegistration(rule, [CapabilityKind.CatalogMetadata]);

        var list = Assert.IsAssignableFrom<IList<CapabilityKind>>(registration.RequiredCapabilities);
        Assert.Throws<NotSupportedException>(() => list.Add(CapabilityKind.UsageStatistics));
        Assert.Throws<NotSupportedException>(() => list[0] = CapabilityKind.UsageStatistics);
        Assert.Throws<NotSupportedException>(() => list.RemoveAt(0));
    }
}
