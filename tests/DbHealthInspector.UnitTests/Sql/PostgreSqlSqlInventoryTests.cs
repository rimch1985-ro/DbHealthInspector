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
    public void Inventory_ContainsExactlyThreeStatements()
    {
        Assert.Equal(3, Inventory().Statements.Count);
    }

    [Fact]
    public void Inventory_OrdersStatementsB001ThenB002ThenB003()
    {
        PostgreSqlSqlInventory inventory = Inventory();

        Assert.Equal(PostgreSqlSqlStatementId.SetTransactionReadOnly, inventory.Statements[0].Id);
        Assert.Equal(PostgreSqlSqlStatementId.ApplyLocalTimeouts, inventory.Statements[1].Id);
        Assert.Equal(PostgreSqlSqlStatementId.VerifySessionState, inventory.Statements[2].Id);
    }

    [Fact]
    public void Inventory_HasUniqueIds()
    {
        PostgreSqlSqlInventory inventory = Inventory();

        int distinct = inventory.Statements.Select(statement => statement.Id).Distinct().Count();

        Assert.Equal(inventory.Statements.Count, distinct);
    }

    [Fact]
    public void StatementIdEnum_DeclaresExactlyThreeMembers()
    {
        // A fourth productive statement id would need a later authorized gate.
        Assert.Equal(3, Enum.GetValues<PostgreSqlSqlStatementId>().Length);
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
    [InlineData("pg_class")]
    [InlineData("pg_index")]
    [InlineData("pg_namespace")]
    [InlineData("pg_stat")]
    public void ProductiveSql_ContainsNoTestOnlyOrCatalogMetadataIdentifier(string forbidden)
    {
        // These must never appear in any form: pg_sleep belongs only to the IntegrationTests
        // timeout harness, and the catalog metadata relations belong to GC-DHI-04C onward.
        foreach (PostgreSqlSqlStatementDefinition definition in Inventory().Statements)
        {
            Assert.DoesNotContain(forbidden, definition.Sql, StringComparison.OrdinalIgnoreCase);
        }
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
