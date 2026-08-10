using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// The productive inventory is frozen at exactly B001, B002 and B003 in that order
/// (GC-DHI-04B §7, §11). Constructing it also runs every definition through
/// <c>PostgreSqlSqlSafetyValidator</c>, so these tests double as proof that the real productive
/// SQL passes the fail-closed validator.
/// </summary>
public sealed class PostgreSqlSqlInventoryTests
{
    private static PostgreSqlSqlInventory Inventory() => new();

    [Fact]
    public void Inventory_ContainsExactlyEightStatements()
    {
        Assert.Equal(8, Inventory().Statements.Count);
    }

    [Fact]
    public void Inventory_OrdersStatementsB001ThroughD001()
    {
        PostgreSqlSqlInventory inventory = Inventory();

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
            inventory.Statements.Select(statement => statement.Id).ToArray());
    }

    [Fact]
    public void Inventory_HasUniqueIds()
    {
        PostgreSqlSqlInventory inventory = Inventory();

        int distinct = inventory.Statements.Select(statement => statement.Id).Distinct().Count();

        Assert.Equal(inventory.Statements.Count, distinct);
    }

    [Fact]
    public void StatementIdEnum_DeclaresExactlyEightMembers()
    {
        // A ninth productive statement id — an index query above all — needs a later authorized
        // gate.
        Assert.Equal(8, Enum.GetValues<PostgreSqlSqlStatementId>().Length);
    }

    [Fact]
    public void CommandKindEnum_DeclaresExactlySevenMembers()
    {
        Assert.Equal(7, Enum.GetValues<PostgreSqlSqlCommandKind>().Length);
    }

    [Fact]
    public void ParameterTypeEnum_DeclaresExactlyTwoMembers()
    {
        // Int32 for the three timeouts, TextArray for the two schema filters. Nothing else is
        // bindable, so no object, dynamic or generic conversion path can exist.
        Assert.Equal(2, Enum.GetValues<PostgreSqlSqlParameterType>().Length);
    }

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.ReadServerIdentity))]
    [InlineData(nameof(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess))]
    [InlineData(nameof(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess))]
    [InlineData(nameof(PostgreSqlSqlStatementId.ReadStatisticsReset))]
    public void CapabilityStatements_TakeNoParameters(string idName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);

        Assert.Empty(Inventory().Resolve(id).Parameters);
    }

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.SetTransactionReadOnly), nameof(PostgreSqlSqlCommandKind.SetTransactionReadOnly), 0)]
    [InlineData(nameof(PostgreSqlSqlStatementId.ApplyLocalTimeouts), nameof(PostgreSqlSqlCommandKind.SelectConfiguration), 3)]
    [InlineData(nameof(PostgreSqlSqlStatementId.VerifySessionState), nameof(PostgreSqlSqlCommandKind.SelectVerification), 3)]
    public void Resolve_ReturnsExpectedKindAndParameterCount(string idName, string kindName, int parameterCount)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        var kind = Enum.Parse<PostgreSqlSqlCommandKind>(kindName);

        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(id);

        Assert.Equal(id, definition.Id);
        Assert.Equal(kind, definition.Kind);
        Assert.Equal(parameterCount, definition.Parameters.Count);
    }

    [Fact]
    public void Resolve_B001_ReturnsExactSql()
    {
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.SetTransactionReadOnly);

        Assert.Equal("SET TRANSACTION READ ONLY", definition.Sql);
    }

    [Fact]
    public void Resolve_B002_ReturnsExactSql()
    {
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.ApplyLocalTimeouts);

        const string expected = """
            SELECT
                pg_catalog.set_config(
                    'statement_timeout',
                    $1::text || 'ms',
                    true),
                pg_catalog.set_config(
                    'lock_timeout',
                    $2::text || 'ms',
                    true),
                pg_catalog.set_config(
                    'idle_in_transaction_session_timeout',
                    $3::text || 'ms',
                    true)
            """;

        Assert.Equal(expected, definition.Sql);
    }

    [Fact]
    public void Resolve_B003_ReturnsExactSql()
    {
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.VerifySessionState);

        const string expected = """
            SELECT
                pg_catalog.current_setting(
                    'transaction_read_only')::boolean
                    AS is_read_only,
                pg_catalog.current_setting(
                    'transaction_isolation')
                    AS isolation_level,
                pg_catalog.current_setting(
                    'statement_timeout')::interval
                    = ($1 * interval '1 millisecond')
                    AS statement_timeout_matches,
                pg_catalog.current_setting(
                    'lock_timeout')::interval
                    = ($2 * interval '1 millisecond')
                    AS lock_timeout_matches,
                pg_catalog.current_setting(
                    'idle_in_transaction_session_timeout')::interval
                    = ($3 * interval '1 millisecond')
                    AS idle_timeout_matches
            """;

        Assert.Equal(expected, definition.Sql);
    }

    [Fact]
    public void Resolve_C001_ReturnsExactSql()
    {
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.ReadServerIdentity);

        const string expected = """
            SELECT
                pg_catalog.current_setting(
                    'server_version_num')::integer
                    AS server_version_number,
                pg_catalog.current_database()::text
                    AS database_name,
                current_user::text
                    AS current_user
            """;

        Assert.Equal(expected, definition.Sql);
        Assert.Equal(PostgreSqlSqlCommandKind.SelectServerIdentity, definition.Kind);
    }

    [Fact]
    public void Resolve_C002_ReturnsExactSql()
    {
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess);

        const string expected = """
            SELECT
                pg_catalog.has_schema_privilege(
                    current_user,
                    'pg_catalog',
                    'USAGE')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_namespace',
                    'SELECT')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_class',
                    'SELECT')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_inherits',
                    'SELECT')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_index',
                    'SELECT')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_attribute',
                    'SELECT')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_am',
                    'SELECT')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_constraint',
                    'SELECT')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_collation',
                    'SELECT')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_opclass',
                    'SELECT')
            AND pg_catalog.has_function_privilege(
                current_user,
                'pg_catalog.pg_table_size(regclass)',
                'EXECUTE')
            AND pg_catalog.has_function_privilege(
                current_user,
                'pg_catalog.pg_indexes_size(regclass)',
                'EXECUTE')
            AND pg_catalog.has_function_privilege(
                current_user,
                'pg_catalog.pg_total_relation_size(regclass)',
                'EXECUTE')
                    AS catalog_metadata_available
            """;

        Assert.Equal(expected, definition.Sql);
        Assert.Equal(PostgreSqlSqlCommandKind.SelectCapabilityCheck, definition.Kind);
    }

    [Fact]
    public void Resolve_D001_ReturnsExactSql()
    {
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.ReadTableSnapshots);

        // An independent transcription of GC-DHI-04D §9, not derived from the productive constant.
        const string expected = """
            SELECT
                namespace.nspname::text
                    AS schema_name,
                relation.relname::text
                    AS table_name,
                relation.relkind::text
                    AS relation_kind,
                relation.relpersistence::text
                    AS relation_persistence,
                relation.relispartition
                    AS is_partition,
                CASE
                    WHEN relation.relkind = 'v'
                        OR relation.reltuples < 0
                        THEN NULL::bigint
                    ELSE relation.reltuples::bigint
                END
                    AS estimated_row_count,
                CASE
                    WHEN relation.relkind IN ('r', 'm', 'p')
                        THEN pg_catalog.pg_table_size(relation.oid)
                    ELSE 0::bigint
                END
                    AS table_size_bytes,
                CASE
                    WHEN relation.relkind IN ('r', 'm', 'p')
                        THEN pg_catalog.pg_indexes_size(relation.oid)
                    ELSE 0::bigint
                END
                    AS index_size_bytes,
                CASE
                    WHEN relation.relkind IN ('r', 'm', 'p')
                        THEN pg_catalog.pg_total_relation_size(relation.oid)
                    ELSE 0::bigint
                END
                    AS total_size_bytes,
                EXISTS (
                    SELECT 1
                    FROM pg_catalog.pg_constraint AS constraint_record
                    WHERE constraint_record.conrelid = relation.oid
                      AND constraint_record.contype = 'p'
                )
                    AS has_primary_key
            FROM pg_catalog.pg_class AS relation
            INNER JOIN pg_catalog.pg_namespace AS namespace
                ON namespace.oid = relation.relnamespace
            WHERE relation.relkind IN ('r', 'p', 'v', 'm', 'f')
              AND namespace.nspname <> 'pg_catalog'
              AND namespace.nspname <> 'information_schema'
              AND namespace.nspname NOT LIKE 'pg_toast%'
              AND namespace.nspname NOT LIKE 'pg_temp_%'
              AND (
                  pg_catalog.cardinality($1::text[]) = 0
                  OR namespace.nspname::text = ANY($1::text[])
              )
              AND NOT (
                  namespace.nspname::text = ANY($2::text[])
              )
            ORDER BY
                namespace.nspname,
                relation.relname
            """;

        Assert.Equal(expected, definition.Sql);
        Assert.Equal(PostgreSqlSqlCommandKind.SelectTableMetadata, definition.Kind);
        Assert.Equal(2, definition.Parameters.Count);
        Assert.Equal(PostgreSqlSqlParameterType.TextArray, definition.Parameters[0].Type);
        Assert.Equal(PostgreSqlSqlParameterType.TextArray, definition.Parameters[1].Type);
        Assert.Equal(1, definition.Parameters[0].Position);
        Assert.Equal(2, definition.Parameters[1].Position);
    }

    [Fact]
    public void D001_ExcludesEverySystemSchemaFamily()
    {
        string sql = Inventory().Resolve(PostgreSqlSqlStatementId.ReadTableSnapshots).Sql;

        // The four mandatory exclusions are part of the frozen text, not of the bound filter, so
        // no include list can re-enable them.
        Assert.Contains("namespace.nspname <> 'pg_catalog'", sql, StringComparison.Ordinal);
        Assert.Contains("namespace.nspname <> 'information_schema'", sql, StringComparison.Ordinal);
        Assert.Contains("namespace.nspname NOT LIKE 'pg_toast%'", sql, StringComparison.Ordinal);
        Assert.Contains("namespace.nspname NOT LIKE 'pg_temp_%'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void D001_RestrictsRelationKindsAndNeverNamesAnIndex()
    {
        string sql = Inventory().Resolve(PostgreSqlSqlStatementId.ReadTableSnapshots).Sql;

        Assert.Contains("relation.relkind IN ('r', 'p', 'v', 'm', 'f')", sql, StringComparison.Ordinal);

        // 'i' is the index relkind; admitting it would be GC-DHI-04E work.
        Assert.DoesNotContain("'i'", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void D001_TakesItsSchemaFiltersOnlyAsBoundArrays()
    {
        string sql = Inventory().Resolve(PostgreSqlSqlStatementId.ReadTableSnapshots).Sql;

        Assert.Contains("pg_catalog.cardinality($1::text[]) = 0", sql, StringComparison.Ordinal);
        Assert.Contains("namespace.nspname::text = ANY($1::text[])", sql, StringComparison.Ordinal);
        Assert.Contains("namespace.nspname::text = ANY($2::text[])", sql, StringComparison.Ordinal);
    }

    [Fact]
    public void D001_DerivesPrimaryKeyOnlyFromPgConstraint()
    {
        string sql = Inventory().Resolve(PostgreSqlSqlStatementId.ReadTableSnapshots).Sql;

        Assert.Contains("constraint_record.contype = 'p'", sql, StringComparison.Ordinal);
        Assert.Contains("constraint_record.conrelid = relation.oid", sql, StringComparison.Ordinal);

        // Never inferred from an index or a relhasindex flag.
        Assert.DoesNotContain("relhasindex", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("indisprimary", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void D001_UsesTheThreeAuthorizedSizeFunctionsAndNoAggregate()
    {
        string sql = Inventory().Resolve(PostgreSqlSqlStatementId.ReadTableSnapshots).Sql;

        Assert.Contains("pg_catalog.pg_table_size(relation.oid)", sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_indexes_size(relation.oid)", sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_total_relation_size(relation.oid)", sql, StringComparison.Ordinal);

        // A partitioned root reports only its own OID's sizes: no descendant aggregation.
        foreach (string forbidden in new[] { "pg_partition_tree", "pg_inherits", "sum(", "WITH RECURSIVE", "pg_size_pretty" })
        {
            Assert.DoesNotContain(forbidden, sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void C002_ChecksTheThreeSizeFunctionsD001Calls()
    {
        string sql = Inventory().Resolve(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess).Sql;

        Assert.Contains("'pg_catalog.pg_table_size(regclass)'", sql, StringComparison.Ordinal);
        Assert.Contains("'pg_catalog.pg_indexes_size(regclass)'", sql, StringComparison.Ordinal);
        Assert.Contains("'pg_catalog.pg_total_relation_size(regclass)'", sql, StringComparison.Ordinal);

        // Exactly three function checks, no more.
        Assert.Equal(3, sql.Split("has_function_privilege").Length - 1);
    }

    [Fact]
    public void Resolve_C003_ReturnsExactSql()
    {
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess);

        const string expected = """
            SELECT
                pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_stat_database',
                    'SELECT')
                AND pg_catalog.has_table_privilege(
                    current_user,
                    'pg_catalog.pg_stat_all_indexes',
                    'SELECT')
                    AS usage_statistics_available
            """;

        Assert.Equal(expected, definition.Sql);
        Assert.Equal(PostgreSqlSqlCommandKind.SelectCapabilityCheck, definition.Kind);
    }

    [Fact]
    public void Resolve_C004_ReturnsExactSql()
    {
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.ReadStatisticsReset);

        const string expected = """
            SELECT
                statistics.stats_reset
            FROM pg_catalog.pg_stat_database AS statistics
            WHERE statistics.datname = pg_catalog.current_database()
            """;

        Assert.Equal(expected, definition.Sql);
        Assert.Equal(PostgreSqlSqlCommandKind.SelectStatistics, definition.Kind);
    }

    [Fact]
    public void CatalogAllowlist_ContainsExactlyTheTenFrozenEntries()
    {
        // The 04C baseline. Widening it silently would let a later gate read a catalog the
        // capability check never actually proved reachable.
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess);

        string[] expected =
        [
            "'pg_catalog'",
            "'pg_catalog.pg_namespace'",
            "'pg_catalog.pg_class'",
            "'pg_catalog.pg_inherits'",
            "'pg_catalog.pg_index'",
            "'pg_catalog.pg_attribute'",
            "'pg_catalog.pg_am'",
            "'pg_catalog.pg_constraint'",
            "'pg_catalog.pg_collation'",
            "'pg_catalog.pg_opclass'",
        ];

        foreach (string entry in expected)
        {
            Assert.Contains(entry, definition.Sql, StringComparison.Ordinal);
        }

        // Exactly ten privilege calls: one schema check plus nine table checks.
        Assert.Equal(1, CountOccurrences(definition.Sql, "has_schema_privilege"));
        Assert.Equal(9, CountOccurrences(definition.Sql, "has_table_privilege"));
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.ApplyLocalTimeouts))]
    [InlineData(nameof(PostgreSqlSqlStatementId.VerifySessionState))]
    public void ParameterDeclarations_AreOrderedInt32Positions(string idName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);

        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(id);

        Assert.Equal([1, 2, 3], definition.Parameters.Select(parameter => parameter.Position).ToArray());
        Assert.All(definition.Parameters, parameter => Assert.Equal(PostgreSqlSqlParameterType.Int32, parameter.Type));
    }

    [Fact]
    public void Resolve_ThrowsForUnknownId()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Inventory().Resolve((PostgreSqlSqlStatementId)999));

        Assert.Equal("id", exception.ParamName);
    }

    [Fact]
    public void Statements_AreNotModifiableThroughTheExposedCollection()
    {
        PostgreSqlSqlInventory inventory = Inventory();

        Assert.Throws<NotSupportedException>(
            () => ((IList<PostgreSqlSqlStatementDefinition>)inventory.Statements).Add(inventory.Statements[0]));
    }

    [Fact]
    public void Parameters_AreNotModifiableThroughTheExposedCollection()
    {
        PostgreSqlSqlStatementDefinition definition = Inventory().Resolve(PostgreSqlSqlStatementId.ApplyLocalTimeouts);

        Assert.Throws<NotSupportedException>(
            () => ((IList<PostgreSqlSqlParameterDefinition>)definition.Parameters).Clear());
    }

    [Fact]
    public void Default_IsStableAcrossReads()
    {
        Assert.Same(PostgreSqlSqlInventory.Default, PostgreSqlSqlInventory.Default);
    }

    [Theory]
    [InlineData("UPDATE")]
    [InlineData("INSERT")]
    [InlineData("DELETE")]
    [InlineData("MERGE")]
    [InlineData("CREATE")]
    [InlineData("ALTER")]
    [InlineData("DROP")]
    [InlineData("TRUNCATE")]
    [InlineData("LOCK")]
    [InlineData("GRANT")]
    [InlineData("REVOKE")]
    [InlineData("COPY")]
    public void ProductiveSql_ContainsNoProhibitedKeywordAsAWholeToken(string forbidden)
    {
        // Whole-token matching, not substring: `lock_timeout` and `lock_timeout_matches` are
        // legitimate PostgreSQL setting names that merely contain "lock", and rejecting them
        // would contradict the validator's own token rule. `_` is a word character, so
        // \bLOCK\b does not match inside `lock_timeout`.
        var pattern = new System.Text.RegularExpressions.Regex(
            $@"\b{System.Text.RegularExpressions.Regex.Escape(forbidden)}\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (PostgreSqlSqlStatementDefinition definition in Inventory().Statements)
        {
            Assert.False(
                pattern.IsMatch(definition.Sql),
                $"Productive statement {definition.Id} contains the prohibited keyword token.");
        }
    }

    [Theory]
    [InlineData("pg_sleep")]
    [InlineData("pg_stat_statements")]
    [InlineData("EXPLAIN")]
    [InlineData("count(")]
    public void ProductiveSql_ContainsNoTestOnlyOrProhibitedIdentifier(string forbidden)
    {
        // pg_sleep belongs only to the IntegrationTests timeout harness; pg_stat_statements,
        // EXPLAIN and COUNT(*) are prohibited outright by the gate definition.
        foreach (PostgreSqlSqlStatementDefinition definition in Inventory().Statements)
        {
            Assert.DoesNotContain(forbidden, definition.Sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Theory]
    [InlineData("pg_index")]
    [InlineData("pg_attribute")]
    [InlineData("pg_inherits")]
    [InlineData("pg_am")]
    [InlineData("pg_collation")]
    [InlineData("pg_opclass")]
    [InlineData("pg_stat_all_indexes")]
    public void AllowlistedRelationsD001DoesNotNeed_AreOnlyPrivilegeChecked_NeverQueried(string relation)
    {
        // These relations are named only as string arguments to has_table_privilege: the product
        // asks PostgreSQL *whether* they are readable and never reads a row from them. Querying
        // pg_index, pg_attribute, pg_inherits, pg_am, pg_collation or pg_opclass would be index,
        // column or partition-tree work reserved for GC-DHI-04E.
        foreach (PostgreSqlSqlStatementDefinition definition in Inventory().Statements)
        {
            Assert.DoesNotContain($"FROM pg_catalog.{relation}", definition.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"JOIN pg_catalog.{relation}", definition.Sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OnlyC004AndD001QueryRelations_AndOnlyTheOnesTheirGatesAuthorized()
    {
        // Every FROM clause in the entire productive inventory, and nothing else reads a relation.
        PostgreSqlSqlStatementDefinition[] withFrom = [.. Inventory().Statements
            .Where(statement => statement.Sql.Contains("FROM ", StringComparison.OrdinalIgnoreCase))];

        Assert.Equal(
            [PostgreSqlSqlStatementId.ReadStatisticsReset, PostgreSqlSqlStatementId.ReadTableSnapshots],
            withFrom.Select(statement => statement.Id).ToArray());

        PostgreSqlSqlStatementDefinition c004 = withFrom[0];
        Assert.Contains("FROM pg_catalog.pg_stat_database", c004.Sql, StringComparison.Ordinal);

        // D001 reads exactly pg_class, joined to pg_namespace, with a correlated pg_constraint
        // existence test — the three relations GC-DHI-04D authorizes and no others.
        PostgreSqlSqlStatementDefinition d001 = withFrom[1];
        Assert.Contains("FROM pg_catalog.pg_class AS relation", d001.Sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN pg_catalog.pg_namespace AS namespace", d001.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM pg_catalog.pg_constraint AS constraint_record", d001.Sql, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductiveSql_ContainsNoSemicolon()
    {
        foreach (PostgreSqlSqlStatementDefinition definition in Inventory().Statements)
        {
            Assert.DoesNotContain(';', definition.Sql);
        }
    }

    [Fact]
    public void Inventory_ExposesNoLookupBySqlText()
    {
        // There must be no way to ask the inventory for a statement by its text, which would be
        // the first step toward a raw-SQL path.
        System.Reflection.MethodInfo[] methods = typeof(PostgreSqlSqlInventory)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(method => method.DeclaringType == typeof(PostgreSqlSqlInventory))
            .ToArray();

        Assert.DoesNotContain(
            methods,
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)));
    }
}
