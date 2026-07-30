using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.UnitTests.TestSupport;

/// <summary>
/// A small, configurable <see cref="IInspectionRule"/> fake for orchestration tests. Never uses
/// DBH001–DBH005; test codes such as <c>DBH900</c>/<c>DBH901</c> are valid <see cref="FindingCode"/>
/// values without being part of the approved <see cref="FindingCodes"/> catalog.
/// </summary>
internal sealed class FakeInspectionRule : IInspectionRule
{
    private readonly Func<DatabaseSnapshot, IReadOnlyList<Finding>?> _evaluate;

    public FindingCode Code { get; }

    public RuleVersion Version { get; }

    public string Name { get; }

    public FindingCategory Category { get; }

    public int EvaluateCallCount { get; private set; }

    public FakeInspectionRule(
        string code,
        Func<DatabaseSnapshot, IReadOnlyList<Finding>?> evaluate,
        string? name = null,
        FindingCategory category = FindingCategory.Structure,
        RuleVersion? version = null)
    {
        Code = new FindingCode(code);
        Version = version ?? RuleVersion.Initial;
        Name = name ?? $"TEST_RULE_{code}";
        Category = category;
        _evaluate = evaluate;
    }

    /// <summary>
    /// Always returns an empty finding list.
    /// </summary>
    public static FakeInspectionRule NoFindings(string code, FindingCategory category = FindingCategory.Structure) =>
        new(code, _ => [], category: category);

    /// <summary>
    /// Its <see cref="Evaluate"/> throws <paramref name="exception"/>.
    /// </summary>
    public static FakeInspectionRule Throwing(string code, Exception exception, FindingCategory category = FindingCategory.Structure) =>
        new(code, _ => throw exception, category: category);

    public IReadOnlyList<Finding> Evaluate(DatabaseSnapshot snapshot)
    {
        EvaluateCallCount++;

        // The interface promises a non-null result, but nothing enforces that at runtime; the
        // null-forgiving operator here lets a fake deliberately violate that promise so the
        // orchestrator's defensive check can be tested.
        return _evaluate(snapshot)!;
    }
}
