using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Indexes;

namespace DbHealthInspector.UnitTests.Indexes;

/// <summary>
/// The E001 group-to-<see cref="IndexSnapshot"/> mapping (GC-DHI-04E §11–§19): a whole index at a
/// time, fail-closed on every contradiction, and nothing inferred or reconstructed from DDL.
/// </summary>
public sealed class PostgreSqlIndexSnapshotMapperTests
{
    private const string MappingMessage = "The PostgreSQL index metadata row is invalid.";
    private const string LeakMessage = "Sensitive data was exposed.";

    // --- Row builders ---------------------------------------------------------------------------

    /// <summary>One well-formed key attribute row. Every field is overridable so a test can make
    /// exactly one thing wrong.</summary>
    private static PostgreSqlIndexMetadataRow KeyRow(
        string schemaName = "public",
        string tableName = "orders",
        string indexName = "orders_a_idx",
        string accessMethod = "btree",
        string indexRelationKind = "i",
        bool isIndexPartition = false,
        int attributeCount = 1,
        int keyAttributeCount = 1,
        int attributePosition = 1,
        bool isKey = true,
        string? columnName = "a",
        string? expression = null,
        string? collationSchema = null,
        string? collationName = null,
        string? operatorClassSchema = "pg_catalog",
        string? operatorClassName = "text_ops",
        string[]? operatorClassOptions = null,
        bool? isOrderable = true,
        bool? isAscending = true,
        bool? isDescending = false,
        bool? nullsFirst = false,
        bool? nullsLast = true,
        string? partialPredicate = null,
        bool isUnique = false,
        bool? nullsNotDistinct = null,
        bool isPrimaryKey = false,
        bool backsConstraint = false,
        bool isValid = true,
        bool isReady = true,
        bool isLive = true,
        long sizeBytes = 8192) =>
        new(schemaName, tableName, indexName, accessMethod, indexRelationKind, isIndexPartition,
            attributeCount, keyAttributeCount, attributePosition, isKey, columnName, expression,
            collationSchema, collationName, operatorClassSchema, operatorClassName,
            operatorClassOptions, isOrderable, isAscending, isDescending, nullsFirst, nullsLast,
            partialPredicate, isUnique, nullsNotDistinct, isPrimaryKey, backsConstraint, isValid,
            isReady, isLive, sizeBytes);

    /// <summary>
    /// One well-formed INCLUDE attribute row: a plain stored column with every key-only field null,
    /// exactly as E001 returns it.
    /// </summary>
    private static PostgreSqlIndexMetadataRow IncludeRow(
        int attributeCount,
        int keyAttributeCount,
        int attributePosition,
        string columnName,
        string indexName = "orders_a_idx",
        string? expression = null,
        string? collationSchema = null,
        string? collationName = null,
        string? operatorClassSchema = null,
        string? operatorClassName = null,
        string[]? operatorClassOptions = null,
        bool? isOrderable = null,
        bool? isAscending = null,
        bool? isDescending = null,
        bool? nullsFirst = null,
        bool? nullsLast = null) =>
        new("public", "orders", indexName, "btree", "i", false,
            attributeCount, keyAttributeCount, attributePosition, false, columnName, expression,
            collationSchema, collationName, operatorClassSchema, operatorClassName,
            operatorClassOptions, isOrderable, isAscending, isDescending, nullsFirst, nullsLast,
            null, false, null, false, false, true, true, true, 8192);

    private static IndexSnapshot Map(params PostgreSqlIndexMetadataRow[] rows) =>
        PostgreSqlIndexSnapshotMapper.Map(rows, null);

    private static PostgreSqlIndexSnapshotMappingException Rejects(params PostgreSqlIndexMetadataRow[] rows) =>
        Assert.Throws<PostgreSqlIndexSnapshotMappingException>(
            () => PostgreSqlIndexSnapshotMapper.Map(rows, null));

    // --- Group shape ----------------------------------------------------------------------------

    [Fact]
    public void ASingleKeyIndex_Maps()
    {
        IndexSnapshot snapshot = Map(KeyRow());

        Assert.Equal("public", snapshot.SchemaName);
        Assert.Equal("orders", snapshot.TableName);
        Assert.Equal("orders_a_idx", snapshot.IndexName);
        Assert.Equal("btree", snapshot.AccessMethod);
        Assert.Single(snapshot.KeyParts);
        Assert.Empty(snapshot.IncludedColumns);
        Assert.Equal(8192, snapshot.SizeBytes);
    }

    [Fact]
    public void AnEmptyGroup_IsRejected() => Rejects();

    [Fact]
    public void ARowCountDifferentFromAttributeCount_IsRejected()
    {
        // Two rows claiming a three-attribute index.
        Rejects(
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 1),
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 2, columnName: "b"));
    }

    [Fact]
    public void ADuplicatePosition_IsRejected() =>
        Rejects(
            KeyRow(attributeCount: 2, keyAttributeCount: 2, attributePosition: 1),
            KeyRow(attributeCount: 2, keyAttributeCount: 2, attributePosition: 1, columnName: "b"));

    [Fact]
    public void APositionGap_IsRejected() =>
        Rejects(
            KeyRow(attributeCount: 2, keyAttributeCount: 2, attributePosition: 1),
            KeyRow(attributeCount: 2, keyAttributeCount: 2, attributePosition: 3, columnName: "b"));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveAttributeCount_IsRejected(int attributeCount) =>
        Rejects(KeyRow(attributeCount: attributeCount));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ANonPositiveKeyAttributeCount_IsRejected(int keyAttributeCount) =>
        Rejects(KeyRow(keyAttributeCount: keyAttributeCount));

    [Fact]
    public void AKeyAttributeCountAboveAttributeCount_IsRejected() =>
        Rejects(KeyRow(attributeCount: 1, keyAttributeCount: 2));

    [Fact]
    public void AHeaderThatDiffersWithinTheGroup_IsRejected()
    {
        // Same identity, contradictory index-wide fact: two indexes folded into one group.
        Rejects(
            KeyRow(attributeCount: 2, keyAttributeCount: 2, attributePosition: 1, isValid: true),
            KeyRow(attributeCount: 2, keyAttributeCount: 2, attributePosition: 2, columnName: "b", isValid: false));
    }

    [Fact]
    public void AnAttributeMarkedKeyBeyondTheKeyCount_IsRejected() =>
        Rejects(
            KeyRow(attributeCount: 2, keyAttributeCount: 1, attributePosition: 1),
            KeyRow(attributeCount: 2, keyAttributeCount: 1, attributePosition: 2, columnName: "b"));

    [Fact]
    public void AnAttributeMarkedNonKeyInsideTheKeyRange_IsRejected() =>
        Rejects(IncludeRow(1, 1, 1, "a"));

    [Theory]
    [InlineData("x")]
    [InlineData("r")]
    [InlineData("")]
    public void AnUnknownIndexRelationKind_IsRejected(string relationKind) =>
        Rejects(KeyRow(indexRelationKind: relationKind));

    // Blank required identifiers are covered by ABlankRequiredIdentifier_IsRejected in the
    // null-versus-blank section below, over a strictly wider set of blank forms.

    // --- Keys and INCLUDE -----------------------------------------------------------------------

    [Fact]
    public void AMulticolumnIndex_PreservesKeyOrderByPosition()
    {
        // Deliberately supplied out of order: position, not arrival order, decides.
        IndexSnapshot snapshot = Map(
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 3, columnName: "c"),
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 1, columnName: "a"),
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 2, columnName: "b"));

        Assert.Equal([1, 2, 3], snapshot.KeyParts.Select(part => part.Position).ToArray());
        Assert.Equal(["a", "b", "c"], snapshot.KeyParts.Select(part => part.ColumnName ?? string.Empty).ToArray());
    }

    [Fact]
    public void AnExpressionKey_CarriesTheExpressionAndNoColumn()
    {
        IndexSnapshot snapshot = Map(KeyRow(columnName: null, expression: "lower(a)"));

        IndexKeyPartSnapshot part = Assert.Single(snapshot.KeyParts);
        Assert.Null(part.ColumnName);
        Assert.Equal("lower(a)", part.Expression);
    }

    [Fact]
    public void AMixedColumnAndExpressionIndex_Maps()
    {
        IndexSnapshot snapshot = Map(
            KeyRow(attributeCount: 2, keyAttributeCount: 2, attributePosition: 1, columnName: "a"),
            KeyRow(attributeCount: 2, keyAttributeCount: 2, attributePosition: 2, columnName: null, expression: "lower(b)"));

        Assert.Equal("a", snapshot.KeyParts[0].ColumnName);
        Assert.Equal("lower(b)", snapshot.KeyParts[1].Expression);
    }

    [Fact]
    public void AKeyWithBothColumnAndExpression_IsRejected() =>
        Rejects(KeyRow(columnName: "a", expression: "lower(a)"));

    [Fact]
    public void AKeyWithNeitherColumnNorExpression_IsRejected() =>
        Rejects(KeyRow(columnName: null, expression: null));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AKeyWithABlankExpression_IsRejected(string blank) =>
        Rejects(KeyRow(columnName: null, expression: blank));

    [Fact]
    public void IncludedColumns_PreserveAttributeOrder()
    {
        IndexSnapshot snapshot = Map(
            KeyRow(attributeCount: 3, keyAttributeCount: 1, attributePosition: 1, columnName: "a"),
            IncludeRow(3, 1, 3, "z"),
            IncludeRow(3, 1, 2, "m"));

        // Ordered by attribute position, not by name and not by arrival.
        Assert.Equal(["m", "z"], snapshot.IncludedColumns.ToArray());
        Assert.Single(snapshot.KeyParts);
    }

    [Fact]
    public void ADuplicateIncludedColumn_IsRejected() =>
        Rejects(
            KeyRow(attributeCount: 3, keyAttributeCount: 1, attributePosition: 1),
            IncludeRow(3, 1, 2, "dup"),
            IncludeRow(3, 1, 3, "dup"));

    /// <summary>
    /// An INCLUDE attribute is a plain stored column: every key-only field must be null. Each case
    /// populates exactly one of them.
    /// </summary>
    public static TheoryData<string> IncludeContamination() =>
        ["expression", "collationSchema", "collationName", "opclassSchema", "opclassName", "options", "orderable", "ascending", "descending", "nullsFirst", "nullsLast"];

    [Theory]
    [MemberData(nameof(IncludeContamination))]
    public void AnIncludeAttributeCarryingKeyMetadata_IsRejected(string field)
    {
        PostgreSqlIndexMetadataRow contaminated = field switch
        {
            "expression" => IncludeRow(2, 1, 2, "b", expression: "lower(b)"),
            "collationSchema" => IncludeRow(2, 1, 2, "b", collationSchema: "pg_catalog"),
            "collationName" => IncludeRow(2, 1, 2, "b", collationName: "default"),
            "opclassSchema" => IncludeRow(2, 1, 2, "b", operatorClassSchema: "pg_catalog"),
            "opclassName" => IncludeRow(2, 1, 2, "b", operatorClassName: "text_ops"),
            "options" => IncludeRow(2, 1, 2, "b", operatorClassOptions: []),
            "orderable" => IncludeRow(2, 1, 2, "b", isOrderable: true),
            "ascending" => IncludeRow(2, 1, 2, "b", isAscending: true),
            "descending" => IncludeRow(2, 1, 2, "b", isDescending: false),
            "nullsFirst" => IncludeRow(2, 1, 2, "b", nullsFirst: false),
            _ => IncludeRow(2, 1, 2, "b", nullsLast: true),
        };

        Rejects(KeyRow(attributeCount: 2, keyAttributeCount: 1, attributePosition: 1), contaminated);
    }

    [Fact]
    public void AnIncludeAttributeWithoutAColumnName_IsRejected() =>
        Rejects(
            KeyRow(attributeCount: 2, keyAttributeCount: 1, attributePosition: 1),
            IncludeRow(2, 1, 2, "  "));

    // --- Qualified structural identity ----------------------------------------------------------

    [Fact]
    public void AnOperatorClass_IsSchemaQualifiedAndQuoted() =>
        Assert.Equal(
            "\"pg_catalog\".\"text_ops\"",
            Assert.Single(Map(KeyRow()).KeyParts).OperatorClass);

    [Fact]
    public void ACollation_IsSchemaQualifiedAndQuoted() =>
        Assert.Equal(
            "\"pg_catalog\".\"default\"",
            Assert.Single(Map(KeyRow(collationSchema: "pg_catalog", collationName: "default")).KeyParts).Collation);

    [Fact]
    public void AnAbsentCollation_IsNull() =>
        Assert.Null(Assert.Single(Map(KeyRow()).KeyParts).Collation);

    [Fact]
    public void AnEmbeddedDoubleQuote_IsDoubled()
    {
        IndexKeyPartSnapshot part = Assert.Single(
            Map(KeyRow(operatorClassSchema: "we\"ird", operatorClassName: "op\"s")).KeyParts);

        Assert.Equal("\"we\"\"ird\".\"op\"\"s\"", part.OperatorClass);
    }

    [Fact]
    public void QualifiedIdentity_IsInjectiveAcrossQuotePlacement()
    {
        // Without doubling, ("a\"" , "b") and ("a", "\"b") could collide.
        string first = Assert.Single(Map(KeyRow(operatorClassSchema: "a\"", operatorClassName: "b")).KeyParts).OperatorClass!;
        string second = Assert.Single(Map(KeyRow(operatorClassSchema: "a", operatorClassName: "\"b")).KeyParts).OperatorClass!;

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void UnicodeIdentifiers_ArePreservedVerbatim()
    {
        IndexKeyPartSnapshot part = Assert.Single(
            Map(KeyRow(operatorClassSchema: "esquéma_ñ", operatorClassName: "ops_日本")).KeyParts);

        Assert.Equal("\"esquéma_ñ\".\"ops_日本\"", part.OperatorClass);
    }

    [Fact]
    public void AHalfPresentCollation_IsRejected()
    {
        Rejects(KeyRow(collationSchema: "pg_catalog", collationName: null));
        Rejects(KeyRow(collationSchema: null, collationName: "default"));
    }

    [Fact]
    public void AKeyWithoutAnOperatorClass_IsRejected()
    {
        Rejects(KeyRow(operatorClassSchema: null, operatorClassName: null));
        Rejects(KeyRow(operatorClassSchema: "pg_catalog", operatorClassName: null));
        Rejects(KeyRow(operatorClassSchema: null, operatorClassName: "text_ops"));
    }

    // --- Operator-class options (GC-DHI-04E §15, D1) --------------------------------------------

    private static string OperatorClassOf(string[]? options) =>
        Assert.Single(Map(KeyRow(operatorClassOptions: options)).KeyParts).OperatorClass!;

    [Fact]
    public void SqlNullOptions_AddNoSuffix() =>
        Assert.Equal("\"pg_catalog\".\"text_ops\"", OperatorClassOf(null));

    [Fact]
    public void AnEmptyOptionArray_IsDistinctFromSqlNull()
    {
        Assert.Equal("\"pg_catalog\".\"text_ops\"|options[0;]", OperatorClassOf([]));
        Assert.NotEqual(OperatorClassOf(null), OperatorClassOf([]));
    }

    [Fact]
    public void OneOption_IsLengthPrefixed()
    {
        // "values_per_range=32" is 19 UTF-16 code units; the prefix is String.Length exactly.
        Assert.Equal(19, "values_per_range=32".Length);
        Assert.Equal(
            "\"pg_catalog\".\"text_ops\"|options[1;19:values_per_range=32]",
            OperatorClassOf(["values_per_range=32"]));
    }

    [Fact]
    public void MultipleOptions_AreEncodedInStoredOrder() =>
        Assert.Equal("\"pg_catalog\".\"text_ops\"|options[2;1:a2:bb]", OperatorClassOf(["a", "bb"]));

    [Fact]
    public void OptionOrder_IsPartOfTheIdentityAndIsNeverSorted()
    {
        string ab = OperatorClassOf(["a", "b"]);
        string ba = OperatorClassOf(["b", "a"]);

        Assert.NotEqual(ab, ba);
        Assert.EndsWith("|options[2;1:a1:b]", ab, StringComparison.Ordinal);
        Assert.EndsWith("|options[2;1:b1:a]", ba, StringComparison.Ordinal);
    }

    [Fact]
    public void DifferentOptionValues_ProduceDifferentIdentities() =>
        Assert.NotEqual(OperatorClassOf(["values_per_range=32"]), OperatorClassOf(["values_per_range=64"]));

    [Fact]
    public void IdenticalStructuralInput_ProducesIdenticalIdentity() =>
        Assert.Equal(OperatorClassOf(["x=1", "y=2"]), OperatorClassOf(["x=1", "y=2"]));

    [Fact]
    public void AnEmptyStringOption_IsEncodedAsZeroLength() =>
        Assert.Equal("\"pg_catalog\".\"text_ops\"|options[1;0:]", OperatorClassOf([""]));

    [Fact]
    public void AnEmptyStringOption_IsDistinctFromNoOptionsAndFromAnEmptyArray()
    {
        Assert.NotEqual(OperatorClassOf([""]), OperatorClassOf(null));
        Assert.NotEqual(OperatorClassOf([""]), OperatorClassOf([]));
    }

    /// <summary>
    /// Values containing the encoding's own delimiters must not be able to forge structure. Each
    /// pair is a different structural input whose naive concatenation could collide.
    /// </summary>
    public static TheoryData<string[], string[]> AdversarialOptionPairs() => new()
    {
        { ["a:b"], ["a", "b"] },
        { ["a;b"], ["a", "b"] },
        { ["1:a"], ["a"] },
        { ["]"], ["", ""] },
        { ["|options[1;1:x]"], ["x"] },
        { ["2;1:a1:b"], ["a", "b"] },
        { ["a", ""], ["a"] },
        { ["", "a"], ["a"] },
        { ["\"quoted\""], ["quoted"] },
        { ["日本語=1"], ["1"] },
    };

    [Theory]
    [MemberData(nameof(AdversarialOptionPairs))]
    public void AdversarialOptionValues_NeverCollide(string[] left, string[] right) =>
        Assert.NotEqual(OperatorClassOf(left), OperatorClassOf(right));

    [Fact]
    public void OptionLengths_AreUtf16CodeUnitsExactly()
    {
        // A non-BMP character is one rune but two UTF-16 code units, and the contract is
        // String.Length. Encoding it as 1 would make the format ambiguous for .NET readers.
        const string surrogatePair = "\U0001F600";
        Assert.Equal(2, surrogatePair.Length);

        Assert.Equal("\"pg_catalog\".\"text_ops\"|options[1;2:\U0001F600]", OperatorClassOf([surrogatePair]));
    }

    [Fact]
    public void OptionValues_AreNeitherTrimmedNorNormalized()
    {
        // Leading/trailing space kept, and two Unicode spellings of the same grapheme stay
        // distinct: no NFC/NFD folding.
        Assert.Equal("\"pg_catalog\".\"text_ops\"|options[1;3: a ]", OperatorClassOf([" a "]));
        Assert.NotEqual(OperatorClassOf(["é"]), OperatorClassOf(["é"]));
    }

    [Fact]
    public void ANullOptionElement_IsRejected() =>
        Rejects(KeyRow(operatorClassOptions: ["ok", null!]));

    // --- Ordering (GC-DHI-04E §16) --------------------------------------------------------------

    [Theory]
    [InlineData(true, false, true, false, IndexSortDirection.Ascending, IndexNullsOrdering.First)]
    [InlineData(true, false, false, true, IndexSortDirection.Ascending, IndexNullsOrdering.Last)]
    [InlineData(false, true, true, false, IndexSortDirection.Descending, IndexNullsOrdering.First)]
    [InlineData(false, true, false, true, IndexSortDirection.Descending, IndexNullsOrdering.Last)]
    public void AnOrderableKey_MapsDirectly(
        bool ascending, bool descending, bool nullsFirst, bool nullsLast,
        IndexSortDirection expectedDirection, IndexNullsOrdering expectedNulls)
    {
        IndexKeyPartSnapshot part = Assert.Single(Map(KeyRow(
            isOrderable: true, isAscending: ascending, isDescending: descending,
            nullsFirst: nullsFirst, nullsLast: nullsLast)).KeyParts);

        Assert.Equal(expectedDirection, part.SortDirection);
        Assert.Equal(expectedNulls, part.NullsOrdering);
    }

    [Fact]
    public void ANonOrderableKey_NormalizesToAscendingNullsLast()
    {
        // The single admitted non-orderable shape: five falses. Hash, GIN, BRIN and friends.
        IndexKeyPartSnapshot part = Assert.Single(Map(KeyRow(
            accessMethod: "hash", operatorClassName: "int4_ops",
            isOrderable: false, isAscending: false, isDescending: false,
            nullsFirst: false, nullsLast: false)).KeyParts);

        Assert.Equal(IndexSortDirection.Ascending, part.SortDirection);
        Assert.Equal(IndexNullsOrdering.Last, part.NullsOrdering);
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, true)]
    public void ANonOrderableKeyWithAnyOtherFlagSet_IsRejected(
        bool ascending, bool descending, bool nullsFirst, bool nullsLast) =>
        Rejects(KeyRow(
            isOrderable: false, isAscending: ascending, isDescending: descending,
            nullsFirst: nullsFirst, nullsLast: nullsLast));

    [Fact]
    public void AnOrderableKeyClaimingBothDirections_IsRejected() =>
        Rejects(KeyRow(isOrderable: true, isAscending: true, isDescending: true, nullsFirst: false, nullsLast: true));

    [Fact]
    public void AnOrderableKeyClaimingNeitherDirection_IsRejected() =>
        Rejects(KeyRow(isOrderable: true, isAscending: false, isDescending: false, nullsFirst: false, nullsLast: true));

    [Fact]
    public void AnOrderableKeyClaimingBothNullsPlacements_IsRejected() =>
        Rejects(KeyRow(isOrderable: true, isAscending: true, isDescending: false, nullsFirst: true, nullsLast: true));

    [Fact]
    public void AnOrderableKeyClaimingNeitherNullsPlacement_IsRejected() =>
        Rejects(KeyRow(isOrderable: true, isAscending: true, isDescending: false, nullsFirst: false, nullsLast: false));

    [Theory]
    [InlineData("orderable")]
    [InlineData("ascending")]
    [InlineData("descending")]
    [InlineData("nullsFirst")]
    [InlineData("nullsLast")]
    public void ANullOrderingPropertyOnAKey_IsRejectedAndNeverBecomesAToken(string missing)
    {
        PostgreSqlIndexMetadataRow row = missing switch
        {
            "orderable" => KeyRow(isOrderable: null),
            "ascending" => KeyRow(isAscending: null),
            "descending" => KeyRow(isDescending: null),
            "nullsFirst" => KeyRow(nullsFirst: null),
            _ => KeyRow(nullsLast: null),
        };

        Rejects(row);
    }

    // --- Remaining index-wide contracts ---------------------------------------------------------

    [Fact]
    public void APartialPredicate_IsCarriedVerbatim() =>
        Assert.Equal("(c > 5)", Map(KeyRow(partialPredicate: "(c > 5)")).PartialPredicate);

    [Fact]
    public void ANonPartialIndex_HasNoPredicate() => Assert.Null(Map(KeyRow()).PartialPredicate);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankPredicate_IsRejected(string blank) => Rejects(KeyRow(partialPredicate: blank));

    [Fact]
    public void ANonUniqueIndex_ReportsNoNullsNotDistinct()
    {
        IndexSnapshot snapshot = Map(KeyRow(isUnique: false, nullsNotDistinct: null));

        Assert.False(snapshot.IsUnique);
        Assert.Null(snapshot.NullsNotDistinct);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AUniqueIndex_CarriesTheServerBoolean(bool nullsNotDistinct)
    {
        IndexSnapshot snapshot = Map(KeyRow(isUnique: true, nullsNotDistinct: nullsNotDistinct));

        Assert.True(snapshot.IsUnique);
        Assert.Equal(nullsNotDistinct, snapshot.NullsNotDistinct);
    }

    [Fact]
    public void ANonUniqueIndexClaimingNullsNotDistinct_IsRejected() =>
        Rejects(KeyRow(isUnique: false, nullsNotDistinct: false));

    [Fact]
    public void AUniqueIndexWithoutNullsNotDistinct_IsRejected() =>
        Rejects(KeyRow(isUnique: true, nullsNotDistinct: null));

    [Fact]
    public void APrimaryKey_IsUniqueAndBacksAConstraint()
    {
        IndexSnapshot snapshot = Map(KeyRow(
            isUnique: true, nullsNotDistinct: false, isPrimaryKey: true, backsConstraint: true));

        Assert.True(snapshot.IsPrimaryKey);
        Assert.True(snapshot.BacksConstraint);
    }

    [Fact]
    public void APrimaryKeyThatIsNotUnique_IsRejected() =>
        Rejects(KeyRow(isUnique: false, nullsNotDistinct: null, isPrimaryKey: true, backsConstraint: true));

    [Fact]
    public void APrimaryKeyThatBacksNoConstraint_IsRejected() =>
        Rejects(KeyRow(isUnique: true, nullsNotDistinct: false, isPrimaryKey: true, backsConstraint: false));

    [Fact]
    public void ABacksConstraintIndexNeedNotBeAPrimaryKey()
    {
        // Unique and exclusion constraints also back an index without being the primary key.
        IndexSnapshot snapshot = Map(KeyRow(
            isUnique: true, nullsNotDistinct: false, isPrimaryKey: false, backsConstraint: true));

        Assert.False(snapshot.IsPrimaryKey);
        Assert.True(snapshot.BacksConstraint);
    }

    /// <summary>
    /// Validity, readiness and liveness are independent server facts: all eight combinations are
    /// carried through unchanged and none is derived from another. Exhaustive coverage replaces a
    /// fabricated catalog race, which the gate explicitly forbids.
    /// </summary>
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void ValidReadyAndLive_ArePreservedIndependently(bool isValid, bool isReady, bool isLive)
    {
        IndexSnapshot snapshot = Map(KeyRow(isValid: isValid, isReady: isReady, isLive: isLive));

        Assert.Equal(isValid, snapshot.IsValid);
        Assert.Equal(isReady, snapshot.IsReady);
        Assert.Equal(isLive, snapshot.IsLive);
    }

    [Fact]
    public void APhysicalIndex_CarriesItsSize() =>
        Assert.Equal(24576, Map(KeyRow(indexRelationKind: "i", sizeBytes: 24576)).SizeBytes);

    [Fact]
    public void APhysicalIndexPartition_CarriesItsOwnSize()
    {
        IndexSnapshot snapshot = Map(KeyRow(indexRelationKind: "i", isIndexPartition: true, sizeBytes: 8192));

        Assert.Equal(8192, snapshot.SizeBytes);
    }

    [Fact]
    public void AVirtualPartitionedRoot_ReportsZeroSize() =>
        Assert.Equal(0, Map(KeyRow(indexRelationKind: "I", sizeBytes: 0)).SizeBytes);

    [Fact]
    public void AVirtualIndexClaimingStorage_IsRejected() =>
        Rejects(KeyRow(indexRelationKind: "I", sizeBytes: 8192));

    [Fact]
    public void ANegativeSize_IsRejected() => Rejects(KeyRow(sizeBytes: -1));

    [Fact]
    public void AScanCount_IsCarriedThroughWhenSupplied() =>
        Assert.Equal(42, PostgreSqlIndexSnapshotMapper.Map([KeyRow()], 42).ScanCount);

    [Fact]
    public void AnAbsentScanCount_StaysNullAndIsNotTurnedIntoZero() =>
        Assert.Null(PostgreSqlIndexSnapshotMapper.Map([KeyRow()], null).ScanCount);

    // --- Leakage ---------------------------------------------------------------------------------

    [Fact]
    public void AMappingRejection_NamesNothingItReceived()
    {
        const string marker = "sensitive-marker-04e";

        PostgreSqlIndexSnapshotMappingException exception = Rejects(KeyRow(
            schemaName: marker + "-schema",
            tableName: marker + "-table",
            indexName: marker + "-index",
            expression: marker + "-expression",
            partialPredicate: marker + "-predicate",
            collationSchema: marker + "-collation-schema",
            collationName: marker + "-collation-name",
            operatorClassSchema: marker + "-opclass-schema",
            operatorClassName: marker + "-opclass-name",
            operatorClassOptions: [marker + "-option"],
            // The actual defect: a key with both a column and an expression.
            columnName: "a"));

        Assert.Equal(MappingMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        foreach (string surface in new[]
                 {
                     exception.Message,
                     exception.ToString(),
                     exception.StackTrace ?? string.Empty,
                 })
        {
            Assert.False(surface.Contains(marker, StringComparison.Ordinal), LeakMessage);
        }
    }

    [Fact]
    public void EveryRejectionLooksTheSame()
    {
        string[] messages =
        [
            Rejects(KeyRow(indexRelationKind: "x")).Message,
            Rejects(KeyRow(sizeBytes: -1)).Message,
            Rejects(KeyRow(isOrderable: null)).Message,
            Rejects(KeyRow(operatorClassSchema: null)).Message,
            Rejects(KeyRow(collationSchema: "pg_catalog", collationName: null)).Message,
            Rejects(KeyRow(operatorClassOptions: [null!])).Message,
        ];

        Assert.All(messages, message => Assert.Equal(MappingMessage, message));
    }

    // --- R1-03: SQL NULL is not the same fact as a present-but-blank string ---------------------

    /// <summary>The present-but-blank strings a broken row can carry.</summary>
    public static TheoryData<string> BlankValues() => new() { "", " ", "\t", "\r\n", "   " };

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void ASimpleKeyWithABlankExpression_IsRejected(string blank)
    {
        // ColumnName is populated, so this is a simple key -- and a simple key's Expression must be
        // SQL NULL. A blank string is a value the server supplied, not an absence: reading it as
        // "no expression" would silently accept a row that contradicts itself.
        Rejects(KeyRow(columnName: "a", expression: blank));
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AnExpressionKeyWithABlankColumnName_IsRejected(string blank)
    {
        Rejects(KeyRow(columnName: blank, expression: "(lower(a))"));
    }

    // Both halves SQL NULL is covered by AKeyWithNeitherColumnNorExpression_IsRejected above.

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AKeyWithBothBlank_IsRejected(string blank)
    {
        Rejects(KeyRow(columnName: blank, expression: blank));
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void ABlankCollationSchemaWithANullName_IsRejected(string blank)
    {
        // Neither absent (that is both halves NULL) nor present. Folding blank into absence here
        // would silently drop a collation the server did report.
        Rejects(KeyRow(collationSchema: blank, collationName: null));
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void ANullCollationSchemaWithABlankName_IsRejected(string blank) =>
        Rejects(KeyRow(collationSchema: null, collationName: blank));

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void ABlankCollationSchemaWithAValidName_IsRejected(string blank) =>
        Rejects(KeyRow(collationSchema: blank, collationName: "en_US"));

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AValidCollationSchemaWithABlankName_IsRejected(string blank) =>
        Rejects(KeyRow(collationSchema: "pg_catalog", collationName: blank));

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void BothCollationHalvesBlank_IsRejected(string blank) =>
        Rejects(KeyRow(collationSchema: blank, collationName: blank));

    [Fact]
    public void BothCollationHalvesNull_MeansNoCollation()
    {
        // The only valid absence.
        IndexSnapshot snapshot = Map(KeyRow(collationSchema: null, collationName: null));

        Assert.Null(Assert.Single(snapshot.KeyParts).Collation);
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void ABlankOperatorClassSchema_IsRejected(string blank) =>
        Rejects(KeyRow(operatorClassSchema: blank, operatorClassName: "text_ops"));

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void ABlankOperatorClassName_IsRejected(string blank) =>
        Rejects(KeyRow(operatorClassSchema: "pg_catalog", operatorClassName: blank));

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void ABlankPartialPredicate_IsRejected(string blank)
    {
        // Non-null means the index is partial, so the predicate must be usable; a blank one is not
        // a second way of saying "not partial".
        Rejects(KeyRow(partialPredicate: blank));
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void ABlankRequiredIdentifier_IsRejected(string blank)
    {
        Rejects(KeyRow(schemaName: blank));
        Rejects(KeyRow(tableName: blank));
        Rejects(KeyRow(indexName: blank));
        Rejects(KeyRow(accessMethod: blank));
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AnIncludeColumnWithABlankName_IsRejected(string blank) =>
        Rejects(
            KeyRow(attributeCount: 2, keyAttributeCount: 1),
            IncludeRow(attributeCount: 2, keyAttributeCount: 1, attributePosition: 2, columnName: blank));

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void AnIncludeColumnWithBlankKeyOnlyMetadata_IsRejected(string blank)
    {
        // A blank string is a populated field: it must fail exactly as a real value would, because
        // an INCLUDE attribute carries none of this metadata at all.
        Rejects(
            KeyRow(attributeCount: 2, keyAttributeCount: 1),
            IncludeRow(2, 1, 2, "b", expression: blank));

        Rejects(
            KeyRow(attributeCount: 2, keyAttributeCount: 1),
            IncludeRow(2, 1, 2, "b", collationSchema: blank, collationName: blank));

        Rejects(
            KeyRow(attributeCount: 2, keyAttributeCount: 1),
            IncludeRow(2, 1, 2, "b", operatorClassSchema: blank, operatorClassName: blank));
    }

    [Theory]
    [MemberData(nameof(BlankValues))]
    public void ABlankValueIsRejectedAtAnyRowPosition(string blank)
    {
        // First, middle and last attribute of a three-key index.
        Rejects(
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 1, columnName: "a", expression: blank),
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 2, columnName: "b"),
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 3, columnName: "c"));

        Rejects(
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 1, columnName: "a"),
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 2, columnName: "b", expression: blank),
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 3, columnName: "c"));

        Rejects(
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 1, columnName: "a"),
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 2, columnName: "b"),
            KeyRow(attributeCount: 3, keyAttributeCount: 3, attributePosition: 3, columnName: "c", expression: blank));
    }

    [Fact]
    public void ABlankValueIsNeverTrimmedIntoAValidOne()
    {
        // Whitespace is detected, never removed: a value that is legitimate keeps exactly the bytes
        // the server sent, including surrounding whitespace.
        IndexSnapshot snapshot = Map(KeyRow(columnName: "  spaced  "));

        Assert.Equal("  spaced  ", Assert.Single(snapshot.KeyParts).ColumnName);
    }

    [Fact]
    public void EveryBlankRejectionLooksLikeEveryOther()
    {
        string[] messages =
        [
            Rejects(KeyRow(columnName: "a", expression: " ")).Message,
            Rejects(KeyRow(columnName: "", expression: "(lower(a))")).Message,
            Rejects(KeyRow(collationSchema: " ", collationName: null)).Message,
            Rejects(KeyRow(operatorClassName: "\t")).Message,
            Rejects(KeyRow(partialPredicate: "\r\n")).Message,
        ];

        Assert.All(messages, message => Assert.Equal(MappingMessage, message));
    }

    [Fact]
    public void TheExceptionHasNoMessageOrInnerConstructor()
    {
        System.Reflection.ConstructorInfo[] constructors = typeof(PostgreSqlIndexSnapshotMappingException)
            .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        System.Reflection.ConstructorInfo only = Assert.Single(constructors);
        Assert.Empty(only.GetParameters());
    }
}
