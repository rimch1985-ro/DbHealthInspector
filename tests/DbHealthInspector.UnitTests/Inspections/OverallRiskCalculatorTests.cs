using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Inspections;

public sealed class OverallRiskCalculatorTests
{
    private static readonly FakeInspectionRule Rule = FakeInspectionRule.NoFindings("DBH900");

    private static Finding Build(FindingSeverity severity, FindingConfidence confidence = FindingConfidence.Low) =>
        InspectionFindingBuilder.For(Rule, severity: severity, confidence: confidence);

    [Fact]
    public void Calculate_ReturnsNoneForNoFindings()
    {
        Assert.Equal(OverallRisk.None, OverallRiskCalculator.Calculate([]));
    }

    [Fact]
    public void Calculate_ReturnsLowWhenOnlyInfoFindingsExist()
    {
        Finding[] findings = [Build(FindingSeverity.Info), Build(FindingSeverity.Info)];

        Assert.Equal(OverallRisk.Low, OverallRiskCalculator.Calculate(findings));
    }

    [Fact]
    public void Calculate_ReturnsMediumWhenAtLeastOneWarningExists()
    {
        Finding[] findings = [Build(FindingSeverity.Info), Build(FindingSeverity.Warning)];

        Assert.Equal(OverallRisk.Medium, OverallRiskCalculator.Calculate(findings));
    }

    [Fact]
    public void Calculate_ReturnsHighWhenAtLeastOneCriticalExists()
    {
        Finding[] findings = [Build(FindingSeverity.Info), Build(FindingSeverity.Critical)];

        Assert.Equal(OverallRisk.High, OverallRiskCalculator.Calculate(findings));
    }

    [Fact]
    public void Calculate_CriticalDominatesWarningAndInfo()
    {
        Finding[] findings =
        [
            Build(FindingSeverity.Info),
            Build(FindingSeverity.Warning),
            Build(FindingSeverity.Critical),
        ];

        Assert.Equal(OverallRisk.High, OverallRiskCalculator.Calculate(findings));
    }

    [Fact]
    public void Calculate_WarningDominatesInfo()
    {
        Finding[] findings = [Build(FindingSeverity.Warning), Build(FindingSeverity.Info)];

        Assert.Equal(OverallRisk.Medium, OverallRiskCalculator.Calculate(findings));
    }

    [Theory]
    [InlineData(FindingConfidence.Low)]
    [InlineData(FindingConfidence.Medium)]
    [InlineData(FindingConfidence.High)]
    public void Calculate_IsUnaffectedByConfidence(FindingConfidence confidence)
    {
        Finding[] findings = [Build(FindingSeverity.Critical, confidence)];

        Assert.Equal(OverallRisk.High, OverallRiskCalculator.Calculate(findings));
    }

    [Fact]
    public void Calculate_RejectsNullFindings()
    {
        Assert.Throws<ArgumentNullException>(() => OverallRiskCalculator.Calculate(null!));
    }
}
