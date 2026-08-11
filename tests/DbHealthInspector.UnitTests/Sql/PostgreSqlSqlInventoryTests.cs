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

    private static string Sha256Of(string sql) =>
        Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();

    [Fact]
    public void Inventory_ContainsExactlyTenStatements()
    {
        Assert.Equal(10, Inventory().Statements.Count);
    }

    [Fact]
    public void Inventory_OrdersStatementsB001ThroughE002()
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
                PostgreSqlSqlStatementId.ReadIndexMetadata,
                PostgreSqlSqlStatementId.ReadIndexUsageStatistics,
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
    public void StatementIdEnum_DeclaresExactlyTenMembers()
    {
        // An eleventh productive statement id needs a later authorized gate.
        Assert.Equal(10, Enum.GetValues<PostgreSqlSqlStatementId>().Length);
    }

    [Fact]
    public void CommandKindEnum_DeclaresExactlyEightMembers()
    {
        // GC-DHI-04E adds SelectIndexMetadata for E001. E002 reuses SelectStatistics rather than
        // inventing a ninth kind, because it reads a statistics view exactly as C004 does.
        Assert.Equal(8, Enum.GetValues<PostgreSqlSqlCommandKind>().Length);
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

    /// <summary>
    /// C002's exact bytes, pinned by the hash GC-DHI-04E §8 declares rather than by a second copy
    /// of the text. A duplicated literal would only prove the inventory equals itself; the digest
    /// is an independent value the implementation cannot influence.
    /// </summary>
    [Fact]
    public void Resolve_C002_MatchesTheNormativeDigest()
    {
        PostgreSqlSqlStatementDefinition definition =
            Inventory().Resolve(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess);

        Assert.Equal(2027, definition.Sql.Length);
        Assert.Equal(
            "777cb44afb178c299566f1a8c0251e3ab9ba47480bd578b6a339f4d1c24c5a90",
            Sha256Of(definition.Sql));
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
    public void C002_ChecksEveryFunctionD001AndE001Call()
    {
        string sql = Inventory().Resolve(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess).Sql;

        // The three D001 size functions, unchanged by GC-DHI-04E...
        Assert.Contains("'pg_catalog.pg_table_size(regclass)'", sql, StringComparison.Ordinal);
        Assert.Contains("'pg_catalog.pg_indexes_size(regclass)'", sql, StringComparison.Ordinal);
        Assert.Contains("'pg_catalog.pg_total_relation_size(regclass)'", sql, StringComparison.Ordinal);

        // ...plus exactly the four E001 calls.
        Assert.Contains("'pg_catalog.pg_relation_size(regclass)'", sql, StringComparison.Ordinal);
        Assert.Contains("'pg_catalog.pg_get_indexdef(oid,integer,boolean)'", sql, StringComparison.Ordinal);
        Assert.Contains("'pg_catalog.pg_get_expr(pg_node_tree,oid,boolean)'", sql, StringComparison.Ordinal);
        Assert.Contains(
            "'pg_catalog.pg_index_column_has_property(regclass,integer,text)'",
            sql,
            StringComparison.Ordinal);

        // Exactly seven function checks, no more. attoptions is read straight from pg_attribute,
        // so it adds no function call and therefore no privilege check.
        Assert.Equal(7, sql.Split("has_function_privilege").Length - 1);
        Assert.DoesNotContain("attoptions", sql, StringComparison.Ordinal);
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
    [InlineData("pg_inherits")]
    public void AllowlistedRelationsNoStatementNeeds_AreOnlyPrivilegeChecked_NeverQueried(string relation)
    {
        // After GC-DHI-04E only pg_inherits remains privilege-checked without being read: E001
        // deliberately does not traverse the partition tree, so nothing selects from it. The rest
        // of the C002 allowlist is now genuinely queried by D001, E001 or E002.
        foreach (PostgreSqlSqlStatementDefinition definition in Inventory().Statements
            .Where(statement => statement.Id != PostgreSqlSqlStatementId.CheckCatalogMetadataAccess))
        {
            Assert.DoesNotContain(
                "FROM pg_catalog." + relation,
                definition.Sql,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(
                "JOIN pg_catalog." + relation,
                definition.Sql,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OnlyC004D001E001AndE002QueryRelations_AndOnlyTheOnesTheirGatesAuthorized()
    {
        // Every FROM clause in the entire productive inventory, and nothing else reads a relation.
        PostgreSqlSqlStatementDefinition[] withFrom = [.. Inventory().Statements
            .Where(statement => statement.Sql.Contains("FROM ", StringComparison.OrdinalIgnoreCase))];

        Assert.Equal(
            [
                PostgreSqlSqlStatementId.ReadStatisticsReset,
                PostgreSqlSqlStatementId.ReadTableSnapshots,
                PostgreSqlSqlStatementId.ReadIndexMetadata,
                PostgreSqlSqlStatementId.ReadIndexUsageStatistics,
            ],
            withFrom.Select(statement => statement.Id).ToArray());

        PostgreSqlSqlStatementDefinition c004 = withFrom[0];
        Assert.Contains("FROM pg_catalog.pg_stat_database", c004.Sql, StringComparison.Ordinal);

        // D001 reads exactly pg_class, joined to pg_namespace, with a correlated pg_constraint
        // existence test — the three relations GC-DHI-04D authorizes and no others.
        PostgreSqlSqlStatementDefinition d001 = withFrom[1];
        Assert.Contains("FROM pg_catalog.pg_class AS relation", d001.Sql, StringComparison.Ordinal);
        Assert.Contains("INNER JOIN pg_catalog.pg_namespace AS namespace", d001.Sql, StringComparison.Ordinal);
        Assert.Contains("FROM pg_catalog.pg_constraint AS constraint_record", d001.Sql, StringComparison.Ordinal);

        // E001 reads the index catalogs GC-DHI-04E authorizes and no others. pg_inherits is
        // absent on purpose: descendant traversal is prohibited.
        PostgreSqlSqlStatementDefinition e001 = withFrom[2];
        Assert.Contains("FROM pg_catalog.pg_index AS index_record", e001.Sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_class AS index_relation", e001.Sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_class AS table_relation", e001.Sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_namespace AS table_namespace", e001.Sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_am AS access_method", e001.Sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_attribute AS index_attribute", e001.Sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_collation AS collation_record", e001.Sql, StringComparison.Ordinal);
        Assert.Contains("pg_catalog.pg_opclass AS operator_class", e001.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("pg_inherits", e001.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("pg_partition_tree", e001.Sql, StringComparison.Ordinal);

        // E002 reads only the per-index statistics view.
        PostgreSqlSqlStatementDefinition e002 = withFrom[3];
        Assert.Contains("FROM pg_catalog.pg_stat_all_indexes AS statistics", e002.Sql, StringComparison.Ordinal);
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
