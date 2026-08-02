using Npgsql;
using Testcontainers.PostgreSql;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// A <b>dedicated, disposable</b> PostgreSQL 18 container in which the inspection role has lost
/// every effective path to the usage-statistics views, so the optional-statistics degradation can
/// be proven against a real server rather than a fake.
/// </summary>
/// <remarks>
/// <para>
/// This fixture revokes privileges on <c>pg_catalog</c> views, which is a database-wide change.
/// It therefore never reuses <see cref="PostgreSqlServerFixture"/>'s container: the mutation stays
/// inside this container and dies with it.
/// </para>
/// <para>
/// Revoking the role's direct grant alone would prove nothing, because <c>PUBLIC</c> holds
/// <c>SELECT</c> on these views by default and the role would still have an effective path. The
/// fixture therefore revokes from <c>PUBLIC</c> as well, grants the role no statistics-related
/// membership (notably not <c>pg_monitor</c> or <c>pg_read_all_stats</c>), and asserts the
/// resulting effective privilege is genuinely false before any probe runs.
/// </para>
/// </remarks>
public sealed class PostgreSqlStatisticsRevokedFixture : IAsyncLifetime
{
    public const string DatabaseName = "dbhealth_revoked_test";
    public const string InspectionRoleName = "dbhealth_revoked_role";

    private const string AdminUser = "dbhealth_revoked_admin";
    private const string AdminPassword = "synthetic-revoked-admin-password";
    private const string InspectionPassword = "synthetic-revoked-inspection-password";

    private PostgreSqlContainer? _container;

    /// <summary>
    /// The connection string for the unprivileged inspection role whose statistics access was
    /// revoked.
    /// </summary>
    public string InspectionConnectionString => BuildConnectionString(InspectionRoleName, InspectionPassword);

    /// <summary>
    /// Starts the container, applies the revocation and <b>verifies it took effect</b> — all under
    /// one independent deadline. A failure at any stage releases the container immediately and
    /// surfaces the original failure, so this fixture can never hand a suite a container in which
    /// the revocation silently did not apply.
    /// </summary>
    public ValueTask InitializeAsync() =>
        TestFixtureLifecycle.InitializeGuardedAsync(
            async token =>
            {
                _container = new PostgreSqlBuilder(PostgreSqlServerFixture.ImageReference)
                    .WithDatabase(DatabaseName)
                    .WithUsername(AdminUser)
                    .WithPassword(AdminPassword)
                    .Build();

                await _container.StartAsync(token);
                await RevokeStatisticsAccessAsync(token);
                await VerifyRevocationAsync(token);
            },
            ReleaseContainerAsync,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Normal disposal. The container is always destroyed, so the mutated ACLs never outlive this
    /// suite; a genuine disposal failure propagates rather than being hidden.
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
    /// Refuses to hand over a container in which the revocation did not actually remove the
    /// effective privilege, the role gained a bypass, or the required catalog access was damaged.
    /// </summary>
    /// <remarks>
    /// The messages are deliberately neutral: no connection string, password, host, port,
    /// container detail, SQL, database name or role name.
    /// </remarks>
    private async Task VerifyRevocationAsync(CancellationToken cancellationToken)
    {
        (bool statDatabase, bool statAllIndexes) = await ReadEffectiveStatisticsPrivilegesAsync(cancellationToken);
        if (statDatabase || statAllIndexes)
        {
            throw new InvalidOperationException("The statistics revocation did not take effect.");
        }

        if (await ReadIsSuperuserAsync(cancellationToken))
        {
            throw new InvalidOperationException("The inspection role unexpectedly holds a privilege bypass.");
        }

        if ((await ReadRoleMembershipsAsync(cancellationToken)).Count != 0)
        {
            throw new InvalidOperationException("The inspection role unexpectedly holds a role membership.");
        }

        if (!await ReadEffectiveCatalogPrivilegeAsync(cancellationToken))
        {
            throw new InvalidOperationException("The required catalog access did not survive the revocation.");
        }
    }

    /// <summary>
    /// Opens an administrative connection for fixture setup and out-of-band verification.
    /// </summary>
    public async Task<NpgsqlConnection> OpenAdminConnectionAsync(CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(BuildConnectionString(AdminUser, AdminPassword));
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>
    /// The role's effective <c>SELECT</c> privilege on the two statistics views, as PostgreSQL
    /// itself computes it — direct grants, <c>PUBLIC</c> and memberships all included.
    /// </summary>
    public async Task<(bool StatDatabase, bool StatAllIndexes)> ReadEffectiveStatisticsPrivilegesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_stat_database', 'SELECT'),
                pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_stat_all_indexes', 'SELECT')
            """,
            connection);
        command.Parameters.AddWithValue("role", InspectionRoleName);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetBoolean(0), reader.GetBoolean(1));
    }

    /// <summary>
    /// The role's effective privilege over the required catalog allowlist, which the revocation
    /// must leave untouched.
    /// </summary>
    public async Task<bool> ReadEffectiveCatalogPrivilegeAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                pg_catalog.has_schema_privilege(@role, 'pg_catalog', 'USAGE')
                AND pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_namespace', 'SELECT')
                AND pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_class', 'SELECT')
                AND pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_inherits', 'SELECT')
                AND pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_index', 'SELECT')
                AND pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_attribute', 'SELECT')
                AND pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_am', 'SELECT')
                AND pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_constraint', 'SELECT')
                AND pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_collation', 'SELECT')
                AND pg_catalog.has_table_privilege(@role, 'pg_catalog.pg_opclass', 'SELECT')
            """,
            connection);
        command.Parameters.AddWithValue("role", InspectionRoleName);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>
    /// Every role the inspection role is a member of. Expected to be empty: no statistics
    /// membership may quietly restore the access this fixture removed.
    /// </summary>
    public async Task<IReadOnlyList<string>> ReadRoleMembershipsAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT granted.rolname
            FROM pg_catalog.pg_auth_members AS membership
            JOIN pg_catalog.pg_roles AS granted ON granted.oid = membership.roleid
            JOIN pg_catalog.pg_roles AS member ON member.oid = membership.member
            WHERE member.rolname = @role
            """,
            connection);
        command.Parameters.AddWithValue("role", InspectionRoleName);

        var memberships = new List<string>();
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            memberships.Add(reader.GetString(0));
        }

        return memberships;
    }

    /// <summary>
    /// Whether the inspection role is a superuser. Must be false: a superuser bypasses every
    /// privilege check and would make the revocation meaningless.
    /// </summary>
    public async Task<bool> ReadIsSuperuserAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT rolsuper FROM pg_catalog.pg_roles WHERE rolname = @role", connection);
        command.Parameters.AddWithValue("role", InspectionRoleName);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private string BuildConnectionString(string username, string password)
    {
        PostgreSqlContainer container = _container
            ?? throw new InvalidOperationException("The revoked-statistics fixture has not been initialized.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5432),
            Database = DatabaseName,
            Username = username,
            Password = password,
        }.ConnectionString;
    }

    private async Task RevokeStatisticsAccessAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);

        // Administrative fixture SQL. None of this exists in the productive inventory.
        string[] statements =
        [
            $"CREATE ROLE \"{InspectionRoleName}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{InspectionPassword}'",
            $"GRANT CONNECT ON DATABASE \"{DatabaseName}\" TO \"{InspectionRoleName}\"",

            // PUBLIC holds SELECT on these views by default, so revoking only the role's direct
            // grant would leave an effective path open and the test would prove nothing.
            "REVOKE SELECT ON pg_catalog.pg_stat_database FROM PUBLIC",
            "REVOKE SELECT ON pg_catalog.pg_stat_all_indexes FROM PUBLIC",
            $"REVOKE SELECT ON pg_catalog.pg_stat_database FROM \"{InspectionRoleName}\"",
            $"REVOKE SELECT ON pg_catalog.pg_stat_all_indexes FROM \"{InspectionRoleName}\"",
        ];

        foreach (string statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}

/// <summary>
/// The permission-loss suite: its own collection, its own single fixture, no parallelism.
/// </summary>
/// <remarks>
/// Kept separate from <see cref="PostgreSqlServerSuite"/> so the ACL mutation lives in a container
/// that only this suite owns, and so running either suite in isolation never starts the other's
/// container (GC-DHI-04C-C1, R1-07). Both collections disable parallelization, so the two suites
/// still run sequentially when the whole category runs.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlStatisticsRevokedSuite : ICollectionFixture<PostgreSqlStatisticsRevokedFixture>
{
    public const string Name = "PostgreSqlStatisticsRevoked";
}
