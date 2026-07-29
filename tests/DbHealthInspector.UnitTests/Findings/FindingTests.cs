using DbHealthInspector.Core;
using DbHealthInspector.Core.Findings;
using DbHealthInspector.Core.Fingerprinting;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Findings;

public sealed class FindingTests
{
    [Fact]
    public void Constructor_ExposesEveryConstructorArgument()
    {
        Finding finding = SampleData.SampleFinding();

        Assert.Equal(FindingCodes.TableWithoutPrimaryKey, finding.Code);
        Assert.Equal(RuleVersion.Initial, finding.RuleVersion);
        Assert.Equal(FindingCategory.Structure, finding.Category);
        Assert.Equal(FindingSeverity.Warning, finding.Severity);
        Assert.Equal(FindingConfidence.High, finding.Confidence);
        Assert.Equal(SampleData.TableReference(), finding.ObjectReference);
        Assert.Equal(DatabaseEngine.PostgreSql, finding.Engine);
        Assert.Equal("The table does not define a primary key.", finding.Message);
        Assert.Equal(
            "Review whether the table requires a stable natural or surrogate key.",
            finding.Recommendation);
        Assert.Equal("docs/diagnostics/DBH001.md", finding.DocumentationReference);
        Assert.Single(finding.Evidence);
    }

    [Fact]
    public void Constructor_RejectsNullMessage()
    {
        Assert.Throws<ArgumentNullException>(() => SampleData.SampleFinding(message: null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankMessage(string message)
    {
        Assert.Throws<ArgumentException>(() => SampleData.SampleFinding(message: message));
    }

    [Fact]
    public void Constructor_RejectsNullRecommendation()
    {
        Assert.Throws<ArgumentNullException>(() => SampleData.SampleFinding(recommendation: null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankRecommendation(string recommendation)
    {
        Assert.Throws<ArgumentException>(() => SampleData.SampleFinding(recommendation: recommendation));
    }

    [Fact]
    public void Constructor_RejectsNullDocumentationReference()
    {
        Assert.Throws<ArgumentNullException>(() => BuildFinding(documentationReference: null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankDocumentationReference(string documentationReference)
    {
        Assert.Throws<ArgumentException>(() => BuildFinding(documentationReference: documentationReference));
    }

    [Fact]
    public void Constructor_RejectsNullEngine()
    {
        Assert.Throws<ArgumentNullException>(() => new Finding(
            FindingCodes.TableWithoutPrimaryKey,
            RuleVersion.Initial,
            FindingCategory.Structure,
            FindingSeverity.Warning,
            FindingConfidence.High,
            SampleData.TableReference(),
            "message",
            "recommendation",
            [SampleData.ExcludedEvidence()],
            "docs/diagnostics/DBH001.md",
            engine: null!));
    }

    [Fact]
    public void Constructor_CopiesEvidenceDefensively()
    {
        var mutableEvidence = new List<EvidenceItem> { SampleData.ExcludedEvidence() };

        Finding finding = SampleData.SampleFinding(evidence: mutableEvidence);
        mutableEvidence.Add(SampleData.IncludedEvidence());

        Assert.Single(finding.Evidence);
    }

    [Fact]
    public void Constructor_RejectsANullEvidenceElement()
    {
        Assert.Throws<ArgumentException>(() =>
            SampleData.SampleFinding(evidence: [SampleData.ExcludedEvidence(), null!]));
    }

    [Fact]
    public void Constructor_RejectsDuplicateEvidenceKeys()
    {
        EvidenceItem[] evidence =
        [
            SampleData.ExcludedEvidence(key: "estimatedRows", value: "1"),
            SampleData.ExcludedEvidence(key: "estimatedRows", value: "2"),
        ];

        Assert.Throws<ArgumentException>(() => SampleData.SampleFinding(evidence: evidence));
    }

    [Fact]
    public void Evidence_IsNotModifiableThroughTheExposedList()
    {
        Finding finding = SampleData.SampleFinding();

        var evidenceAsList = Assert.IsAssignableFrom<IList<EvidenceItem>>(finding.Evidence);
        Assert.Throws<NotSupportedException>(() => evidenceAsList.Add(SampleData.IncludedEvidence()));
    }

    [Fact]
    public void Engine_PreservesTheReceivedEngine()
    {
        var sqlServer = new DatabaseEngine("SqlServer");

        Finding finding = SampleData.SampleFinding(engine: sqlServer);

        Assert.Equal(sqlServer, finding.Engine);
    }

    [Fact]
    public void Fingerprint_CanBeIndependentlyRecomputedFromFindingProperties()
    {
        Finding finding = SampleData.SampleFinding(
            evidence: [SampleData.IncludedEvidence(), SampleData.ExcludedEvidence()]);

        // Rebuilt using only finding.Engine / finding.Code / finding.ObjectReference /
        // finding.Evidence — no data outside the Finding object itself — proving the
        // fingerprint is not derived from information the Finding does not retain.
        var recomputedInput = new FindingFingerprintInput(
            finding.Engine, finding.Code, finding.ObjectReference, finding.Evidence);
        FindingFingerprint recomputed = FindingFingerprintGenerator.Generate(recomputedInput);

        Assert.Equal(finding.Fingerprint, recomputed);
    }

    [Fact]
    public void Fingerprint_MatchesTheGeneratorForTheSameLogicalInput()
    {
        DatabaseObjectReference objectReference = SampleData.TableReference();
        FindingCode code = FindingCodes.TableWithoutPrimaryKey;
        EvidenceItem[] evidence = [SampleData.IncludedEvidence(), SampleData.ExcludedEvidence()];

        Finding finding = SampleData.SampleFinding(code: code, objectReference: objectReference, evidence: evidence);

        var expectedInput = new FindingFingerprintInput(DatabaseEngine.PostgreSql, code, objectReference, evidence);
        FindingFingerprint expected = FindingFingerprintGenerator.Generate(expectedInput);

        Assert.Equal(expected, finding.Fingerprint);
    }

    [Fact]
    public void Fingerprint_DiffersWhenOnlyEngineDiffers()
    {
        Finding postgres = SampleData.SampleFinding(engine: DatabaseEngine.PostgreSql);
        Finding otherEngine = SampleData.SampleFinding(engine: new DatabaseEngine("SqlServer"));

        Assert.NotEqual(postgres.Fingerprint, otherEngine.Fingerprint);
    }

    [Theory]
    [InlineData(FindingSeverity.Info)]
    [InlineData(FindingSeverity.Warning)]
    [InlineData(FindingSeverity.Critical)]
    public void Fingerprint_IsUnaffectedBySeverity(FindingSeverity severity)
    {
        Finding baseline = SampleData.SampleFinding();
        Finding varied = SampleData.SampleFinding(severity: severity);

        Assert.Equal(baseline.Fingerprint, varied.Fingerprint);
    }

    [Theory]
    [InlineData(FindingConfidence.Low)]
    [InlineData(FindingConfidence.Medium)]
    [InlineData(FindingConfidence.High)]
    public void Fingerprint_IsUnaffectedByConfidence(FindingConfidence confidence)
    {
        Finding baseline = SampleData.SampleFinding();
        Finding varied = SampleData.SampleFinding(confidence: confidence);

        Assert.Equal(baseline.Fingerprint, varied.Fingerprint);
    }

    [Fact]
    public void Fingerprint_IsUnaffectedByMessageOrRecommendation()
    {
        Finding baseline = SampleData.SampleFinding();
        Finding varied = SampleData.SampleFinding(
            message: "A completely different message.",
            recommendation: "A completely different recommendation.");

        Assert.Equal(baseline.Fingerprint, varied.Fingerprint);
    }

    [Fact]
    public void Fingerprint_IsUnaffectedByRuleVersion()
    {
        Finding baseline = SampleData.SampleFinding(ruleVersion: new RuleVersion(1));
        Finding varied = SampleData.SampleFinding(ruleVersion: new RuleVersion(7));

        Assert.Equal(baseline.Fingerprint, varied.Fingerprint);
    }

    [Fact]
    public void Equality_IsReferenceBasedNotStructural()
    {
        // Finding intentionally does not implement value equality (see the type's XML
        // documentation and docs/design/core-domain-contracts.md §3): "the same finding"
        // across two independent constructions is expressed by Fingerprint equality, not by
        // Finding.Equals. This test documents that behavior so it cannot be mistaken for a bug.
        Finding first = SampleData.SampleFinding();
        Finding second = SampleData.SampleFinding();

        Assert.Equal(first.Fingerprint, second.Fingerprint);
        Assert.False(first.Equals(second));
        Assert.NotSame(first, second);
    }

    private static Finding BuildFinding(
        string documentationReference = "docs/diagnostics/DBH001.md",
        DatabaseEngine? engine = null) =>
        new(
            FindingCodes.TableWithoutPrimaryKey,
            RuleVersion.Initial,
            FindingCategory.Structure,
            FindingSeverity.Warning,
            FindingConfidence.High,
            SampleData.TableReference(),
            "message",
            "recommendation",
            [SampleData.ExcludedEvidence()],
            documentationReference,
            engine ?? DatabaseEngine.PostgreSql);
}
