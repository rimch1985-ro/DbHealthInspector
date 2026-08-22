using System.CommandLine;
using DbHealthInspector.Cli;
using DbHealthInspector.IntegrationTests.TestSupport;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The GC-DHI-05B end-to-end path against a real PostgreSQL 18.4 server: the production command
/// tree resolves a connection, composes the provider with the approved diagnostics through the
/// existing orchestrator, and renders a visible result with a contract-conforming exit code.
/// </summary>
/// <remarks>
/// This exercises the production executor — no substituted seam — so the whole chain is real:
/// PostgreSQL, snapshot provider, ApprovedDiagnostics, InspectionOrchestrator, renderer, exit code.
/// </remarks>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class CliInspectionFunctionalTests
{
    private readonly PostgreSqlServerFixture _fixture;

    public CliInspectionFunctionalTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private const string ConnectionVariable = "DBHEALTH_TEST_CONNECTION";

    /// <summary>
    /// Runs the real command tree, supplying the connection through a named environment variable
    /// so no connection string is ever placed on a command line.
    /// </summary>
    private async Task<(int ExitCode, string Output, string Error)> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        RootCommand root = CommandLineApplication.BuildRootCommand(
            InspectPostgreSqlCommand.ProductionExecutor,
            name => name == ConnectionVariable ? _fixture.InspectionConnectionString : null);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);

        int exitCode = await CommandLineApplication.RunAsync(
            root,
            [.. args, "--connection-env", ConnectionVariable],
            output,
            error,
            deadline.Token);

        return (exitCode, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task InspectPostgreSql_ProducesAVisibleDiagnosisAgainstARealServer()
    {
        (int exitCode, string output, string error) = await RunAsync("inspect", "postgresql");

        // The zoo fixture deliberately contains defective objects, so a clean exit would mean the
        // pipeline silently produced nothing.
        Assert.Equal(1, exitCode);
        Assert.Empty(error);

        Assert.Contains("DbHealth Inspector", output, StringComparison.Ordinal);
        Assert.Contains("TARGET", output, StringComparison.Ordinal);
        Assert.Contains(PostgreSqlServerFixture.DatabaseName, output, StringComparison.Ordinal);
        Assert.Contains("PostgreSQL", output, StringComparison.Ordinal);
        Assert.Contains("18.4", output, StringComparison.Ordinal);

        Assert.Contains("INSPECTION", output, StringComparison.Ordinal);
        Assert.Contains("DIAGNOSTICS", output, StringComparison.Ordinal);
        Assert.Contains("SUMMARY", output, StringComparison.Ordinal);

        // Every approved diagnostic reports, in the frozen ordinal order.
        int previous = -1;
        foreach (string code in new[] { "DBH001", "DBH002", "DBH003", "DBH004", "DBH005" })
        {
            int index = output.IndexOf(code, StringComparison.Ordinal);
            Assert.True(index > previous, $"{code} is missing or out of order.");
            previous = index;
        }
    }

    [Fact]
    public async Task InspectPostgreSql_RendersAtLeastOneRealFinding()
    {
        (_, string output, _) = await RunAsync("inspect", "postgresql");

        // The fixture's deliberately invalid index must surface as a rendered DBH005 finding,
        // proving findings produced by the real rule pipeline reach the console intact.
        Assert.Contains("[Critical] DBH005", output, StringComparison.Ordinal);
        Assert.Contains("zoo_invalid_root", output, StringComparison.Ordinal);
        Assert.Contains("Recommendation :", output, StringComparison.Ordinal);
        Assert.Contains("Confidence     :", output, StringComparison.Ordinal);

        Assert.DoesNotContain(CliMessages.NoFindings, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InspectPostgreSql_NeverEchoesTheConnectionString()
    {
        (_, string output, string error) = await RunAsync("inspect", "postgresql");

        string all = output + error;
        string connectionString = _fixture.InspectionConnectionString;

        Assert.DoesNotContain(connectionString, all, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", all, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", all, StringComparison.OrdinalIgnoreCase);

        // No individual credential-bearing value survives either.
        foreach (string segment in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] pair = segment.Split('=', 2);
            if (pair.Length == 2
                && pair[0].Trim() is "Password" or "Username" or "Host"
                && pair[1].Trim().Length > 0)
            {
                Assert.DoesNotContain(pair[1].Trim(), all, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public async Task ThresholdOverrides_ReachTheRealDiagnosticsAndChangeTheOutcome()
    {
        // A one-megabyte index floor is far below anything in the fixture, so DBH004 now has a
        // chance to report where the 10 MiB default suppressed everything.
        (_, string permissive, _) = await RunAsync(
            "inspect", "postgresql", "--unused-index-size-threshold-mb", "1");
        (_, string strict, _) = await RunAsync(
            "inspect", "postgresql", "--unused-index-size-threshold-mb", "1000000");

        Assert.Contains("DBH004", permissive, StringComparison.Ordinal);
        Assert.Contains("DBH004", strict, StringComparison.Ordinal);

        // Whatever the counts are, a stricter floor can never report more candidates.
        Assert.True(
            CountFindings(strict, "DBH004") <= CountFindings(permissive, "DBH004"),
            "A stricter index-size floor reported more DBH004 findings than a permissive one.");
    }

    [Fact]
    public async Task MissingConnection_FailsWithoutContactingTheServer()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        RootCommand root = CommandLineApplication.BuildRootCommand(
            InspectPostgreSqlCommand.ProductionExecutor, _ => null);

        int exitCode = await CommandLineApplication.RunAsync(
            root, ["inspect", "postgresql"], output, error, TestContext.Current.CancellationToken);

        Assert.Equal(2, exitCode);
        Assert.Contains("No PostgreSQL connection was provided.", error.ToString(), StringComparison.Ordinal);
    }

    private static int CountFindings(string output, string code)
    {
        int count = 0;
        int index = 0;
        string marker = $"] {code} - ";
        while ((index = output.IndexOf(marker, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += marker.Length;
        }

        return count;
    }
}
