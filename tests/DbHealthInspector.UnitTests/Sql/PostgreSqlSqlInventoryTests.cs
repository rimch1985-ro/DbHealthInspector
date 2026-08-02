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
    public void Inventory_ContainsExactlySevenStatements()
    {
        Assert.Equal(7, Inventory().Statements.Count);
    }

    [Fact]
    public void Inventory_OrdersStatementsB001ThroughC004()
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
    public void StatementIdEnum_DeclaresExactlySevenMembers()
    {
        // An eighth productive statement id would need a later authorized gate.
        Assert.Equal(7, Enum.GetValues<PostgreSqlSqlStatementId>().Length);
    }

    [Fact]
    public void CommandKindEnum_DeclaresExactlySixMembers()
    {
        Assert.Equal(6, Enum.GetValues<PostgreSqlSqlCommandKind>().Length);
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
                    AS catalog_metadata_available
            """;

        Assert.Equal(expected, definition.Sql);
        Assert.Equal(PostgreSqlSqlCommandKind.SelectCapabilityCheck, definition.Kind);
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
    [InlineData("pg_class")]
    [InlineData("pg_namespace")]
    [InlineData("pg_index")]
    [InlineData("pg_attribute")]
    [InlineData("pg_inherits")]
    [InlineData("pg_constraint")]
    [InlineData("pg_am")]
    [InlineData("pg_collation")]
    [InlineData("pg_opclass")]
    [InlineData("pg_stat_all_indexes")]
    public void CatalogRelations_AreOnlyPrivilegeChecked_NeverQueried(string relation)
    {
        // GC-DHI-04C names these relations only as string arguments to has_table_privilege — it
        // asks PostgreSQL *whether* they are readable and never reads a row from them. Actually
        // querying them belongs to GC-DHI-04D/04E, so each occurrence must sit inside a quoted
        // literal and never after a FROM or JOIN.
        foreach (PostgreSqlSqlStatementDefinition definition in Inventory().Statements)
        {
            Assert.DoesNotContain($"FROM pg_catalog.{relation}", definition.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"JOIN pg_catalog.{relation}", definition.Sql, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void OnlyC004QueriesARelation_AndItIsTheStatisticsView()
    {
        // The single FROM clause in the entire productive inventory. Anything else reading a
        // relation would be table/index metadata work reserved for a later gate.
        PostgreSqlSqlStatementDefinition[] withFrom = Inventory().Statements
            .Where(statement => statement.Sql.Contains("FROM ", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        PostgreSqlSqlStatementDefinition only = Assert.Single(withFrom);
        Assert.Equal(PostgreSqlSqlStatementId.ReadStatisticsReset, only.Id);
        Assert.Contains("FROM pg_catalog.pg_stat_database", only.Sql, StringComparison.Ordinal);
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
