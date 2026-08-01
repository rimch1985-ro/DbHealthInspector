using DbHealthInspector.PostgreSql.Connections;
using Npgsql;

namespace DbHealthInspector.UnitTests.Connections;

public sealed class PostgreSqlConnectionStringPolicyNormalizationTests
{
    private static string MaliciousConnectionString() =>
        new NpgsqlConnectionStringBuilder("Host=localhost;Port=5432;Database=testdb;Username=testuser;Password=testpass")
        {
            PersistSecurityInfo = true,
            IncludeErrorDetail = true,
            LogParameters = true,
            IncludeFailedBatchedCommand = true,
            NoResetOnClose = true,
            Enlist = true,
            Multiplexing = true,
            ApplicationName = "CallerSuppliedAppName",
        }.ConnectionString;

    [Fact]
    public void ParseAndNormalize_ForcesPersistSecurityInfoToFalse()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(MaliciousConnectionString());

        Assert.False(normalized.PersistSecurityInfo);
    }

    [Fact]
    public void ParseAndNormalize_ForcesIncludeErrorDetailToFalse()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(MaliciousConnectionString());

        Assert.False(normalized.IncludeErrorDetail);
    }

    [Fact]
    public void ParseAndNormalize_ForcesLogParametersToFalse()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(MaliciousConnectionString());

        Assert.False(normalized.LogParameters);
    }

    [Fact]
    public void ParseAndNormalize_ForcesIncludeFailedBatchedCommandToFalse()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(MaliciousConnectionString());

        Assert.False(normalized.IncludeFailedBatchedCommand);
    }

    [Fact]
    public void ParseAndNormalize_ForcesNoResetOnCloseToFalse()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(MaliciousConnectionString());

        Assert.False(normalized.NoResetOnClose);
    }

    [Fact]
    public void ParseAndNormalize_ForcesEnlistToFalse()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(MaliciousConnectionString());

        Assert.False(normalized.Enlist);
    }

    [Fact]
    public void ParseAndNormalize_ForcesMultiplexingToFalse()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(MaliciousConnectionString());

        Assert.False(normalized.Multiplexing);
    }

    [Fact]
    public void ParseAndNormalize_OverridesTheApplicationNameRegardlessOfCallerInput()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(MaliciousConnectionString());

        Assert.Equal("DbHealthInspector", normalized.ApplicationName);
    }

    [Fact]
    public void ParseAndNormalize_SetsTheApplicationNameEvenWhenTheCallerSuppliedNone()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize("Host=localhost");

        Assert.Equal("DbHealthInspector", normalized.ApplicationName);
    }

    [Fact]
    public void ParseAndNormalize_DoesNotAlterUnrelatedSettings()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(
            "Host=db.example.com;Port=6543;Database=testdb;Username=testuser;Password=testpass;Pooling=false;Timeout=45");

        Assert.Equal("db.example.com", normalized.Host);
        Assert.Equal(6543, normalized.Port);
        Assert.Equal("testdb", normalized.Database);
        Assert.False(normalized.Pooling);
        Assert.Equal(45, normalized.Timeout);
    }

    // --- All 8 settings + ApplicationName in a single literal connection string (GC-DHI-04A-C1 §12) ---

    [Fact]
    public void ParseAndNormalize_AllEightDangerousSettingsForcedSafe_WhenEnabledTogetherByLiteralKeyword()
    {
        const string connectionString =
            "Host=localhost;" +
            "Persist Security Info=true;" +
            "Include Error Detail=true;" +
            "Log Parameters=true;" +
            "Include Failed Batched Command=true;" +
            "No Reset On Close=true;" +
            "Enlist=true;" +
            "Multiplexing=true;" +
            "Application Name=synthetic-sensitive-application";

        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(connectionString);

        Assert.False(normalized.PersistSecurityInfo);
        Assert.False(normalized.IncludeErrorDetail);
        Assert.False(normalized.LogParameters);
        Assert.False(normalized.IncludeFailedBatchedCommand);
        Assert.False(normalized.NoResetOnClose);
        Assert.False(normalized.Enlist);
        Assert.False(normalized.Multiplexing);
        Assert.Equal("DbHealthInspector", normalized.ApplicationName);
        Assert.DoesNotContain("synthetic-sensitive-application", normalized.ConnectionString);
    }

    // --- Aliases and casing (GC-DHI-04A-C1 §12) --------------------------------------------
    // Npgsql 10.0.3 defines no distinct keyword synonym beyond the property's own display name
    // (confirmed directly against NpgsqlConnectionStringPropertyAttribute.Synonyms — empty for
    // every setting in this gate's normalization list); what it does accept is a
    // case-insensitive, space-insensitive form of that one name (e.g. "PersistSecurityInfo" or
    // "persistsecurityinfo" for "Persist Security Info"). No parser of our own is introduced —
    // these all go through the real NpgsqlConnectionStringBuilder.

    [Theory]
    [InlineData("PersistSecurityInfo")]
    [InlineData("persistsecurityinfo")]
    public void ParseAndNormalize_ForcesPersistSecurityInfoToFalse_RegardlessOfKeywordCasingOrSpacing(string keyword)
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(
            $"Host=localhost;{keyword}=true");

        Assert.False(normalized.PersistSecurityInfo);
    }

    [Theory]
    [InlineData("ApplicationName")]
    [InlineData("APPLICATIONNAME")]
    public void ParseAndNormalize_OverridesApplicationName_RegardlessOfKeywordCasingOrSpacing(string keyword)
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(
            $"Host=localhost;{keyword}=synthetic-sensitive-application");

        Assert.Equal("DbHealthInspector", normalized.ApplicationName);
    }

    // --- Repeated keys cannot restore an unsafe value (GC-DHI-04A-C1 §12) --------------------

    [Fact]
    public void ParseAndNormalize_ForcesPersistSecurityInfoToFalse_EvenWhenTheCallerRepeatsTheKey()
    {
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(
            "Host=localhost;Persist Security Info=true;Persist Security Info=true");

        Assert.False(normalized.PersistSecurityInfo);
    }

    [Fact]
    public void ParseAndNormalize_UsesTheSameNormalizedConfiguration_ThatNpgsqlDataSourceBuilderWouldConsume()
    {
        // The exact NpgsqlConnectionStringBuilder.ConnectionString inspected here is the same
        // string PostgreSqlConnectionFactory.Create passes into NpgsqlDataSourceBuilder — there
        // is no second, separately-maintained "safe" configuration.
        NpgsqlConnectionStringBuilder normalized = PostgreSqlConnectionStringPolicy.ParseAndNormalize(
            "Host=localhost;Persist Security Info=true;Application Name=synthetic-sensitive-application");

        var reparsed = new NpgsqlConnectionStringBuilder(normalized.ConnectionString);

        Assert.False(reparsed.PersistSecurityInfo);
        Assert.Equal("DbHealthInspector", reparsed.ApplicationName);
    }
}
