using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The optional-statistics degradation proven against a <b>real</b> PostgreSQL 18 server whose
/// inspection role genuinely cannot read the statistics views (GC-DHI-04C §21). A unit-only
/// substitute is explicitly not accepted for this contract.
/// </summary>
/// <remarks>
/// Every test body carries its own deadline, independent of the fixture's initialization budget
/// (GC-DHI-04C-C1, R1-05), and the decisive test observes the real statements as they execute
/// rather than inferring them from the composed result (R1-06).
/// </remarks>
[Collection(PostgreSqlStatisticsRevokedSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class CapabilityProbePermissionLossTests
{
    private readonly PostgreSqlStatisticsRevokedFixture _fixture;

    public CapabilityProbePermissionLossTests(PostgreSqlStatisticsRevokedFixture fixture) => _fixture = fixture;

    /// <summary>
    /// This test body's own budget. The fixture is already initialized by the time it runs, so
    /// container start-up is deliberately not counted against it.
    /// </summary>
    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    private async Task<PostgreSqlServerProbeResult> ProbeAsync(CancellationToken cancellationToken)
    {
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        return await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default,
            PostgreSqlServerCapabilityProbe.ProbeAsync,
            cancellationToken);
    }

    // --- Preconditions: the revocation is real -------------------------------------------------------

    [Fact]
    public async Task Precondition_EffectiveStatisticsPrivilegeIsGenuinelyFalse()
    {
        using CancellationTokenSource deadline = TestDeadline();

        (bool statDatabase, bool statAllIndexes) = await _fixture.ReadEffectiveStatisticsPrivilegesAsync(deadline.Token);

        // has_table_privilege is PostgreSQL's own effective computation: direct grants, PUBLIC
        // and memberships all included. Both must be false for the probe result to mean anything.
        Assert.False(statDatabase, "The role must not be able to read pg_stat_database.");
        Assert.False(statAllIndexes, "The role must not be able to read pg_stat_all_indexes.");
    }

    [Fact]
    public async Task Precondition_RoleIsNotASuperuserAndHoldsNoStatisticsMembership()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // A superuser bypasses every privilege check, which would make the revocation meaningless.
        Assert.False(await _fixture.ReadIsSuperuserAsync(deadline.Token));

        IReadOnlyList<string> memberships = await _fixture.ReadRoleMembershipsAsync(deadline.Token);

        Assert.Empty(memberships);
        Assert.DoesNotContain("pg_monitor", memberships);
        Assert.DoesNotContain("pg_read_all_stats", memberships);
    }

    [Fact]
    public async Task Precondition_RequiredCatalogAccessSurvivedTheRevocation()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // Only the statistics views were revoked; the catalog allowlist must be untouched, or the
        // probe would fail for the wrong reason.
        Assert.True(await _fixture.ReadEffectiveCatalogPrivilegeAsync(deadline.Token));
    }

    // --- Directly observed execution -------------------------------------------------------------------

    [Fact]
    public async Task Probe_RunsC003False_AndNeverExecutesC004_ObservedOnTheRealServer()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken,
            observe: true);

        RecordingPostgreSqlStatementGateway recorder = session.Recorder!;

        PostgreSqlServerProbeResult result = await PostgreSqlServerCapabilityProbe.ProbeAsync(
            session.Operations, cancellationToken);

        IReadOnlyList<PostgreSqlSqlStatementId> executed = recorder.ExecutedStatements;

        // The exact statements that really reached PostgreSQL, in order: the session's own
        // B001-B003 initialization, then C001-C003 — and nothing else.
        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
                PostgreSqlSqlStatementId.CheckUsageStatisticsAccess,
            ],
            executed);

        // The boolean the server itself returned at ordinal 0, observed at the row seam rather
        // than inferred from the composed capability.
        Assert.False(recorder.ObservedUsageStatisticsAvailable);

        // C004 was never even attempted; the recorder logs attempts, not just successes.
        Assert.DoesNotContain(PostgreSqlSqlStatementId.ReadStatisticsReset, executed);

        Assert.Equal(CapabilityStatus.Unavailable, result.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);
        Assert.Equal(CapabilityStatus.Available, result.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(CapabilityStatus.Disabled, result.Capabilities.GetState(CapabilityKind.DataProfiling).Status);
        Assert.Null(result.Statistics.StatisticsResetAtUtc);
    }

    [Fact]
    public async Task Recorder_ExposesACopyOfTheExecutedSequence()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken,
            observe: true);

        RecordingPostgreSqlStatementGateway recorder = session.Recorder!;

        // A caller can never reach, and therefore never mutate, the recorder's own list.
        Assert.NotSame(recorder.ExecutedStatements, recorder.ExecutedStatements);
    }

    // --- The degradation itself ------------------------------------------------------------------------

    [Fact]
    public async Task Probe_ContinuesAndReportsStatisticsUnavailable()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlServerProbeResult result = await ProbeAsync(deadline.Token);

        CapabilityState statistics = result.Capabilities.GetState(CapabilityKind.UsageStatistics);
        Assert.Equal(CapabilityStatus.Unavailable, statistics.Status);
        Assert.Equal("Usage statistics are unavailable for this inspection.", statistics.Reason);
        Assert.Null(result.Statistics.StatisticsResetAtUtc);
    }

    [Fact]
    public async Task Probe_KeepsTheOtherCapabilitiesCorrect()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlServerProbeResult result = await ProbeAsync(deadline.Token);

        Assert.Equal(3, result.Capabilities.States.Count);

        CapabilityState catalog = result.Capabilities.GetState(CapabilityKind.CatalogMetadata);
        Assert.Equal(CapabilityStatus.Available, catalog.Status);
        Assert.Null(catalog.Reason);

        CapabilityState profiling = result.Capabilities.GetState(CapabilityKind.DataProfiling);
        Assert.Equal(CapabilityStatus.Disabled, profiling.Status);
        Assert.Equal("Data profiling is disabled by product policy.", profiling.Reason);

        Assert.Equal(PostgreSqlVersionSupportStatus.Supported, result.VersionSupport);
        Assert.Equal(18, result.MajorVersion);
    }

    [Fact]
    public async Task Probe_ReportsTheIdentityOfTheRevokedRole()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlServerProbeResult result = await ProbeAsync(deadline.Token);

        Assert.Equal(PostgreSqlStatisticsRevokedFixture.DatabaseName, result.Metadata.DatabaseName);
        Assert.Equal(PostgreSqlStatisticsRevokedFixture.InspectionRoleName, result.Metadata.CurrentUser);
    }

    [Fact]
    public async Task Probe_LeaksNoServerDetailThroughAnyReason()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlServerProbeResult result = await ProbeAsync(deadline.Token);

        string[] forbidden =
        [
            "permission denied",
            "42501",
            "pg_stat",
            "pg_catalog",
            PostgreSqlStatisticsRevokedFixture.DatabaseName,
            PostgreSqlStatisticsRevokedFixture.InspectionRoleName,
        ];

        foreach (CapabilityState state in result.Capabilities.States)
        {
            string reason = state.Reason ?? string.Empty;

            foreach (string marker in forbidden)
            {
                // The marker is deliberately not part of the assertion, so a failure cannot print
                // the very value the test exists to keep out of test output.
                bool leaked = reason.Contains(marker, StringComparison.OrdinalIgnoreCase);
                Assert.False(leaked, "Sensitive data was exposed.");
            }
        }

        // The result's own rendering must not expose identity either.
        bool renderedIdentity = result.ToString()!
            .Contains(PostgreSqlStatisticsRevokedFixture.InspectionRoleName, StringComparison.Ordinal);
        Assert.False(renderedIdentity, "Sensitive data was exposed.");
    }

    [Fact]
    public async Task Probe_RemainsUsableAfterwards()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlServerProbeResult first = await ProbeAsync(deadline.Token);
        PostgreSqlServerProbeResult second = await ProbeAsync(deadline.Token);

        Assert.Equal(
            first.Capabilities.GetState(CapabilityKind.UsageStatistics).Status,
            second.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);
        Assert.Equal(CapabilityStatus.Unavailable, second.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);
    }
}
