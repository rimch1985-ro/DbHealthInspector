using DbHealthInspector.UnitTests.Cli.TestSupport;

namespace DbHealthInspector.UnitTests.Cli;

public sealed class CliHelpAndCommandTreeTests
{
    [Theory]
    [InlineData("--help")]
    [InlineData("inspect", "--help")]
    [InlineData("inspect", "postgresql", "--help")]
    public async Task EveryHelpLevel_Succeeds(params string[] args)
    {
        var harness = new CliHarness();

        int exitCode = await harness.RunAsync(args);

        Assert.Equal(0, exitCode);
        Assert.NotEmpty(harness.Output);
    }

    [Fact]
    public async Task RootHelp_ListsTheInspectCommand()
    {
        var harness = new CliHarness();

        await harness.RunAsync("--help");

        Assert.Contains("inspect", harness.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectHelp_ListsThePostgreSqlCommand()
    {
        var harness = new CliHarness();

        await harness.RunAsync("inspect", "--help");

        Assert.Contains("postgresql", harness.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--connection")]
    [InlineData("--connection-env")]
    [InlineData("--large-table-row-threshold")]
    [InlineData("--large-table-size-threshold-mb")]
    [InlineData("--unused-index-size-threshold-mb")]
    public async Task PostgreSqlHelp_ListsEveryApprovedOption(string option)
    {
        var harness = new CliHarness();

        await harness.RunAsync("inspect", "postgresql", "--help");

        Assert.Contains(option, harness.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--output")]
    [InlineData("--schema")]
    [InlineData("--exclude-schema")]
    [InlineData("--statement-timeout-seconds")]
    [InlineData("--target-label")]
    [InlineData("--verbose")]
    public async Task PostgreSqlHelp_DoesNotOfferDeferredOptions(string option)
    {
        var harness = new CliHarness();

        await harness.RunAsync("inspect", "postgresql", "--help");

        Assert.DoesNotContain(option, harness.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgreSqlHelp_WarnsAgainstCommandLineSecrets()
    {
        var harness = new CliHarness();

        await harness.RunAsync("inspect", "postgresql", "--help");

        Assert.Contains("shell history", harness.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("process listings", harness.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DBHEALTH_CONNECTION", harness.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgreSqlHelp_StatesTheBinaryMegabyteUnit()
    {
        var harness = new CliHarness();

        await harness.RunAsync("inspect", "postgresql", "--help");

        // A reader must never be left guessing between 10^6 and 2^20.
        Assert.Contains("1048576", harness.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostgreSqlHelp_StatesTheInspectionIsReadOnly()
    {
        var harness = new CliHarness();

        await harness.RunAsync("inspect", "postgresql", "--help");

        Assert.Contains("read-only", harness.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InspectWithoutSubcommand_IsAUsageFailure()
    {
        var harness = new CliHarness();

        Assert.Equal(2, await harness.RunAsync("inspect"));
    }

    [Fact]
    public async Task UnknownOption_IsAUsageFailure()
    {
        var harness = new CliHarness().WithEnvironment("DBHEALTH_CONNECTION", "Host=h;Database=d");

        // System.CommandLine 2.0.10 returns 1 for a parse error on its own; the CLI must force 2.
        Assert.Equal(2, await harness.RunAsync("inspect", "postgresql", "--nope"));
    }

    [Fact]
    public async Task UnknownCommand_IsAUsageFailure()
    {
        var harness = new CliHarness();

        Assert.Equal(2, await harness.RunAsync("inspect", "mysql"));
    }
}
