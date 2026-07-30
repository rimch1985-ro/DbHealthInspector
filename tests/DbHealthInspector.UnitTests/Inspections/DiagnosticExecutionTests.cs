using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.UnitTests.Inspections;

/// <summary>
/// Exercises <see cref="DiagnosticExecution"/> through its <see langword="internal"/> factory
/// methods, which are the only way to construct one — so every reachable instance already
/// satisfies the Status/FindingCount/UnavailableCapabilities/Failure invariants by construction.
/// These tests verify each factory enforces its own preconditions and produces the documented
/// combination.
/// </summary>
public sealed class DiagnosticExecutionTests
{
    private static readonly FindingCode Code = new("DBH900");
    private static readonly RuleVersion Version = RuleVersion.Initial;
    private const string RuleName = "TEST_RULE";
    private const FindingCategory Category = FindingCategory.Structure;

    [Fact]
    public void Completed_ProducesTheDocumentedCombination()
    {
        DiagnosticExecution execution = DiagnosticExecution.Completed(Code, Version, RuleName, Category, 3);

        Assert.Equal(DiagnosticExecutionStatus.Completed, execution.Status);
        Assert.Equal(3, execution.FindingCount);
        Assert.Empty(execution.UnavailableCapabilities);
        Assert.Null(execution.Failure);
        Assert.Equal(Code, execution.Code);
        Assert.Equal(Version, execution.RuleVersion);
        Assert.Equal(RuleName, execution.RuleName);
        Assert.Equal(Category, execution.Category);
    }

    [Fact]
    public void Completed_AllowsZeroFindings()
    {
        DiagnosticExecution execution = DiagnosticExecution.Completed(Code, Version, RuleName, Category, 0);

        Assert.Equal(0, execution.FindingCount);
    }

    [Fact]
    public void Completed_RejectsNegativeFindingCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiagnosticExecution.Completed(Code, Version, RuleName, Category, -1));
    }

    [Fact]
    public void SkippedUnavailableCapability_ProducesTheDocumentedCombination()
    {
        DiagnosticExecution execution = DiagnosticExecution.SkippedUnavailableCapability(
            Code, Version, RuleName, Category, [CapabilityKind.UsageStatistics]);

        Assert.Equal(DiagnosticExecutionStatus.SkippedUnavailableCapability, execution.Status);
        Assert.Equal(0, execution.FindingCount);
        Assert.Equal([CapabilityKind.UsageStatistics], execution.UnavailableCapabilities);
        Assert.Null(execution.Failure);
    }

    [Fact]
    public void SkippedUnavailableCapability_RecordsMultipleUnavailableCapabilities()
    {
        DiagnosticExecution execution = DiagnosticExecution.SkippedUnavailableCapability(
            Code, Version, RuleName, Category, [CapabilityKind.UsageStatistics, CapabilityKind.CatalogMetadata]);

        Assert.Equal(2, execution.UnavailableCapabilities.Count);
    }

    [Fact]
    public void SkippedUnavailableCapability_OrdersCapabilitiesByAscendingNumericValueRegardlessOfInputOrder()
    {
        DiagnosticExecution execution = DiagnosticExecution.SkippedUnavailableCapability(
            Code, Version, RuleName, Category, [CapabilityKind.UsageStatistics, CapabilityKind.CatalogMetadata]);

        Assert.Equal(
            [CapabilityKind.CatalogMetadata, CapabilityKind.UsageStatistics], execution.UnavailableCapabilities);
    }

    [Fact]
    public void SkippedUnavailableCapability_ProducesTheSameOrderForDifferentInputOrders()
    {
        DiagnosticExecution executionA = DiagnosticExecution.SkippedUnavailableCapability(
            Code, Version, RuleName, Category, [CapabilityKind.UsageStatistics, CapabilityKind.CatalogMetadata]);
        DiagnosticExecution executionB = DiagnosticExecution.SkippedUnavailableCapability(
            Code, Version, RuleName, Category, [CapabilityKind.CatalogMetadata, CapabilityKind.UsageStatistics]);

        Assert.Equal(executionA.UnavailableCapabilities, executionB.UnavailableCapabilities);
    }

    [Fact]
    public void SkippedUnavailableCapability_OrdersThreeCapabilitiesAscendingRegardlessOfInputOrder()
    {
        DiagnosticExecution execution = DiagnosticExecution.SkippedUnavailableCapability(
            Code, Version, RuleName, Category,
            [CapabilityKind.DataProfiling, CapabilityKind.CatalogMetadata, CapabilityKind.UsageStatistics]);

        Assert.Equal(
            [CapabilityKind.CatalogMetadata, CapabilityKind.UsageStatistics, CapabilityKind.DataProfiling],
            execution.UnavailableCapabilities);
    }

    [Fact]
    public void SkippedUnavailableCapability_CanonicalOrderIsUnaffectedBySourceMutation()
    {
        var source = new List<CapabilityKind> { CapabilityKind.UsageStatistics, CapabilityKind.CatalogMetadata };

        DiagnosticExecution execution = DiagnosticExecution.SkippedUnavailableCapability(
            Code, Version, RuleName, Category, source);
        source.Clear();
        source.Add(CapabilityKind.DataProfiling);

        Assert.Equal(
            [CapabilityKind.CatalogMetadata, CapabilityKind.UsageStatistics], execution.UnavailableCapabilities);
    }

    [Fact]
    public void SkippedUnavailableCapability_RejectsAnEmptyCapabilityList()
    {
        Assert.Throws<ArgumentException>(() =>
            DiagnosticExecution.SkippedUnavailableCapability(Code, Version, RuleName, Category, []));
    }

    [Fact]
    public void SkippedUnavailableCapability_RejectsAnUndefinedCapability()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiagnosticExecution.SkippedUnavailableCapability(Code, Version, RuleName, Category, [(CapabilityKind)999]));
    }

    [Fact]
    public void SkippedUnavailableCapability_UnavailableCapabilitiesRejectsAllMutation()
    {
        DiagnosticExecution execution = DiagnosticExecution.SkippedUnavailableCapability(
            Code, Version, RuleName, Category, [CapabilityKind.UsageStatistics]);

        var list = Assert.IsAssignableFrom<IList<CapabilityKind>>(execution.UnavailableCapabilities);
        Assert.Throws<NotSupportedException>(() => list.Add(CapabilityKind.CatalogMetadata));
        Assert.Throws<NotSupportedException>(() => list[0] = CapabilityKind.CatalogMetadata);
        Assert.Throws<NotSupportedException>(() => list.Remove(CapabilityKind.UsageStatistics));
    }

    [Fact]
    public void Failed_ProducesTheDocumentedCombination()
    {
        var failure = new DiagnosticExecutionFailure(
            DiagnosticFailureKind.UnhandledRuleException, "The diagnostic rule failed during evaluation.");

        DiagnosticExecution execution = DiagnosticExecution.Failed(Code, Version, RuleName, Category, failure);

        Assert.Equal(DiagnosticExecutionStatus.Failed, execution.Status);
        Assert.Equal(0, execution.FindingCount);
        Assert.Empty(execution.UnavailableCapabilities);
        Assert.Same(failure, execution.Failure);
    }

    [Fact]
    public void Failed_RejectsNullFailure()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DiagnosticExecution.Failed(Code, Version, RuleName, Category, null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Factories_RejectBlankRuleName(string ruleName)
    {
        Assert.Throws<ArgumentException>(() => DiagnosticExecution.Completed(Code, Version, ruleName, Category, 0));
    }

    [Fact]
    public void Factories_RejectNullCode()
    {
        Assert.Throws<ArgumentNullException>(() => DiagnosticExecution.Completed(null!, Version, RuleName, Category, 0));
    }

    [Fact]
    public void Factories_RejectNullRuleVersion()
    {
        Assert.Throws<ArgumentNullException>(() => DiagnosticExecution.Completed(Code, null!, RuleName, Category, 0));
    }

    [Fact]
    public void Factories_RejectUndefinedCategory()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            DiagnosticExecution.Completed(Code, Version, RuleName, (FindingCategory)999, 0));
    }
}
