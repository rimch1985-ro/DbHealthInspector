using DbHealthInspector.UnitTests.Cli.TestSupport;

namespace DbHealthInspector.UnitTests.Cli;

public sealed class CliSecretRedactionTests
{
    private const string Sentinel = "SUPER_SECRET_SENTINEL";
    private const string SecretConnection = "Host=db.internal;Port=5432;Username=admin;Password=" + Sentinel;

    private static void AssertNoSentinel(CliHarness harness)
    {
        Assert.DoesNotContain(Sentinel, harness.Output, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinel, harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExceptionMessageCarryingASecret_NeverReachesTheConsole()
    {
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", SecretConnection);
        harness.Behavior = _ => throw new InvalidOperationException(
            $"Failed to connect using '{SecretConnection}'.");

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        AssertNoSentinel(harness);
        Assert.Contains(
            "The PostgreSQL inspection could not be completed.", harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecretInInnerExceptionMessage_NeverReachesTheConsole()
    {
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", SecretConnection);
        harness.Behavior = _ => throw new InvalidOperationException(
            "Outer failure.", new InvalidOperationException($"inner saw {Sentinel}"));

        Assert.Equal(2, await harness.RunAsync("inspect", "postgresql"));
        AssertNoSentinel(harness);
    }

    [Fact]
    public async Task SecretInExceptionData_NeverReachesTheConsole()
    {
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", SecretConnection);
        harness.Behavior = _ =>
        {
            var exception = new InvalidOperationException("Outer failure.");
            exception.Data["connection"] = SecretConnection;
            throw exception;
        };

        Assert.Equal(2, await harness.RunAsync("inspect", "postgresql"));
        AssertNoSentinel(harness);
    }

    [Fact]
    public async Task SecretInAnArgumentException_NeverReachesTheConsole()
    {
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", SecretConnection);
        harness.Behavior = _ => throw new ArgumentException($"bad value {Sentinel}", "connectionString");

        Assert.Equal(2, await harness.RunAsync("inspect", "postgresql"));
        AssertNoSentinel(harness);

        // An ArgumentException reaching the handler arose *after* provider configuration, so it is
        // an internal defect and maps to the generic failure — not to "your connection
        // configuration is invalid", which would send the user to fix something that is not broken
        // (Codex R1-01). The redaction guarantee is unchanged either way.
        Assert.Contains(
            "The PostgreSQL inspection could not be completed.", harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulRun_NeverEchoesTheConnectionString()
    {
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", SecretConnection);
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.Healthy());

        Assert.Equal(0, await harness.RunAsync("inspect", "postgresql"));
        AssertNoSentinel(harness);

        // Nor the other connection coordinates.
        Assert.DoesNotContain("db.internal", harness.All, StringComparison.Ordinal);
        Assert.DoesNotContain("admin", harness.All, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ConnectionSuppliedOnTheCommandLine_IsNeverEchoed()
    {
        var harness = new CliHarness();
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.Healthy());

        Assert.Equal(0, await harness.RunAsync("inspect", "postgresql", "--connection", SecretConnection));
        AssertNoSentinel(harness);
    }

    [Fact]
    public async Task MistypedConnectionOption_DoesNotEchoTheSecretAsAnUnmatchedToken()
    {
        // System.CommandLine 2.0.10's own parse diagnostics print unmatched tokens verbatim, so a
        // mistyped option name would otherwise write the whole connection string — password and
        // all — to standard error. Verified empirically against 2.0.10 before this CLI chose to
        // suppress those diagnostics entirely.
        var harness = new CliHarness();

        int exitCode = await harness.RunAsync("inspect", "postgresql", "--connectio", SecretConnection);

        Assert.Equal(2, exitCode);
        AssertNoSentinel(harness);
        Assert.Contains("The command line could not be understood.", harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SecretAsAStrayPositionalArgument_IsNotEchoed()
    {
        var harness = new CliHarness();

        int exitCode = await harness.RunAsync("inspect", "postgresql", SecretConnection);

        Assert.Equal(2, exitCode);
        AssertNoSentinel(harness);
    }

    [Fact]
    public async Task NamedEnvironmentVariable_NameIsEchoedButValueIsNot()
    {
        var harness = new CliHarness().WithEnvironment("MY_SECRET_CONN", SecretConnection);
        harness.Behavior = _ => Task.FromResult(CliHarness.Inspections.Healthy());

        Assert.Equal(0, await harness.RunAsync(
            "inspect", "postgresql", "--connection-env", "MY_SECRET_CONN"));

        AssertNoSentinel(harness);
        Assert.Equal(SecretConnection, harness.ObservedConnectionString);
    }
}
