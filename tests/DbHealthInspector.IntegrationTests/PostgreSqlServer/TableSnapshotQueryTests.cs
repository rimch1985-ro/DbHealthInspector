using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// D001 against a real PostgreSQL 18.4 server holding one relation of every kind it admits
/// (GC-DHI-04D §22), driven through the exact production path: connection factory → verified
/// session → capability probe → typed table-snapshot operation.
/// </summary>
/// <remarks>
/// The probe runs first and its verdict is required before D001 is called. GC-DHI-04D implements
/// the query and the mapper, not the provider, so that ordering is enforced <b>here</b>, by the
/// test composition — the operation view deliberately does not police it. GC-DHI-04F owns the
/// productive sequencing.
/// </remarks>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class TableSnapshotQueryTests
{
    private readonly PostgreSqlServerFixture _fixture;

    public TableSnapshotQueryTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    /// <summary>
    /// The composed, test-owned sequence: verify the session, probe, require a supported server
    /// with reachable catalog metadata, and only then read table snapshots.
    /// </summary>
    private async Task<PostgreSqlTableSnapshotQueryResult> ComposeAsync(
        PostgreSqlSchemaFilter filter,
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

                return await view.ReadTableSnapshotsAsync(filter, token);
            },
            cancellationToken);
    }

    private static TableSnapshot Find(PostgreSqlTableSnapshotQueryResult result, string table) =>
        Assert.Single(
            result.Tables,
            snapshot => snapshot.SchemaName == PostgreSqlServerFixture.TableSchema && snapshot.TableName == table);

    private static PostgreSqlSchemaFilter ZooOnly => new([PostgreSqlServerFixture.TableSchema], []);

    // --- The whole relation zoo -----------------------------------------------------------------

    [Fact]
    public async Task Query_ReturnsEveryRelationKindItAdmits()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(ZooOnly, deadline.Token);

        Assert.Equal(
            [
                "events", "events_amer", "events_emea", "events_emea_2026",
                "orders_matview", "orders_view", "orders_with_pk", "orders_without_pk",
                "remote_orders", "scratch_unlogged",
            ],
            result.Tables.Select(snapshot => snapshot.TableName).ToArray());
    }

    [Fact]
    public async Task Query_MapsAnOrdinaryTableWithAPrimaryKey()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "orders_with_pk");

        Assert.Equal(RelationKind.OrdinaryTable, table.RelationKind);
        Assert.False(table.IsPartitionedRoot);
        Assert.False(table.IsPartition);
        Assert.True(table.HasPrimaryKey);
        Assert.True(table.TableSizeBytes > 0);
        Assert.True(table.IndexSizeBytes > 0);
        Assert.True(table.TotalSizeBytes > 0);
    }

    [Fact]
    public async Task Query_MapsAnOrdinaryTableWithoutAPrimaryKey()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "orders_without_pk");

        Assert.Equal(RelationKind.OrdinaryTable, table.RelationKind);
        Assert.False(table.HasPrimaryKey);
        Assert.Equal(0, table.IndexSizeBytes);
    }

    [Fact]
    public async Task Query_MapsAnUnloggedTableAsAnOrdinaryTable()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "scratch_unlogged");

        // relpersistence 'u' is still an ordinary table: only 't' would be temporary.
        Assert.Equal(RelationKind.OrdinaryTable, table.RelationKind);
        Assert.True(table.HasPrimaryKey);
        Assert.True(table.IndexSizeBytes > 0);
    }

    [Fact]
    public async Task Query_MapsAViewWithZeroSizesAndNoEstimate()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "orders_view");

        Assert.Equal(RelationKind.View, table.RelationKind);
        Assert.Null(table.EstimatedRowCount);
        Assert.Equal(0, table.TableSizeBytes);
        Assert.Equal(0, table.IndexSizeBytes);
        Assert.Equal(0, table.TotalSizeBytes);
        Assert.False(table.HasPrimaryKey);
    }

    [Fact]
    public async Task Query_MapsAMaterializedViewWithRealSizes()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "orders_matview");

        Assert.Equal(RelationKind.MaterializedView, table.RelationKind);
        Assert.True(table.TableSizeBytes > 0);
        Assert.True(table.TotalSizeBytes > 0);
    }

    // --- Estimates -------------------------------------------------------------------------------

    [Fact]
    public async Task Query_ReportsANonNegativeEstimateForAnAnalyzedTable()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "orders_with_pk");

        Assert.NotNull(table.EstimatedRowCount);
        Assert.True(table.EstimatedRowCount >= 0);
        Assert.Equal(PostgreSqlServerFixture.AnalyzedRowCount, table.EstimatedRowCount);
    }

    [Fact]
    public async Task Query_ReportsNullForANeverAnalyzedTable()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "orders_without_pk");

        // PostgreSQL reports reltuples = -1 until the first ANALYZE; D001 maps that to NULL, which
        // means "unknown" and never zero.
        Assert.Null(table.EstimatedRowCount);
    }

    [Fact]
    public async Task Query_NeverReportsANegativeEstimate()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(ZooOnly, deadline.Token);

        Assert.All(result.Tables, table => Assert.True(table.EstimatedRowCount is null or >= 0));
    }

    // --- Partitions -------------------------------------------------------------------------------

    [Fact]
    public async Task Query_MapsThePartitionedRoot()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "events");

        Assert.Equal(RelationKind.PartitionedTable, table.RelationKind);
        Assert.True(table.IsPartitionedRoot);
        Assert.False(table.IsPartition);
    }

    [Fact]
    public async Task Query_MapsALeafPartition()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "events_amer");

        Assert.Equal(RelationKind.Partition, table.RelationKind);
        Assert.False(table.IsPartitionedRoot);
        Assert.True(table.IsPartition);
        Assert.True(table.TableSizeBytes > 0);
    }

    [Fact]
    public async Task Query_MapsASubpartitionedPartitionAsAPartition_NotAsARoot()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // events_emea is relkind 'p' *and* relispartition true: partitioned, yet itself a
        // partition. Partition state must win, or the middle of the tree would look like a root.
        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "events_emea");

        Assert.Equal(RelationKind.Partition, table.RelationKind);
        Assert.False(table.IsPartitionedRoot);
        Assert.True(table.IsPartition);
    }

    [Fact]
    public async Task Query_MapsALeafBelowASubpartitionedPartition()
    {
        using CancellationTokenSource deadline = TestDeadline();

        TableSnapshot table = Find(await ComposeAsync(ZooOnly, deadline.Token), "events_emea_2026");

        Assert.Equal(RelationKind.Partition, table.RelationKind);
        Assert.True(table.IsPartition);
        Assert.True(table.TableSizeBytes > 0);
    }

    [Fact]
    public async Task Query_DoesNotAggregateDescendantSizesIntoThePartitionRoot()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(ZooOnly, deadline.Token);

        TableSnapshot root = Find(result, "events");
        TableSnapshot subpartitioned = Find(result, "events_emea");
        TableSnapshot leafAmer = Find(result, "events_amer");
        TableSnapshot leafEmea = Find(result, "events_emea_2026");

        // Every leaf genuinely holds data, and each appears in its own right.
        Assert.True(leafAmer.TotalSizeBytes > 0);
        Assert.True(leafEmea.TotalSizeBytes > 0);

        // The contract is non-aggregation, so the root is compared against what PostgreSQL itself
        // returns for the root's own OID -- not against zero. Freezing zero would turn one
        // version's incidental answer into a requirement.
        (long directTable, long directIndex, long directTotal) = await _fixture.ReadDirectRelationSizesAsync(
            PostgreSqlServerFixture.TableSchema, "events", deadline.Token);

        Assert.Equal(directTable, root.TableSizeBytes);
        Assert.Equal(directIndex, root.IndexSizeBytes);
        Assert.Equal(directTotal, root.TotalSizeBytes);

        // The same holds for the intermediate partition, which is a root of its own subtree.
        (long emeaTable, long emeaIndex, long emeaTotal) = await _fixture.ReadDirectRelationSizesAsync(
            PostgreSqlServerFixture.TableSchema, "events_emea", deadline.Token);

        Assert.Equal(emeaTable, subpartitioned.TableSizeBytes);
        Assert.Equal(emeaIndex, subpartitioned.IndexSizeBytes);
        Assert.Equal(emeaTotal, subpartitioned.TotalSizeBytes);

        // And the adapter added nothing: the root's total is not the descendants' total, on any
        // version where the descendants actually hold data.
        Assert.NotEqual(leafAmer.TotalSizeBytes + leafEmea.TotalSizeBytes, root.TotalSizeBytes);
        Assert.True(root.TotalSizeBytes < leafAmer.TotalSizeBytes + leafEmea.TotalSizeBytes);
    }

    [Fact]
    public async Task Query_ReportsEveryRelationsOwnDirectSizes()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(ZooOnly, deadline.Token);

        // Whatever the relation is -- root, intermediate partition, leaf or ordinary table --
        // D001's three numbers are the three size functions applied to that relation's own OID.
        foreach (string relation in new[] { "events", "events_emea", "events_amer", "events_emea_2026" })
        {
            TableSnapshot snapshot = Find(result, relation);
            (long table, long index, long total) = await _fixture.ReadDirectRelationSizesAsync(
                PostgreSqlServerFixture.TableSchema, relation, deadline.Token);

            Assert.Equal(table, snapshot.TableSizeBytes);
            Assert.Equal(index, snapshot.IndexSizeBytes);
            Assert.Equal(total, snapshot.TotalSizeBytes);
            Assert.True(snapshot.TableSizeBytes >= 0);
            Assert.True(snapshot.IndexSizeBytes >= 0);
            Assert.True(snapshot.TotalSizeBytes >= 0);
        }
    }

    [Fact]
    public async Task Query_ReportsThePartitionTreeAsIndependentRelations()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(ZooOnly, deadline.Token);

        // The structural property that survives any version's size behaviour: the root, the
        // intermediate partition and both leaves each exist exactly once and stand on their own.
        foreach (string relation in new[] { "events", "events_emea", "events_amer", "events_emea_2026" })
        {
            Assert.Single(
                result.Tables,
                snapshot => snapshot.SchemaName == PostgreSqlServerFixture.TableSchema
                    && snapshot.TableName == relation);
        }
    }

    [Fact]
    public async Task Query_CountsEachPartitionExactlyOnce()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(ZooOnly, deadline.Token);

        // No double counting: each relation appears once, so summing the result cannot inflate a
        // partitioned table's footprint.
        Assert.Equal(
            result.Tables.Count,
            result.Tables.Select(table => (table.SchemaName, table.TableName)).Distinct().Count());
    }

    // --- Sizes -------------------------------------------------------------------------------------

    [Fact]
    public async Task Query_NeverReportsANegativeSize()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(ZooOnly, deadline.Token);

        Assert.All(result.Tables, table =>
        {
            Assert.True(table.TableSizeBytes >= 0);
            Assert.True(table.IndexSizeBytes >= 0);
            Assert.True(table.TotalSizeBytes >= 0);
        });
    }

    // --- Filters -----------------------------------------------------------------------------------

    [Fact]
    public async Task AnEmptyFilter_ReturnsEveryEligibleSchema()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result =
            await ComposeAsync(PostgreSqlSchemaFilter.IncludeEverything, deadline.Token);

        string[] schemas = [.. result.Tables.Select(table => table.SchemaName).Distinct()];

        Assert.Contains(PostgreSqlServerFixture.SyntheticSchema, schemas);
        Assert.Contains(PostgreSqlServerFixture.TableSchema, schemas);
        Assert.Contains(PostgreSqlServerFixture.SecondaryTableSchema, schemas);
    }

    [Fact]
    public async Task AnIncludeFilter_ReturnsOnlyTheNamedSchemas()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(
            new PostgreSqlSchemaFilter([PostgreSqlServerFixture.SecondaryTableSchema], []), deadline.Token);

        TableSnapshot only = Assert.Single(result.Tables);
        Assert.Equal(PostgreSqlServerFixture.SecondaryTableSchema, only.SchemaName);
        Assert.Equal("secondary_table", only.TableName);
    }

    [Fact]
    public async Task AnIncludeFilterNamingSeveralSchemas_ReturnsAllOfThem()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(
            new PostgreSqlSchemaFilter(
                [PostgreSqlServerFixture.SecondaryTableSchema, PostgreSqlServerFixture.SyntheticSchema], []),
            deadline.Token);

        Assert.Equal(
            [PostgreSqlServerFixture.SyntheticSchema, PostgreSqlServerFixture.SecondaryTableSchema],
            result.Tables.Select(table => table.SchemaName).Distinct().ToArray());
    }

    [Fact]
    public async Task AnExcludeFilter_RemovesOnlyTheNamedSchemas()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(
            new PostgreSqlSchemaFilter([], [PostgreSqlServerFixture.TableSchema]), deadline.Token);

        Assert.DoesNotContain(
            result.Tables, table => table.SchemaName == PostgreSqlServerFixture.TableSchema);
        Assert.Contains(
            result.Tables, table => table.SchemaName == PostgreSqlServerFixture.SecondaryTableSchema);
    }

    [Fact]
    public async Task IncludeAndExcludeTogether_NarrowTheResult()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(
            new PostgreSqlSchemaFilter(
                [PostgreSqlServerFixture.TableSchema, PostgreSqlServerFixture.SecondaryTableSchema],
                [PostgreSqlServerFixture.SyntheticSchema]),
            deadline.Token);

        Assert.Equal(
            [PostgreSqlServerFixture.TableSchema, PostgreSqlServerFixture.SecondaryTableSchema],
            result.Tables.Select(table => table.SchemaName).Distinct().ToArray());
    }

    [Fact]
    public async Task AFilterMatchingNothing_ReturnsAnEmptyResult()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(
            new PostgreSqlSchemaFilter(["schema_that_does_not_exist"], []), deadline.Token);

        Assert.Empty(result.Tables);
    }

    [Fact]
    public async Task AFilterIsCaseSensitive()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // The schema exists, but not under this spelling.
        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(
            new PostgreSqlSchemaFilter([PostgreSqlServerFixture.TableSchema.ToUpperInvariant()], []), deadline.Token);

        Assert.Empty(result.Tables);
    }

    // --- System and temporary schemas ---------------------------------------------------------------

    [Fact]
    public async Task Query_NeverReturnsASystemSchema()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result =
            await ComposeAsync(PostgreSqlSchemaFilter.IncludeEverything, deadline.Token);

        Assert.All(result.Tables, table =>
        {
            Assert.NotEqual("pg_catalog", table.SchemaName);
            Assert.NotEqual("information_schema", table.SchemaName);
            Assert.False(table.SchemaName.StartsWith("pg_toast", StringComparison.Ordinal));
            Assert.False(table.SchemaName.StartsWith("pg_temp_", StringComparison.Ordinal));
        });
    }

    [Fact]
    public async Task AnIncludeFilterCannotReEnableASystemSchema()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // The mandatory exclusions live in D001's frozen text, so naming a system schema in the
        // include list simply matches nothing.
        PostgreSqlTableSnapshotQueryResult result = await ComposeAsync(
            new PostgreSqlSchemaFilter(["pg_catalog", "information_schema"], []), deadline.Token);

        Assert.Empty(result.Tables);
    }

    [Fact]
    public async Task Query_NeverReturnsATemporaryTable()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result =
            await ComposeAsync(PostgreSqlSchemaFilter.IncludeEverything, deadline.Token);

        // pg_temp_* is excluded, so the TemporaryTable branch is unreachable from a normal query.
        Assert.DoesNotContain(result.Tables, table => table.RelationKind == RelationKind.TemporaryTable);
    }

    // --- Ordering ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Query_OrdersBySchemaThenTable_Ordinally()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult result =
            await ComposeAsync(PostgreSqlSchemaFilter.IncludeEverything, deadline.Token);

        (string Schema, string Table)[] actual =
            [.. result.Tables.Select(table => (table.SchemaName, table.TableName))];
        (string Schema, string Table)[] expected =
            [.. actual.OrderBy(pair => pair.Schema, StringComparer.Ordinal).ThenBy(pair => pair.Table, StringComparer.Ordinal)];

        Assert.Equal(expected, actual);
    }

    // --- Session guarantees -------------------------------------------------------------------------------

    [Fact]
    public async Task Query_LeavesPersistentStateUnchangedAndRollsBack()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        string markerBefore = (await _fixture.ReadControlMarkerAsync(cancellationToken))!;
        (bool schemaBefore, bool tableBefore, long tableCountBefore) = await _fixture.ReadSchemaShapeAsync(cancellationToken);

        await ComposeAsync(PostgreSqlSchemaFilter.IncludeEverything, cancellationToken);

        Assert.Equal(markerBefore, await _fixture.ReadControlMarkerAsync(cancellationToken));
        Assert.Equal(1, await _fixture.ReadControlRowCountAsync(cancellationToken));

        (bool schemaAfter, bool tableAfter, long tableCountAfter) = await _fixture.ReadSchemaShapeAsync(cancellationToken);
        Assert.True(schemaBefore && schemaAfter);
        Assert.True(tableBefore && tableAfter);
        Assert.Equal(tableCountBefore, tableCountAfter);

        await using NpgsqlConnection admin = await _fixture.OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE usename = @role AND state IN ('idle in transaction', 'idle in transaction (aborted)')",
            admin);
        command.Parameters.AddWithValue("role", PostgreSqlServerFixture.InspectionRoleName);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(cancellationToken))!);
    }

    [Fact]
    public async Task Query_LeavesThePoolReusable()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlTableSnapshotQueryResult first = await ComposeAsync(ZooOnly, deadline.Token);
        PostgreSqlTableSnapshotQueryResult second = await ComposeAsync(ZooOnly, deadline.Token);

        Assert.Equal(first.Tables.Count, second.Tables.Count);
        Assert.NotEmpty(second.Tables);
    }

    [Fact]
    public async Task Query_RespectsRealCancellation()
    {
        using CancellationTokenSource deadline = TestDeadline();

        await using PostgreSqlConnectionFactory factory =
            PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync<PostgreSqlTableSnapshotQueryResult>(
                PostgreSqlInspectionSessionOptions.Default,
                async (view, token) =>
                {
                    // Cancel after the session is verified but before D001 runs.
                    await cts.CancelAsync();
                    return await view.ReadTableSnapshotsAsync(PostgreSqlSchemaFilter.IncludeEverything, token);
                },
                cts.Token).AsTask());

        // The pool still works afterwards.
        PostgreSqlTableSnapshotQueryResult afterwards = await ComposeAsync(ZooOnly, deadline.Token);
        Assert.NotEmpty(afterwards.Tables);
    }

    [Fact]
    public async Task Query_ReadsNoBusinessRow()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken,
            observe: true);

        PostgreSqlServerProbeResult probe = await PostgreSqlServerCapabilityProbe.ProbeAsync(
            session.Operations, cancellationToken);
        Assert.Equal(PostgreSqlVersionSupportStatus.Supported, probe.VersionSupport);

        await session.Operations.ReadTableSnapshotsAsync(ZooOnly, cancellationToken);

        // Exactly the statements the composition asked for: session initialization, the probe,
        // and one D001. Nothing read a table's rows.
        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
                PostgreSqlSqlStatementId.CheckUsageStatisticsAccess,
                PostgreSqlSqlStatementId.ReadStatisticsReset,
                PostgreSqlSqlStatementId.ReadTableSnapshots,
            ],
            session.Recorder!.ExecutedStatements);
    }

    [Fact]
    public async Task Query_RunsD001ExactlyOncePerCall()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken,
            observe: true);

        await session.Operations.ReadTableSnapshotsAsync(ZooOnly, cancellationToken);

        Assert.Equal(
            1,
            session.Recorder!.ExecutedStatements.Count(id => id == PostgreSqlSqlStatementId.ReadTableSnapshots));
    }
}
