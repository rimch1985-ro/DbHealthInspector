using System.Text.RegularExpressions;

using DbHealthInspector.Core;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Fingerprinting;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Fingerprinting;

/// <summary>
/// Covers every fingerprint scenario required by the GC-DHI-03A gate prompt. Message,
/// recommendation, severity, confidence and rule-version invariance are covered in
/// <c>Findings.FindingTests</c> because those fields exist only on <c>Finding</c>, not on
/// <see cref="FindingFingerprintInput"/>.
/// </summary>
public sealed class FindingFingerprintGeneratorTests
{
    private static readonly Regex FingerprintFormat = new(
        "^sha256:[0-9a-f]{64}$", RegexOptions.Compiled);

    private static FindingFingerprintInput BuildInput(
        DatabaseEngine? engine = null,
        FindingCode? code = null,
        DatabaseObjectType objectType = DatabaseObjectType.Table,
        string? schemaName = "operations",
        string objectName = "import_batch_rows",
        string? parentObjectName = null,
        IReadOnlyCollection<EvidenceItem>? evidence = null)
    {
        evidence ??=
        [
            SampleData.IncludedEvidence("indexDefinition", "CREATE INDEX ix (customer_id)"),
            SampleData.IncludedEvidence("columnList", "(customer_id)"),
            SampleData.ExcludedEvidence(),
        ];

        var objectReference = new DatabaseObjectReference(objectType, schemaName, objectName, parentObjectName);

        return new FindingFingerprintInput(
            engine ?? DatabaseEngine.PostgreSql, code ?? FindingCodes.TableWithoutPrimaryKey, objectReference, evidence);
    }

    [Fact]
    public void SameData_ProducesTheSameFingerprint()
    {
        FindingFingerprint first = FindingFingerprintGenerator.Generate(BuildInput());
        FindingFingerprint second = FindingFingerprintGenerator.Generate(BuildInput());

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentEvidenceOrder_ProducesTheSameFingerprint()
    {
        EvidenceItem[] forwardOrder =
        [
            SampleData.IncludedEvidence("indexDefinition", "CREATE INDEX ix (customer_id)"),
            SampleData.IncludedEvidence("columnList", "(customer_id)"),
            SampleData.ExcludedEvidence(),
        ];
        EvidenceItem[] reverseOrder = [.. forwardOrder.Reverse()];

        FindingFingerprint first = FindingFingerprintGenerator.Generate(BuildInput(evidence: forwardOrder));
        FindingFingerprint second = FindingFingerprintGenerator.Generate(BuildInput(evidence: reverseOrder));

        Assert.Equal(first, second);
    }

    [Fact]
    public void ChangingAnIncludedEvidenceValue_ProducesADifferentFingerprint()
    {
        FindingFingerprint baseline = FindingFingerprintGenerator.Generate(BuildInput());
        FindingFingerprint varied = FindingFingerprintGenerator.Generate(BuildInput(evidence:
        [
            SampleData.IncludedEvidence("indexDefinition", "CREATE INDEX ix (order_date)"),
            SampleData.IncludedEvidence("columnList", "(customer_id)"),
            SampleData.ExcludedEvidence(),
        ]));

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ChangingAnExcludedEvidenceValue_ProducesTheSameFingerprint()
    {
        FindingFingerprint baseline = FindingFingerprintGenerator.Generate(BuildInput());
        FindingFingerprint varied = FindingFingerprintGenerator.Generate(BuildInput(evidence:
        [
            SampleData.IncludedEvidence("indexDefinition", "CREATE INDEX ix (customer_id)"),
            SampleData.IncludedEvidence("columnList", "(customer_id)"),
            SampleData.ExcludedEvidence(value: "999999"),
        ]));

        Assert.Equal(baseline, varied);
    }

    [Fact]
    public void ChangingTheEngine_ProducesADifferentFingerprint()
    {
        FindingFingerprint baseline = FindingFingerprintGenerator.Generate(BuildInput());
        FindingFingerprint varied = FindingFingerprintGenerator.Generate(
            BuildInput(engine: new DatabaseEngine("SqlServer")));

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ChangingTheFindingCode_ProducesADifferentFingerprint()
    {
        FindingFingerprint baseline = FindingFingerprintGenerator.Generate(BuildInput());
        FindingFingerprint varied = FindingFingerprintGenerator.Generate(
            BuildInput(code: FindingCodes.LargeTable));

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ChangingTheObjectType_ProducesADifferentFingerprint()
    {
        FindingFingerprint baseline = FindingFingerprintGenerator.Generate(BuildInput());
        FindingFingerprint varied = FindingFingerprintGenerator.Generate(
            BuildInput(objectType: DatabaseObjectType.Column));

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ChangingTheSchema_ProducesADifferentFingerprint()
    {
        FindingFingerprint baseline = FindingFingerprintGenerator.Generate(BuildInput());
        FindingFingerprint varied = FindingFingerprintGenerator.Generate(BuildInput(schemaName: "reporting"));

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ChangingTheParent_ProducesADifferentFingerprint()
    {
        FindingFingerprint baseline = FindingFingerprintGenerator.Generate(
            BuildInput(objectType: DatabaseObjectType.Index, objectName: "ix_orders_customer_id", parentObjectName: "orders"));
        FindingFingerprint varied = FindingFingerprintGenerator.Generate(
            BuildInput(objectType: DatabaseObjectType.Index, objectName: "ix_orders_customer_id", parentObjectName: "orders_v2"));

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void ChangingTheName_ProducesADifferentFingerprint()
    {
        FindingFingerprint baseline = FindingFingerprintGenerator.Generate(BuildInput());
        FindingFingerprint varied = FindingFingerprintGenerator.Generate(BuildInput(objectName: "other_table"));

        Assert.NotEqual(baseline, varied);
    }

    [Fact]
    public void NullField_And_EmptyField_CanonicalizeToDifferentBytes()
    {
        // No public domain type can hold an empty optional string (every optional string
        // rejects "" the same way it rejects null-required fields; see
        // docs/design/core-domain-contracts.md §3, §11), so this distinction can no longer be
        // demonstrated by building two DatabaseObjectReference instances. It is still a real
        // property of the canonicalization algorithm (§9.5), verified here directly through the
        // internal canonical-field operation instead of through a weakened public contract.
        byte[] nullEncoding = FindingFingerprintGenerator.EncodeCanonicalField(null);
        byte[] emptyEncoding = FindingFingerprintGenerator.EncodeCanonicalField(string.Empty);

        Assert.NotEqual(nullEncoding, emptyEncoding);
        Assert.Single(nullEncoding);
        Assert.Equal(0, nullEncoding[0]);
        Assert.Equal(1, emptyEncoding[0]);
    }

    [Fact]
    public void DelimiterSusceptibleInputs_ProduceDifferentFingerprints()
    {
        // Naive "schema|name" concatenation would collide for ("ab","c") and ("a","bc").
        FindingFingerprint first = FindingFingerprintGenerator.Generate(
            BuildInput(schemaName: "ab", objectName: "c"));
        FindingFingerprint second = FindingFingerprintGenerator.Generate(
            BuildInput(schemaName: "a", objectName: "bc"));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void ComposedAndDecomposedUnicodeEquivalents_ProduceTheSameFingerprint()
    {
        // Built from \u escapes (not literal source-file text) so the two forms cannot be
        // silently normalized to the same bytes before the test even runs.
        string composed = "café_schema"; // "e" with acute accent pre-composed as U+00E9.
        string decomposed = "café_schema"; // "e" (U+0065) then combining acute U+0301.

        Assert.NotEqual(composed, decomposed, StringComparer.Ordinal);

        FindingFingerprint first = FindingFingerprintGenerator.Generate(BuildInput(schemaName: composed));
        FindingFingerprint second = FindingFingerprintGenerator.Generate(BuildInput(schemaName: decomposed));

        Assert.Equal(first, second);
    }

    [Fact]
    public void DifferentCapitalization_ProducesDifferentFingerprints()
    {
        FindingFingerprint lower = FindingFingerprintGenerator.Generate(BuildInput(schemaName: "sales"));
        FindingFingerprint upper = FindingFingerprintGenerator.Generate(BuildInput(schemaName: "Sales"));

        Assert.NotEqual(lower, upper);
    }

    [Fact]
    public void Generate_ProducesTheDocumentedFormat()
    {
        FindingFingerprint fingerprint = FindingFingerprintGenerator.Generate(BuildInput());

        Assert.Matches(FingerprintFormat, fingerprint.Value);
    }

    [Fact]
    public void GoldenVector_StaysStableForAFixedInput()
    {
        var objectReference = new DatabaseObjectReference(
            DatabaseObjectType.Table, "ops", "import_batch_rows");
        EvidenceItem[] evidence =
        [
            new EvidenceItem("estimatedRows", "25000", FingerprintParticipation.Exclude, "rows"),
            new EvidenceItem("totalSizeBytes", "4194304", FingerprintParticipation.Exclude, "bytes"),
            new EvidenceItem("hasPrimaryKey", "false", FingerprintParticipation.Include),
        ];
        var input = new FindingFingerprintInput(
            DatabaseEngine.PostgreSql, FindingCodes.TableWithoutPrimaryKey, objectReference, evidence);

        FindingFingerprint fingerprint = FindingFingerprintGenerator.Generate(input);

        Assert.Equal(
            "sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444",
            fingerprint.Value);
    }
}
