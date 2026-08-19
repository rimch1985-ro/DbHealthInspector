using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Snapshots;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.PostgreSql15;

/// <summary>
/// The GC-DHI-04F provider and the shared 04A–04E contracts on the oldest supported major,
/// PostgreSQL <b>15.18</b>.
/// </summary>
/// <remarks>
/// The assertions come from <see cref="CrossVersionSnapshotAssertions"/>, the same helpers the
/// PostgreSQL 18 suite uses, so the two majors cannot drift apart. Cases that are genuinely 18-only
/// empirical evidence stay in the PostgreSQL 18 suite; nothing here duplicates them.
/// </remarks>
[Collection(PostgreSql15Suite.Name)]
[Trait("Category", "PostgreSql15")]
public sealed class PostgreSql15ProviderTests
{
    private readonly PostgreSql15Fixture _fixture;

    public PostgreSql15ProviderTests(PostgreSql15Fixture fixture) => _fixture = fixture;

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

    // --- Server identity ---------------------------------------------------------------------------

    [Fact]
    public async Task TheFixtureRunsTheExactPinnedMajor()
    {
        using CancellationTokenSource deadline = TestDeadline();

        int versionNumber = await _fixture.ReadServerVersionNumberAsync(deadline.Token);

        // 15.18 exactly: floating postgres:15 is forbidden.
        Assert.Equal(150018, versionNumber);
    }

    // --- Complete snapshot -------------------------------------------------------------------------

    [Fact]
    public async Task Capture_ProducesACompleteSupportedSnapshot()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        CrossVersionSnapshotAssertions.AssertSupportedSnapshotShape(snapshot, "15");

        Assert.Equal(PostgreSql15Fixture.DatabaseName, snapshot.Metadata.DatabaseName);
        Assert.Equal(PostgreSql15Fixture.InspectionRoleName, snapshot.Metadata.CurrentUser);
        Assert.NotEmpty(snapshot.Tables);
        Assert.NotEmpty(snapshot.Indexes);
    }

    [Fact]
    public async Task Capture_MapsTheCommonIndexZooIdenticallyToTheNewerMajor()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        CrossVersionSnapshotAssertions.AssertCommonIndexZoo(snapshot, PostgreSql15Fixture.ObjectSchema);
    }

    [Fact]
    public async Task Capture_EncodesTheRawAttoptionsThisMajorStored()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        // The canonical operator-class identities are already asserted by the shared zoo helper.
        // What this test adds is the missing anchor on *this* major: the raw pg_attribute.attoptions
        // PostgreSQL 15 actually stored. Without it, the 15 suite would only ever compare the mapper
        // to a constant, never to the catalog it claims to encode (GC-DHI-04F-C4, R4-02).
        IReadOnlyList<string> storedAb = await _fixture.ReadOperatorClassOptionsAsync(
            PostgreSql15Fixture.ObjectSchema, "zoo_opts_order_ab", token);
        IReadOnlyList<string> storedBa = await _fixture.ReadOperatorClassOptionsAsync(
            PostgreSql15Fixture.ObjectSchema, "zoo_opts_order_ba", token);

        // Element by element, against the fixture DDL rather than against each other. The DDL writes
        // (n_distinct_per_range=16, false_positive_rate=0.05) for AB and the reverse for BA.
        const string NDistinct = "n_distinct_per_range=16";
        const string FalsePositive = "false_positive_rate=0.05";

        Assert.Equal(2, storedAb.Count);
        Assert.Equal(NDistinct, storedAb[0]);
        Assert.Equal(FalsePositive, storedAb[1]);

        Assert.Equal(2, storedBa.Count);
        Assert.Equal(FalsePositive, storedBa[0]);
        Assert.Equal(NDistinct, storedBa[1]);

        // Same set…
        Assert.Equal(
            storedAb.OrderBy(option => option, StringComparer.Ordinal),
            storedBa.OrderBy(option => option, StringComparer.Ordinal));

        // …genuinely different order. This is the assertion that fails if PostgreSQL 15 ever returns
        // both arrays in the same sequence, which would make the pair prove nothing.
        Assert.NotEqual(storedAb.ToArray(), storedBa.ToArray());

        // The frozen 04E encoding is |options[<count>;<len>:<value><len>:<value>…] with lengths in
        // UTF-16 code units. The lengths are re-derived here from the raw catalog values rather than
        // trusted as the literals 23 and 24, so a wrong hand-count cannot survive.
        Assert.Equal(23, NDistinct.Length);
        Assert.Equal(24, FalsePositive.Length);
        Assert.Equal(NDistinct.Length, storedAb[0].Length);
        Assert.Equal(FalsePositive.Length, storedAb[1].Length);

        const string ExpectedAb =
            "\"pg_catalog\".\"int4_bloom_ops\"|options[2;23:n_distinct_per_range=1624:false_positive_rate=0.05]";
        const string ExpectedBa =
            "\"pg_catalog\".\"int4_bloom_ops\"|options[2;24:false_positive_rate=0.0523:n_distinct_per_range=16]";

        // The bridge: raw catalog values -> independently spelled canonical identity -> the identity
        // the provider actually mapped. Neither expectation is produced by the product encoder.
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();
        DatabaseSnapshot snapshot = await provider.CaptureAsync(token);

        string mappedAb = MapCanonical(snapshot, "zoo_opts_order_ab");
        string mappedBa = MapCanonical(snapshot, "zoo_opts_order_ba");

        Assert.Equal(ExpectedAb, mappedAb);
        Assert.Equal(ExpectedBa, mappedBa);
        Assert.NotEqual(mappedAb, mappedBa);

        // Each canonical identity carries its own raw values in the stored order, so the pairing of
        // raw array to mapped identity cannot be swapped without failing.
        Assert.Equal(
            $"|options[2;{storedAb[0].Length}:{storedAb[0]}{storedAb[1].Length}:{storedAb[1]}]",
            mappedAb["\"pg_catalog\".\"int4_bloom_ops\"".Length..]);
        Assert.Equal(
            $"|options[2;{storedBa[0].Length}:{storedBa[0]}{storedBa[1].Length}:{storedBa[1]}]",
            mappedBa["\"pg_catalog\".\"int4_bloom_ops\"".Length..]);
    }

    private static string MapCanonical(DatabaseSnapshot snapshot, string indexName)
    {
        IndexSnapshot index = Assert.Single(
            snapshot.Indexes,
            candidate => candidate.SchemaName == PostgreSql15Fixture.ObjectSchema
                && candidate.IndexName == indexName);

        return Assert.Single(index.KeyParts).OperatorClass!;
    }

    [Fact]
    public async Task Capture_MapsOrdinaryPartitionedAndMemberRelations()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        TableSnapshot Table(string name) =>
            Assert.Single(snapshot.Tables,
                table => table.SchemaName == PostgreSql15Fixture.ObjectSchema && table.TableName == name);

        Assert.Equal(RelationKind.OrdinaryTable, Table(PostgreSql15Fixture.IndexedTable).RelationKind);
        Assert.Equal(RelationKind.View, Table("orders_view").RelationKind);
        Assert.Equal(RelationKind.MaterializedView, Table("orders_matview").RelationKind);

        TableSnapshot root = Table("partitioned_orders");
        Assert.Equal(RelationKind.PartitionedTable, root.RelationKind);
        Assert.True(root.IsPartitionedRoot);
        Assert.False(root.IsPartition);

        TableSnapshot member = Table("partitioned_orders_emea");
        Assert.Equal(RelationKind.Partition, member.RelationKind);
        Assert.True(member.IsPartition);
        Assert.False(member.IsPartitionedRoot);
    }

    [Fact]
    public async Task Capture_MapsThePartitionedIndexRootAndItsPhysicalPartition()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        IndexSnapshot root = Assert.Single(
            snapshot.Indexes,
            index => index.SchemaName == PostgreSql15Fixture.ObjectSchema
                && index.TableName == "partitioned_orders"
                && index.IndexName == "zoo_partitioned");

        // A virtual root has no storage of its own and never aggregates its partitions'.
        Assert.Equal(0, root.SizeBytes);
        Assert.Null(root.ScanCount);

        // Its physical member exists in its own right, against its own table.
        Assert.Contains(
            snapshot.Indexes,
            index => index.TableName == "partitioned_orders_emea" && index.SizeBytes >= 0);
    }

    [Fact]
    public async Task Capture_ReportsTheDeterministicallyInvalidIndex()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        IndexSnapshot invalid = Assert.Single(
            snapshot.Indexes, index => index.IndexName == "zoo_invalid_root");

        // Reported, never suppressed, and the three flags stay independent.
        Assert.False(invalid.IsValid);
        Assert.Equal(0, invalid.SizeBytes);
        Assert.Null(invalid.ScanCount);
    }

    // --- Filtering ------------------------------------------------------------------------------------

    [Fact]
    public async Task Capture_RespectsIncludeAndExcludeFilters()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        await using (PostgreSqlDatabaseSnapshotProvider included =
            CreateProvider([PostgreSql15Fixture.ObjectSchema], []))
        {
            DatabaseSnapshot snapshot = await included.CaptureAsync(token);
            Assert.All(snapshot.Tables, table => Assert.Equal(PostgreSql15Fixture.ObjectSchema, table.SchemaName));
            Assert.NotEmpty(snapshot.Tables);
        }

        await using PostgreSqlDatabaseSnapshotProvider excluded =
            CreateProvider([], [PostgreSql15Fixture.ObjectSchema]);

        DatabaseSnapshot without = await excluded.CaptureAsync(token);
        Assert.DoesNotContain(without.Tables, table => table.SchemaName == PostgreSql15Fixture.ObjectSchema);
    }

    [Fact]
    public async Task Capture_ExcludesSystemSchemasAndAnEmptyScopeIsValid()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        await using (PostgreSqlDatabaseSnapshotProvider everything = CreateProvider())
        {
            DatabaseSnapshot snapshot = await everything.CaptureAsync(token);
            Assert.DoesNotContain(snapshot.Schemas, schema => schema.SchemaName == "pg_catalog");
            Assert.DoesNotContain(snapshot.Schemas, schema => schema.SchemaName == "information_schema");
        }

        await using PostgreSqlDatabaseSnapshotProvider empty = CreateProvider(["schema_that_does_not_exist"], []);

        DatabaseSnapshot none = await empty.CaptureAsync(token);
        Assert.Empty(none.Schemas);
        Assert.Empty(none.Tables);
        Assert.Empty(none.Indexes);
        Assert.NotNull(none.Metadata);
    }

    // --- Capability branches ---------------------------------------------------------------------------

    [Fact]
    public async Task Capture_SatisfiesTheSharedTableAndPartitionSemantics()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        CrossVersionSnapshotAssertions.AssertCommonTableSemantics(
            snapshot,
            PostgreSql15Fixture.ObjectSchema,
            PostgreSql15Fixture.IndexedTable,
            "partitioned_orders",
            "partitioned_orders_emea");
    }

    [Fact]
    public async Task Capture_SatisfiesTheSharedViewSemantics()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        CrossVersionSnapshotAssertions.AssertCommonViewSemantics(
            snapshot, PostgreSql15Fixture.ObjectSchema);
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
            PostgreSql15Fixture.ObjectSchema,
            "partitioned_orders",
            "zoo_partitioned",
            "partitioned_orders_emea",
            "partitioned_orders_emea_region_idx",
            "region",
            "\"pg_catalog\".\"default\"",
            "\"pg_catalog\".\"text_ops\"");

        CrossVersionSnapshotAssertions.AssertInvalidIndexSemantics(
            snapshot, PostgreSql15Fixture.ObjectSchema, "zoo_invalid_root");
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
            CreateProvider([PostgreSql15Fixture.ObjectSchema], []);
        await using PostgreSqlDatabaseSnapshotProvider excludedProvider =
            CreateProvider([], [PostgreSql15Fixture.ObjectSchema]);
        await using PostgreSqlDatabaseSnapshotProvider everythingProvider = CreateProvider();

        CrossVersionSnapshotAssertions.AssertFilteringSemantics(
            await includedProvider.CaptureAsync(token),
            await excludedProvider.CaptureAsync(token),
            await everythingProvider.CaptureAsync(token),
            PostgreSql15Fixture.ObjectSchema);
    }

    [Fact]
    public async Task Capture_WithStatisticsUnavailable_SkipsE002AndNullsEveryScanCount()
    {
        using CancellationTokenSource deadline = TestDeadline();

        await using var provider = PostgreSqlDatabaseSnapshotProvider.Create(
            _fixture.StatisticsRevokedConnectionString);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);

        CrossVersionSnapshotAssertions.AssertUsageStatisticsUnavailable(snapshot);
    }

    // --- Read-only, rollback, reuse ------------------------------------------------------------------------

    [Fact]
    public async Task Capture_IsReadOnlyRollsBackAndLeavesTheSessionReusable()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        long rowsBefore = await ReadRowCountAsync(token);

        await using (PostgreSqlDatabaseSnapshotProvider provider = CreateProvider())
        {
            _ = await provider.CaptureAsync(token);
            _ = await provider.CaptureAsync(token);
        }

        Assert.Equal(rowsBefore, await ReadRowCountAsync(token));
    }

    [Fact]
    public async Task Capture_IsCancellableAndTheProviderStaysUsable()
    {
        using CancellationTokenSource deadline = TestDeadline();
        await using PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        DatabaseSnapshot snapshot = await provider.CaptureAsync(deadline.Token);
        Assert.NotEmpty(snapshot.Tables);
    }

    [Fact]
    public async Task ConcurrentCaptures_AndPostDisposalRejection()
    {
        using CancellationTokenSource deadline = TestDeadline();
        PostgreSqlDatabaseSnapshotProvider provider = CreateProvider();

        DatabaseSnapshot[] snapshots = await Task.WhenAll(
            provider.CaptureAsync(deadline.Token),
            provider.CaptureAsync(deadline.Token));

        Assert.All(snapshots, snapshot => Assert.NotEmpty(snapshot.Tables));

        await provider.DisposeAsync();
        await provider.DisposeAsync();

        ObjectDisposedException exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.CaptureAsync(deadline.Token));
        Assert.Equal(nameof(PostgreSqlDatabaseSnapshotProvider), exception.ObjectName);
    }

    // --- Same-session proof on this major -----------------------------------------------------------------

    [Fact]
    public async Task EveryCatalogStatement_RunsOnOneBackendInOneScope()
    {
        using CancellationTokenSource deadline = TestDeadline();

        await using PostgreSqlConnectionFactory connectionFactory =
            PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);

        var prototype = new SameSessionProofScope(connectionFactory, PostgreSqlSqlInventory.Default);
        var created = new List<SameSessionProofScope>();

        await using (var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            new ProofScopeFactory(prototype, created),
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default))
        {
            _ = await provider.CaptureAsync(deadline.Token);
        }

        SameSessionProofScope used = Assert.Single(created);
        IReadOnlyList<ObservedStatement> observed = used.Observed;

        // B001-B003 first, unprobed, so SET TRANSACTION READ ONLY stays the first statement.
        Assert.All(observed.Take(3), entry => Assert.Null(entry.BackendProcessId));

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

        Assert.Single(probed.Select(entry => entry.BackendProcessId!.Value).Distinct());
        Assert.True(used.TransactionBelongedToConnection);
    }

    // --- Same-transaction proof on this major -------------------------------------------------------------

    [Fact]
    public async Task Capture_IgnoresCommitsMadeAtBothTransactionBarriers()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        const string LateTable = "pg15_late_table";
        const string LateIndex = "pg15_late_index";
        const string LateIndexOnExisting = "pg15_late_on_existing";

        await CleanupAsync(CancellationToken.None);

        await using PostgreSqlConnectionFactory connectionFactory =
            PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);

        // Two deterministic barriers, semantically identical to the PostgreSQL 18 proof
        // (GC-DHI-04F-C1, R1-05). No sleep and no timing assertion is involved.
        var prototype = new SameSessionProofScope(connectionFactory, PostgreSqlSqlInventory.Default)
        {
            BeforeStatementAsync = async id =>
            {
                if (id == PostgreSqlSqlStatementId.ReadTableSnapshots)
                {
                    // Barrier 1 — after C004, before D001: a brand new table and its index.
                    await RunAdminAsync(
                        [
                            $"CREATE TABLE \"{PostgreSql15Fixture.ObjectSchema}\".\"{LateTable}\" (id integer PRIMARY KEY)",
                            $"CREATE INDEX \"{LateIndex}\" ON \"{PostgreSql15Fixture.ObjectSchema}\".\"{LateTable}\" (id)",
                        ],
                        token);
                }
                else if (id == PostgreSqlSqlStatementId.ReadIndexMetadata)
                {
                    // Barrier 2 — after D001, before E001: a new index on a table that already
                    // existed when the capture's snapshot was established. This is the case a
                    // single barrier cannot cover, because the table is visible but the index is
                    // not, so E001/E002 reconciliation is what must refuse it.
                    await RunAdminAsync(
                        [
                            $"CREATE INDEX \"{LateIndexOnExisting}\" ON \"{PostgreSql15Fixture.ObjectSchema}\".\"{PostgreSql15Fixture.IndexedTable}\" (amount)",
                        ],
                        token);
                }
            },
        };

        var created = new List<SameSessionProofScope>();

        try
        {
            await using var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
                new ProofScopeFactory(prototype, created),
                PostgreSqlSchemaFilter.IncludeEverything,
                PostgreSqlInspectionSessionOptions.Default);

            DatabaseSnapshot snapshot = await provider.CaptureAsync(token);

            // The RepeatableRead snapshot predates both commits, so none of the three objects
            // appears — including the second-barrier index on a pre-existing, visible table.
            Assert.DoesNotContain(snapshot.Tables, table => table.TableName == LateTable);
            Assert.DoesNotContain(snapshot.Indexes, index => index.IndexName == LateIndex);
            Assert.DoesNotContain(snapshot.Indexes, index => index.IndexName == LateIndexOnExisting);

            // Its table *was* in the snapshot, which is what makes the second barrier meaningful:
            // the index was withheld by isolation, not by the table being absent.
            Assert.Contains(snapshot.Tables, table => table.TableName == PostgreSql15Fixture.IndexedTable);

            // E001 and E002 ran inside the same scope and transaction as everything else, so the
            // withheld index could not have entered through statistics reconciliation either.
            SameSessionProofScope used = Assert.Single(created);
            Assert.True(used.TransactionBelongedToConnection);
            Assert.Contains(used.Observed, entry => entry.Id == PostgreSqlSqlStatementId.ReadIndexMetadata);
            Assert.Contains(used.Observed, entry => entry.Id == PostgreSqlSqlStatementId.ReadIndexUsageStatistics);
            Assert.Single(used.Observed
                .Where(entry => entry.BackendProcessId is not null)
                .Select(entry => entry.BackendProcessId!.Value)
                .Distinct());

            // All three commits really happened: a fresh observation sees every one of them.
            await using var after = CreateProvider();
            DatabaseSnapshot later = await after.CaptureAsync(token);
            Assert.Contains(later.Tables, table => table.TableName == LateTable);
            Assert.Contains(later.Indexes, index => index.IndexName == LateIndex);
            Assert.Contains(later.Indexes, index => index.IndexName == LateIndexOnExisting);
        }
        finally
        {
            await CleanupAsync(CancellationToken.None);
        }

        async Task CleanupAsync(CancellationToken cancellationToken) =>
            await RunAdminAsync(
                [
                    $"DROP INDEX IF EXISTS \"{PostgreSql15Fixture.ObjectSchema}\".\"{LateIndexOnExisting}\"",
                    $"DROP TABLE IF EXISTS \"{PostgreSql15Fixture.ObjectSchema}\".\"{LateTable}\" CASCADE",
                ],
                cancellationToken);
    }

    // --- Read-only safety on this major (GC-DHI-04F-C1, R1-05) ---------------------------------------------

    [Fact]
    public async Task TheCaptureSessionIsRepeatableReadAndReadOnly()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            token);

        // The verified B003 state the provider's own runner requires, observed on this major.
        Assert.True(session.State.IsReadOnly);
        Assert.Equal("repeatable read", session.State.IsolationLevel);
        Assert.True(session.State.StatementTimeoutMatches);
        Assert.True(session.State.LockTimeoutMatches);
        Assert.True(session.State.IdleInTransactionTimeoutMatches);
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
            new ProofScopeFactory(prototype, created),
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default))
        {
            _ = await provider.CaptureAsync(deadline.Token);
        }

        SameSessionProofScope used = Assert.Single(created);
        ObservedTransactionState state = Assert.IsType<ObservedTransactionState>(used.TransactionState);

        // Read from the capture's own live transaction, not inferred from the API that set it.
        Assert.Equal("repeatable read", state.IsolationLevel);
        Assert.Equal("on", state.ReadOnly);
        Assert.Equal("off", state.Deferrable);

        // And it really was the capture's transaction on the capture's connection.
        Assert.True(used.TransactionBelongedToConnection);
    }

    [Fact]
    public async Task AWriteInsideTheInspectionSession_IsRejectedByReadOnlyEnforcement()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        long before = await ReadRowCountAsync(token);

        await using (TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            token))
        {
            // Test-owned write attempt: the role is deliberately able to write, so a rejection
            // proves read-only enforcement rather than a missing privilege.
            await using NpgsqlCommand command = session.CreateTestOnlyCommand(
                $"DELETE FROM \"{PostgreSql15Fixture.ObjectSchema}\".\"{PostgreSql15Fixture.IndexedTable}\"");

            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(token));

            // 25006 read_only_sql_transaction, on this major too.
            Assert.Equal("25006", exception.SqlState);
        }

        Assert.Equal(before, await ReadRowCountAsync(token));
    }

    [Fact]
    public async Task AfterManyCapturesThePoolIsStillReusableAndStateUnchanged()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        long before = await ReadRowCountAsync(token);

        await using (PostgreSqlDatabaseSnapshotProvider provider = CreateProvider())
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                DatabaseSnapshot snapshot = await provider.CaptureAsync(token);
                Assert.NotEmpty(snapshot.Tables);
            }
        }

        // Rollback left nothing behind and the pool is still healthy.
        Assert.Equal(before, await ReadRowCountAsync(token));

        await using PostgreSqlDatabaseSnapshotProvider reused = CreateProvider();
        Assert.NotEmpty((await reused.CaptureAsync(token)).Tables);
    }

    private sealed class ProofScopeFactory : IPostgreSqlInspectionSessionScopeFactory
    {
        private readonly SameSessionProofScope _prototype;
        private readonly List<SameSessionProofScope> _created;

        internal ProofScopeFactory(SameSessionProofScope prototype, List<SameSessionProofScope> created)
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

    private async Task RunAdminAsync(IReadOnlyList<string> statements, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _fixture.OpenAdminConnectionAsync(cancellationToken);

        foreach (string statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            _ = await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private async Task<long> ReadRowCountAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await _fixture.OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            $"SELECT count(*) FROM \"{PostgreSql15Fixture.ObjectSchema}\".\"{PostgreSql15Fixture.IndexedTable}\"",
            connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
