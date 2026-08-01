using DbHealthInspector.PostgreSql.Connections;
using Npgsql;

namespace DbHealthInspector.UnitTests.Connections;

/// <summary>
/// Full <c>Options</c> matrix (GC-DHI-04A-C1 §11): absent, explicitly empty (unquoted and
/// quoted), quoted whitespace-only, a session parameter, keyword casing, and Npgsql's real
/// last-wins semantics for a repeated key.
/// </summary>
public sealed class PostgreSqlConnectionStringPolicyOptionsTests
{
    [Fact]
    public void ParseAndNormalize_Allows_WhenOptionsIsAbsent()
    {
        NpgsqlConnectionStringBuilder builder = PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost");

        Assert.True(string.IsNullOrEmpty(builder.Options));
    }

    [Fact]
    public void ParseAndNormalize_Allows_WhenOptionsIsExplicitlyEmptyUnquoted()
    {
        NpgsqlConnectionStringBuilder builder = PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Options=");

        Assert.True(string.IsNullOrEmpty(builder.Options));
    }

    [Fact]
    public void ParseAndNormalize_Allows_WhenOptionsIsExplicitlyEmptyQuoted()
    {
        NpgsqlConnectionStringBuilder builder = PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Options=''");

        Assert.True(string.IsNullOrEmpty(builder.Options));
    }

    [Fact]
    public void ParseAndNormalize_Rejects_WhenOptionsIsWhitespaceOnlySingleQuoted()
    {
        // A quoted whitespace-only value survives Npgsql's own parsing (it is not empty), so the
        // policy's own !IsNullOrEmpty(Options) check is what rejects it here.
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Options='   '"));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void ParseAndNormalize_Rejects_WhenOptionsIsWhitespaceOnlyDoubleQuoted()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Options=\" \""));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void ParseAndNormalize_Rejects_WhenOptionsCarriesASessionParameter()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Options=-c statement_timeout=5000"));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("connectionString", exception.ParamName);
    }

    [Theory]
    [InlineData("options")]
    [InlineData("OPTIONS")]
    [InlineData("OpTiOnS")]
    public void ParseAndNormalize_Rejects_RegardlessOfKeywordCasing(string keyword)
    {
        // Npgsql 10.0.3 defines no distinct synonym for "Options" (confirmed directly against
        // NpgsqlConnectionStringPropertyAttribute.Synonyms — empty for this property); its
        // keyword matching is case-insensitive instead, so casing is what this covers.
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize($"Host=localhost;{keyword}=-c some_setting=value"));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAndNormalize_Allows_WhenARepeatedOptionsKeyEndsUpEmpty()
    {
        // Npgsql applies last-wins semantics for a repeated key: confirmed directly that
        // "Options=-c foo=1;Options=" parses to Options == "".
        NpgsqlConnectionStringBuilder builder = PostgreSqlConnectionStringPolicy.ParseAndNormalize(
            "Host=localhost;Options=-c some_setting=value;Options=");

        Assert.True(string.IsNullOrEmpty(builder.Options));
    }

    [Fact]
    public void ParseAndNormalize_Rejects_WhenARepeatedOptionsKeyEndsUpNonEmpty()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Options=;Options=-c some_setting=value"));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ParseAndNormalize_RejectedOptions_NeverAppearsInTheExceptionMessage()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Options=-c SECRET-OPTIONS-MARKER=1"));

        Assert.DoesNotContain("SECRET-OPTIONS-MARKER", exception.Message);
    }

    [Fact]
    public void ParseAndNormalize_RejectedOptions_RetainsNoInnerExceptionOrData()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Options=-c some_setting=value"));

        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }
}
