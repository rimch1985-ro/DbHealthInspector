using DbHealthInspector.Core;
using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.UnitTests.TestSupport;

/// <summary>
/// Builds <see cref="Finding"/> instances that satisfy a <see cref="FakeInspectionRule"/>'s
/// contract (matching code, rule version, category and engine), for orchestration tests.
/// </summary>
internal static class InspectionFindingBuilder
{
    public static Finding For(
        FakeInspectionRule rule,
        DatabaseEngine? engine = null,
        FindingSeverity severity = FindingSeverity.Info,
        FindingConfidence confidence = FindingConfidence.Low,
        string schemaName = "sales",
        string objectName = "orders",
        FindingCode? code = null,
        RuleVersion? ruleVersion = null,
        FindingCategory? category = null,
        IReadOnlyCollection<EvidenceItem>? evidence = null) =>
        new(
            code ?? rule.Code,
            ruleVersion ?? rule.Version,
            category ?? rule.Category,
            severity,
            confidence,
            new DatabaseObjectReference(DatabaseObjectType.Table, schemaName, objectName),
            "Test finding message.",
            "Test finding recommendation.",
            evidence ?? [new EvidenceItem("objectName", objectName, FingerprintParticipation.Include)],
            "docs/design/inspection-orchestration.md",
            engine ?? DatabaseEngine.PostgreSql);
}
