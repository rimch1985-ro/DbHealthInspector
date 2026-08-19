using Npgsql;
using Testcontainers.PostgreSql;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// A dedicated PostgreSQL <b>15.18</b> container carrying the cross-version object zoo, so the
/// GC-DHI-04F provider and the shared 04A–04E contracts are verified on the oldest supported major
/// as well as the newest.
/// </summary>
/// <remarks>
/// <para>
/// Completely isolated from <see cref="PostgreSqlServerFixture"/>: its own pinned image, container,
/// database, credentials and roles. No mutable state is shared between majors, so neither suite can
/// contaminate the other and either can run alone.
/// </para>
/// <para>
/// The zoo is deliberately the <i>common</i> subset. Cases that are genuinely 18-only empirical
/// evidence — the relation-state discovery matrix, the FDW proofs and the permission-loss
/// topologies — remain in the PostgreSQL 18 suite, which stays the exhaustive platform.
/// </para>
/// </remarks>
public sealed class PostgreSql15Fixture : IAsyncLifetime
{
    /// <summary>
    /// The exact PostgreSQL 15 image, pinned by tag and immutable digest. Never floating
    /// <c>postgres:15</c>. Verified to report <c>15.18 (Debian 15.18-1.pgdg13+1)</c> and
    /// <c>server_version_num = 150018</c>.
    /// </summary>
    public const string ImageReference =
        "postgres:15.18@sha256:6eb0add3b77c081df18aa518ce43df58fdcc40f2e6d868a6fd08038dc7acd425";

    public const string DatabaseName = "dbhealth_pg15_test";
    public const string InspectionRoleName = "dbhealth_pg15_role";

    /// <summary>The schema holding the cross-version table and index zoo.</summary>
    public const string ObjectSchema = "dbhealth_pg15_objects";

    /// <summary>A second schema, so include and exclude filters have something to choose between.</summary>
    public const string SecondarySchema = "dbhealth_pg15_secondary";

    public const string IndexedTable = "indexed_orders";

    public const long IndexedRowCount = 500;

    private const string AdminUser = "dbhealth_pg15_admin";
    private const string AdminPassword = "synthetic-pg15-admin-password";
    private const string InspectionPassword = "synthetic-pg15-inspection-password";

    private PostgreSqlContainer? _container;

    public string InspectionConnectionString => BuildConnectionString(InspectionRoleName, InspectionPassword);

    public string AdminConnectionString => BuildConnectionString(AdminUser, AdminPassword);

    public ValueTask InitializeAsync() =>
        TestFixtureLifecycle.InitializeGuardedAsync(
            async token =>
            {
                _container = new PostgreSqlBuilder(ImageReference)
                    .WithDatabase(DatabaseName)
                    .WithUsername(AdminUser)
                    .WithPassword(AdminPassword)
                    .Build();

                await _container.StartAsync(token);
                await CreateObjectsAsync(token);
            },
            ReleaseContainerAsync,
            TestContext.Current.CancellationToken);

    public ValueTask DisposeAsync() => ReleaseContainerAsync();

    private async ValueTask ReleaseContainerAsync()
    {
        if (_container is { } container)
        {
            _container = null;
            await container.DisposeAsync();
        }
    }

    public async Task<NpgsqlConnection> OpenAdminConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>The server's own reported version number, for cross-version assertions.</summary>
    public async Task<int> ReadServerVersionNumberAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT current_setting('server_version_num')::integer", connection);

        return (int)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Reads one index attribute's raw <c>attoptions</c> out of band, so a mapped structural
    /// identity can be compared against what this server actually stored.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadOperatorClassOptionsAsync(
        string schema, string indexName, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT COALESCE(index_attribute.attoptions, '{}'::text[])
            FROM pg_catalog.pg_class AS index_relation
            INNER JOIN pg_catalog.pg_namespace AS index_namespace
                ON index_namespace.oid = index_relation.relnamespace
            INNER JOIN pg_catalog.pg_attribute AS index_attribute
                ON index_attribute.attrelid = index_relation.oid
               AND index_attribute.attnum = 1
            WHERE index_namespace.nspname = @schema
              AND index_relation.relname = @index
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("index", indexName);

        return (string[])(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Revokes the optional usage-statistics privilege, so the degraded branch can be proven on
    /// this major too. Applied to a dedicated role, never to the normal inspection role.
    /// </summary>
    public string StatisticsRevokedConnectionString =>
        BuildConnectionString(StatisticsRevokedRoleName, InspectionPassword);

    public const string StatisticsRevokedRoleName = "dbhealth_pg15_nostats_role";

    private string BuildConnectionString(string username, string password)
    {
        PostgreSqlContainer container = _container
            ?? throw new InvalidOperationException("The PostgreSQL 15 fixture has not been initialized.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5432),
            Database = DatabaseName,
            Username = username,
            Password = password,
        }.ConnectionString;
    }

    /// <summary>
    /// Builds the cross-version zoo. Every access method used is a PostgreSQL built-in and every
    /// shape below was confirmed to be accepted by 15.18; no extension is installed.
    /// </summary>
    private async Task CreateObjectsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);

        string[] statements =
        [
            $"CREATE ROLE \"{InspectionRoleName}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{InspectionPassword}'",
            $"GRANT CONNECT ON DATABASE \"{DatabaseName}\" TO \"{InspectionRoleName}\"",

            // A second role whose optional usage-statistics access is revoked, proving the
            // degraded branch on this major without disturbing the normal role.
            $"CREATE ROLE \"{StatisticsRevokedRoleName}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{InspectionPassword}'",
            $"GRANT CONNECT ON DATABASE \"{DatabaseName}\" TO \"{StatisticsRevokedRoleName}\"",
            $"REVOKE SELECT ON pg_catalog.pg_stat_all_indexes FROM PUBLIC",
            $"REVOKE SELECT ON pg_catalog.pg_stat_all_indexes FROM \"{StatisticsRevokedRoleName}\"",
            $"GRANT SELECT ON pg_catalog.pg_stat_all_indexes TO \"{InspectionRoleName}\"",

            $"CREATE SCHEMA \"{ObjectSchema}\"",
            $"CREATE SCHEMA \"{SecondarySchema}\"",

            $"""
             CREATE TABLE "{ObjectSchema}"."{IndexedTable}" (
                 id integer NOT NULL,
                 code text NOT NULL,
                 label text,
                 amount integer NOT NULL,
                 quantity integer NOT NULL,
                 document jsonb,
                 span int4range,
                 CONSTRAINT indexed_orders_pkey PRIMARY KEY (id)
             )
             """,

            // A unique constraint and an exclusion constraint, each backing an index.
            $"ALTER TABLE \"{ObjectSchema}\".\"{IndexedTable}\" ADD CONSTRAINT indexed_orders_code_key UNIQUE (code)",
            $"ALTER TABLE \"{ObjectSchema}\".\"{IndexedTable}\" ADD CONSTRAINT indexed_orders_span_excl EXCLUDE USING gist (span WITH &&)",

            // B-tree shapes.
            $"CREATE INDEX zoo_btree_simple ON \"{ObjectSchema}\".\"{IndexedTable}\" (amount)",
            $"CREATE INDEX zoo_btree_multi ON \"{ObjectSchema}\".\"{IndexedTable}\" (amount ASC NULLS LAST, quantity DESC NULLS FIRST)",
            $"CREATE UNIQUE INDEX zoo_unique_nnd ON \"{ObjectSchema}\".\"{IndexedTable}\" (label) NULLS NOT DISTINCT",
            $"CREATE INDEX zoo_include ON \"{ObjectSchema}\".\"{IndexedTable}\" (amount) INCLUDE (quantity, code)",
            $"CREATE INDEX zoo_expression ON \"{ObjectSchema}\".\"{IndexedTable}\" (lower(code))",
            $"CREATE INDEX zoo_mixed ON \"{ObjectSchema}\".\"{IndexedTable}\" (amount, lower(code))",
            $"CREATE INDEX zoo_partial ON \"{ObjectSchema}\".\"{IndexedTable}\" (amount) WHERE quantity > 10",
            $"CREATE INDEX zoo_collation ON \"{ObjectSchema}\".\"{IndexedTable}\" (code COLLATE \"C\")",
            $"CREATE INDEX zoo_opclass ON \"{ObjectSchema}\".\"{IndexedTable}\" (code text_pattern_ops)",

            // Non-B-tree access methods: each is non-orderable, exercising the normalization path.
            $"CREATE INDEX zoo_hash ON \"{ObjectSchema}\".\"{IndexedTable}\" USING hash (amount)",
            $"CREATE INDEX zoo_gin ON \"{ObjectSchema}\".\"{IndexedTable}\" USING gin (document)",
            $"CREATE INDEX zoo_gist ON \"{ObjectSchema}\".\"{IndexedTable}\" USING gist (span)",
            $"CREATE INDEX zoo_spgist ON \"{ObjectSchema}\".\"{IndexedTable}\" USING spgist (span)",
            $"CREATE INDEX zoo_brin ON \"{ObjectSchema}\".\"{IndexedTable}\" USING brin (amount)",

            // Operator-class options, including the inverse stored order pair.
            $"CREATE INDEX zoo_opts_32 ON \"{ObjectSchema}\".\"{IndexedTable}\" USING brin (amount int4_minmax_multi_ops(values_per_range=32))",
            $"CREATE INDEX zoo_opts_64 ON \"{ObjectSchema}\".\"{IndexedTable}\" USING brin (amount int4_minmax_multi_ops(values_per_range=64))",
            $"CREATE INDEX zoo_opts_order_ab ON \"{ObjectSchema}\".\"{IndexedTable}\" USING brin (amount int4_bloom_ops(n_distinct_per_range=16, false_positive_rate=0.05))",
            $"CREATE INDEX zoo_opts_order_ba ON \"{ObjectSchema}\".\"{IndexedTable}\" USING brin (amount int4_bloom_ops(false_positive_rate=0.05, n_distinct_per_range=16))",

            // A partitioned table with one partition: a virtual index root plus its physical child.
            $"""
             CREATE TABLE "{ObjectSchema}".partitioned_orders (
                 id integer NOT NULL,
                 region text NOT NULL
             ) PARTITION BY LIST (region)
             """,
            $"CREATE TABLE \"{ObjectSchema}\".partitioned_orders_emea PARTITION OF \"{ObjectSchema}\".partitioned_orders FOR VALUES IN ('emea')",
            $"CREATE INDEX zoo_partitioned ON \"{ObjectSchema}\".partitioned_orders (region)",

            // ON ONLY the partitioned parent: deterministically invalid until a matching index is
            // attached for every partition. No CONCURRENTLY timing trick and no catalog write.
            $"CREATE INDEX zoo_invalid_root ON ONLY \"{ObjectSchema}\".partitioned_orders (id)",

            // A view and a materialized view, so relation-kind mapping is covered here too.
            $"CREATE VIEW \"{ObjectSchema}\".orders_view AS SELECT id FROM \"{ObjectSchema}\".\"{IndexedTable}\"",
            $"CREATE MATERIALIZED VIEW \"{ObjectSchema}\".orders_matview AS SELECT id FROM \"{ObjectSchema}\".\"{IndexedTable}\"",

            // A populated second schema, so include/exclude filters have something to select.
            $"CREATE TABLE \"{SecondarySchema}\".secondary_table (id integer PRIMARY KEY)",

            // Labels are distinct: zoo_unique_nnd is UNIQUE ... NULLS NOT DISTINCT, so repeated
            // NULLs would collide. The flag itself is what the mapper reads; the data only has to
            // satisfy the index.
            $"INSERT INTO \"{ObjectSchema}\".\"{IndexedTable}\" SELECT g, 'code-' || g, 'label-' || g, g, g, NULL, NULL FROM generate_series(1, {IndexedRowCount}) g",
            $"ANALYZE \"{ObjectSchema}\".\"{IndexedTable}\"",

            $"GRANT USAGE ON SCHEMA \"{ObjectSchema}\" TO \"{InspectionRoleName}\"",
            $"GRANT USAGE ON SCHEMA \"{SecondarySchema}\" TO \"{InspectionRoleName}\"",
            $"GRANT USAGE ON SCHEMA \"{ObjectSchema}\" TO \"{StatisticsRevokedRoleName}\"",
            $"GRANT USAGE ON SCHEMA \"{SecondarySchema}\" TO \"{StatisticsRevokedRoleName}\"",
        ];

        foreach (string statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

/// <summary>
/// The PostgreSQL 15 compatibility suite: its own collection, its own container, no parallelism.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSql15Suite : ICollectionFixture<PostgreSql15Fixture>
{
    public const string Name = "PostgreSql15";
}
