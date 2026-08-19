using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// The Core semantics both pinned majors must agree on, written once so the PostgreSQL 15 and
/// PostgreSQL 18 suites cannot drift apart (GC-DHI-04F §27).
/// </summary>
/// <remarks>
/// These assert <b>contracts</b>, never byte equality across servers. Version text, OIDs, sizes,
/// estimates, scan counts, reset timestamps and deparsed expression text may legitimately differ
/// between majors and are checked for their contract rather than compared to the other server.
/// </remarks>
internal static class CrossVersionSnapshotAssertions
{
    /// <summary>
    /// Everything a complete, supported snapshot must satisfy on any supported major.
    /// </summary>
    internal static void AssertSupportedSnapshotShape(DatabaseSnapshot snapshot, string expectedMajorPrefix)
    {
        Assert.Equal(DatabaseEngine.PostgreSql, snapshot.Metadata.Engine);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Metadata.DatabaseName));
        Assert.False(string.IsNullOrWhiteSpace(snapshot.Metadata.EngineVersion));

        // Only the major is a cross-version expectation; the rest of the version text is not.
        Assert.StartsWith(expectedMajorPrefix, snapshot.Metadata.EngineVersion, StringComparison.Ordinal);

        Assert.Equal(
            CapabilityStatus.Available,
            snapshot.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(
            CapabilityStatus.Disabled,
            snapshot.Capabilities.GetState(CapabilityKind.DataProfiling).Status);

        AssertDeterministicOrder(snapshot);
        AssertReferentialClosure(snapshot);
        AssertValueContracts(snapshot);
    }

    /// <summary>Every collection is in the frozen ordinal order.</summary>
    internal static void AssertDeterministicOrder(DatabaseSnapshot snapshot)
    {
        string[] schemaNames = [.. snapshot.Schemas.Select(schema => schema.SchemaName)];
        Assert.Equal([.. schemaNames.Order(StringComparer.Ordinal)], schemaNames);

        var tableKeys = snapshot.Tables.Select(table => (table.SchemaName, table.TableName)).ToArray();
        Assert.Equal(
            [.. tableKeys
                .OrderBy(key => key.SchemaName, StringComparer.Ordinal)
                .ThenBy(key => key.TableName, StringComparer.Ordinal)],
            tableKeys);

        var indexKeys = snapshot.Indexes
            .Select(index => (index.SchemaName, index.TableName, index.IndexName)).ToArray();
        Assert.Equal(
            [.. indexKeys
                .OrderBy(key => key.SchemaName, StringComparer.Ordinal)
                .ThenBy(key => key.TableName, StringComparer.Ordinal)
                .ThenBy(key => key.IndexName, StringComparer.Ordinal)],
            indexKeys);
    }

    /// <summary>Every index closes against a table, and every schema is one a table lives in.</summary>
    internal static void AssertReferentialClosure(DatabaseSnapshot snapshot)
    {
        var tables = snapshot.Tables.Select(table => (table.SchemaName, table.TableName)).ToHashSet();
        Assert.All(snapshot.Indexes, index => Assert.Contains((index.SchemaName, index.TableName), tables));

        var schemas = snapshot.Schemas.Select(schema => schema.SchemaName).ToHashSet(StringComparer.Ordinal);
        Assert.All(snapshot.Tables, table => Assert.Contains(table.SchemaName, schemas));
        Assert.Equal(snapshot.Tables.Select(table => table.SchemaName).Distinct().Count(), schemas.Count);
    }

    /// <summary>
    /// Value-domain contracts that hold on every major: non-negative sizes, nullable-or-exact
    /// estimates and scan counts, at least one key part per index, and a UTC reset timestamp.
    /// </summary>
    internal static void AssertValueContracts(DatabaseSnapshot snapshot)
    {
        Assert.All(snapshot.Tables, table =>
        {
            Assert.True(table.TableSizeBytes >= 0);
            Assert.True(table.IndexSizeBytes >= 0);
            Assert.True(table.TotalSizeBytes >= 0);
            Assert.True(table.EstimatedRowCount is null or >= 0);
            Assert.False(table.IsPartitionedRoot && table.IsPartition);
        });

        Assert.All(snapshot.Indexes, index =>
        {
            Assert.NotEmpty(index.KeyParts);
            Assert.True(index.SizeBytes >= 0);
            Assert.True(index.ScanCount is null or >= 0);
            Assert.False(string.IsNullOrWhiteSpace(index.AccessMethod));

            // Key positions are unique and INCLUDE names are unique, on every major.
            Assert.Equal(index.KeyParts.Count, index.KeyParts.Select(part => part.Position).Distinct().Count());
            Assert.Equal(
                index.IncludedColumns.Count,
                index.IncludedColumns.Distinct(StringComparer.Ordinal).Count());
        });

        if (snapshot.Statistics.StatisticsResetAtUtc is { } reset)
        {
            Assert.Equal(TimeSpan.Zero, reset.Offset);
        }
    }

    /// <summary>
    /// The index shapes the common zoo guarantees on both majors, asserted structurally rather than
    /// by comparing one server's text to the other's.
    /// </summary>
    internal static void AssertCommonIndexZoo(DatabaseSnapshot snapshot, string schema)
    {
        IndexSnapshot Find(string name) =>
            Assert.Single(snapshot.Indexes, index => index.SchemaName == schema && index.IndexName == name);

        // Ordering: both directions and both nulls placements are represented.
        IndexSnapshot multi = Find("zoo_btree_multi");
        Assert.Equal(2, multi.KeyParts.Count);
        Assert.Equal(IndexSortDirection.Ascending, multi.KeyParts[0].SortDirection);
        Assert.Equal(IndexNullsOrdering.Last, multi.KeyParts[0].NullsOrdering);
        Assert.Equal(IndexSortDirection.Descending, multi.KeyParts[1].SortDirection);
        Assert.Equal(IndexNullsOrdering.First, multi.KeyParts[1].NullsOrdering);

        // Uniqueness and NULLS NOT DISTINCT.
        IndexSnapshot unique = Find("zoo_unique_nnd");
        Assert.True(unique.IsUnique);
        Assert.True(unique.NullsNotDistinct);

        // INCLUDE, in the order the server stored.
        IndexSnapshot include = Find("zoo_include");
        Assert.Equal(["quantity", "code"], include.IncludedColumns.ToArray());
        Assert.Single(include.KeyParts);

        // Expression and mixed keys: presence is the contract, not the deparsed text.
        IndexSnapshot expression = Find("zoo_expression");
        Assert.Null(Assert.Single(expression.KeyParts).ColumnName);
        Assert.False(string.IsNullOrWhiteSpace(Assert.Single(expression.KeyParts).Expression));

        IndexSnapshot mixed = Find("zoo_mixed");
        Assert.Equal(2, mixed.KeyParts.Count);
        Assert.NotNull(mixed.KeyParts[0].ColumnName);
        Assert.NotNull(mixed.KeyParts[1].Expression);

        // Partial predicate: present and non-blank.
        Assert.False(string.IsNullOrWhiteSpace(Find("zoo_partial").PartialPredicate));
        Assert.Null(Find("zoo_btree_simple").PartialPredicate);

        // Exact qualified collation identity. The fixture writes COLLATE "C", so the expectation
        // is the fixture's own DDL rendered by the frozen 04E identity format — not a prefix and
        // not derived from the observed value.
        Assert.Equal("\"pg_catalog\".\"C\"", Assert.Single(Find("zoo_collation").KeyParts).Collation);

        // Exact qualified operator-class identity, with no options suffix.
        Assert.Equal(
            "\"pg_catalog\".\"text_pattern_ops\"",
            Assert.Single(Find("zoo_opclass").KeyParts).OperatorClass);

        // Exact access methods, taken from the fixture DDL.
        foreach ((string index, string accessMethod) in ExpectedAccessMethods)
        {
            Assert.Equal(accessMethod, Find(index).AccessMethod);
        }

        // Non-orderable access methods normalize identically on both majors.
        foreach (string nonOrderable in new[] { "zoo_hash", "zoo_gin", "zoo_gist", "zoo_spgist", "zoo_brin" })
        {
            IndexKeyPartSnapshot part = Assert.Single(Find(nonOrderable).KeyParts);
            Assert.Equal(IndexSortDirection.Ascending, part.SortDirection);
            Assert.Equal(IndexNullsOrdering.Last, part.NullsOrdering);
        }

        // Exact operator-class identities including the ordered options encoding. Each expectation
        // is spelled out from the fixture DDL and the frozen 04E encoding rules, never produced by
        // calling the product encoder.
        Assert.Equal(
            "\"pg_catalog\".\"int4_minmax_multi_ops\"|options[1;19:values_per_range=32]",
            Assert.Single(Find("zoo_opts_32").KeyParts).OperatorClass);
        Assert.Equal(
            "\"pg_catalog\".\"int4_minmax_multi_ops\"|options[1;19:values_per_range=64]",
            Assert.Single(Find("zoo_opts_64").KeyParts).OperatorClass);

        // The inverse stored-order pair: same option set, opposite storage sequence, therefore
        // different canonical identities. PostgreSQL keeps attoptions in DDL order on both majors.
        // Format is |options[<count>;<len>:<value><len>:<value>…] — one ';' after the count, then
        // length-prefixed values concatenated with no separator, because the length prefix already
        // makes each value's extent unambiguous. Lengths are UTF-16 code units:
        //   "n_distinct_per_range=16"   -> 23
        //   "false_positive_rate=0.05"  -> 24
        const string OrderAbExpected =
            "\"pg_catalog\".\"int4_bloom_ops\"|options[2;23:n_distinct_per_range=1624:false_positive_rate=0.05]";
        const string OrderBaExpected =
            "\"pg_catalog\".\"int4_bloom_ops\"|options[2;24:false_positive_rate=0.0523:n_distinct_per_range=16]";

        string orderAb = Assert.Single(Find("zoo_opts_order_ab").KeyParts).OperatorClass!;
        string orderBa = Assert.Single(Find("zoo_opts_order_ba").KeyParts).OperatorClass!;

        Assert.Equal(OrderAbExpected, orderAb);
        Assert.Equal(OrderBaExpected, orderBa);
        Assert.NotEqual(orderAb, orderBa);

        // Constraint association and primary keys.
        Assert.True(Find("indexed_orders_pkey").IsPrimaryKey);
        Assert.True(Find("indexed_orders_pkey").BacksConstraint);
        Assert.True(Find("indexed_orders_code_key").BacksConstraint);
        Assert.True(Find("indexed_orders_span_excl").BacksConstraint);
        Assert.False(Find("zoo_btree_simple").BacksConstraint);

        // Validity flags are independent and a valid index reports all three.
        IndexSnapshot simple = Find("zoo_btree_simple");
        Assert.True(simple.IsValid);
        Assert.True(simple.IsReady);
        Assert.True(simple.IsLive);
    }

    /// <summary>
    /// The access method each common index is declared with, taken from the fixture DDL rather
    /// than from the snapshot under test.
    /// </summary>
    private static readonly (string Index, string AccessMethod)[] ExpectedAccessMethods =
    [
        ("zoo_btree_simple", "btree"),
        ("zoo_btree_multi", "btree"),
        ("zoo_unique_nnd", "btree"),
        ("zoo_include", "btree"),
        ("zoo_expression", "btree"),
        ("zoo_mixed", "btree"),
        ("zoo_partial", "btree"),
        ("zoo_collation", "btree"),
        ("zoo_opclass", "btree"),
        ("zoo_hash", "hash"),
        ("zoo_gin", "gin"),
        ("zoo_gist", "gist"),
        ("zoo_spgist", "spgist"),
        ("zoo_brin", "brin"),
        ("zoo_opts_32", "brin"),
        ("zoo_opts_64", "brin"),
        ("zoo_opts_order_ab", "brin"),
        ("zoo_opts_order_ba", "brin"),
        ("indexed_orders_pkey", "btree"),
        ("indexed_orders_code_key", "btree"),
        ("indexed_orders_span_excl", "gist"),
    ];

    /// <summary>
    /// The view and materialized view both fixtures build, with the exact Core relation kind each
    /// must map to.
    /// </summary>
    internal static void AssertCommonViewSemantics(DatabaseSnapshot snapshot, string schema)
    {
        TableSnapshot Find(string name) =>
            Assert.Single(snapshot.Tables, table => table.SchemaName == schema && table.TableName == name);

        TableSnapshot view = Find("orders_view");
        Assert.Equal(schema, view.SchemaName);
        Assert.Equal("orders_view", view.TableName);
        Assert.Equal(RelationKind.View, view.RelationKind);
        Assert.False(view.IsPartitionedRoot);
        Assert.False(view.IsPartition);

        TableSnapshot materialized = Find("orders_matview");
        Assert.Equal(schema, materialized.SchemaName);
        Assert.Equal("orders_matview", materialized.TableName);
        Assert.Equal(RelationKind.MaterializedView, materialized.RelationKind);
        Assert.False(materialized.IsPartitionedRoot);
        Assert.False(materialized.IsPartition);

        // A view reports no rows and no storage of its own; the materialized one may hold both,
        // so only its local domain is checked rather than a cross-version value.
        Assert.Equal(0, view.TableSizeBytes);
        Assert.Equal(0, view.IndexSizeBytes);
        Assert.Equal(0, view.TotalSizeBytes);
        Assert.True(materialized.TableSizeBytes >= 0);
        Assert.True(materialized.TotalSizeBytes >= 0);
    }

    /// <summary>
    /// The table-relation semantics both majors must agree on: ordinary tables, a partitioned root
    /// and one physical member, with root/partition flags never contradicting each other.
    /// </summary>
    internal static void AssertCommonTableSemantics(
        DatabaseSnapshot snapshot,
        string schema,
        string ordinaryTable,
        string partitionedRoot,
        string partitionMember)
    {
        TableSnapshot Find(string name) =>
            Assert.Single(snapshot.Tables, table => table.SchemaName == schema && table.TableName == name);

        // An ordinary table is frozen in full: exact identity, exact kind, and *both* partition
        // flags negative. Asserting only the kind would let a mapper that leaks a partition flag
        // onto a non-partitioned relation pass unnoticed (GC-DHI-04F-C4, R4-01).
        TableSnapshot ordinary = Find(ordinaryTable);
        Assert.Equal(schema, ordinary.SchemaName);
        Assert.Equal(ordinaryTable, ordinary.TableName);
        Assert.Equal(RelationKind.OrdinaryTable, ordinary.RelationKind);
        Assert.False(ordinary.IsPartitionedRoot);
        Assert.False(ordinary.IsPartition);

        TableSnapshot root = Find(partitionedRoot);
        Assert.Equal(schema, root.SchemaName);
        Assert.Equal(partitionedRoot, root.TableName);
        Assert.Equal(RelationKind.PartitionedTable, root.RelationKind);
        Assert.True(root.IsPartitionedRoot);
        Assert.False(root.IsPartition);

        TableSnapshot member = Find(partitionMember);
        Assert.Equal(schema, member.SchemaName);
        Assert.Equal(partitionMember, member.TableName);
        Assert.Equal(RelationKind.Partition, member.RelationKind);
        Assert.True(member.IsPartition);
        Assert.False(member.IsPartitionedRoot);

        // The three relations are genuinely distinct kinds rather than one kind repeated.
        Assert.Equal(
            3,
            new[] { ordinary.RelationKind, root.RelationKind, member.RelationKind }.Distinct().Count());
    }

    /// <summary>
    /// The index-member semantics both majors must agree on: a virtual partitioned root carries no
    /// storage and no counter, while its physical member stands on its own.
    /// </summary>
    /// <param name="snapshot">The capture under test.</param>
    /// <param name="schema">The schema both relations live in, from the fixture constants.</param>
    /// <param name="partitionedRoot">The partitioned parent table, from the fixture DDL.</param>
    /// <param name="rootIndexName">The index declared on the parent, from the fixture DDL.</param>
    /// <param name="partitionMember">The physical partition, from the fixture DDL.</param>
    /// <param name="memberIndexName">
    /// The exact name PostgreSQL gives the member index it creates on the partition. Supplied by
    /// the caller as a frozen constant rather than matched by prefix or convention, and confirmed
    /// identical on both pinned majors (GC-DHI-04F-C4, R4-01).
    /// </param>
    /// <param name="expectedKeyColumn">The indexed column, from the fixture DDL.</param>
    /// <param name="expectedKeyCollation">
    /// The qualified collation identity the indexed column's type carries, spelled out from the
    /// fixture DDL in the frozen 04E identity format.
    /// </param>
    /// <param name="expectedKeyOperatorClass">
    /// The qualified default operator-class identity for the indexed column's type, likewise
    /// spelled out rather than read back from another snapshot.
    /// </param>
    /// <remarks>
    /// The three <c>expectedKey…</c> arguments describe the <b>one</b> key the fixture DDL declares.
    /// The root and its member are each asserted against them separately, so neither side is ever
    /// the other's expectation (GC-DHI-04F-C5, R5-01).
    /// </remarks>
    internal static void AssertCommonIndexMemberSemantics(
        DatabaseSnapshot snapshot,
        string schema,
        string partitionedRoot,
        string rootIndexName,
        string partitionMember,
        string memberIndexName,
        string expectedKeyColumn,
        string expectedKeyCollation,
        string expectedKeyOperatorClass)
    {
        // The single frozen key contract, applied independently to whichever side is passed in. It
        // reads only the fixture-derived arguments, never another snapshot, so a symmetric mapper
        // defect that corrupts root and member the same way fails on both rather than cancelling out.
        void AssertKeyMatchesFrozenContract(IndexKeyPartSnapshot key)
        {
            Assert.Equal(1, key.Position);
            Assert.Equal(expectedKeyColumn, key.ColumnName);
            Assert.Null(key.Expression);
            Assert.Equal(expectedKeyCollation, key.Collation);
            Assert.Equal(expectedKeyOperatorClass, key.OperatorClass);
            Assert.Equal(IndexSortDirection.Ascending, key.SortDirection);
            Assert.Equal(IndexNullsOrdering.Last, key.NullsOrdering);
        }

        IndexSnapshot root = Assert.Single(
            snapshot.Indexes,
            index => index.SchemaName == schema
                && index.TableName == partitionedRoot
                && index.IndexName == rootIndexName);

        // Exact identity and access method, from the fixture DDL.
        Assert.Equal(schema, root.SchemaName);
        Assert.Equal(partitionedRoot, root.TableName);
        Assert.Equal(rootIndexName, root.IndexName);
        Assert.Equal("btree", root.AccessMethod);

        // A virtual root never aggregates its partitions' storage or usage, and is fully valid.
        Assert.Equal(0, root.SizeBytes);
        Assert.Null(root.ScanCount);
        Assert.Single(root.KeyParts);
        Assert.True(root.IsValid);
        Assert.True(root.IsReady);
        Assert.True(root.IsLive);
        Assert.Empty(root.IncludedColumns);
        Assert.Null(root.PartialPredicate);
        Assert.False(root.IsUnique);
        Assert.False(root.IsPrimaryKey);
        Assert.False(root.BacksConstraint);

        // Its physical member exists in its own right, located by its exact frozen name rather than
        // by "the only index on that table", so a rename or a stray extra member is a failure.
        IndexSnapshot member = Assert.Single(
            snapshot.Indexes,
            index => index.SchemaName == schema
                && index.TableName == partitionMember
                && index.IndexName == memberIndexName);

        Assert.Equal(schema, member.SchemaName);
        Assert.Equal(partitionMember, member.TableName);
        Assert.Equal(memberIndexName, member.IndexName);
        Assert.Equal("btree", member.AccessMethod);
        Assert.True(member.SizeBytes >= 0);
        Assert.True(member.IsValid);
        Assert.True(member.IsReady);
        Assert.True(member.IsLive);

        // Both key structures are frozen independently, from the fixture DDL and the frozen 04E
        // identity format. Neither is copied from the other: expectations read back from another
        // snapshot produced by the same mapper would agree with any consistent defect
        // (GC-DHI-04F-C4, R4-01; GC-DHI-04F-C5, R5-01).
        IndexKeyPartSnapshot rootKey = Assert.Single(root.KeyParts);
        IndexKeyPartSnapshot memberKey = Assert.Single(member.KeyParts);

        AssertKeyMatchesFrozenContract(rootKey);
        AssertKeyMatchesFrozenContract(memberKey);

        // Only now, with both sides independently anchored, is root/member agreement meaningful. It
        // is supplementary evidence, never the source of an expectation — and it compares the whole
        // key structure rather than a subset, so a field that diverges between the two cannot slip
        // through unexamined.
        Assert.Equal(rootKey.Position, memberKey.Position);
        Assert.Equal(rootKey.ColumnName, memberKey.ColumnName);
        Assert.Equal(rootKey.Expression, memberKey.Expression);
        Assert.Equal(rootKey.Collation, memberKey.Collation);
        Assert.Equal(rootKey.OperatorClass, memberKey.OperatorClass);
        Assert.Equal(rootKey.SortDirection, memberKey.SortDirection);
        Assert.Equal(rootKey.NullsOrdering, memberKey.NullsOrdering);

        // The member's table is a real partition, not merely a name that looks like one.
        TableSnapshot memberTable = Assert.Single(
            snapshot.Tables, table => table.SchemaName == schema && table.TableName == partitionMember);
        Assert.Equal(schema, memberTable.SchemaName);
        Assert.Equal(partitionMember, memberTable.TableName);
        Assert.Equal(RelationKind.Partition, memberTable.RelationKind);
        Assert.True(memberTable.IsPartition);
        Assert.False(memberTable.IsPartitionedRoot);

        // …and the index it belongs to is that same relation, closed by identity rather than by
        // the naming convention PostgreSQL happened to use for the member.
        Assert.Equal(memberTable.SchemaName, member.SchemaName);
        Assert.Equal(memberTable.TableName, member.TableName);
    }

    /// <summary>
    /// The deterministic invalid-index contract: reported, never suppressed, with the three
    /// validity flags kept independent.
    /// </summary>
    internal static void AssertInvalidIndexSemantics(DatabaseSnapshot snapshot, string schema, string indexName)
    {
        IndexSnapshot invalid = Assert.Single(
            snapshot.Indexes, index => index.SchemaName == schema && index.IndexName == indexName);

        // The complete tuple, not just IsValid. `CREATE INDEX ... ON ONLY <partitioned table>`
        // leaves the root invalid but still ready and live; that exact triple was observed
        // identically on PostgreSQL 15.18 and 18.4, so no version switch is warranted
        // (GC-DHI-04F-C3, R3-02).
        Assert.False(invalid.IsValid);
        Assert.True(invalid.IsReady);
        Assert.True(invalid.IsLive);

        Assert.Equal(0, invalid.SizeBytes);
        Assert.Null(invalid.ScanCount);

        // Still a fully mapped index rather than a placeholder.
        Assert.Equal(schema, invalid.SchemaName);
        Assert.Equal(indexName, invalid.IndexName);
        Assert.Equal("btree", invalid.AccessMethod);
        Assert.Single(invalid.KeyParts);
    }

    /// <summary>
    /// Usage statistics when the capability is available: the counter's contract, not its value.
    /// </summary>
    internal static void AssertUsageStatisticsAvailable(DatabaseSnapshot snapshot)
    {
        Assert.Equal(
            CapabilityStatus.Available,
            snapshot.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);

        // Live counters legitimately differ between servers, so only the domain is asserted.
        Assert.All(snapshot.Indexes, index => Assert.True(index.ScanCount is null or >= 0));

        // At least one physical index carries a real counter.
        Assert.Contains(snapshot.Indexes, index => index.ScanCount is >= 0);

        if (snapshot.Statistics.StatisticsResetAtUtc is { } reset)
        {
            Assert.Equal(TimeSpan.Zero, reset.Offset);
        }
    }

    /// <summary>
    /// Usage statistics when the capability is unavailable: absence is unknown, never zero.
    /// </summary>
    /// <param name="snapshot">The degraded capture.</param>
    /// <param name="expectIndexes">
    /// Whether the degraded fixture actually contains indexes. This is a <b>fixture</b> property,
    /// not a version difference: PostgreSQL 15's degraded role sees the full object zoo, while the
    /// GC-DHI-04C statistics-revoked container is deliberately minimal and holds none. The rest of
    /// the contract is asserted identically either way.
    /// </param>
    internal static void AssertUsageStatisticsUnavailable(DatabaseSnapshot snapshot, bool expectIndexes = true)
    {
        Assert.Equal(
            CapabilityStatus.Unavailable,
            snapshot.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);

        Assert.Null(snapshot.Statistics.StatisticsResetAtUtc);

        if (expectIndexes)
        {
            Assert.NotEmpty(snapshot.Indexes);
        }

        // Holds for however many indexes the fixture has, including none.
        Assert.All(snapshot.Indexes, index => Assert.Null(index.ScanCount));

        // The metadata itself stays complete: only the optional counter is given up.
        AssertReferentialClosure(snapshot);
        AssertDeterministicOrder(snapshot);
    }

    /// <summary>
    /// Filtering semantics: the same logical filter selects the same object identities on either
    /// major, and the permanent system-schema exclusions always apply.
    /// </summary>
    internal static void AssertFilteringSemantics(
        DatabaseSnapshot included,
        DatabaseSnapshot excluded,
        DatabaseSnapshot everything,
        string filteredSchema)
    {
        // Include: every object belongs to the requested schema, and there is something to see.
        Assert.NotEmpty(included.Tables);
        Assert.All(included.Schemas, schema => Assert.Equal(filteredSchema, schema.SchemaName));
        Assert.All(included.Tables, table => Assert.Equal(filteredSchema, table.SchemaName));
        Assert.All(included.Indexes, index => Assert.Equal(filteredSchema, index.SchemaName));

        // Exclude: the schema is entirely absent.
        Assert.DoesNotContain(excluded.Schemas, schema => schema.SchemaName == filteredSchema);
        Assert.DoesNotContain(excluded.Tables, table => table.SchemaName == filteredSchema);
        Assert.DoesNotContain(excluded.Indexes, index => index.SchemaName == filteredSchema);

        // Include and exclude partition the unfiltered capture for that schema, ordinally.
        Assert.Equal(
            [.. everything.Tables
                .Where(table => table.SchemaName == filteredSchema)
                .Select(table => table.TableName)
                .Order(StringComparer.Ordinal)],
            [.. included.Tables.Select(table => table.TableName).Order(StringComparer.Ordinal)]);

        // System schemas are never reachable, whatever the caller asked for.
        foreach (DatabaseSnapshot snapshot in new[] { included, excluded, everything })
        {
            Assert.DoesNotContain(snapshot.Schemas, schema => schema.SchemaName == "pg_catalog");
            Assert.DoesNotContain(snapshot.Schemas, schema => schema.SchemaName == "information_schema");
            Assert.DoesNotContain(
                snapshot.Schemas,
                schema => schema.SchemaName.StartsWith("pg_toast", StringComparison.Ordinal));
            Assert.DoesNotContain(
                snapshot.Schemas,
                schema => schema.SchemaName.StartsWith("pg_temp_", StringComparison.Ordinal));
        }
    }
}
