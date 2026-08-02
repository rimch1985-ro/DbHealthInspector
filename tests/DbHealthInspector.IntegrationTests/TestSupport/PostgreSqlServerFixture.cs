using Npgsql;
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

    private string BuildConnectionString(string username, string password)
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
        }.ConnectionString;
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
    }
}

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
