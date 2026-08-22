using DbHealthInspector.UnitTests.Cli.TestSupport;

namespace DbHealthInspector.UnitTests.Cli;

public sealed class CliConnectionResolutionTests
{
    private const string FromOption = "Host=option;Database=d";
    private const string FromNamed = "Host=named;Database=d";
    private const string FromDefault = "Host=default;Database=d";

    [Fact]
    public async Task ConnectionOption_WinsOverBothEnvironmentSources()
    {
        var harness = new CliHarness()
            .WithEnvironment("MY_CONN", FromNamed)
            .WithEnvironment("DBHEALTH_CONNECTION", FromDefault);

        int exitCode = await harness.RunAsync(
            "inspect", "postgresql", "--connection", FromOption, "--connection-env", "MY_CONN");

        Assert.Equal(0, exitCode);
        Assert.Equal(FromOption, harness.ObservedConnectionString);
    }

    [Fact]
    public async Task NamedEnvironmentVariable_WinsOverTheDefaultVariable()
    {
        var harness = new CliHarness()
            .WithEnvironment("MY_CONN", FromNamed)
            .WithEnvironment("DBHEALTH_CONNECTION", FromDefault);

        int exitCode = await harness.RunAsync("inspect", "postgresql", "--connection-env", "MY_CONN");

        Assert.Equal(0, exitCode);
        Assert.Equal(FromNamed, harness.ObservedConnectionString);
    }

    [Fact]
    public async Task DefaultVariable_IsUsedWhenNeitherOptionIsSupplied()
    {
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", FromDefault);

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(0, exitCode);
        Assert.Equal(FromDefault, harness.ObservedConnectionString);
    }

    [Fact]
    public async Task NamedVariableMissing_FailsAndDoesNotFallBackToTheDefaultVariable()
    {
        // The decisive precedence rule: naming a variable is a specific instruction, so silently
        // inspecting whatever DBHEALTH_CONNECTION points at could inspect the wrong database.
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", FromDefault);

        int exitCode = await harness.RunAsync("inspect", "postgresql", "--connection-env", "ABSENT");

        Assert.Equal(2, exitCode);
        Assert.Null(harness.ObservedConnectionString);
        Assert.DoesNotContain("default", harness.All, StringComparison.Ordinal);
        Assert.Contains(
            "The environment variable named by --connection-env is not set or is empty.",
            harness.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task NamedVariableBlank_FailsWithoutFallback()
    {
        var harness = new CliHarness()
            .WithEnvironment("BLANK_CONN", "   ")
            .WithEnvironment("DBHEALTH_CONNECTION", FromDefault);

        int exitCode = await harness.RunAsync("inspect", "postgresql", "--connection-env", "BLANK_CONN");

        Assert.Equal(2, exitCode);
        Assert.Null(harness.ObservedConnectionString);
    }

    [Fact]
    public async Task BlankConnectionOption_FailsWithoutFallback()
    {
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", FromDefault);

        int exitCode = await harness.RunAsync("inspect", "postgresql", "--connection", "   ");

        Assert.Equal(2, exitCode);
        Assert.Null(harness.ObservedConnectionString);
    }

    [Fact]
    public async Task NoConnectionAnywhere_FailsWithTheFixedMessage()
    {
        var harness = new CliHarness();

        int exitCode = await harness.RunAsync("inspect", "postgresql");

        Assert.Equal(2, exitCode);
        Assert.Contains("No PostgreSQL connection was provided.", harness.Error, StringComparison.Ordinal);
        Assert.Contains("DBHEALTH_CONNECTION", harness.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BlankDefaultVariable_IsTreatedAsAbsent()
    {
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", "");

        Assert.Equal(2, await harness.RunAsync("inspect", "postgresql"));
    }
}
