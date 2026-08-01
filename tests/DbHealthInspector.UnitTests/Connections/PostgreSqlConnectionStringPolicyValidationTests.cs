using DbHealthInspector.PostgreSql.Connections;
using Npgsql;

namespace DbHealthInspector.UnitTests.Connections;

public sealed class PostgreSqlConnectionStringPolicyValidationTests
{
    [Fact]
    public void ParseAndNormalize_ThrowsArgumentNullException_WhenConnectionStringIsNull()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize(null!));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void ParseAndNormalize_ThrowsArgumentException_WhenConnectionStringIsEmpty()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize(string.Empty));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void ParseAndNormalize_ThrowsArgumentException_WhenConnectionStringIsWhitespaceOnly()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("   "));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void ParseAndNormalize_DoesNotSilentlyTrimAWhitespaceOnlyValueIntoSomethingElse()
    {
        // A whitespace-only input must be rejected outright rather than trimmed to empty and
        // then treated as "no connection string supplied but otherwise fine".
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("\t \n"));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void ParseAndNormalize_ThrowsArgumentException_WhenSyntaxIsMalformed()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Password='unterminated"));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("connectionString", exception.ParamName);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ParseAndNormalize_ThrowsArgumentException_WhenAKeyValueHasAnInvalidType()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Port=not-a-number"));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("connectionString", exception.ParamName);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ParseAndNormalize_ThrowsArgumentException_ForInputWithNoRecognizableKeyValuePairs()
    {
        // Npgsql surfaces this particular shape of malformed input as a KeyNotFoundException
        // rather than an ArgumentException; the policy must still translate it into the fixed,
        // sanitized ArgumentException rather than letting it propagate as-is.
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("this-is-not-a-valid-connection-string;;;==="));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("connectionString", exception.ParamName);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void ParseAndNormalize_DoesNotIncludeTheOriginalValueInTheExceptionMessage()
    {
        const string connectionString = "Host=localhost;Port=not-a-number;SECRET-MARKER-XYZ=1";

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize(connectionString));

        Assert.DoesNotContain("SECRET-MARKER-XYZ", exception.Message);
        Assert.DoesNotContain("not-a-number", exception.Message);
    }

    [Fact]
    public void ParseAndNormalize_DoesNotCopyDataFromTheOriginalParsingFailure()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost;Port=not-a-number"));

        Assert.Empty(exception.Data);
    }

    [Fact]
    public void ParseAndNormalize_Succeeds_ForAnOrdinaryValidConnectionString()
    {
        NpgsqlConnectionStringBuilder builder = PostgreSqlConnectionStringPolicy.ParseAndNormalize(
            "Host=localhost;Port=5432;Database=testdb;Username=testuser;Password=testpass");

        Assert.Equal("localhost", builder.Host);
    }
}
