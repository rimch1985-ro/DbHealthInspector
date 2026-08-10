using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.UnitTests.Tables;

/// <summary>
/// The schema filter's contract (GC-DHI-04D §5): exact names only, ordinal and case-sensitive,
/// defensively copied, ordinally sorted, and fail-closed on anything ambiguous.
/// </summary>
public sealed class PostgreSqlSchemaFilterTests
{
    private const string LeakMessage = "Sensitive data was exposed.";

    private static PostgreSqlSchemaFilter Filter(IReadOnlyList<string> included, IReadOnlyList<string> excluded) =>
        new(included, excluded);

    // --- Accepted shapes ------------------------------------------------------------------------

    [Fact]
    public void EmptyAndEmpty_IsValidAndRestrictsNothing()
    {
        PostgreSqlSchemaFilter filter = Filter([], []);

        Assert.Empty(filter.IncludedSchemas);
        Assert.Empty(filter.ExcludedSchemas);
    }

    [Fact]
    public void IncludeEverything_IsTheEmptyFilter()
    {
        Assert.Empty(PostgreSqlSchemaFilter.IncludeEverything.IncludedSchemas);
        Assert.Empty(PostgreSqlSchemaFilter.IncludeEverything.ExcludedSchemas);
    }

    [Fact]
    public void IncludeOnly_IsValid()
    {
        PostgreSqlSchemaFilter filter = Filter(["sales", "public"], []);

        Assert.Equal(["public", "sales"], filter.IncludedSchemas);
        Assert.Empty(filter.ExcludedSchemas);
    }

    [Fact]
    public void ExcludeOnly_IsValid()
    {
        PostgreSqlSchemaFilter filter = Filter([], ["staging", "archive"]);

        Assert.Empty(filter.IncludedSchemas);
        Assert.Equal(["archive", "staging"], filter.ExcludedSchemas);
    }

    [Fact]
    public void BothLists_MayBePopulatedWhenTheyDoNotOverlap()
    {
        PostgreSqlSchemaFilter filter = Filter(["public"], ["staging"]);

        Assert.Equal(["public"], filter.IncludedSchemas);
        Assert.Equal(["staging"], filter.ExcludedSchemas);
    }

    [Fact]
    public void NamesDifferingOnlyByCase_AreDistinctSchemas()
    {
        // PostgreSQL treats "Public" and "public" as different schemas, so rejecting these as
        // duplicates would be wrong.
        PostgreSqlSchemaFilter filter = Filter(["Public", "public", "PUBLIC"], []);

        Assert.Equal(3, filter.IncludedSchemas.Count);
    }

    [Fact]
    public void ACaseDifferenceAcrossLists_IsNotAnOverlap()
    {
        PostgreSqlSchemaFilter filter = Filter(["public"], ["Public"]);

        Assert.Equal(["public"], filter.IncludedSchemas);
        Assert.Equal(["Public"], filter.ExcludedSchemas);
    }

    [Fact]
    public void ASystemSchemaNameIsAccepted_ButCannotReEnableAnything()
    {
        // The filter does not police system schemas; D001's frozen WHERE clause does, so naming
        // one here simply matches nothing.
        PostgreSqlSchemaFilter filter = Filter(["pg_catalog"], []);

        Assert.Equal(["pg_catalog"], filter.IncludedSchemas);
    }

    // --- Ordering and defensive copying ----------------------------------------------------------

    [Fact]
    public void NamesAreSortedOrdinally()
    {
        // Ordinal puts every uppercase letter before every lowercase one; a culture-aware sort
        // would not.
        PostgreSqlSchemaFilter filter = Filter(["b", "A", "a", "B"], []);

        Assert.Equal(["A", "B", "a", "b"], filter.IncludedSchemas);
    }

    [Fact]
    public void MutatingTheCallerArray_CannotChangeTheFilter()
    {
        string[] included = ["public"];
        string[] excluded = ["staging"];

        PostgreSqlSchemaFilter filter = Filter(included, excluded);

        included[0] = "hijacked";
        excluded[0] = "hijacked";

        Assert.Equal(["public"], filter.IncludedSchemas);
        Assert.Equal(["staging"], filter.ExcludedSchemas);
    }

    [Fact]
    public void TheExposedCollectionsAreReadOnly()
    {
        PostgreSqlSchemaFilter filter = Filter(["public"], ["staging"]);

        Assert.True(((IList<string>)filter.IncludedSchemas).IsReadOnly);
        Assert.True(((IList<string>)filter.ExcludedSchemas).IsReadOnly);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)filter.IncludedSchemas).Add("extra"));
    }

    [Fact]
    public void TwoFiltersBuiltFromTheSameNamesInAnyOrder_BindTheSameArrays()
    {
        PostgreSqlSchemaFilter first = Filter(["b", "a"], ["d", "c"]);
        PostgreSqlSchemaFilter second = Filter(["a", "b"], ["c", "d"]);

        Assert.Equal(first.IncludedSchemas, second.IncludedSchemas);
        Assert.Equal(first.ExcludedSchemas, second.ExcludedSchemas);
    }

    // --- Rejected shapes ---------------------------------------------------------------------------

    [Fact]
    public void ANullCollection_IsRejected()
    {
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter(null!, []));
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter([], null!));
    }

    [Fact]
    public void ANullName_IsRejected()
    {
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter([null!], []));
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter([], [null!]));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    [InlineData("\n")]
    [InlineData("   ")]
    public void AnEmptyOrWhitespaceName_IsRejected(string name)
    {
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter([name], []));
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter([], [name]));
    }

    [Theory]
    [InlineData("\0")]
    [InlineData("pub\0lic")]
    [InlineData("public\0")]
    public void ANameContainingNul_IsRejected(string name)
    {
        // A NUL can truncate an identifier inside the driver or the server.
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter([name], []));
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter([], [name]));
    }

    [Fact]
    public void ADuplicateWithinEitherList_IsRejected()
    {
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter(["public", "public"], []));
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter([], ["staging", "staging"]));
    }

    [Fact]
    public void TheSameNameInBothLists_IsRejected()
    {
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter(["public"], ["public"]));
        Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter(["a", "b", "c"], ["z", "b"]));
    }

    // --- The rejection says nothing --------------------------------------------------------------

    [Fact]
    public void ARejection_NamesNeitherTheSchemaNorTheReason()
    {
        const string marker = "marker-schema-04d";

        PostgreSqlSchemaFilterException exception = Assert.Throws<PostgreSqlSchemaFilterException>(
            () => Filter([marker, marker], []));

        Assert.Equal("The PostgreSQL schema filter is invalid.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        foreach (string surface in new[] { exception.Message, exception.ToString(), exception.StackTrace ?? string.Empty })
        {
            bool leaked = surface.Contains(marker, StringComparison.Ordinal);
            Assert.False(leaked, LeakMessage);
        }
    }

    [Fact]
    public void EveryRejectionLooksTheSame()
    {
        // A caller must not be able to tell *why* a filter was refused: overlap, duplicate, NUL
        // and whitespace all produce the same exception with the same message.
        string[] messages =
        [
            Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter(["a"], ["a"])).Message,
            Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter(["a", "a"], [])).Message,
            Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter(["a\0b"], [])).Message,
            Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter([" "], [])).Message,
            Assert.Throws<PostgreSqlSchemaFilterException>(() => Filter(null!, [])).Message,
        ];

        Assert.All(messages, message => Assert.Equal("The PostgreSQL schema filter is invalid.", message));
    }

    [Fact]
    public void TheExceptionHasNoMessageOrInnerConstructor()
    {
        System.Reflection.ConstructorInfo[] constructors = typeof(PostgreSqlSchemaFilterException)
            .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        System.Reflection.ConstructorInfo only = Assert.Single(constructors);
        Assert.Empty(only.GetParameters());
    }
}
