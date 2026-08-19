using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Snapshots;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The GC-DHI-04F provider against a real PostgreSQL 18.4 server: one capture, one connection, one
/// transaction, one complete <see cref="DatabaseSnapshot"/>.
/// </summary>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class DatabaseSnapshotProviderTests
{
    private readonly PostgreSqlServerFixture _fixture;

    public DatabaseSnapshotProviderTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    private PostgreSqlDatabaseSnapshotProvider CreateProvider() =>
        PostgreSqlDatabaseSnapshotProvider.Create(_fixture.InspectionConnectionString);

    private PostgreSqlDatabaseSnapshotProvider CreateProvider(
        IReadOnlyCollection<string> included, IReadOnlyCollection<string> excluded) =>
        PostgreSqlDatabaseSnapshotProvider.Create(
            _fixture.InspectionConnectionString, included, excluded, TimeSpan.FromSeconds(30));

    // --- Complete snapshot ----------------------------------------------------------------------

    [Fact]
    public async Task Capture_ProducesACompleteSupportedSnapshot()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        Assert.Equal(DatabaseEngine.PostgreSql, snapshot.Metadata.Engine);
        Assert.Equal(PostgreSqlServerFixture.DatabaseName, snapshot.Metadata.DatabaseName);
        Assert.Equal(PostgreSqlServerFixture.InspectionRoleName, snapshot.Metadata.CurrentUser);
        Assert.StartsWith("18", snapshot.Metadata.EngineVersion, StringComparison.Ordinal);

        Assert.Equal(
            CapabilityStatus.Available,
            snapshot.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(
            CapabilityStatus.Disabled,
            snapshot.Capabilities.GetState(CapabilityKind.DataProfiling).Status);

        Assert.NotEmpty(snapshot.Schemas);
        Assert.NotEmpty(snapshot.Tables);
        Assert.NotEmpty(snapshot.Indexes);
    }

    // --- Shared cross-version contract, executed on this major too (GC-DHI-04F-C1, R1-06) ---------

    [Fact]
    public async Task Capture_SatisfiesTheSharedCrossVersionSnapshotContract()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        // The identical helper the PostgreSQL 15 suite calls. Running it from both majors is what
        // makes it a shared contract rather than a 15-only assertion set.
        CrossVersionSnapshotAssertions.AssertSupportedSnapshotShape(snapshot, "18");
    }

    [Fact]
    public async Task Capture_MapsTheCommonIndexZooIdenticallyToTheOlderMajor()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        CrossVersionSnapshotAssertions.AssertCommonIndexZoo(snapshot, PostgreSqlServerFixture.IndexSchema);
    }

    [Fact]
    public async Task Capture_SatisfiesTheSharedTableAndPartitionSemantics()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        CrossVersionSnapshotAssertions.AssertCommonTableSemantics(
            snapshot,
            PostgreSqlServerFixture.IndexSchema,
            PostgreSqlServerFixture.IndexedTable,
            "partitioned_orders",
            "partitioned_orders_emea");
    }

    [Fact]
    public async Task Capture_SatisfiesTheSharedViewSemantics()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        // This fixture builds its view and materialized view in the table schema rather than the
        // index schema; the shared contract is identical either way.
        CrossVersionSnapshotAssertions.AssertCommonViewSemantics(
            snapshot, PostgreSqlServerFixture.TableSchema);
    }

    [Fact]
    public async Task Capture_SatisfiesTheSharedIndexMemberAndValiditySemantics()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        // The member's name, key column, collation and operator class are frozen here from this
        // fixture's own DDL — `partitioned_orders (region text)` indexed by `zoo_partitioned` —
        // and never read back from the capture (GC-DHI-04F-C4, R4-01).
        CrossVersionSnapshotAssertions.AssertCommonIndexMemberSemantics(
            snapshot,
            PostgreSqlServerFixture.IndexSchema,
            "partitioned_orders",
            "zoo_partitioned",
            "partitioned_orders_emea",
            "partitioned_orders_emea_region_idx",
            "region",
            "\"pg_catalog\".\"default\"",
            "\"pg_catalog\".\"text_ops\"");

        CrossVersionSnapshotAssertions.AssertInvalidIndexSemantics(
            snapshot, PostgreSqlServerFixture.IndexSchema, "zoo_invalid_root");
    }

    [Fact]
    public async Task Capture_SatisfiesTheSharedUsageStatisticsAvailableContract()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        CrossVersionSnapshotAssertions.AssertUsageStatisticsAvailable(snapshot);
    }

    [Fact]
    public async Task Capture_SatisfiesTheSharedFilteringSemantics()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        await using PostgreSqlDatabaseSnapshotProvider includedProvider =
            CreateProvider([PostgreSqlServerFixture.IndexSchema], []);
        await using PostgreSqlDatabaseSnapshotProvider excludedProvider =
            CreateProvider([], [PostgreSqlServerFixture.IndexSchema]);
        await using PostgreSqlDatabaseSnapshotProvider everythingProvider = CreateProvider();

        CrossVersionSnapshotAssertions.AssertFilteringSemantics(
            await includedProvider.CaptureAsync(token),
            await excludedProvider.CaptureAsync(token),
            await everythingProvider.CaptureAsync(token),
            PostgreSqlServerFixture.IndexSchema);
    }

    [Fact]
    public async Task Capture_ClosesEveryIndexAgainstATableInTheSameSnapshot()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        var tables = snapshot.Tables.Select(table => (table.SchemaName, table.TableName)).ToHashSet();

        Assert.All(
            snapshot.Indexes,
            index => Assert.Contains((index.SchemaName, index.TableName), tables));
    }

    [Fact]
    public async Task Capture_DerivesSchemasFromTablesAndOrdersEverythingOrdinally()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        string[] schemaNames = [.. snapshot.Schemas.Select(schema => schema.SchemaName)];
        Assert.Equal([.. schemaNames.Order(StringComparer.Ordinal)], schemaNames);

        // Every schema is one a table actually lives in, and every table's schema is present.
        Assert.Equal(
            [.. snapshot.Tables.Select(table => table.SchemaName).Distinct().Order(StringComparer.Ordinal)],
            schemaNames);

        Assert.Equal(
            [.. snapshot.Tables.Select(t => (t.SchemaName, t.TableName)).OrderBy(t => t.SchemaName, StringComparer.Ordinal).ThenBy(t => t.TableName, StringComparer.Ordinal)],
            [.. snapshot.Tables.Select(t => (t.SchemaName, t.TableName))]);

        Assert.Equal(
            [.. snapshot.Indexes.Select(i => (i.SchemaName, i.TableName, i.IndexName)).OrderBy(i => i.SchemaName, StringComparer.Ordinal).ThenBy(i => i.TableName, StringComparer.Ordinal).ThenBy(i => i.IndexName, StringComparer.Ordinal)],
            [.. snapshot.Indexes.Select(i => (i.SchemaName, i.TableName, i.IndexName))]);
    }

    [Fact]
    public async Task Capture_ExcludesSystemSchemas()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        Assert.DoesNotContain(snapshot.Schemas, schema => schema.SchemaName == "pg_catalog");
        Assert.DoesNotContain(snapshot.Schemas, schema => schema.SchemaName == "information_schema");
        Assert.DoesNotContain(snapshot.Schemas, schema => schema.SchemaName.StartsWith("pg_toast", StringComparison.Ordinal));
    }

    // --- Filtering ------------------------------------------------------------------------------

    [Fact]
    public async Task Capture_RespectsTheIncludeFilter()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider =
            CreateProvider([PostgreSqlServerFixture.IndexSchema], []);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        Assert.NotEmpty(snapshot.Schemas);
        Assert.All(snapshot.Schemas, schema => Assert.Equal(PostgreSqlServerFixture.IndexSchema, schema.SchemaName));
        Assert.All(snapshot.Tables, table => Assert.Equal(PostgreSqlServerFixture.IndexSchema, table.SchemaName));
        Assert.All(snapshot.Indexes, index => Assert.Equal(PostgreSqlServerFixture.IndexSchema, index.SchemaName));
    }

    [Fact]
    public async Task Capture_RespectsTheExcludeFilter()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider =
            CreateProvider([], [PostgreSqlServerFixture.IndexSchema]);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        Assert.DoesNotContain(snapshot.Schemas, schema => schema.SchemaName == PostgreSqlServerFixture.IndexSchema);
        Assert.DoesNotContain(snapshot.Tables, table => table.SchemaName == PostgreSqlServerFixture.IndexSchema);
        Assert.DoesNotContain(snapshot.Indexes, index => index.SchemaName == PostgreSqlServerFixture.IndexSchema);
    }

    [Fact]
    public async Task Capture_WithAnEmptyMatchingScope_ReturnsAValidEmptySnapshot()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider(["schema_that_does_not_exist"], []);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        // Complete and valid: metadata and capabilities remain mandatory even with no objects.
        Assert.Empty(snapshot.Schemas);
        Assert.Empty(snapshot.Tables);
        Assert.Empty(snapshot.Indexes);
        Assert.NotNull(snapshot.Metadata);
        Assert.Equal(
            CapabilityStatus.Available,
            snapshot.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status);
    }

    // --- Read-only, rollback and reuse -----------------------------------------------------------

    [Fact]
    public async Task Capture_LeavesPersistentStateUnchangedAndTheSessionReusable()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        string? before = await _fixture.ReadControlMarkerAsync(token);
        long rowsBefore = await _fixture.ReadControlRowCountAsync(token);

        await using (PostgreSqlDatabaseSnapshotProvider provider = CreateProvider())
        {
            // Three sequential captures on one provider: the pool and the data source stay usable.
            _ = await provider.CaptureAsync(token);
            _ = await provider.CaptureAsync(token);
            _ = await provider.CaptureAsync(token);
        }

        Assert.Equal(before, await _fixture.ReadControlMarkerAsync(token));
        Assert.Equal(rowsBefore, await _fixture.ReadControlRowCountAsync(token));
    }

    [Fact]
    public async Task Capture_IsCancellableAndLeavesNoLingeringTransaction()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        // The provider is still usable afterwards, so nothing was left half-open.
        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);
        Assert.NotEmpty(snapshot.Tables);
    }

    [Fact]
    public async Task ConcurrentCaptures_EachProduceACompleteSnapshot()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot[] snapshots = await Task.WhenAll(
            provider.CaptureAsync(deadline.Token),
            provider.CaptureAsync(deadline.Token),
            provider.CaptureAsync(deadline.Token));

        Assert.All(snapshots, snapshot =>
        {
            Assert.NotEmpty(snapshot.Tables);
            Assert.NotEmpty(snapshot.Indexes);
        });

        // Structurally equivalent captures of the same database.
        Assert.All(snapshots, snapshot => Assert.Equal(snapshots[0].Tables.Count, snapshot.Tables.Count));
        Assert.All(snapshots, snapshot => Assert.Equal(snapshots[0].Indexes.Count, snapshot.Indexes.Count));
    }

    [Fact]
    public async Task ACaptureAfterDisposal_IsRejected()
    {
        using CancellationTokenSource deadline = TestDeadline();
        PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        _ = await provider.CaptureAsync(deadline.Token);
        await provider.DisposeAsync();
        await provider.DisposeAsync();

        ObjectDisposedException exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.CaptureAsync(deadline.Token));

        Assert.Equal(nameof(PostgreSqlDatabaseSnapshotProvider), exception.ObjectName);
    }

    // --- Same-session proof (GC-DHI-04F §31) -------------------------------------------------------

    [Fact]
    public async Task EveryCatalogStatement_RunsOnOneBackendInOneScope()
    {
        using CancellationTokenSource deadline = TestDeadline();

        await using PostgreSqlConnectionFactory connectionFactory =
            PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var scope = new SameSessionProofScope(connectionFactory, PostgreSqlSqlInventory.Default);

        // The factory hands out one scope per capture; this capture uses exactly one.
        var createdScopes = new List<SameSessionProofScope>();
        var recordingFactory = new RecordingScopeFactory(scope, createdScopes);

        await using (var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            recordingFactory, PostgreSqlSchemaFilter.IncludeEverything, PostgreSqlInspectionSessionOptions.Default))
        {
            _ = await provider.CaptureAsync(deadline.Token);
        }

        SameSessionProofScope used = Assert.Single(createdScopes);
        IReadOnlyList<ObservedStatement> observed = used.Observed;

        // B001-B003 ran first and were deliberately not probed.
        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
            ],
            observed.Take(3).Select(entry => entry.Id).ToArray());
        Assert.All(observed.Take(3), entry => Assert.Null(entry.BackendProcessId));

        // Every executed C/D/E statement was observed, and all on the same backend.
        ObservedStatement[] probed = [.. observed.Skip(3)];
        Assert.Equal(
            [
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
                PostgreSqlSqlStatementId.CheckUsageStatisticsAccess,
                PostgreSqlSqlStatementId.ReadStatisticsReset,
                PostgreSqlSqlStatementId.ReadTableSnapshots,
                PostgreSqlSqlStatementId.ReadIndexMetadata,
                PostgreSqlSqlStatementId.ReadIndexUsageStatistics,
            ],
            probed.Select(entry => entry.Id).ToArray());

        int[] backends = [.. probed.Select(entry => entry.BackendProcessId!.Value)];
        Assert.Single(backends.Distinct());

        // Reference identity closes the gap that equal PIDs alone would leave: B001-E002 all ran
        // through this one scope's own connection and the transaction begun on it. Recorded while
        // both were still live, since cleanup has since disposed them.
        Assert.NotNull(used.Connection);
        Assert.NotNull(used.Transaction);
        Assert.True(used.TransactionBelongedToConnection);
    }

    [Fact]
    public async Task TheCaptureTransactionIsObservedNonDeferrableOnTheServerItself()
    {
        using CancellationTokenSource deadline = TestDeadline();

        await using PostgreSqlConnectionFactory connectionFactory =
            PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);

        var prototype = new SameSessionProofScope(connectionFactory, PostgreSqlSqlInventory.Default);
        var created = new List<SameSessionProofScope>();

        await using (var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            new RecordingScopeFactory(prototype, created),
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default))
        {
            _ = await provider.CaptureAsync(deadline.Token);
        }

        SameSessionProofScope used = Assert.Single(created);
        ObservedTransactionState state = Assert.IsType<ObservedTransactionState>(used.TransactionState);

        // The same direct observation the PostgreSQL 15 suite makes, kept symmetric so neither
        // major relies on inference (GC-DHI-04F-C2, R1-05).
        Assert.Equal("repeatable read", state.IsolationLevel);
        Assert.Equal("on", state.ReadOnly);
        Assert.Equal("off", state.Deferrable);
        Assert.True(used.TransactionBelongedToConnection);
    }

    private sealed class RecordingScopeFactory : IPostgreSqlInspectionSessionScopeFactory
    {
        private readonly SameSessionProofScope _prototype;
        private readonly List<SameSessionProofScope> _created;

        internal RecordingScopeFactory(SameSessionProofScope prototype, List<SameSessionProofScope> created)
        {
            _prototype = prototype;
            _created = created;
        }

        public IPostgreSqlInspectionSessionScope Create()
        {
            var scope = (SameSessionProofScope)_prototype.Create();
            _created.Add(scope);
            return scope;
        }
    }

    // --- Same-transaction proof (GC-DHI-04F §32) ----------------------------------------------------

    [Fact]
    public async Task Capture_SeesOneTransactionSnapshotAndIgnoresConcurrentCommits()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        const string LateTable = "provider_late_table";
        const string LateIndex = "provider_late_index";
        const string LateIndexOnExisting = "provider_late_on_existing";

        await DropProofObjectsAsync(token);

        await using PostgreSqlConnectionFactory connectionFactory =
            PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var prototype = new SameSessionProofScope(connectionFactory, PostgreSqlSqlInventory.Default);
        var createdScopes = new List<SameSessionProofScope>();

        // Deterministic barriers: the out-of-band commits happen at exact points in the frozen
        // sequence, never on a timer.
        prototype.BeforeStatementAsync = async id =>
        {
            if (id == PostgreSqlSqlStatementId.ReadTableSnapshots)
            {
                await CommitOutOfBandAsync(
                    [
                        $"CREATE TABLE \"{PostgreSqlServerFixture.IndexSchema}\".\"{LateTable}\" (id integer PRIMARY KEY)",
                        $"CREATE INDEX \"{LateIndex}\" ON \"{PostgreSqlServerFixture.IndexSchema}\".\"{LateTable}\" (id)",
                    ],
                    token);
            }
            else if (id == PostgreSqlSqlStatementId.ReadIndexMetadata)
            {
                await CommitOutOfBandAsync(
                    [
                        $"CREATE INDEX \"{LateIndexOnExisting}\" ON \"{PostgreSqlServerFixture.IndexSchema}\".\"{PostgreSqlServerFixture.IndexedTable}\" (amount)",
                    ],
                    token);
            }
        };

        DatabaseSnapshot snapshot;

        try
        {
            await using var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
                new RecordingScopeFactory(prototype, createdScopes),
                PostgreSqlSchemaFilter.IncludeEverything,
                PostgreSqlInspectionSessionOptions.Default);

            snapshot = await provider.CaptureAsync(token);

            // The capture's RepeatableRead snapshot predates both commits, so neither appears.
            Assert.DoesNotContain(snapshot.Tables, table => table.TableName == LateTable);
            Assert.DoesNotContain(snapshot.Indexes, index => index.IndexName == LateIndex);
            Assert.DoesNotContain(snapshot.Indexes, index => index.IndexName == LateIndexOnExisting);

            // A fresh out-of-band observation sees both, proving the commits really happened and
            // that the absence above is isolation rather than a failed setup.
            IReadOnlyList<string> visibleNow = await ReadIndexNamesAsync(token);
            Assert.Contains(LateIndex, visibleNow);
            Assert.Contains(LateIndexOnExisting, visibleNow);
        }
        finally
        {
            await DropProofObjectsAsync(CancellationToken.None);
        }

        async Task DropProofObjectsAsync(CancellationToken cancellationToken) =>
            await CommitOutOfBandAsync(
                [
                    $"DROP INDEX IF EXISTS \"{PostgreSqlServerFixture.IndexSchema}\".\"{LateIndexOnExisting}\"",
                    $"DROP TABLE IF EXISTS \"{PostgreSqlServerFixture.IndexSchema}\".\"{LateTable}\" CASCADE",
                ],
                cancellationToken);
    }

    /// <summary>
    /// Runs administrative, test-only DDL on a separate committed session.
    /// </summary>
    private async Task CommitOutOfBandAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _fixture.OpenAdminConnectionAsync(cancellationToken);

        foreach (string statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<IReadOnlyList<string>> ReadIndexNamesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _fixture.OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT index_relation.relname
            FROM pg_catalog.pg_class AS index_relation
            INNER JOIN pg_catalog.pg_namespace AS index_namespace
                ON index_namespace.oid = index_relation.relnamespace
            WHERE index_namespace.nspname = @schema
              AND index_relation.relkind IN ('i', 'I')
            """,
            connection);
        command.Parameters.AddWithValue("schema", PostgreSqlServerFixture.IndexSchema);

        var names = new List<string>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }
}
