using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The foreign-table contract (GC-DHI-04D §22 and §26): a real <c>postgres_fdw</c> table over a
/// loopback server is detected from <b>local</b> catalogs, reported with zero sizes, and never
/// causes the inspection to open a remote connection or read a remote row.
/// </summary>
/// <remarks>
/// The loopback server, its user mapping and the role's grants are all genuinely usable — a
/// positive control in this file proves a remote read really does work. That is what makes "D001
/// opened no remote connection" a statement about D001 rather than about a broken setup.
/// </remarks>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class TableSnapshotForeignTableTests
{
    private const string ForeignTableName = "remote_orders";

    private readonly PostgreSqlServerFixture _fixture;

    public TableSnapshotForeignTableTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    private async Task<PostgreSqlTableSnapshotQueryResult> ComposeAsync(CancellationToken cancellationToken)
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

                return await view.ReadTableSnapshotsAsync(
                    new PostgreSqlSchemaFilter([PostgreSqlServerFixture.TableSchema], []), token);
            },
            cancellationToken);
    }

    private static TableSnapshot ForeignTable(PostgreSqlTableSnapshotQueryResult result) =>
        Assert.Single(result.Tables, table => table.TableName == ForeignTableName);

    [Fact]
    public async Task TheForeignTableIsDetectedFromLocalCatalogs()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = ForeignTable(await ComposeAsync(deadline.Token));

        Assert.Equal(RelationKind.ForeignTable, table.RelationKind);
        Assert.Equal(PostgreSqlServerFixture.TableSchema, table.SchemaName);
        Assert.False(table.IsPartitionedRoot);
        Assert.False(table.IsPartition);
    }

    [Fact]
    public async Task TheForeignTableReportsZeroSizes()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = ForeignTable(await ComposeAsync(deadline.Token));

        // A foreign table has no local storage, and D001's CASE keeps the size functions away
        // from it entirely.
        Assert.Equal(0, table.TableSizeBytes);
        Assert.Equal(0, table.IndexSizeBytes);
        Assert.Equal(0, table.TotalSizeBytes);
    }

    [Fact]
    public async Task TheForeignTableReportsNoEstimateAndNoPrimaryKey()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = ForeignTable(await ComposeAsync(deadline.Token));

        Assert.Null(table.EstimatedRowCount);
        Assert.False(table.HasPrimaryKey);
    }

    // --- Same-session foreign-connection proof (R1-17, R1-18) ---------------------------------

    private static IEnumerable<ForeignServerConnection> Target(IReadOnlyList<ForeignServerConnection> rows) =>
        rows.Where(row => row.ServerName == PostgreSqlServerFixture.ForeignServerName);

    [Fact]
    public async Task D001OpensNoConnectionToTheForeignServer_ObservedInTheSameSession()
    {
        using CancellationTokenSource deadline = TestDeadline();

        ForeignConnectionProof proof =
            await _fixture.ProveD001OpensNoForeignConnectionAsync(deadline.Token);

        // Before: this backend holds no connection to the loopback server.
        Assert.Empty(Target(proof.BeforeD001));

        // D001 really ran and really saw the relation zoo, so the zero below is not the zero of a
        // statement that did nothing.
        Assert.True(proof.RelationsRead > 0);

        // After: still none. Because the observation runs on the very backend that executed D001,
        // a connection that had been opened and closed again would still have to appear here --
        // postgres_fdw keeps its entry, marked closed, rather than forgetting it. This is the gap
        // a before/after pg_stat_activity sample from another connection cannot close.
        Assert.Empty(Target(proof.AfterD001));
    }

    [Fact]
    public async Task ThePositiveControl_ProvesTheDetectorSeesARealRemoteConnection()
    {
        using CancellationTokenSource deadline = TestDeadline();

        ForeignConnectionProof proof =
            await _fixture.ProveD001OpensNoForeignConnectionAsync(deadline.Token);

        // The suite -- never the product -- reads the foreign table on that same backend.
        Assert.Equal(PostgreSqlServerFixture.AnalyzedRowCount, proof.RemoteRows);

        // The detector now reports exactly the server that was used, in the same session where it
        // reported nothing twice before. Without this the two empty results above would be
        // consistent with a detector that can never see anything at all.
        ForeignServerConnection connection = Assert.Single(Target(proof.AfterRemoteRead));
        Assert.Equal(PostgreSqlServerFixture.ForeignServerName, connection.ServerName);
        Assert.True(connection.Valid);
        Assert.NotNull(connection.RemoteBackendPid);
    }

    [Fact]
    public async Task TheDetectorIsStableAcrossRepeatedProofs()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // Each proof owns a fresh backend, so an earlier remote read cannot leak into a later
        // observation: the negative result must not depend on pooling or on timing.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            ForeignConnectionProof proof =
                await _fixture.ProveD001OpensNoForeignConnectionAsync(deadline.Token);

            Assert.Empty(Target(proof.BeforeD001));
            Assert.Empty(Target(proof.AfterD001));
            Assert.Single(Target(proof.AfterRemoteRead));
        }
    }

    [Fact]
    public async Task TheProductionInspectionPath_AlsoLeavesNoForeignBackend()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        // Supporting cross-session evidence for the full production path, which runs on a backend
        // the test cannot observe from the inside. It is deliberately not the primary proof --
        // that is the same-session test above -- because a connection opened and closed between
        // two samples would be invisible here.
        long before = await _fixture.ReadForeignServerBackendCountAsync(cancellationToken);

        await ComposeAsync(cancellationToken);

        Assert.Equal(before, await _fixture.ReadForeignServerBackendCountAsync(cancellationToken));
    }
}
