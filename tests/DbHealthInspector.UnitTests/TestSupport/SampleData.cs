using DbHealthInspector.Core;
using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.UnitTests.TestSupport;

/// <summary>
/// Small, realistic sample values shared across Core domain model tests, so each test only
/// states the field it actually varies.
/// </summary>
internal static class SampleData
{
    public static DatabaseObjectReference TableReference(
        string? schemaName = "operations", string objectName = "import_batch_rows") =>
        new(DatabaseObjectType.Table, schemaName, objectName);

    public static DatabaseObjectReference IndexReference(
        string? schemaName = "sales",
        string objectName = "ix_orders_customer_id",
        string? parentObjectName = "orders") =>
        new(DatabaseObjectType.Index, schemaName, objectName, parentObjectName);

    public static EvidenceItem IncludedEvidence(
        string key = "indexDefinition",
        string value = "CREATE INDEX ix_orders_customer_id ON orders (customer_id)",
        string? unit = null) =>
        new(key, value, FingerprintParticipation.Include, unit);

    public static EvidenceItem ExcludedEvidence(
        string key = "estimatedRows", string value = "25000", string? unit = "rows") =>
        new(key, value, FingerprintParticipation.Exclude, unit);

    public static Finding SampleFinding(
        FindingCode? code = null,
        RuleVersion? ruleVersion = null,
        FindingSeverity severity = FindingSeverity.Warning,
        FindingConfidence confidence = FindingConfidence.High,
        string message = "The table does not define a primary key.",
        string recommendation = "Review whether the table requires a stable natural or surrogate key.",
        IReadOnlyCollection<EvidenceItem>? evidence = null,
        DatabaseObjectReference? objectReference = null,
        DatabaseEngine? engine = null) =>
        new(
            code ?? FindingCodes.TableWithoutPrimaryKey,
            ruleVersion ?? RuleVersion.Initial,
            FindingCategory.Structure,
            severity,
            confidence,
            objectReference ?? TableReference(),
            message,
            recommendation,
            evidence ?? [ExcludedEvidence()],
            "docs/diagnostics/DBH001.md",
            engine ?? DatabaseEngine.PostgreSql);
}
