using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// The <see cref="PostgreSqlSqlParameterType.TextArray"/> payload (GC-DHI-04D §5): ordered,
/// non-null, defensively copied and closed by construction.
/// </summary>
public sealed class PostgreSqlSqlTextArrayParameterTests
{
    [Fact]
    public void OnlyTwoParameterTypesExist()
    {
        Assert.Equal(
            [PostgreSqlSqlParameterType.Int32, PostgreSqlSqlParameterType.TextArray],
            Enum.GetValues<PostgreSqlSqlParameterType>());
    }

    [Fact]
    public void ElementOrderIsPreservedExactly()
    {
        // The value carries what it was given; sorting is the schema filter's job, not this one's.
        PostgreSqlSqlParameterValue value = PostgreSqlSqlParameterValue.TextArray(1, ["zebra", "apple", "Mango"]);

        Assert.Equal(["zebra", "apple", "Mango"], value.TextArrayValue);
    }

    [Fact]
    public void AnEmptyArrayIsValid()
    {
        PostgreSqlSqlParameterValue value = PostgreSqlSqlParameterValue.TextArray(2, []);

        Assert.Empty(value.TextArrayValue);
        Assert.Equal(PostgreSqlSqlParameterType.TextArray, value.Type);
        Assert.Equal(2, value.Position);
    }

    [Fact]
    public void MutatingTheCallerArrayCannotChangeTheBoundValue()
    {
        string[] source = ["public", "sales"];

        PostgreSqlSqlParameterValue value = PostgreSqlSqlParameterValue.TextArray(1, source);
        source[0] = "hijacked";

        Assert.Equal(["public", "sales"], value.TextArrayValue);
    }

    [Fact]
    public void TheExposedCollectionIsReadOnly()
    {
        PostgreSqlSqlParameterValue value = PostgreSqlSqlParameterValue.TextArray(1, ["public"]);

        Assert.True(((IList<string>)value.TextArrayValue).IsReadOnly);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)value.TextArrayValue).Add("extra"));
    }

    [Fact]
    public void ANullSequenceIsRejected() =>
        Assert.Throws<ArgumentNullException>(() => PostgreSqlSqlParameterValue.TextArray(1, null!));

    [Fact]
    public void ANullElementIsRejected() =>
        Assert.Throws<ArgumentException>(() => PostgreSqlSqlParameterValue.TextArray(1, ["public", null!]));

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void APositionBelowOneIsRejected(int position) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => PostgreSqlSqlParameterValue.TextArray(position, []));

    [Fact]
    public void ReadingTheTextPayloadOfAnInt32ValueIsRefused()
    {
        // The payload is closed by construction: an Int32 value has no text array to hand out, and
        // there is no object-typed accessor that would blur the two.
        PostgreSqlSqlParameterValue value = PostgreSqlSqlParameterValue.Int32(1, 42);

        Assert.Throws<InvalidOperationException>(() => value.TextArrayValue);
    }

    [Fact]
    public void EqualityComparesElementsOrdinallyAndInOrder()
    {
        PostgreSqlSqlParameterValue first = PostgreSqlSqlParameterValue.TextArray(1, ["a", "b"]);
        PostgreSqlSqlParameterValue same = PostgreSqlSqlParameterValue.TextArray(1, ["a", "b"]);
        PostgreSqlSqlParameterValue reordered = PostgreSqlSqlParameterValue.TextArray(1, ["b", "a"]);
        PostgreSqlSqlParameterValue differentCase = PostgreSqlSqlParameterValue.TextArray(1, ["A", "b"]);
        PostgreSqlSqlParameterValue otherPosition = PostgreSqlSqlParameterValue.TextArray(2, ["a", "b"]);

        Assert.Equal(first, same);
        Assert.Equal(first.GetHashCode(), same.GetHashCode());
        Assert.NotEqual(first, reordered);
        Assert.NotEqual(first, differentCase);
        Assert.NotEqual(first, otherPosition);
    }

    [Fact]
    public void ATextArrayIsNeverEqualToAnInt32Value()
    {
        Assert.NotEqual(
            PostgreSqlSqlParameterValue.TextArray(1, []),
            PostgreSqlSqlParameterValue.Int32(1, 0));
    }

    [Fact]
    public void NoObjectOrDynamicPayloadIsExposed()
    {
        // Only the two closed accessors exist; nothing returns object, and no caller-supplied
        // parameter name can be attached.
        System.Reflection.PropertyInfo[] properties = typeof(PostgreSqlSqlParameterValue).GetProperties(
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic);

        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(object));
        Assert.DoesNotContain(properties, property => property.Name.Contains("Name", StringComparison.Ordinal));
        Assert.Equal(
            ["Int32Value", "Position", "TextArrayValue", "Type"],
            properties.Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray());
    }
}
