using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// The validator's second layer: the frozen statement contract (GC-DHI-04C-C1 R1-01,
/// GC-DHI-04D and GC-DHI-04E). Only the ten canonical (id, kind, SQL, parameters) combinations
/// exist; every
/// mutation of any of the four parts is rejected.
/// </summary>
/// <remarks>
/// Mutations are addressed by name rather than by literal SQL, so no statement text — canonical or
/// mutated — ever reaches a test display name or a failure message.
/// </remarks>
public sealed class PostgreSqlSqlFrozenStatementContractTests
{
    private static readonly PostgreSqlSqlParameterDefinition[] NoParameters = [];

    private static PostgreSqlSqlParameterDefinition[] TwoSchemaFilters() =>
    [
        new(1, PostgreSqlSqlParameterType.TextArray, "included schema names"),
        new(2, PostgreSqlSqlParameterType.TextArray, "excluded schema names"),
    ];

    private static PostgreSqlSqlParameterDefinition[] ThreeTimeouts() =>
    [
        new(1, PostgreSqlSqlParameterType.Int32, "statement-timeout milliseconds"),
        new(2, PostgreSqlSqlParameterType.Int32, "lock-timeout milliseconds"),
        new(3, PostgreSqlSqlParameterType.Int32, "idle-in-transaction-timeout milliseconds"),
    ];

    /// <summary>
    /// The ten canonical definitions, transcribed independently of
    /// <see cref="PostgreSqlSqlInventory"/>'s own definition list.
    /// </summary>
    /// <remarks>
    /// The (id, kind, parameter) triples are written out here by hand on purpose: deriving them
    /// from the inventory being tested would make the matrix agree with any inventory, including a
    /// wrong one. Only the SQL text is shared, because a second copy of ten frozen statements would
    /// be a drift hazard of its own and the exact bytes are pinned separately by hash.
    /// </remarks>
    private static (PostgreSqlSqlStatementId Id, PostgreSqlSqlCommandKind Kind, string Sql, PostgreSqlSqlParameterDefinition[] Parameters)[] Canonical() =>
    [
        (PostgreSqlSqlStatementId.SetTransactionReadOnly, PostgreSqlSqlCommandKind.SetTransactionReadOnly,
            PostgreSqlSqlInventory.SetTransactionReadOnlySql, NoParameters),
        (PostgreSqlSqlStatementId.ApplyLocalTimeouts, PostgreSqlSqlCommandKind.SelectConfiguration,
            PostgreSqlSqlInventory.ApplyLocalTimeoutsSql, ThreeTimeouts()),
        (PostgreSqlSqlStatementId.VerifySessionState, PostgreSqlSqlCommandKind.SelectVerification,
            PostgreSqlSqlInventory.VerifySessionStateSql, ThreeTimeouts()),
        (PostgreSqlSqlStatementId.ReadServerIdentity, PostgreSqlSqlCommandKind.SelectServerIdentity,
            PostgreSqlSqlInventory.ReadServerIdentitySql, NoParameters),
        (PostgreSqlSqlStatementId.CheckCatalogMetadataAccess, PostgreSqlSqlCommandKind.SelectCapabilityCheck,
            PostgreSqlSqlInventory.CheckCatalogMetadataAccessSql, NoParameters),
        (PostgreSqlSqlStatementId.CheckUsageStatisticsAccess, PostgreSqlSqlCommandKind.SelectCapabilityCheck,
            PostgreSqlSqlInventory.CheckUsageStatisticsAccessSql, NoParameters),
        (PostgreSqlSqlStatementId.ReadStatisticsReset, PostgreSqlSqlCommandKind.SelectStatistics,
            PostgreSqlSqlInventory.ReadStatisticsResetSql, NoParameters),
        (PostgreSqlSqlStatementId.ReadTableSnapshots, PostgreSqlSqlCommandKind.SelectTableMetadata,
            PostgreSqlSqlInventory.ReadTableSnapshotsSql, TwoSchemaFilters()),
        (PostgreSqlSqlStatementId.ReadIndexMetadata, PostgreSqlSqlCommandKind.SelectIndexMetadata,
            PostgreSqlSqlInventory.ReadIndexMetadataSql, TwoSchemaFilters()),
        (PostgreSqlSqlStatementId.ReadIndexUsageStatistics, PostgreSqlSqlCommandKind.SelectStatistics,
            PostgreSqlSqlInventory.ReadIndexUsageStatisticsSql, TwoSchemaFilters()),
    ];

    private static void Validate(
        PostgreSqlSqlStatementId id,
        PostgreSqlSqlCommandKind kind,
        string sql,
        params PostgreSqlSqlParameterDefinition[] parameters) =>
        PostgreSqlSqlSafetyValidator.Validate(
            new PostgreSqlSqlStatementDefinition(id, kind, sql, parameters, "frozen-contract test"));

    private static void AssertRejected(
        PostgreSqlSqlStatementId id,
        PostgreSqlSqlCommandKind kind,
        string sql,
        params PostgreSqlSqlParameterDefinition[] parameters) =>
        Assert.Throws<PostgreSqlSqlSafetyException>(() => Validate(id, kind, sql, parameters));

    // --- Positive: exactly the ten canonical definitions --------------------------------------

    [Fact]
    public void EveryCanonicalDefinition_IsAccepted()
    {
        foreach ((PostgreSqlSqlStatementId id, PostgreSqlSqlCommandKind kind, string sql, PostgreSqlSqlParameterDefinition[] parameters) in Canonical())
        {
            Validate(id, kind, sql, parameters);
        }

        Assert.Equal(10, Canonical().Length);
    }

    [Fact]
    public void AcrossEveryIdKindAndSqlCombination_ExactlyTenAreAccepted()
    {
        var canonical = Canonical();
        var accepted = new List<(PostgreSqlSqlStatementId Id, PostgreSqlSqlCommandKind Kind, int SqlIndex)>();

        foreach (PostgreSqlSqlStatementId id in Enum.GetValues<PostgreSqlSqlStatementId>())
        {
            foreach (PostgreSqlSqlCommandKind kind in Enum.GetValues<PostgreSqlSqlCommandKind>())
            {
                for (var sqlIndex = 0; sqlIndex < canonical.Length; sqlIndex++)
                {
                    try
                    {
                        Validate(id, kind, canonical[sqlIndex].Sql, canonical[sqlIndex].Parameters);
                        accepted.Add((id, kind, sqlIndex));
                    }
                    catch (PostgreSqlSqlSafetyException)
                    {
                        // Expected for every non-canonical combination.
                    }
                }
            }
        }

        // 10 ids x 8 kinds x 10 SQL texts = 800 combinations; exactly ten survive, so 790 are
        // rejected.
        Assert.Equal(800, Enum.GetValues<PostgreSqlSqlStatementId>().Length
            * Enum.GetValues<PostgreSqlSqlCommandKind>().Length
            * canonical.Length);
        Assert.Equal(10, accepted.Count);
        Assert.Equal(790, 800 - accepted.Count);

        for (var index = 0; index < canonical.Length; index++)
        {
            Assert.Contains((canonical[index].Id, canonical[index].Kind, index), accepted);
        }
    }

    [Fact]
    public void EveryDeclaredStatementId_HasAFrozenContract()
    {
        // A future enum member added without a contract would be unauthorized rather than
        // silently unconstrained — this test is what makes that visible.
        PostgreSqlSqlStatementId[] contracted = [.. Canonical().Select(entry => entry.Id)];

        Assert.Equal(Enum.GetValues<PostgreSqlSqlStatementId>().Length, contracted.Distinct().Count());

        foreach (PostgreSqlSqlStatementId id in Enum.GetValues<PostgreSqlSqlStatementId>())
        {
            Assert.Contains(id, contracted);
        }
    }

    // --- ID / kind binding ---------------------------------------------------------------------

    public static TheoryData<string, string> WrongKindPairs() => new()
    {
        { nameof(PostgreSqlSqlStatementId.ReadServerIdentity), nameof(PostgreSqlSqlCommandKind.SelectCapabilityCheck) },
        { nameof(PostgreSqlSqlStatementId.ReadServerIdentity), nameof(PostgreSqlSqlCommandKind.SelectStatistics) },
        { nameof(PostgreSqlSqlStatementId.ReadServerIdentity), nameof(PostgreSqlSqlCommandKind.SelectConfiguration) },
        { nameof(PostgreSqlSqlStatementId.ReadServerIdentity), nameof(PostgreSqlSqlCommandKind.SelectVerification) },
        { nameof(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess), nameof(PostgreSqlSqlCommandKind.SelectServerIdentity) },
        { nameof(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess), nameof(PostgreSqlSqlCommandKind.SelectStatistics) },
        { nameof(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess), nameof(PostgreSqlSqlCommandKind.SelectServerIdentity) },
        { nameof(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess), nameof(PostgreSqlSqlCommandKind.SelectStatistics) },
        { nameof(PostgreSqlSqlStatementId.ReadStatisticsReset), nameof(PostgreSqlSqlCommandKind.SelectCapabilityCheck) },
        { nameof(PostgreSqlSqlStatementId.ReadStatisticsReset), nameof(PostgreSqlSqlCommandKind.SelectServerIdentity) },
        { nameof(PostgreSqlSqlStatementId.ApplyLocalTimeouts), nameof(PostgreSqlSqlCommandKind.SelectVerification) },
        { nameof(PostgreSqlSqlStatementId.VerifySessionState), nameof(PostgreSqlSqlCommandKind.SelectConfiguration) },
        { nameof(PostgreSqlSqlStatementId.SetTransactionReadOnly), nameof(PostgreSqlSqlCommandKind.SelectVerification) },
        { nameof(PostgreSqlSqlStatementId.ReadTableSnapshots), nameof(PostgreSqlSqlCommandKind.SelectCapabilityCheck) },
        { nameof(PostgreSqlSqlStatementId.ReadTableSnapshots), nameof(PostgreSqlSqlCommandKind.SelectStatistics) },
        { nameof(PostgreSqlSqlStatementId.ReadTableSnapshots), nameof(PostgreSqlSqlCommandKind.SelectServerIdentity) },
        { nameof(PostgreSqlSqlStatementId.ReadTableSnapshots), nameof(PostgreSqlSqlCommandKind.SelectConfiguration) },
        { nameof(PostgreSqlSqlStatementId.ReadTableSnapshots), nameof(PostgreSqlSqlCommandKind.SelectVerification) },
        { nameof(PostgreSqlSqlStatementId.ReadServerIdentity), nameof(PostgreSqlSqlCommandKind.SelectTableMetadata) },
        { nameof(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess), nameof(PostgreSqlSqlCommandKind.SelectTableMetadata) },
        { nameof(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess), nameof(PostgreSqlSqlCommandKind.SelectTableMetadata) },
        { nameof(PostgreSqlSqlStatementId.ReadStatisticsReset), nameof(PostgreSqlSqlCommandKind.SelectTableMetadata) },
    };

    [Theory]
    [MemberData(nameof(WrongKindPairs))]
    public void CanonicalSql_UnderTheWrongCommandKind_IsRejected(string idName, string kindName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        var kind = Enum.Parse<PostgreSqlSqlCommandKind>(kindName);
        (_, _, string sql, PostgreSqlSqlParameterDefinition[] parameters) = Canonical().Single(entry => entry.Id == id);

        AssertRejected(id, kind, sql, parameters);
    }

    [Fact]
    public void ASharedCommandKind_IsNotPermissionToRunTheOtherStatement()
    {
        // C002 and C003 are both SelectCapabilityCheck, yet neither may carry the other's SQL.
        AssertRejected(
            PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
            PostgreSqlSqlCommandKind.SelectCapabilityCheck,
            PostgreSqlSqlInventory.CheckUsageStatisticsAccessSql);

        AssertRejected(
            PostgreSqlSqlStatementId.CheckUsageStatisticsAccess,
            PostgreSqlSqlCommandKind.SelectCapabilityCheck,
            PostgreSqlSqlInventory.CheckCatalogMetadataAccessSql);
    }

    [Fact]
    public void EveryCanonicalIdCarryingAnotherCanonicalSql_IsRejected()
    {
        var canonical = Canonical();

        for (var declared = 0; declared < canonical.Length; declared++)
        {
            for (var borrowed = 0; borrowed < canonical.Length; borrowed++)
            {
                if (declared == borrowed)
                {
                    continue;
                }

                AssertRejected(
                    canonical[declared].Id,
                    canonical[declared].Kind,
                    canonical[borrowed].Sql,
                    canonical[borrowed].Parameters);
            }
        }
    }

    // --- SQL the lexical layer alone would have accepted ---------------------------------------

    private static Dictionary<string, string> LexicallySafeImpostors() => new(StringComparer.Ordinal)
    {
        ["TrivialSelect"] = "SELECT 1",
        ["VersionFunction"] = "SELECT version()",
        ["BusinessTable"] = "SELECT * FROM business_table",
        ["CatalogRelation"] = "SELECT relname FROM pg_catalog.pg_class",
        ["UnionOfTrivialSelects"] = "SELECT 1 UNION SELECT 2",
        ["Aliased"] = "SELECT 1 AS catalog_metadata_available",
    };

    public static TheoryData<string> ImpostorNames() => [.. LexicallySafeImpostors().Keys];

    [Theory]
    [MemberData(nameof(ImpostorNames))]
    public void SqlTheLexicalLayerAccepts_IsStillNotAnAuthorizedDefinition(string impostor)
    {
        string sql = LexicallySafeImpostors()[impostor];

        // Layer 1 genuinely accepts these: they are safely classified SELECTs. That is exactly
        // why layer 2 has to exist.
        PostgreSqlSqlSafetyValidator.ValidateText(sql);

        foreach ((PostgreSqlSqlStatementId id, PostgreSqlSqlCommandKind kind, _, _) in Canonical())
        {
            if (kind == PostgreSqlSqlCommandKind.SetTransactionReadOnly)
            {
                continue;
            }

            AssertRejected(id, kind, sql);
        }
    }

    // --- Per-statement SQL mutations -----------------------------------------------------------

    /// <summary>
    /// Mutations that apply to any <c>SELECT</c>: added or removed text, a second row source, set
    /// operations, a placeholder, and the forms the lexical layer independently prohibits.
    /// </summary>
    private static Dictionary<string, string> StructuralMutations(string sql) => new(StringComparer.Ordinal)
    {
        ["Prefix"] = "SELECT 1, " + sql["SELECT".Length..],
        ["Suffix"] = sql + " AS trailing_alias",
        ["Semicolon"] = sql + ";",
        ["LineComment"] = sql + " -- trailing note",
        ["BlockComment"] = "/* leading note */ " + sql,
        ["ExtraFrom"] = sql + " FROM pg_catalog.pg_class",
        ["Join"] = sql + " JOIN pg_catalog.pg_class ON true",
        ["SecondRowSource"] = sql + " FROM pg_catalog.pg_class, pg_catalog.pg_namespace",
        ["Subquery"] = sql + ", (SELECT 1) AS subquery_column",
        ["Union"] = sql + " UNION SELECT 1",
        ["Intersect"] = sql + " INTERSECT SELECT 1",
        ["Except"] = sql + " EXCEPT SELECT 1",
        ["Lateral"] = sql + ", LATERAL (SELECT 1) AS lateral_source",
        ["Placeholder"] = sql + ", $1",
        ["ForUpdate"] = sql + " FOR UPDATE",
        ["SelectInto"] = sql + " INTO materialized_copy",
        ["BusinessTable"] = sql + " FROM public.business_table",
    };

    private static Dictionary<string, string> ServerIdentityMutations()
    {
        string sql = PostgreSqlSqlInventory.ReadServerIdentitySql;
        Dictionary<string, string> mutations = StructuralMutations(sql);

        mutations["RemovedToken"] = sql.Replace("::text", string.Empty, StringComparison.Ordinal);
        mutations["ChangedFunction"] = sql.Replace("current_setting", "current_schema", StringComparison.Ordinal);
        mutations["ChangedFunctionSchema"] = sql.Replace("pg_catalog.", "public.", StringComparison.Ordinal);
        mutations["ChangedObject"] = sql.Replace("current_database", "current_schema", StringComparison.Ordinal);
        mutations["ChangedStringLiteral"] = sql.Replace("'server_version_num'", "'server_version'", StringComparison.Ordinal);
        mutations["ParameterizedSetting"] = sql.Replace("'server_version_num'", "$1", StringComparison.Ordinal);

        return mutations;
    }

    private static Dictionary<string, string> CatalogCheckMutations()
    {
        string sql = PostgreSqlSqlInventory.CheckCatalogMetadataAccessSql;
        Dictionary<string, string> mutations = StructuralMutations(sql);

        mutations["RemovedToken"] = sql.Replace("AND pg_catalog.has_table_privilege(\n        current_user,\n        'pg_catalog.pg_opclass',\n        'SELECT')\n", string.Empty, StringComparison.Ordinal);
        mutations["ChangedFunction"] = sql.Replace("has_table_privilege", "has_any_column_privilege", StringComparison.Ordinal);
        mutations["ChangedFunctionSchema"] = sql.Replace("pg_catalog.has_", "public.has_", StringComparison.Ordinal);
        mutations["ChangedObject"] = sql.Replace("'pg_catalog.pg_class'", "'pg_catalog.pg_type'", StringComparison.Ordinal);
        mutations["ChangedObjectSchema"] = sql.Replace("'pg_catalog.pg_class'", "'public.pg_class'", StringComparison.Ordinal);
        mutations["ChangedStringLiteral"] = sql.Replace("'SELECT'", "'INSERT'", StringComparison.Ordinal);
        mutations["ParameterizedPrivilege"] = sql.Replace("'SELECT'", "$1", StringComparison.Ordinal);
        mutations["ExtraRelation"] = sql.Replace(
            "AS catalog_metadata_available",
            "AND pg_catalog.has_table_privilege(current_user, 'pg_catalog.pg_proc', 'SELECT') AS catalog_metadata_available",
            StringComparison.Ordinal);

        return mutations;
    }

    private static Dictionary<string, string> StatisticsCheckMutations()
    {
        string sql = PostgreSqlSqlInventory.CheckUsageStatisticsAccessSql;
        Dictionary<string, string> mutations = StructuralMutations(sql);

        mutations["RemovedToken"] = sql.Replace("AS usage_statistics_available", string.Empty, StringComparison.Ordinal);
        mutations["ChangedFunction"] = sql.Replace("has_table_privilege", "has_any_column_privilege", StringComparison.Ordinal);
        mutations["ChangedFunctionSchema"] = sql.Replace("pg_catalog.has_", "public.has_", StringComparison.Ordinal);
        mutations["ChangedObject"] = sql.Replace("pg_stat_database", "pg_stat_user_tables", StringComparison.Ordinal);
        mutations["ChangedObjectSchema"] = sql.Replace("'pg_catalog.pg_stat_database'", "'public.pg_stat_database'", StringComparison.Ordinal);
        mutations["ChangedStringLiteral"] = sql.Replace("'SELECT'", "'INSERT'", StringComparison.Ordinal);
        mutations["ParameterizedPrivilege"] = sql.Replace("'SELECT'", "$1", StringComparison.Ordinal);

        return mutations;
    }

    private static Dictionary<string, string> StatisticsReadMutations()
    {
        string sql = PostgreSqlSqlInventory.ReadStatisticsResetSql;
        Dictionary<string, string> mutations = StructuralMutations(sql);

        mutations["RemovedToken"] = sql.Replace(" AS statistics", string.Empty, StringComparison.Ordinal);
        mutations["ChangedFunction"] = sql.Replace("current_database", "current_schema", StringComparison.Ordinal);
        mutations["ChangedFunctionSchema"] = sql.Replace("pg_catalog.current_database", "public.current_database", StringComparison.Ordinal);
        mutations["ChangedObject"] = sql.Replace("pg_stat_database", "pg_stat_user_tables", StringComparison.Ordinal);
        mutations["ChangedObjectSchema"] = sql.Replace("FROM pg_catalog.", "FROM public.", StringComparison.Ordinal);
        mutations["ChangedColumn"] = sql.Replace("statistics.stats_reset", "statistics.datname", StringComparison.Ordinal);
        mutations["ParameterizedPredicate"] = sql.Replace("pg_catalog.current_database()", "$1", StringComparison.Ordinal);

        return mutations;
    }

    public static TheoryData<string> ServerIdentityMutationNames() => [.. ServerIdentityMutations().Keys];

    public static TheoryData<string> CatalogCheckMutationNames() => [.. CatalogCheckMutations().Keys];

    public static TheoryData<string> StatisticsCheckMutationNames() => [.. StatisticsCheckMutations().Keys];

    public static TheoryData<string> StatisticsReadMutationNames() => [.. StatisticsReadMutations().Keys];

    [Theory]
    [MemberData(nameof(ServerIdentityMutationNames))]
    public void C001_RejectsEveryMutation(string mutation)
    {
        string mutated = ServerIdentityMutations()[mutation];

        Assert.NotEqual(PostgreSqlSqlInventory.ReadServerIdentitySql, mutated);
        AssertRejected(
            PostgreSqlSqlStatementId.ReadServerIdentity, PostgreSqlSqlCommandKind.SelectServerIdentity, mutated);
    }

    [Theory]
    [MemberData(nameof(CatalogCheckMutationNames))]
    public void C002_RejectsEveryMutation(string mutation)
    {
        string mutated = CatalogCheckMutations()[mutation];

        Assert.NotEqual(PostgreSqlSqlInventory.CheckCatalogMetadataAccessSql, mutated);
        AssertRejected(
            PostgreSqlSqlStatementId.CheckCatalogMetadataAccess, PostgreSqlSqlCommandKind.SelectCapabilityCheck, mutated);
    }

    [Theory]
    [MemberData(nameof(StatisticsCheckMutationNames))]
    public void C003_RejectsEveryMutation(string mutation)
    {
        string mutated = StatisticsCheckMutations()[mutation];

        Assert.NotEqual(PostgreSqlSqlInventory.CheckUsageStatisticsAccessSql, mutated);
        AssertRejected(
            PostgreSqlSqlStatementId.CheckUsageStatisticsAccess, PostgreSqlSqlCommandKind.SelectCapabilityCheck, mutated);
    }

    [Theory]
    [MemberData(nameof(StatisticsReadMutationNames))]
    public void C004_RejectsEveryMutation(string mutation)
    {
        string mutated = StatisticsReadMutations()[mutation];

        Assert.NotEqual(PostgreSqlSqlInventory.ReadStatisticsResetSql, mutated);
        AssertRejected(
            PostgreSqlSqlStatementId.ReadStatisticsReset, PostgreSqlSqlCommandKind.SelectStatistics, mutated);
    }

    private static Dictionary<string, string> TableSnapshotMutations()
    {
        string sql = PostgreSqlSqlInventory.ReadTableSnapshotsSql;
        Dictionary<string, string> mutations = StructuralMutations(sql);

        // Catalog table, join and correlated existence test.
        mutations["ChangedCatalogTable"] = sql.Replace("pg_catalog.pg_class AS relation", "pg_catalog.pg_foreign_table AS relation", StringComparison.Ordinal);
        mutations["ChangedJoinTarget"] = sql.Replace("INNER JOIN pg_catalog.pg_namespace", "INNER JOIN pg_catalog.pg_authid", StringComparison.Ordinal);
        mutations["ChangedJoinPredicate"] = sql.Replace("ON namespace.oid = relation.relnamespace", "ON namespace.oid = relation.reltablespace", StringComparison.Ordinal);
        mutations["ChangedJoinType"] = sql.Replace("INNER JOIN", "LEFT JOIN", StringComparison.Ordinal);
        mutations["ChangedConstraintTable"] = sql.Replace("pg_catalog.pg_constraint AS constraint_record", "pg_catalog.pg_index AS constraint_record", StringComparison.Ordinal);

        // Mandatory system-schema filters.
        mutations["RemovedCatalogFilter"] = sql.Replace("  AND namespace.nspname <> 'pg_catalog'\n", "", StringComparison.Ordinal);
        mutations["RemovedInformationSchemaFilter"] = sql.Replace("  AND namespace.nspname <> 'information_schema'\n", "", StringComparison.Ordinal);
        mutations["RemovedToastFilter"] = sql.Replace("  AND namespace.nspname NOT LIKE 'pg_toast%'\n", "", StringComparison.Ordinal);
        mutations["RemovedTempFilter"] = sql.Replace("  AND namespace.nspname NOT LIKE 'pg_temp_%'\n", "", StringComparison.Ordinal);

        // Relation-kind allowlist.
        mutations["AddedIndexRelkind"] = sql.Replace("IN ('r', 'p', 'v', 'm', 'f')", "IN ('r', 'p', 'v', 'm', 'f', 'i')", StringComparison.Ordinal);
        mutations["AddedSequenceRelkind"] = sql.Replace("IN ('r', 'p', 'v', 'm', 'f')", "IN ('r', 'p', 'v', 'm', 'f', 'S')", StringComparison.Ordinal);
        mutations["RemovedForeignRelkind"] = sql.Replace("IN ('r', 'p', 'v', 'm', 'f')", "IN ('r', 'p', 'v', 'm')", StringComparison.Ordinal);
        mutations["ChangedSizedRelkinds"] = sql.Replace("relation.relkind IN ('r', 'm', 'p')", "relation.relkind IN ('r', 'm')", StringComparison.Ordinal);

        // Parameters.
        mutations["SwappedParameters"] = sql
            .Replace("$1::text[]", "$9::text[]", StringComparison.Ordinal)
            .Replace("$2::text[]", "$1::text[]", StringComparison.Ordinal)
            .Replace("$9::text[]", "$2::text[]", StringComparison.Ordinal);
        mutations["MissingIncludeParameter"] = sql.Replace("$1::text[]", "$2::text[]", StringComparison.Ordinal);
        mutations["ExtraParameter"] = sql.Replace("= ANY($2::text[])", "= ANY($2::text[]) OR namespace.nspname = $3", StringComparison.Ordinal);
        mutations["ChangedArrayType"] = sql.Replace("::text[]", "::name[]", StringComparison.Ordinal);
        mutations["ConcatenatedSchema"] = sql.Replace("namespace.nspname::text = ANY($1::text[])", "namespace.nspname = 'public'", StringComparison.Ordinal);

        // Result semantics.
        mutations["ChangedPrimaryKeyPredicate"] = sql.Replace("constraint_record.contype = 'p'", "constraint_record.contype = 'u'", StringComparison.Ordinal);
        mutations["ChangedPrimaryKeyCorrelation"] = sql.Replace("constraint_record.conrelid = relation.oid", "constraint_record.conindid = relation.oid", StringComparison.Ordinal);
        mutations["ChangedSizeFunction"] = sql.Replace("pg_catalog.pg_table_size(relation.oid)", "pg_catalog.pg_relation_size(relation.oid)", StringComparison.Ordinal);
        mutations["AggregatedRootSize"] = sql.Replace("pg_catalog.pg_total_relation_size(relation.oid)", "pg_catalog.pg_total_relation_size(relation.oid) + 1", StringComparison.Ordinal);
        mutations["ChangedEstimateGuard"] = sql.Replace("OR relation.reltuples < 0", "OR relation.reltuples < -1", StringComparison.Ordinal);
        mutations["ChangedOrderBy"] = sql.Replace("ORDER BY\n    namespace.nspname,\n    relation.relname", "ORDER BY\n    relation.relname,\n    namespace.nspname", StringComparison.Ordinal);
        mutations["RemovedOrderBy"] = sql.Replace("\nORDER BY\n    namespace.nspname,\n    relation.relname", "", StringComparison.Ordinal);
        mutations["CountAggregate"] = sql.Replace("relation.relispartition\n        AS is_partition", "COUNT(*)\n        AS is_partition", StringComparison.Ordinal);

        // GC-DHI-04E index SQL must never be accepted under D001's identity.
        mutations["IndexStatement"] = """
            SELECT
                namespace.nspname::text
                    AS schema_name,
                index_relation.relname::text
                    AS index_name
            FROM pg_catalog.pg_index AS index_record
            INNER JOIN pg_catalog.pg_class AS index_relation
                ON index_relation.oid = index_record.indexrelid
            INNER JOIN pg_catalog.pg_namespace AS namespace
                ON namespace.oid = index_relation.relnamespace
            """;

        return mutations;
    }

    public static TheoryData<string> TableSnapshotMutationNames() => [.. TableSnapshotMutations().Keys];

    [Theory]
    [MemberData(nameof(TableSnapshotMutationNames))]
    public void D001_RejectsEveryMutation(string mutation)
    {
        string mutated = TableSnapshotMutations()[mutation];

        Assert.NotEqual(PostgreSqlSqlInventory.ReadTableSnapshotsSql, mutated);
        AssertRejected(
            PostgreSqlSqlStatementId.ReadTableSnapshots,
            PostgreSqlSqlCommandKind.SelectTableMetadata,
            mutated,
            TwoSchemaFilters());
    }

    [Fact]
    public void D001_RejectsAWrongParameterCount()
    {
        // One declaration, none at all, and three: only the exact pair is authorized.
        AssertRejected(
            PostgreSqlSqlStatementId.ReadTableSnapshots, PostgreSqlSqlCommandKind.SelectTableMetadata,
            PostgreSqlSqlInventory.ReadTableSnapshotsSql,
            new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.TextArray, "only one"));

        AssertRejected(
            PostgreSqlSqlStatementId.ReadTableSnapshots, PostgreSqlSqlCommandKind.SelectTableMetadata,
            PostgreSqlSqlInventory.ReadTableSnapshotsSql);

        AssertRejected(
            PostgreSqlSqlStatementId.ReadTableSnapshots, PostgreSqlSqlCommandKind.SelectTableMetadata,
            PostgreSqlSqlInventory.ReadTableSnapshotsSql,
            [.. TwoSchemaFilters(), new PostgreSqlSqlParameterDefinition(3, PostgreSqlSqlParameterType.TextArray, "extra")]);
    }

    [Fact]
    public void D001_RejectsAWrongParameterType()
    {
        // Now that two parameter types exist, the frozen contract's type comparison is live.
        AssertRejected(
            PostgreSqlSqlStatementId.ReadTableSnapshots, PostgreSqlSqlCommandKind.SelectTableMetadata,
            PostgreSqlSqlInventory.ReadTableSnapshotsSql,
            new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "wrong type"),
            new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.TextArray, "excluded schema names"));

        AssertRejected(
            PostgreSqlSqlStatementId.ReadTableSnapshots, PostgreSqlSqlCommandKind.SelectTableMetadata,
            PostgreSqlSqlInventory.ReadTableSnapshotsSql,
            new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.TextArray, "included schema names"),
            new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.Int32, "wrong type"));
    }

    [Fact]
    public void B002AndB003_RejectTextArrayDeclarations()
    {
        // The timeout statements keep their exact Int32 contract.
        PostgreSqlSqlParameterDefinition[] textArrays =
        [
            new(1, PostgreSqlSqlParameterType.TextArray, "wrong"),
            new(2, PostgreSqlSqlParameterType.TextArray, "wrong"),
            new(3, PostgreSqlSqlParameterType.TextArray, "wrong"),
        ];

        AssertRejected(
            PostgreSqlSqlStatementId.ApplyLocalTimeouts, PostgreSqlSqlCommandKind.SelectConfiguration,
            PostgreSqlSqlInventory.ApplyLocalTimeoutsSql, textArrays);
        AssertRejected(
            PostgreSqlSqlStatementId.VerifySessionState, PostgreSqlSqlCommandKind.SelectVerification,
            PostgreSqlSqlInventory.VerifySessionStateSql, textArrays);
    }

    // --- The lexical layer, exercised by mutating canonical statements -------------------------

    /// <summary>
    /// Mutations the scanner rejects on its own, independently of the frozen contract. Applied to
    /// canonical SQL rather than to invented text, so the scanner is proven against exactly the
    /// statements the product ships (GC-DHI-04C-C1 §5).
    /// </summary>
    private static Dictionary<string, string> LexicallyProhibitedMutations(string sql) => new(StringComparer.Ordinal)
    {
        ["TrailingSemicolon"] = sql + ";",
        ["SecondStatement"] = sql + "; SELECT 1",
        ["LineComment"] = sql + " -- trailing note",
        ["BlockComment"] = "/* leading note */ " + sql,
        ["DollarQuote"] = sql + " $tag$ payload $tag$",
        ["EmptyDollarQuote"] = sql + " $$ payload $$",
        ["BackslashCommand"] = sql + " \\d",
        ["ForUpdate"] = sql + " FOR UPDATE",
        ["ForShare"] = sql + " FOR SHARE",
        ["SelectInto"] = sql + " INTO materialized_copy",
        ["SinglePipe"] = sql + " | 1",
        ["TriplePipe"] = sql + " ||| 1",
        ["EmbeddedNul"] = sql + "\0",
    };

    public static TheoryData<string> LexicallyProhibitedMutationNames() =>
        [.. LexicallyProhibitedMutations("SELECT 1").Keys];

    [Theory]
    [MemberData(nameof(LexicallyProhibitedMutationNames))]
    public void TheLexicalLayerAlone_RejectsThisMutationOfEveryCanonicalStatement(string mutation)
    {
        foreach ((_, _, string sql, _) in Canonical())
        {
            string mutated = LexicallyProhibitedMutations(sql)[mutation];

            Assert.Throws<PostgreSqlSqlSafetyException>(() => PostgreSqlSqlSafetyValidator.ValidateText(mutated));
        }
    }

    [Fact]
    public void EveryCanonicalStatement_StillPassesTheLexicalLayerUnmutated()
    {
        // The scanner must not have been tightened into rejecting what the product actually runs.
        foreach ((_, _, string sql, _) in Canonical())
        {
            PostgreSqlSqlSafetyValidator.ValidateText(sql);
        }
    }

    // --- Parameter declarations ----------------------------------------------------------------

    public static TheoryData<string> ParameterlessStatements() =>
    [
        nameof(PostgreSqlSqlStatementId.ReadServerIdentity),
        nameof(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess),
        nameof(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess),
        nameof(PostgreSqlSqlStatementId.ReadStatisticsReset),
    ];

    [Theory]
    [MemberData(nameof(ParameterlessStatements))]
    public void ParameterlessStatement_RejectsAnExtraDeclaredParameter(string idName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        (_, PostgreSqlSqlCommandKind kind, string sql, _) = Canonical().Single(entry => entry.Id == id);

        AssertRejected(id, kind, sql, new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "unexpected"));
    }

    [Fact]
    public void ParameterisedStatements_RejectTooFewAndTooManyDeclarations()
    {
        PostgreSqlSqlParameterDefinition[] twoOfThree = [.. ThreeTimeouts().Take(2)];
        PostgreSqlSqlParameterDefinition[] fourDeclarations =
        [
            .. ThreeTimeouts(),
            new PostgreSqlSqlParameterDefinition(4, PostgreSqlSqlParameterType.Int32, "unexpected"),
        ];

        AssertRejected(
            PostgreSqlSqlStatementId.ApplyLocalTimeouts, PostgreSqlSqlCommandKind.SelectConfiguration,
            PostgreSqlSqlInventory.ApplyLocalTimeoutsSql, twoOfThree);
        AssertRejected(
            PostgreSqlSqlStatementId.ApplyLocalTimeouts, PostgreSqlSqlCommandKind.SelectConfiguration,
            PostgreSqlSqlInventory.ApplyLocalTimeoutsSql, fourDeclarations);

        AssertRejected(
            PostgreSqlSqlStatementId.VerifySessionState, PostgreSqlSqlCommandKind.SelectVerification,
            PostgreSqlSqlInventory.VerifySessionStateSql, twoOfThree);
        AssertRejected(
            PostgreSqlSqlStatementId.VerifySessionState, PostgreSqlSqlCommandKind.SelectVerification,
            PostgreSqlSqlInventory.VerifySessionStateSql, fourDeclarations);
    }

    [Fact]
    public void AParameterDeclaredOutOfPosition_CannotEvenBeConstructed()
    {
        // The declaration list is required to be consecutive from position 1, so a wrong position
        // is rejected one layer earlier than the validator.
        Assert.Throws<ArgumentException>(() => new PostgreSqlSqlStatementDefinition(
            PostgreSqlSqlStatementId.ApplyLocalTimeouts,
            PostgreSqlSqlCommandKind.SelectConfiguration,
            PostgreSqlSqlInventory.ApplyLocalTimeoutsSql,
            [
                new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "first"),
                new PostgreSqlSqlParameterDefinition(3, PostgreSqlSqlParameterType.Int32, "out of position"),
                new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.Int32, "out of position"),
            ],
            "frozen-contract test"));
    }

    [Fact]
    public void AParameterOfAnUndefinedType_CannotEvenBeConstructed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PostgreSqlSqlParameterDefinition(1, (PostgreSqlSqlParameterType)999, "undefined type"));
    }

    // --- Unknown enumeration values ------------------------------------------------------------

    [Fact]
    public void AnUndefinedStatementId_CannotEvenBeConstructed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PostgreSqlSqlStatementDefinition(
            (PostgreSqlSqlStatementId)999,
            PostgreSqlSqlCommandKind.SelectServerIdentity,
            PostgreSqlSqlInventory.ReadServerIdentitySql,
            NoParameters,
            "frozen-contract test"));
    }

    [Fact]
    public void AnUndefinedCommandKind_CannotEvenBeConstructed()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PostgreSqlSqlStatementDefinition(
            PostgreSqlSqlStatementId.ReadServerIdentity,
            (PostgreSqlSqlCommandKind)999,
            PostgreSqlSqlInventory.ReadServerIdentitySql,
            NoParameters,
            "frozen-contract test"));
    }

    // --- The exception stays sanitized ----------------------------------------------------------

    [Fact]
    public void AFrozenContractRejection_CarriesNoSqlAndNoDetail()
    {
        PostgreSqlSqlSafetyException exception = Assert.Throws<PostgreSqlSqlSafetyException>(
            () => Validate(
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlCommandKind.SelectServerIdentity,
                "SELECT 1 AS impostor_marker"));

        Assert.Equal("The PostgreSQL statement failed SQL safety validation.", exception.Message);
        bool leaked = exception.ToString().Contains("impostor_marker", StringComparison.Ordinal);
        Assert.False(leaked, "The safety exception exposed statement text.");
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }
}
