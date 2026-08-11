using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Indexes;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// E001 and E002 against a real PostgreSQL 18.4 server holding one index of every shape the gate
/// requires (GC-DHI-04E §25), driven through the exact production path: connection factory →
/// verified session → capability probe → typed index-snapshot operation.
/// </summary>
/// <remarks>
/// The probe runs first and its verdict is required before the operation is called, and its
/// usage-statistics verdict is what decides whether E002 runs at all. GC-DHI-04E implements the
/// query and the mapper, not the provider, so that ordering is enforced <b>here</b>, by the test
/// composition — the operation view deliberately does not police it. GC-DHI-04F owns the
/// productive sequencing.
/// </remarks>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class IndexSnapshotQueryTests
{
    private readonly PostgreSqlServerFixture _fixture;

    public IndexSnapshotQueryTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    private static PostgreSqlSchemaFilter ZooOnly =>
        new([PostgreSqlServerFixture.IndexSchema], []);

    /// <summary>
    /// The composed, test-owned sequence: verify the session, probe, require a supported server
    /// with reachable catalog metadata, and only then read index snapshots. The statistics
    /// capability decides whether E002 runs.
    /// </summary>
    private async Task<(PostgreSqlIndexSnapshotQueryResult Result, bool StatisticsAvailable)> ComposeAsync(
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

                bool statisticsAvailable =
                    probe.Capabilities.GetState(CapabilityKind.UsageStatistics).Status == CapabilityStatus.Available;

                PostgreSqlIndexSnapshotQueryResult result =
                    await view.ReadIndexSnapshotsAsync(filter, statisticsAvailable, token);

                return (result, statisticsAvailable);
            },
            cancellationToken);
    }

    private async Task<PostgreSqlIndexSnapshotQueryResult> ZooAsync(CancellationToken cancellationToken) =>
        (await ComposeAsync(ZooOnly, cancellationToken)).Result;

    private static IndexSnapshot Find(PostgreSqlIndexSnapshotQueryResult result, string indexName) =>
        Assert.Single(result.Indexes, index => index.IndexName == indexName);

    // --- Access methods and basic shapes --------------------------------------------------------

    [Fact]
    public async Task Query_MapsASimpleBtreeIndex()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot index = Find(await ZooAsync(deadline.Token), "zoo_btree_simple");

        Assert.Equal(PostgreSqlServerFixture.IndexSchema, index.SchemaName);
        Assert.Equal(PostgreSqlServerFixture.IndexedTable, index.TableName);
        Assert.Equal("btree", index.AccessMethod);
        IndexKeyPartSnapshot part = Assert.Single(index.KeyParts);
        Assert.Equal("amount", part.ColumnName);
        Assert.Null(part.Expression);
        Assert.True(index.SizeBytes > 0);
        Assert.True(index.IsValid);
        Assert.True(index.IsReady);
        Assert.True(index.IsLive);
    }

    [Fact]
    public async Task Query_MapsAMulticolumnIndexWithPerKeyOrdering()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot index = Find(await ZooAsync(deadline.Token), "zoo_btree_multi");

        Assert.Equal(2, index.KeyParts.Count);
        Assert.Equal([1, 2], index.KeyParts.Select(part => part.Position).ToArray());

        Assert.Equal("amount", index.KeyParts[0].ColumnName);
        Assert.Equal(IndexSortDirection.Ascending, index.KeyParts[0].SortDirection);
        Assert.Equal(IndexNullsOrdering.Last, index.KeyParts[0].NullsOrdering);

        Assert.Equal("quantity", index.KeyParts[1].ColumnName);
        Assert.Equal(IndexSortDirection.Descending, index.KeyParts[1].SortDirection);
        Assert.Equal(IndexNullsOrdering.First, index.KeyParts[1].NullsOrdering);
    }

    [Fact]
    public async Task Query_MapsThePrimaryKeyIndex()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot index = Find(await ZooAsync(deadline.Token), "indexed_orders_pkey");

        Assert.True(index.IsPrimaryKey);
        Assert.True(index.IsUnique);
        Assert.True(index.BacksConstraint);
        Assert.False(index.NullsNotDistinct);
    }

    [Fact]
    public async Task Query_MapsAUniqueConstraintBackedIndex()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot index = Find(await ZooAsync(deadline.Token), "indexed_orders_code_key");

        Assert.True(index.IsUnique);
        Assert.True(index.BacksConstraint);
        Assert.False(index.IsPrimaryKey);
    }

    [Fact]
    public async Task Query_MapsAnExclusionConstraintBackedIndex()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot index = Find(await ZooAsync(deadline.Token), "indexed_orders_span_excl");

        // An exclusion constraint is backed by a GiST index that is not unique.
        Assert.True(index.BacksConstraint);
        Assert.False(index.IsUnique);
        Assert.False(index.IsPrimaryKey);
        Assert.Null(index.NullsNotDistinct);
        Assert.Equal("gist", index.AccessMethod);
    }

    [Fact]
    public async Task Query_MapsAUniqueIndexDeclaringNullsNotDistinct()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot index = Find(await ZooAsync(deadline.Token), "zoo_unique_nnd");

        Assert.True(index.IsUnique);
        Assert.True(index.NullsNotDistinct);
        Assert.False(index.BacksConstraint);
    }

    [Fact]
    public async Task Query_MapsIncludedColumnsInAttributeOrder()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot index = Find(await ZooAsync(deadline.Token), "zoo_include");

        Assert.Single(index.KeyParts);
        Assert.Equal("amount", index.KeyParts[0].ColumnName);

        // Declared INCLUDE (quantity, code): stored order is preserved, not alphabetised.
        Assert.Equal(["quantity", "code"], index.IncludedColumns.ToArray());
    }

    [Fact]
    public async Task Query_MapsAnExpressionIndex()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot index = Find(await ZooAsync(deadline.Token), "zoo_expression");

        IndexKeyPartSnapshot part = Assert.Single(index.KeyParts);
        Assert.Null(part.ColumnName);
        Assert.NotNull(part.Expression);
        Assert.Contains("lower", part.Expression, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_MapsAMixedColumnAndExpressionIndex()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot index = Find(await ZooAsync(deadline.Token), "zoo_mixed");

        Assert.Equal(2, index.KeyParts.Count);
        Assert.Equal("amount", index.KeyParts[0].ColumnName);
        Assert.Null(index.KeyParts[0].Expression);
        Assert.Null(index.KeyParts[1].ColumnName);
        Assert.NotNull(index.KeyParts[1].Expression);
    }

    [Fact]
    public async Task Query_MapsAPartialIndexPredicate()
    {
        using CancellationTokenSource deadline = TestDeadline();
        PostgreSqlIndexSnapshotQueryResult result = await ZooAsync(deadline.Token);

        IndexSnapshot partial = Find(result, "zoo_partial");
        Assert.NotNull(partial.PartialPredicate);
        Assert.Contains("quantity", partial.PartialPredicate, StringComparison.Ordinal);

        // A non-partial index reports no predicate at all.
        Assert.Null(Find(result, "zoo_btree_simple").PartialPredicate);
    }

    [Fact]
    public async Task Query_MapsAnExplicitCollationAsAQualifiedIdentity()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexKeyPartSnapshot part = Assert.Single(Find(await ZooAsync(deadline.Token), "zoo_collation").KeyParts);

        Assert.Equal("\"pg_catalog\".\"C\"", part.Collation);
    }

    [Fact]
    public async Task Query_MapsANonDefaultOperatorClassAsAQualifiedIdentity()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexKeyPartSnapshot part = Assert.Single(Find(await ZooAsync(deadline.Token), "zoo_opclass").KeyParts);

        Assert.Equal("\"pg_catalog\".\"text_pattern_ops\"", part.OperatorClass);
    }

    [Theory]
    [InlineData("zoo_hash", "hash")]
    [InlineData("zoo_gin", "gin")]
    [InlineData("zoo_gist", "gist")]
    [InlineData("zoo_spgist", "spgist")]
    [InlineData("zoo_brin", "brin")]
    public async Task Query_PreservesTheAccessMethodNameVerbatim(string indexName, string accessMethod)
    {
        using CancellationTokenSource deadline = TestDeadline();

        Assert.Equal(accessMethod, Find(await ZooAsync(deadline.Token), indexName).AccessMethod);
    }

    [Theory]
    [InlineData("zoo_hash")]
    [InlineData("zoo_gin")]
    [InlineData("zoo_brin")]
    public async Task Query_NormalizesNonOrderableKeysToAscendingNullsLast(string indexName)
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexKeyPartSnapshot part = Assert.Single(Find(await ZooAsync(deadline.Token), indexName).KeyParts);

        // These access methods report no ordering at all; the pair is a normalization token, not a
        // claim that the server said "ascending".
        Assert.Equal(IndexSortDirection.Ascending, part.SortDirection);
        Assert.Equal(IndexNullsOrdering.Last, part.NullsOrdering);
    }

    // --- Operator-class options (GC-DHI-04E §15) ------------------------------------------------

    [Fact]
    public async Task Query_DistinguishesOperatorClassOptionsWithTheSameOpclass()
    {
        using CancellationTokenSource deadline = TestDeadline();
        PostgreSqlIndexSnapshotQueryResult result = await ZooAsync(deadline.Token);

        string thirtyTwo = Assert.Single(Find(result, "zoo_opts_32").KeyParts).OperatorClass!;
        string sixtyFour = Assert.Single(Find(result, "zoo_opts_64").KeyParts).OperatorClass!;

        // Same operator class, different stored attoptions, therefore different structural
        // identities. Without the options suffix these two would be indistinguishable.
        Assert.StartsWith("\"pg_catalog\".\"int4_minmax_multi_ops\"", thirtyTwo, StringComparison.Ordinal);
        Assert.StartsWith("\"pg_catalog\".\"int4_minmax_multi_ops\"", sixtyFour, StringComparison.Ordinal);
        Assert.NotEqual(thirtyTwo, sixtyFour);

        Assert.Equal(
            "\"pg_catalog\".\"int4_minmax_multi_ops\"|options[1;19:values_per_range=32]",
            thirtyTwo);
        Assert.Equal(
            "\"pg_catalog\".\"int4_minmax_multi_ops\"|options[1;19:values_per_range=64]",
            sixtyFour);
    }

    [Fact]
    public async Task Query_DistinguishesTheSameOptionsStoredInOppositeOrder()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        // Both indexes use the same built-in operator class with the same two option names and the
        // same two values; only the order the DDL supplied them differs. This is the case that
        // separates "preserves stored order" from "sorts the options" — with sorting, the two
        // identities would collapse into one (GC-DHI-04E-C1, R1-04).
        IReadOnlyList<string> storedAb = await _fixture.ReadOperatorClassOptionsAsync(
            PostgreSqlServerFixture.IndexSchema, "zoo_opts_order_ab", token);
        IReadOnlyList<string> storedBa = await _fixture.ReadOperatorClassOptionsAsync(
            PostgreSqlServerFixture.IndexSchema, "zoo_opts_order_ba", token);

        // Same element set...
        Assert.Equal(storedAb.OrderBy(o => o, StringComparer.Ordinal), storedBa.OrderBy(o => o, StringComparer.Ordinal));

        // ...stored in a genuinely different order, straight from pg_attribute.attoptions.
        Assert.Equal(2, storedAb.Count);
        Assert.Equal(2, storedBa.Count);
        Assert.NotEqual(storedAb.ToArray(), storedBa.ToArray());

        PostgreSqlIndexSnapshotQueryResult result = await ZooAsync(token);
        string mappedAb = Assert.Single(Find(result, "zoo_opts_order_ab").KeyParts).OperatorClass!;
        string mappedBa = Assert.Single(Find(result, "zoo_opts_order_ba").KeyParts).OperatorClass!;

        // Same base operator class...
        Assert.StartsWith("\"pg_catalog\".\"int4_bloom_ops\"", mappedAb, StringComparison.Ordinal);
        Assert.StartsWith("\"pg_catalog\".\"int4_bloom_ops\"", mappedBa, StringComparison.Ordinal);

        // ...different structural identity, purely because the stored order differs.
        Assert.NotEqual(mappedAb, mappedBa);

        // And the canonical order is exactly the stored order, element for element.
        Assert.Equal(Encode(storedAb), mappedAb);
        Assert.Equal(Encode(storedBa), mappedBa);
    }

    /// <summary>
    /// Rebuilds the expected canonical identity from raw catalog values, independently of the
    /// mapper, so the assertion compares against the catalog rather than against itself.
    /// </summary>
    private static string Encode(IReadOnlyList<string> storedOptions)
    {
        var builder = new System.Text.StringBuilder("\"pg_catalog\".\"int4_bloom_ops\"|options[");
        builder.Append(storedOptions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(';');
        foreach (string option in storedOptions)
        {
            builder.Append(option.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
            builder.Append(':');
            builder.Append(option);
        }

        builder.Append(']');
        return builder.ToString();
    }

    [Fact]
    public async Task Query_AddsNoOptionsSuffixWhenTheServerStoresNone()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // The same access method without options must not gain an options marker.
        string withoutOptions = Assert.Single(Find(await ZooAsync(deadline.Token), "zoo_brin").KeyParts).OperatorClass!;

        Assert.DoesNotContain("|options[", withoutOptions, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Query_MatchesTheRawAttoptionsTheServerStored()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken token = deadline.Token;

        // Read attoptions out of band and confirm the mapped identity encodes exactly them, in the
        // order the catalog stores.
        IReadOnlyList<string> stored = await _fixture.ReadOperatorClassOptionsAsync(
            PostgreSqlServerFixture.IndexSchema, "zoo_opts_32", token);

        Assert.Equal(["values_per_range=32"], stored.ToArray());

        string mapped = Assert.Single(Find(await ZooAsync(token), "zoo_opts_32").KeyParts).OperatorClass!;
        Assert.EndsWith($"|options[{stored.Count};{stored[0].Length}:{stored[0]}]", mapped, StringComparison.Ordinal);
    }

    // --- Partitioned indexes --------------------------------------------------------------------

    [Fact]
    public async Task Query_MapsAPartitionedIndexRootAsVirtualWithZeroSize()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexSnapshot root = Find(await ZooAsync(deadline.Token), "zoo_partitioned");

        // A partitioned index root has no storage of its own and never aggregates its children's.
        Assert.Equal(0, root.SizeBytes);
        Assert.Null(root.ScanCount);
    }

    [Fact]
    public async Task Query_MapsThePhysicalIndexPartitionIndependently()
    {
        using CancellationTokenSource deadline = TestDeadline();
        PostgreSqlIndexSnapshotQueryResult result = await ZooAsync(deadline.Token);

        IndexSnapshot child = Assert.Single(
            result.Indexes,
            index => index.TableName == "partitioned_orders_emea" && index.IndexName.Contains("region", StringComparison.Ordinal));

        // The child is a real index with its own storage; the root above stays at zero.
        Assert.True(child.SizeBytes > 0);
        Assert.Equal(0, Find(result, "zoo_partitioned").SizeBytes);
    }

    [Fact]
    public async Task Query_ReportsTheInvalidPartitionedRootWithoutSuppressingIt()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // Created ON ONLY the partitioned parent, so PostgreSQL marks it invalid until a matching
        // index is attached for every partition. Deterministic: no concurrency and no catalog write.
        IndexSnapshot invalid = Find(await ZooAsync(deadline.Token), "zoo_invalid_root");

        Assert.False(invalid.IsValid);
        Assert.Equal(0, invalid.SizeBytes);
        Assert.Null(invalid.ScanCount);

        // Validity, readiness and liveness are independent: an invalid index is not suppressed and
        // its other flags are not inferred from IsValid.
        Assert.True(invalid.IsLive);
    }

    // --- Filters and exclusions -----------------------------------------------------------------

    [Fact]
    public async Task Query_HonoursTheIncludeFilter()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlIndexSnapshotQueryResult result = await ZooAsync(deadline.Token);

        Assert.NotEmpty(result.Indexes);
        Assert.All(result.Indexes, index => Assert.Equal(PostgreSqlServerFixture.IndexSchema, index.SchemaName));
    }

    [Fact]
    public async Task Query_HonoursTheExcludeFilter()
    {
        using CancellationTokenSource deadline = TestDeadline();

        (PostgreSqlIndexSnapshotQueryResult result, _) = await ComposeAsync(
            new PostgreSqlSchemaFilter([], [PostgreSqlServerFixture.IndexSchema]), deadline.Token);

        Assert.DoesNotContain(result.Indexes, index => index.SchemaName == PostgreSqlServerFixture.IndexSchema);
    }

    [Fact]
    public async Task Query_NeverReturnsASystemSchemaIndex()
    {
        using CancellationTokenSource deadline = TestDeadline();

        (PostgreSqlIndexSnapshotQueryResult result, _) = await ComposeAsync(
            PostgreSqlSchemaFilter.IncludeEverything, deadline.Token);

        Assert.All(result.Indexes, index =>
        {
            Assert.NotEqual("pg_catalog", index.SchemaName);
            Assert.NotEqual("information_schema", index.SchemaName);
            Assert.DoesNotContain("pg_toast", index.SchemaName, StringComparison.Ordinal);
            Assert.DoesNotContain("pg_temp_", index.SchemaName, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task Query_ReturnsAnEmptyResultForASchemaWithNoIndexes()
    {
        using CancellationTokenSource deadline = TestDeadline();

        (PostgreSqlIndexSnapshotQueryResult result, _) = await ComposeAsync(
            new PostgreSqlSchemaFilter(["dbhealth_absent_schema"], []), deadline.Token);

        Assert.Empty(result.Indexes);
    }

    // --- Result-wide invariants -----------------------------------------------------------------

    [Fact]
    public async Task Query_OrdersIndexesOrdinallyAndCountsEachExactlyOnce()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlIndexSnapshotQueryResult result = await ZooAsync(deadline.Token);

        Assert.Equal(
            result.Indexes.Count,
            result.Indexes.Select(index => (index.SchemaName, index.IndexName)).Distinct().Count());

        var ordered = result.Indexes
            .OrderBy(index => index.SchemaName, StringComparer.Ordinal)
            .ThenBy(index => index.TableName, StringComparer.Ordinal)
            .ThenBy(index => index.IndexName, StringComparer.Ordinal)
            .Select(index => index.IndexName)
            .ToArray();

        Assert.Equal(ordered, result.Indexes.Select(index => index.IndexName).ToArray());
    }

    [Fact]
    public async Task Query_NeverReportsANegativeSizeOrScanCount()
    {
        using CancellationTokenSource deadline = TestDeadline();

        PostgreSqlIndexSnapshotQueryResult result = await ZooAsync(deadline.Token);

        Assert.All(result.Indexes, index =>
        {
            Assert.True(index.SizeBytes >= 0);
            Assert.True(index.ScanCount is null or >= 0);
            Assert.NotEmpty(index.KeyParts);
        });
    }

    // --- Exact SQL executed ---------------------------------------------------------------------

    [Fact]
    public async Task TheExecutedStatementsAreTheFrozenInventoryText()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // Not a copy: the productive constants themselves, proven to run against a live server.
        await using NpgsqlConnection connection = await _fixture.OpenAdminConnectionAsync(deadline.Token);

        long e001Rows = await PostgreSqlServerFixture.CountRowsAsync(
            connection, PostgreSqlSqlInventory.ReadIndexMetadataSql, PostgreSqlServerFixture.IndexSchema, deadline.Token);
        long e002Rows = await PostgreSqlServerFixture.CountRowsAsync(
            connection, PostgreSqlSqlInventory.ReadIndexUsageStatisticsSql, PostgreSqlServerFixture.IndexSchema, deadline.Token);

        Assert.True(e001Rows > 0);
        Assert.True(e002Rows > 0);

        // C002's exact text answers true for the inspection role on the normal fixture.
        Assert.True(await PostgreSqlServerFixture.ReadBooleanAsync(
            connection, PostgreSqlSqlInventory.CheckCatalogMetadataAccessSql, deadline.Token));
    }
}
