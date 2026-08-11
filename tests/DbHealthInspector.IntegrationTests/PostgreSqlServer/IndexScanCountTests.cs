using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Indexes;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The three distinct scan-count states against a real PostgreSQL 18.4 server (GC-DHI-04E §26):
/// zero for a never-scanned physical index, greater than zero once the index has genuinely been
/// used, and null whenever the counter is unknown rather than known to be nothing.
/// </summary>
/// <remarks>
/// The only business rows read anywhere in this file are read by the <b>test suite</b>, to force a
/// real index scan. The product reads none: E001 and E002 touch catalogs and statistics views
/// only. Statistics visibility is forced deterministically with
/// <c>pg_stat_force_next_flush()</c> and observed from a fresh session — never with a sleep, a
/// timing window or <c>pg_stat_statements</c>.
/// </remarks>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class IndexScanCountTests
{
    private readonly PostgreSqlServerFixture _fixture;

    public IndexScanCountTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    private async Task<PostgreSqlIndexSnapshotQueryResult> ReadAsync(
        bool usageStatisticsAvailable,
        CancellationToken cancellationToken)
    {
        await using PostgreSqlConnectionFactory factory =
            PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        return await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default,
            async (view, token) =>
            {
                PostgreSqlServerProbeResult probe = await PostgreSqlServerCapabilityProbe.ProbeAsync(view, token);
                Assert.Equal(PostgreSqlVersionSupportStatus.Supported, probe.VersionSupport);
                Assert.Equal(
                    CapabilityStatus.Available,
                    probe.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status);

                return await view.ReadIndexSnapshotsAsync(
                    new PostgreSqlSchemaFilter([PostgreSqlServerFixture.IndexSchema], []),
                    usageStatisticsAvailable,
                    token);
            },
            cancellationToken);
    }

    private static IndexSnapshot Find(PostgreSqlIndexSnapshotQueryResult result, string indexName) =>
        Assert.Single(result.Indexes, index => index.IndexName == indexName);

    [Fact]
    public async Task AFreshPhysicalIndex_ReportsZeroScans()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // A dedicated index nothing has ever queried. Zero is a *known* value, distinct from null.
        await _fixture.CreateNeverScannedIndexAsync("zoo_never_scanned", deadline.Token);

        try
        {
            IndexSnapshot index = Find(await ReadAsync(true, deadline.Token), "zoo_never_scanned");

            Assert.NotNull(index.ScanCount);
            Assert.Equal(0L, index.ScanCount);
        }
        finally
        {
            await _fixture.DropIndexAsync("zoo_never_scanned", CancellationToken.None);
        }
    }

    [Fact]
    public async Task AnIndexThatWasGenuinelyUsed_ReportsAPositiveScanCount()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        await _fixture.CreateNeverScannedIndexAsync("zoo_to_be_scanned", token);

        try
        {
            // The suite forces a real index scan and proves the planner actually chose the index,
            // then flushes the statistics deterministically.
            bool used = await _fixture.ForceIndexScanAsync("zoo_to_be_scanned", token);
            Assert.True(used, "The planner did not choose the index, so a positive count would prove nothing.");

            IndexSnapshot index = Find(await ReadAsync(true, token), "zoo_to_be_scanned");

            Assert.NotNull(index.ScanCount);
            Assert.True(index.ScanCount > 0, "The index was scanned but the counter did not advance.");
        }
        finally
        {
            await _fixture.DropIndexAsync("zoo_to_be_scanned", CancellationToken.None);
        }
    }

    [Fact]
    public async Task WhenStatisticsAreUnavailable_EveryScanCountIsNull()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // E002 is not executed at all, so no counter is known — for any index, including ones the
        // suite has already scanned. Unknown is never reported as zero.
        PostgreSqlIndexSnapshotQueryResult result = await ReadAsync(false, deadline.Token);

        Assert.NotEmpty(result.Indexes);
        Assert.All(result.Indexes, index => Assert.Null(index.ScanCount));
    }

    [Fact]
    public async Task AVirtualPartitionedRoot_ReportsNullEvenWithStatisticsAvailable()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // A partitioned index root has no storage and no counter of its own, and its partitions'
        // counters are never aggregated into it.
        IndexSnapshot root = Find(await ReadAsync(true, deadline.Token), "zoo_partitioned");

        Assert.Null(root.ScanCount);
        Assert.Equal(0, root.SizeBytes);
    }

    [Fact]
    public async Task ThePhysicalIndexPartition_CarriesItsOwnCounterIndependently()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlIndexSnapshotQueryResult result = await ReadAsync(true, deadline.Token);

        IndexSnapshot child = Assert.Single(
            result.Indexes,
            index => index.TableName == "partitioned_orders_emea");

        // The child is physical, so it has a real counter; the root above stays null.
        Assert.NotNull(child.ScanCount);
        Assert.Null(Find(result, "zoo_partitioned").ScanCount);
    }
}
