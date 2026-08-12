using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Indexes;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// Index snapshots on a server where the optional usage-statistics capability is genuinely
/// unavailable (GC-DHI-04E §23): the required catalog metadata is still reachable, E001 still
/// runs, E002 is not executed at all, and every scan count is null rather than zero.
/// </summary>
/// <remarks>
/// Reuses the GC-DHI-04C statistics-revoked container unchanged: a dedicated database whose
/// inspection role has lost <c>SELECT</c> on the statistics views while keeping every required
/// catalog privilege. Nothing about C003 itself is modified, and statistics never become a
/// required capability.
/// </remarks>
[Collection(PostgreSqlStatisticsRevokedSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class IndexSnapshotStatisticsUnavailableTests
{
    private readonly PostgreSqlStatisticsRevokedFixture _fixture;

    public IndexSnapshotStatisticsUnavailableTests(PostgreSqlStatisticsRevokedFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    [Fact]
    public async Task E001Runs_AndE002IsNeverExecuted()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken,
            observe: true);

        RecordingPostgreSqlStatementGateway recorder = session.Recorder!;

        PostgreSqlServerProbeResult probe =
            await PostgreSqlServerCapabilityProbe.ProbeAsync(session.Operations, cancellationToken);

        // Required metadata is reachable; optional statistics are not.
        Assert.Equal(
            CapabilityStatus.Available,
            probe.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(
            CapabilityStatus.Unavailable,
            probe.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);

        bool statisticsAvailable =
            probe.Capabilities.GetState(CapabilityKind.UsageStatistics).Status == CapabilityStatus.Available;
        Assert.False(statisticsAvailable);

        await session.Operations.ReadIndexSnapshotsAsync(
            PostgreSqlSchemaFilter.IncludeEverything, statisticsAvailable, cancellationToken);

        IReadOnlyList<PostgreSqlSqlStatementId> executed = recorder.ExecutedStatements;

        // E001 ran exactly once; E002 never reached the server at all.
        Assert.Equal(1, executed.Count(id => id == PostgreSqlSqlStatementId.ReadIndexMetadata));
        Assert.Equal(0, executed.Count(id => id == PostgreSqlSqlStatementId.ReadIndexUsageStatistics));
        Assert.DoesNotContain(PostgreSqlSqlStatementId.ReadIndexUsageStatistics, executed);
    }

    [Fact]
    public async Task EveryScanCountIsNull_NeverZero()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken);

        PostgreSqlServerProbeResult probe =
            await PostgreSqlServerCapabilityProbe.ProbeAsync(session.Operations, cancellationToken);
        Assert.Equal(
            CapabilityStatus.Unavailable,
            probe.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);

        PostgreSqlIndexSnapshotQueryResult result = await session.Operations.ReadIndexSnapshotsAsync(
            PostgreSqlSchemaFilter.IncludeEverything, usageStatisticsAvailable: false, cancellationToken);

        // Absence of statistics is unknown, not "no scans". Reporting zero would assert something
        // the server never said. Index metadata itself stays complete.
        Assert.All(result.Indexes, index =>
        {
            Assert.Null(index.ScanCount);
            Assert.NotEmpty(index.KeyParts);
            Assert.NotEmpty(index.AccessMethod);
            Assert.True(index.SizeBytes >= 0);
        });
    }

    // --- Shared cross-version degradation contract (GC-DHI-04F-C2, R1-06) -----------------------

    [Fact]
    public async Task TheProvider_SatisfiesTheSharedUsageStatisticsUnavailableContract()
    {
        using CancellationTokenSource deadline = TestDeadline();

        await using var provider = DbHealthInspector.PostgreSql.Snapshots.PostgreSqlDatabaseSnapshotProvider
            .Create(_fixture.InspectionConnectionString);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        // The identical helper the PostgreSQL 15 suite calls for its own degraded role, so the
        // unavailable-statistics contract is frozen once and asserted on both majors. This
        // container is deliberately minimal and holds no indexes, which is a fixture difference
        // rather than a version one; every other part of the contract is asserted identically.
        CrossVersionSnapshotAssertions.AssertUsageStatisticsUnavailable(snapshot, expectIndexes: false);
    }
}
