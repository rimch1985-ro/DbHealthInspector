using System.Diagnostics;
using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The capability probe against a real PostgreSQL 18.4 server, driven through the exact
/// production path: connection factory → verified session → typed operation view → probe.
/// </summary>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class CapabilityProbeTests
{
    private readonly PostgreSqlServerFixture _fixture;

    public CapabilityProbeTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private static async Task<PostgreSqlServerProbeResult> ProbeAsync(string connectionString, CancellationToken cancellationToken)
    {
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(connectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        return await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default,
            PostgreSqlServerCapabilityProbe.ProbeAsync,
            cancellationToken);
    }

    [Fact]
    public async Task Probe_ReportsTheRealIdentityOfPostgreSql18()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        PostgreSqlServerProbeResult result = await ProbeAsync(_fixture.InspectionConnectionString, cancellationToken);

        Assert.Equal(180004, result.ServerVersionNumber);
        Assert.Equal(18, result.MajorVersion);
        Assert.Equal("18.4", result.Metadata.EngineVersion);
        Assert.Equal(PostgreSqlVersionSupportStatus.Supported, result.VersionSupport);
        Assert.Equal(DatabaseEngine.PostgreSql, result.Metadata.Engine);
        Assert.Equal(PostgreSqlServerFixture.DatabaseName, result.Metadata.DatabaseName);
        Assert.Equal(PostgreSqlServerFixture.InspectionRoleName, result.Metadata.CurrentUser);
    }

    [Fact]
    public async Task Probe_ReportsNormalCapabilities()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        PostgreSqlServerProbeResult result = await ProbeAsync(_fixture.InspectionConnectionString, cancellationToken);

        Assert.Equal(3, result.Capabilities.States.Count);

        CapabilityState catalog = result.Capabilities.GetState(CapabilityKind.CatalogMetadata);
        Assert.Equal(CapabilityStatus.Available, catalog.Status);
        Assert.Null(catalog.Reason);

        CapabilityState statistics = result.Capabilities.GetState(CapabilityKind.UsageStatistics);
        Assert.Equal(CapabilityStatus.Available, statistics.Status);
        Assert.Null(statistics.Reason);

        CapabilityState profiling = result.Capabilities.GetState(CapabilityKind.DataProfiling);
        Assert.Equal(CapabilityStatus.Disabled, profiling.Status);
        Assert.Equal("Data profiling is disabled by product policy.", profiling.Reason);
    }

    [Fact]
    public async Task Probe_ReportsAStatisticsResetThatIsEitherNullOrUtc()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        PostgreSqlServerProbeResult result = await ProbeAsync(_fixture.InspectionConnectionString, cancellationToken);

        // Both are legitimate: a freshly started server may or may not report a reset. What must
        // hold either way is that a reported value is already UTC.
        if (result.Statistics.StatisticsResetAtUtc is { } reset)
        {
            Assert.Equal(TimeSpan.Zero, reset.Offset);
        }
    }

    /// <summary>
    /// The control for the permission-loss suite's observation: on a server where the statistics
    /// views <i>are</i> readable, C003 really returns true and C004 really runs. Together the two
    /// tests prove the observer distinguishes the routes rather than always reporting one.
    /// </summary>
    [Fact]
    public async Task Probe_RunsC003True_AndThenExecutesC004_ObservedOnTheRealServer()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken,
            observe: true);

        RecordingPostgreSqlStatementGateway recorder = session.Recorder!;

        PostgreSqlServerProbeResult result = await PostgreSqlServerCapabilityProbe.ProbeAsync(
            session.Operations, cancellationToken);

        // The exact statements that really reached PostgreSQL, in order, all four C statements
        // included.
        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
                PostgreSqlSqlStatementId.CheckUsageStatisticsAccess,
                PostgreSqlSqlStatementId.ReadStatisticsReset,
            ],
            recorder.ExecutedStatements);

        // Observed at the row seam: the server itself said the statistics views were readable.
        Assert.True(recorder.ObservedUsageStatisticsAvailable);

        Assert.Equal(CapabilityStatus.Available, result.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);
    }

    [Fact]
    public async Task Probe_LeavesTheSessionReadOnlyAndRollsBack()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        string markerBefore = (await _fixture.ReadControlMarkerAsync(cancellationToken))!;
        (bool schemaBefore, bool tableBefore, long tableCountBefore) = await _fixture.ReadSchemaShapeAsync(cancellationToken);

        await ProbeAsync(_fixture.InspectionConnectionString, cancellationToken);

        Assert.Equal(markerBefore, await _fixture.ReadControlMarkerAsync(cancellationToken));
        Assert.Equal(1, await _fixture.ReadControlRowCountAsync(cancellationToken));

        (bool schemaAfter, bool tableAfter, long tableCountAfter) = await _fixture.ReadSchemaShapeAsync(cancellationToken);
        Assert.True(schemaBefore && schemaAfter);
        Assert.True(tableBefore && tableAfter);
        Assert.Equal(tableCountBefore, tableCountAfter);

        // No backend left inside a transaction.
        await using NpgsqlConnection admin = await _fixture.OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE usename = @role AND state IN ('idle in transaction', 'idle in transaction (aborted)')",
            admin);
        command.Parameters.AddWithValue("role", PostgreSqlServerFixture.InspectionRoleName);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(cancellationToken))!);
    }

    [Fact]
    public async Task Probe_LeavesThePoolReusable()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        PostgreSqlServerProbeResult first = await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default, PostgreSqlServerCapabilityProbe.ProbeAsync, cancellationToken);
        PostgreSqlServerProbeResult second = await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default, PostgreSqlServerCapabilityProbe.ProbeAsync, cancellationToken);

        Assert.Equal(first.ServerVersionNumber, second.ServerVersionNumber);
        Assert.Equal(PostgreSqlVersionSupportStatus.Supported, second.VersionSupport);
    }

    [Fact]
    public async Task Probe_RespectsRealCancellation()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync<PostgreSqlServerProbeResult>(
                PostgreSqlInspectionSessionOptions.Default,
                async (view, token) =>
                {
                    // Cancel after the session is verified but before the probe finishes.
                    await cts.CancelAsync();
                    return await PostgreSqlServerCapabilityProbe.ProbeAsync(view, token);
                },
                cts.Token).AsTask());

        // The pool still works afterwards.
        PostgreSqlServerProbeResult afterwards = await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default, PostgreSqlServerCapabilityProbe.ProbeAsync, cancellationToken);
        Assert.Equal(PostgreSqlVersionSupportStatus.Supported, afterwards.VersionSupport);
    }

    [Fact]
    public async Task Probe_CompletesWellWithinAReasonableBudget()
    {
        // Four small statements against a warm container; a regression that made the probe do
        // real work over user data would show up here.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));

        var stopwatch = Stopwatch.StartNew();
        await ProbeAsync(_fixture.InspectionConnectionString, deadline.Token);
        stopwatch.Stop();

        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Probe took {stopwatch.Elapsed}.");
    }
}
