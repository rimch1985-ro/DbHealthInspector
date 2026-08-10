using DbHealthInspector.PostgreSql.Sql;
using Npgsql;
using NpgsqlTypes;
using Testcontainers.PostgreSql;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// A focused PostgreSQL 18 fixture: one container pinned to an exact tag <b>and</b> immutable
/// digest, a synthetic schema, a persistent control table with a single control row, and a
/// deliberately unprivileged inspection role.
/// </summary>
/// <remarks>
/// <para>
/// The administrative role is used only to build and tear down the fixture. Every inspected
/// session connects as <see cref="InspectionRoleName"/>, which is <c>NOSUPERUSER</c> and is
/// granted <c>UPDATE</c> on the control table on purpose: the write-rejection contract must prove
/// that read-only transaction enforcement blocks the write, not that a permission was missing.
/// </para>
/// <para>
/// Credentials here are synthetic and exist only for the lifetime of the container. No connection
/// string or password is ever written to test output.
/// </para>
/// </remarks>
public sealed class PostgreSqlServerFixture : IAsyncLifetime
{
    /// <summary>
    /// The exact PostgreSQL 18 image, pinned by tag and immutable digest. Never <c>latest</c>.
    /// Resolved with <c>docker pull postgres:18.4</c> on 2026-08-01 and verified to report
    /// <c>PostgreSQL 18.4</c> (<c>server_version_num</c> 180004).
    /// </summary>
    public const string ImageReference =
        "postgres:18.4@sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a";

    public const string DatabaseName = "dbhealth_inspector_test";
    public const string SyntheticSchema = "dbhealth_synthetic";
    public const string ControlTable = "control_marker";
    public const string OriginalMarkerValue = "original-control-marker";
    public const string InspectionRoleName = "dbhealth_inspection_role";

    /// <summary>
    /// The schema holding the GC-DHI-04D relation zoo: one relation of every kind D001 admits,
    /// plus the partition tree.
    /// </summary>
    public const string TableSchema = "dbhealth_tables";

    /// <summary>
    /// A second populated schema, so include/exclude filters have something to select between.
    /// </summary>
    public const string SecondaryTableSchema = "dbhealth_tables_secondary";

    /// <summary>
    /// The <c>application_name</c> every <c>postgres_fdw</c> backend announces. A row carrying it
    /// in <c>pg_stat_activity</c> is proof that a remote connection was actually opened.
    /// </summary>
    public const string ForeignServerApplicationName = "dbhealth_fdw_marker";

    /// <summary>
    /// The loopback <c>postgres_fdw</c> server. Naming it lets a test assert on the
    /// <b>target</b> server rather than on a global connection count.
    /// </summary>
    public const string ForeignServerName = "dbhealth_loopback";

    /// <summary>The foreign table defined over <see cref="ForeignServerName"/>.</summary>
    public const string ForeignTableName = "remote_orders";

    /// <summary>The number of rows analyzed into the estimated table.</summary>
    public const long AnalyzedRowCount = 500;

    private const string AdminUser = "dbhealth_admin";
    private const string AdminPassword = "synthetic-admin-password";
    private const string InspectionPassword = "synthetic-inspection-password";

    private PostgreSqlContainer? _container;

    /// <summary>
    /// The fully qualified, safely quoted control table reference. Built from internal constants
    /// only — never from anything a test caller supplies.
    /// </summary>
    public static string QualifiedControlTable => $"\"{SyntheticSchema}\".\"{ControlTable}\"";

    /// <summary>
    /// The connection string for the unprivileged inspection role. Used to construct the
    /// production connection factory.
    /// </summary>
    public string InspectionConnectionString => BuildConnectionString(InspectionRoleName, InspectionPassword);

    /// <summary>
    /// The connection string for the administrative role. Used only by the fixture itself and by
    /// out-of-band verification that persistent state is unchanged.
    /// </summary>
    public string AdminConnectionString => BuildConnectionString(AdminUser, AdminPassword);

    /// <summary>
    /// Initializes under an independent deadline. If any stage after the container starts fails,
    /// the container is released immediately and the original failure is what surfaces.
    /// </summary>
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
                await CreateSyntheticObjectsAsync(token);
            },
            ReleaseContainerAsync,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Normal disposal. A genuine disposal failure propagates rather than being hidden; it cannot
    /// dispose twice, because the reference is cleared before release.
    /// </summary>
    public ValueTask DisposeAsync() => ReleaseContainerAsync();

    /// <summary>
    /// Releases the container exactly once, whether called by failed initialization or by normal
    /// disposal, and tolerates never having created or started one.
    /// </summary>
    private async ValueTask ReleaseContainerAsync()
    {
        if (_container is { } container)
        {
            _container = null;
            await container.DisposeAsync();
        }
    }

    /// <summary>
    /// Opens an administrative connection for fixture setup and out-of-band verification. Never
    /// used to run an inspected session.
    /// </summary>
    public async Task<NpgsqlConnection> OpenAdminConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// Reads the current control marker through a separate administrative connection, so a
    /// session under test cannot influence the observation.
    /// </summary>
    public async Task<string?> ReadControlMarkerAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT marker FROM {QualifiedControlTable} WHERE id = 1", connection);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    /// <summary>
    /// Counts the rows in the control table, so an unexpected insert or delete is detected too.
    /// </summary>
    public async Task<long> ReadControlRowCountAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {QualifiedControlTable}", connection);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return (long)value!;
    }

    /// <summary>
    /// Whether the synthetic schema and control table still exist, and how many tables the schema
    /// holds, so an unexpected extra object is detected.
    /// </summary>
    public async Task<(bool SchemaExists, bool TableExists, long TableCount)> ReadSchemaShapeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);

        await using var command = new NpgsqlCommand(
            """
            SELECT
                EXISTS (SELECT 1 FROM information_schema.schemata WHERE schema_name = @schema),
                EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = @schema AND table_name = @table),
                (SELECT count(*) FROM information_schema.tables WHERE table_schema = @schema)
            """,
            connection);
        command.Parameters.AddWithValue("schema", SyntheticSchema);
        command.Parameters.AddWithValue("table", ControlTable);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetBoolean(0), reader.GetBoolean(1), reader.GetInt64(2));
    }

    private string BuildConnectionString(string username, string password, bool pooling = true)
    {
        PostgreSqlContainer container = _container
            ?? throw new InvalidOperationException("The PostgreSQL fixture has not been initialized.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5432),
            Database = DatabaseName,
            Username = username,
            Password = password,
            Pooling = pooling,
        }.ConnectionString;
    }

    /// <summary>
    /// Opens an administrative connection that is guaranteed to be a <b>new backend</b>, never one
    /// recycled from the pool.
    /// </summary>
    /// <remarks>
    /// <c>postgres_fdw</c> caches its remote connections per backend for the backend's lifetime,
    /// and a pooled <see cref="NpgsqlConnection"/> can hand back a backend on which an earlier
    /// test already opened one. Observing "before" state on such a backend would report a
    /// connection this session never made. A non-pooled connection removes that dependency
    /// entirely, which is why the foreign-connection proof uses this and not
    /// <see cref="OpenAdminConnectionAsync"/>.
    /// </remarks>
    private async Task<NpgsqlConnection> OpenUnpooledAdminConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(BuildConnectionString(AdminUser, AdminPassword, pooling: false));
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private async Task CreateSyntheticObjectsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);

        // Fixture DDL and grants. This is administrative setup that lives only in the
        // IntegrationTests assembly; none of it exists in the productive SQL inventory.
        string[] statements =
        [
            $"CREATE ROLE \"{InspectionRoleName}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{InspectionPassword}'",
            $"CREATE SCHEMA \"{SyntheticSchema}\"",
            $"CREATE TABLE {QualifiedControlTable} (id integer PRIMARY KEY, marker text NOT NULL)",
            $"INSERT INTO {QualifiedControlTable} (id, marker) VALUES (1, '{OriginalMarkerValue}')",
            $"GRANT CONNECT ON DATABASE \"{DatabaseName}\" TO \"{InspectionRoleName}\"",
            $"GRANT USAGE ON SCHEMA \"{SyntheticSchema}\" TO \"{InspectionRoleName}\"",

            // UPDATE is granted deliberately: the write-rejection test must fail with SQLSTATE
            // 25006 (read_only_sql_transaction), never with 42501 (insufficient_privilege).
            $"GRANT SELECT, UPDATE ON {QualifiedControlTable} TO \"{InspectionRoleName}\"",
        ];

        foreach (string statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await CreateRelationZooAsync(connection, cancellationToken);
    }

    /// <summary>
    /// Builds one relation of every kind D001 admits, so a single real query can be checked
    /// against the whole mapping table rather than against a hand-picked subset.
    /// </summary>
    /// <remarks>
    /// Every object here is synthetic fixture DDL. None of it exists in the productive inventory,
    /// and the product neither creates nor reads any of these rows.
    /// </remarks>
    private static async Task CreateRelationZooAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        string[] statements =
        [
            $"CREATE SCHEMA \"{TableSchema}\"",
            $"CREATE SCHEMA \"{SecondaryTableSchema}\"",

            // An ordinary table with a primary key, analyzed so its estimate is known.
            $"CREATE TABLE \"{TableSchema}\".orders_with_pk (id integer PRIMARY KEY, note text)",
            $"INSERT INTO \"{TableSchema}\".orders_with_pk SELECT g, repeat('x', 100) FROM generate_series(1, {AnalyzedRowCount}) g",
            $"ANALYZE \"{TableSchema}\".orders_with_pk",

            // An ordinary table without a primary key, deliberately never analyzed.
            $"CREATE TABLE \"{TableSchema}\".orders_without_pk (id integer, note text)",
            $"INSERT INTO \"{TableSchema}\".orders_without_pk VALUES (1, 'row')",

            // An unlogged table: relpersistence 'u' still maps to OrdinaryTable.
            $"CREATE UNLOGGED TABLE \"{TableSchema}\".scratch_unlogged (id integer PRIMARY KEY)",

            // A three-level partition tree: root, a subpartitioned partition, and two leaves.
            $"CREATE TABLE \"{TableSchema}\".events (id integer, region text, created_at date) PARTITION BY LIST (region)",
            $"CREATE TABLE \"{TableSchema}\".events_emea PARTITION OF \"{TableSchema}\".events FOR VALUES IN ('emea') PARTITION BY RANGE (created_at)",
            $"CREATE TABLE \"{TableSchema}\".events_emea_2026 PARTITION OF \"{TableSchema}\".events_emea FOR VALUES FROM ('2026-01-01') TO ('2027-01-01')",
            $"CREATE TABLE \"{TableSchema}\".events_amer PARTITION OF \"{TableSchema}\".events FOR VALUES IN ('amer')",
            $"INSERT INTO \"{TableSchema}\".events SELECT g, 'emea', DATE '2026-06-01' FROM generate_series(1, 2000) g",
            $"INSERT INTO \"{TableSchema}\".events SELECT g, 'amer', DATE '2026-06-01' FROM generate_series(1, 2000) g",

            $"CREATE VIEW \"{TableSchema}\".orders_view AS SELECT id FROM \"{TableSchema}\".orders_with_pk",
            $"CREATE MATERIALIZED VIEW \"{TableSchema}\".orders_matview AS SELECT id FROM \"{TableSchema}\".orders_with_pk",

            // A second schema, so include and exclude filters have something to choose between.
            $"CREATE TABLE \"{SecondaryTableSchema}\".secondary_table (id integer PRIMARY KEY)",

            // A real postgres_fdw foreign table over a loopback server pointed at this very
            // database. It is fully usable — the user mapping and grants below make a remote read
            // genuinely possible — precisely so that "D001 performed no remote read" is a claim
            // about D001 and not about a broken setup.
            "CREATE EXTENSION postgres_fdw",
            $"""
             CREATE SERVER dbhealth_loopback
                 FOREIGN DATA WRAPPER postgres_fdw
                 OPTIONS (host 'localhost', port '5432', dbname '{DatabaseName}', application_name '{ForeignServerApplicationName}')
             """,
            $"CREATE USER MAPPING FOR \"{InspectionRoleName}\" SERVER dbhealth_loopback OPTIONS (user '{AdminUser}', password '{AdminPassword}')",
            $"CREATE USER MAPPING FOR \"{AdminUser}\" SERVER dbhealth_loopback OPTIONS (user '{AdminUser}', password '{AdminPassword}')",
            $"""
             CREATE FOREIGN TABLE "{TableSchema}".remote_orders (id integer, note text)
                 SERVER dbhealth_loopback
                 OPTIONS (schema_name '{TableSchema}', table_name 'orders_with_pk')
             """,
            $"GRANT USAGE ON FOREIGN SERVER dbhealth_loopback TO \"{InspectionRoleName}\"",

            // A realistic least-privilege inspection role: it may enter the schemas, but D001
            // reads relation metadata from pg_catalog and never selects a business row.
            $"GRANT USAGE ON SCHEMA \"{TableSchema}\" TO \"{InspectionRoleName}\"",
            $"GRANT USAGE ON SCHEMA \"{SecondaryTableSchema}\" TO \"{InspectionRoleName}\"",
        ];

        foreach (string statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// How many <c>postgres_fdw</c> backends are currently connected. Zero means no remote
    /// connection was ever opened for the foreign table.
    /// </summary>
    public async Task<long> ReadForeignServerBackendCountAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_catalog.pg_stat_activity WHERE application_name = @marker",
            connection);
        command.Parameters.AddWithValue("marker", ForeignServerApplicationName);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Reads the foreign table through the loopback server, proving the remote path really works.
    /// The positive control for "D001 opened no remote connection".
    /// </summary>
    public async Task<long> ReadForeignTableRemotelyAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM \"{TableSchema}\".remote_orders", connection);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Reads the three PostgreSQL size functions directly for one relation, out of band.
    /// </summary>
    /// <remarks>
    /// The reference D001 is compared against. Deliberately the same per-OID calls D001 makes and
    /// nothing else: no <c>SUM</c>, no <c>pg_partition_tree</c>, no <c>pg_inherits</c> walk. A
    /// partition root's size is whatever PostgreSQL reports for that root's own OID, which may be
    /// zero on one version and non-zero on another — the contract is that DbHealthInspector does
    /// not aggregate descendants, not that the number is zero.
    /// </remarks>
    public async Task<(long TableSize, long IndexSize, long TotalSize)> ReadDirectRelationSizesAsync(
        string schema,
        string table,
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                pg_catalog.pg_table_size(relation.oid),
                pg_catalog.pg_indexes_size(relation.oid),
                pg_catalog.pg_total_relation_size(relation.oid)
            FROM pg_catalog.pg_class AS relation
            INNER JOIN pg_catalog.pg_namespace AS namespace
                ON namespace.oid = relation.relnamespace
            WHERE namespace.nspname = @schema
              AND relation.relname = @table
            """,
            connection);
        command.Parameters.AddWithValue("schema", schema);
        command.Parameters.AddWithValue("table", table);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
    }

    // --- Empirical relation-state discovery (GC-DHI-04D-C1, R1-09) -------------------------------

    /// <summary>
    /// The DDL forms probed to establish which relation states this server can actually hold.
    /// </summary>
    /// <remarks>
    /// Temporary objects deliberately carry no schema qualifier: PostgreSQL places them in the
    /// session's own <c>pg_temp</c> schema.
    /// </remarks>
    private static (string Label, string Name, string Ddl)[] RelationStateProbes(string schema) =>
    [
        ("ordinary permanent", "m_ord_perm", $"CREATE TABLE {schema}.m_ord_perm(id int)"),
        ("ordinary unlogged", "m_ord_unlog", $"CREATE UNLOGGED TABLE {schema}.m_ord_unlog(id int)"),
        ("ordinary temporary", "m_ord_temp", "CREATE TEMP TABLE m_ord_temp(id int)"),

        ("partitioned permanent", "m_part_perm", $"CREATE TABLE {schema}.m_part_perm(id int) PARTITION BY RANGE(id)"),
        ("partitioned temporary", "m_part_temp", "CREATE TEMP TABLE m_part_temp(id int) PARTITION BY RANGE(id)"),
        ("partitioned unlogged attempt", "m_part_unlog", $"CREATE UNLOGGED TABLE {schema}.m_part_unlog(id int) PARTITION BY RANGE(id)"),

        ("ordinary leaf partition", "m_leaf", $"CREATE TABLE {schema}.m_leaf PARTITION OF {schema}.m_part_perm FOR VALUES FROM (0) TO (10)"),
        ("subpartitioned partition", "m_sub", $"CREATE TABLE {schema}.m_sub PARTITION OF {schema}.m_part_perm FOR VALUES FROM (10) TO (20) PARTITION BY RANGE(id)"),
        ("unlogged leaf of permanent root", "m_leaf_unlog", $"CREATE UNLOGGED TABLE {schema}.m_leaf_unlog PARTITION OF {schema}.m_part_perm FOR VALUES FROM (20) TO (30)"),
        ("temporary leaf of temporary root", "m_leaf_temp", "CREATE TEMP TABLE m_leaf_temp PARTITION OF m_part_temp FOR VALUES FROM (0) TO (10)"),

        // R2-04: the one form GC-DHI-04D-C1's matrix never attempted — a temporary partition that
        // is itself partitioned. relkind 'p' (it is PARTITION BY of its own), relpersistence 't'
        // (it inherits temporariness from its temporary root — PostgreSQL refuses to mix temp and
        // permanent within one partition hierarchy) and relispartition true (it is itself a
        // partition of m_part_temp). Distinct bounds from the leaf above, same temporary root.
        ("temporary subpartitioned partition", "m_sub_temp",
            "CREATE TEMP TABLE m_sub_temp PARTITION OF m_part_temp FOR VALUES FROM (10) TO (20) PARTITION BY RANGE(id)"),

        ("permanent view", "m_view", $"CREATE VIEW {schema}.m_view AS SELECT 1 AS x"),
        ("temporary view", "m_view_temp", "CREATE TEMP VIEW m_view_temp AS SELECT 1 AS x"),
        ("unlogged view attempt", "m_view_unlog", $"CREATE UNLOGGED VIEW {schema}.m_view_unlog AS SELECT 1 AS x"),

        ("materialized view", "m_matview", $"CREATE MATERIALIZED VIEW {schema}.m_matview AS SELECT 1 AS x"),
        ("unlogged materialized view attempt", "m_mv_unlog", $"CREATE UNLOGGED MATERIALIZED VIEW {schema}.m_mv_unlog AS SELECT 1 AS x"),
        ("temporary materialized view attempt", "m_mv_temp", $"CREATE TEMP MATERIALIZED VIEW {schema}.m_mv_temp AS SELECT 1 AS x"),

        ("foreign table", "m_ft",
            $"""
             CREATE FOREIGN TABLE {schema}.m_ft (id integer, note text)
                 SERVER {ForeignServerName}
                 OPTIONS (schema_name '{TableSchema}', table_name 'orders_with_pk')
             """),
        ("foreign table as partition", "m_ft_part",
            $"""
             CREATE FOREIGN TABLE {schema}.m_ft_part PARTITION OF {schema}.m_part_perm FOR VALUES FROM (30) TO (40)
                 SERVER {ForeignServerName}
                 OPTIONS (schema_name '{TableSchema}', table_name 'orders_with_pk')
             """),
    ];

    /// <summary>
    /// Attempts every probe DDL and records what PostgreSQL actually stored, then discards all of
    /// it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Runs inside a single transaction that is always rolled back, with one savepoint per probe
    /// so a rejected form does not abort the ones after it. PostgreSQL's DDL is transactional, so
    /// nothing — not the probe schema, not the temporary objects — outlives the call. The normal
    /// fixture is therefore unchanged by running this.
    /// </para>
    /// <para>
    /// Unpooled, so the temporary objects cannot reach a backend a later test might reuse.
    /// </para>
    /// </remarks>
    public async Task<IReadOnlyList<RelationStateObservation>> DiscoverRelationStateMatrixAsync(
        CancellationToken cancellationToken)
    {
        const string probeSchema = "dbhealth_matrix_probe";

        await using NpgsqlConnection connection = await OpenUnpooledAdminConnectionAsync(cancellationToken);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var createSchema = new NpgsqlCommand($"CREATE SCHEMA {probeSchema}", connection, transaction))
        {
            await createSchema.ExecuteNonQueryAsync(cancellationToken);
        }

        var observations = new List<RelationStateObservation>();

        foreach ((string label, string name, string ddl) in RelationStateProbes(probeSchema))
        {
            await using (var savepoint = new NpgsqlCommand("SAVEPOINT probe", connection, transaction))
            {
                await savepoint.ExecuteNonQueryAsync(cancellationToken);
            }

            try
            {
                await using var attempt = new NpgsqlCommand(ddl, connection, transaction);
                await attempt.ExecuteNonQueryAsync(cancellationToken);

                (string kind, string persistence, bool isPartition) =
                    await ReadCatalogStateAsync(connection, transaction, probeSchema, name, cancellationToken);

                observations.Add(new RelationStateObservation(label, true, kind, persistence, isPartition, null));

                await using var release = new NpgsqlCommand("RELEASE SAVEPOINT probe", connection, transaction);
                await release.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException rejected)
            {
                observations.Add(new RelationStateObservation(
                    label, false, null, null, null, rejected.SqlState));

                await using var undo = new NpgsqlCommand("ROLLBACK TO SAVEPOINT probe", connection, transaction);
                await undo.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        // Nothing the probe created is kept.
        await transaction.RollbackAsync(cancellationToken);

        return observations;
    }

    private static async Task<(string Kind, string Persistence, bool IsPartition)> ReadCatalogStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string probeSchema,
        string relationName,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT relation.relkind::text, relation.relpersistence::text, relation.relispartition
            FROM pg_catalog.pg_class AS relation
            INNER JOIN pg_catalog.pg_namespace AS namespace
                ON namespace.oid = relation.relnamespace
            WHERE relation.relname = @name
              AND (namespace.nspname = @schema OR namespace.nspname LIKE 'pg_temp%')
            """,
            connection,
            transaction);
        command.Parameters.AddWithValue("name", relationName);
        command.Parameters.AddWithValue("schema", probeSchema);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        Assert.True(await reader.ReadAsync(cancellationToken));

        return (reader.GetString(0), reader.GetString(1), reader.GetBoolean(2));
    }

    // --- Same-session postgres_fdw observation (GC-DHI-04D-C1, R1-17 / R1-18) --------------------

    /// <summary>
    /// Reads <c>postgres_fdw_get_connections()</c> on <paramref name="connection"/>.
    /// </summary>
    /// <remarks>
    /// The function reports the remote connections cached by the <b>current local backend</b> and
    /// nothing else — a second session always sees an empty set, which is exactly why sampling
    /// <c>pg_stat_activity</c> from a separate connection could never prove that no transient
    /// connection existed. Every observation must therefore run on the same connection that ran
    /// the statement under test.
    /// </remarks>
    private static async Task<IReadOnlyList<ForeignServerConnection>> ReadForeignConnectionsAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            """
            SELECT server_name, user_name, valid, used_in_xact, closed, remote_backend_pid
            FROM postgres_fdw_get_connections()
            """,
            connection);

        var rows = new List<ForeignServerConnection>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new ForeignServerConnection(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetBoolean(2),
                reader.GetBoolean(3),
                reader.IsDBNull(4) ? null : reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5)));
        }

        return rows;
    }

    /// <summary>
    /// Runs the frozen D001 text on <paramref name="connection"/> and returns how many rows it
    /// produced.
    /// </summary>
    /// <remarks>
    /// Test-only, and deliberately the inventory's own constant rather than a copy, so the
    /// statement observed here is byte-identical to the one production runs. Nothing in
    /// <c>src/</c> knows this path exists.
    /// </remarks>
    private static async Task<long> ExecuteFrozenD001Async(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PostgreSqlSqlInventory.ReadTableSnapshotsSql, connection);
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            Value = new[] { TableSchema },
        });
        command.Parameters.Add(new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            Value = Array.Empty<string>(),
        });

        var rows = 0L;
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows++;
        }

        return rows;
    }

    /// <summary>
    /// Runs the whole negative proof and its positive control inside <b>one</b> local session:
    /// observe, run D001, observe again, then genuinely read the foreign table and observe once
    /// more.
    /// </summary>
    /// <remarks>
    /// Because all five steps share a backend, the second observation rules out a connection that
    /// was opened and closed again — the gap a before/after <c>pg_stat_activity</c> sample cannot
    /// close. The final step is the positive control: it proves the detector reports a connection
    /// when one really is opened, so the two zeros before it mean something.
    /// </remarks>
    public async Task<ForeignConnectionProof> ProveD001OpensNoForeignConnectionAsync(CancellationToken cancellationToken)
    {
        // Unpooled on purpose: a recycled backend can still hold a postgres_fdw connection cached
        // by an earlier test, which would make the "before" observation report something this
        // session never did. The proof must not depend on pool behaviour.
        await using NpgsqlConnection connection = await OpenUnpooledAdminConnectionAsync(cancellationToken);

        IReadOnlyList<ForeignServerConnection> beforeD001 =
            await ReadForeignConnectionsAsync(connection, cancellationToken);

        long relationsRead = await ExecuteFrozenD001Async(connection, cancellationToken);

        IReadOnlyList<ForeignServerConnection> afterD001 =
            await ReadForeignConnectionsAsync(connection, cancellationToken);

        await using var remote = new NpgsqlCommand(
            $"SELECT count(*) FROM \"{TableSchema}\".\"{ForeignTableName}\"", connection);
        var remoteRows = (long)(await remote.ExecuteScalarAsync(cancellationToken))!;

        IReadOnlyList<ForeignServerConnection> afterRemoteRead =
            await ReadForeignConnectionsAsync(connection, cancellationToken);

        return new ForeignConnectionProof(
            beforeD001, afterD001, afterRemoteRead, relationsRead, remoteRows);
    }
}

/// <summary>
/// What one probed DDL form produced: whether the server accepted it and, when it did, the
/// catalog state it recorded.
/// </summary>
public sealed record RelationStateObservation(
    string Label,
    bool Created,
    string? RelationKind,
    string? Persistence,
    bool? IsPartition,
    string? SqlState);

/// <summary>
/// One row of <c>postgres_fdw_get_connections()</c> as PostgreSQL 18 reports it.
/// </summary>
public sealed record ForeignServerConnection(
    string ServerName,
    string UserName,
    bool Valid,
    bool UsedInTransaction,
    bool? Closed,
    int? RemoteBackendPid);

/// <summary>
/// The three same-session observations of the foreign-connection proof, with what each stage ran.
/// </summary>
public sealed record ForeignConnectionProof(
    IReadOnlyList<ForeignServerConnection> BeforeD001,
    IReadOnlyList<ForeignServerConnection> AfterD001,
    IReadOnlyList<ForeignServerConnection> AfterRemoteRead,
    long RelationsRead,
    long RemoteRows);

/// <summary>
/// The normal server-backed suite: one collection, one fixture, no parallelism — so lock and
/// timeout tests can never run concurrently against the same container.
/// </summary>
/// <remarks>
/// It registers <see cref="PostgreSqlServerFixture"/> and nothing else. The permission-loss suite
/// has its own collection and its own container, so running either suite in isolation starts only
/// the container that suite actually needs (GC-DHI-04C-C1, R1-07).
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlServerSuite : ICollectionFixture<PostgreSqlServerFixture>
{
    public const string Name = "PostgreSqlServer";
}
