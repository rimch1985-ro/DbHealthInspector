using Npgsql;
using Testcontainers.PostgreSql;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// A <b>dedicated, disposable</b> PostgreSQL 18 container in which the inspection role has lost
/// <c>EXECUTE</c> on exactly one of the three relation-size functions D001 needs, so the required
/// catalog capability genuinely fails and D001 is never offered (GC-DHI-04D §23).
/// </summary>
/// <remarks>
/// <para>
/// Revoking <c>EXECUTE</c> on a built-in function is a database-wide ACL change, so this fixture
/// never reuses another fixture's container: the mutation stays here and dies with it.
/// </para>
/// <para>
/// Only <c>pg_total_relation_size(regclass)</c> is revoked. The other two size functions and the
/// whole catalog-table allowlist stay intact, so C002 must fail for exactly the reason under test
/// and for no other.
/// </para>
/// <para>
/// Revoking from the role alone would prove nothing: <c>PUBLIC</c> holds <c>EXECUTE</c> on these
/// functions by default, so the fixture revokes from <c>PUBLIC</c> as well, grants the role no
/// membership, keeps it <c>NOSUPERUSER</c>, and verifies the resulting effective privileges before
/// any probe runs.
/// </para>
/// </remarks>
public sealed class PostgreSqlRequiredFunctionRevokedFixture : IAsyncLifetime
{
    public const string DatabaseName = "dbhealth_function_revoked_test";
    public const string InspectionRoleName = "dbhealth_function_revoked_role";

    /// <summary>The one required function whose <c>EXECUTE</c> privilege is removed.</summary>
    public const string RevokedFunction = "pg_catalog.pg_total_relation_size(regclass)";

    private const string AdminUser = "dbhealth_function_admin";
    private const string AdminPassword = "synthetic-function-admin-password";
    private const string InspectionPassword = "synthetic-function-inspection-password";

    private PostgreSqlContainer? _container;

    /// <summary>
    /// The connection string for the inspection role whose function privilege was revoked.
    /// </summary>
    public string InspectionConnectionString => BuildConnectionString(InspectionRoleName, InspectionPassword);

    /// <summary>
    /// Starts the container, applies the revocation and verifies it took effect, all under one
    /// independent deadline. A failure at any stage releases the container immediately and
    /// surfaces the original failure.
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
                await RevokeFunctionExecuteAsync(token);
                await VerifyRevocationAsync(token);
            },
            ReleaseContainerAsync,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Normal disposal. The container is always destroyed, so the mutated function ACL never
    /// outlives this suite; a genuine disposal failure propagates rather than being hidden.
    /// </summary>
    public ValueTask DisposeAsync() => ReleaseContainerAsync();

    private async ValueTask ReleaseContainerAsync()
    {
        if (_container is { } container)
        {
            _container = null;
            await container.DisposeAsync();
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
    /// The role's effective <c>EXECUTE</c> privilege on each of the three required size functions,
    /// as PostgreSQL itself computes it — direct grants, <c>PUBLIC</c> and memberships included.
    /// </summary>
    public async Task<(bool TableSize, bool IndexesSize, bool TotalRelationSize)> ReadEffectiveFunctionPrivilegesAsync(
        CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_table_size(regclass)', 'EXECUTE'),
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_indexes_size(regclass)', 'EXECUTE'),
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_total_relation_size(regclass)', 'EXECUTE')
            """,
            connection);
        command.Parameters.AddWithValue("role", InspectionRoleName);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return (reader.GetBoolean(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    /// <summary>
    /// The role's effective privilege over the catalog-table part of the C002 allowlist, which the
    /// revocation must leave untouched.
    /// </summary>
    public async Task<bool> ReadEffectiveCatalogTablePrivilegeAsync(CancellationToken cancellationToken)
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
    /// Every role the inspection role is a member of. Expected to be empty: no inherited role may
    /// quietly restore the <c>EXECUTE</c> privilege this fixture removed.
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
            ?? throw new InvalidOperationException("The revoked-function fixture has not been initialized.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5432),
            Database = DatabaseName,
            Username = username,
            Password = password,
        }.ConnectionString;
    }

    private async Task RevokeFunctionExecuteAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);

        // Administrative fixture SQL. None of this exists in the productive inventory.
        string[] statements =
        [
            $"CREATE ROLE \"{InspectionRoleName}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{InspectionPassword}'",
            $"GRANT CONNECT ON DATABASE \"{DatabaseName}\" TO \"{InspectionRoleName}\"",

            // PUBLIC holds EXECUTE on these functions by default, so revoking only the role's
            // direct grant would leave an effective path open and the test would prove nothing.
            $"REVOKE EXECUTE ON FUNCTION {RevokedFunction} FROM PUBLIC",
            $"REVOKE EXECUTE ON FUNCTION {RevokedFunction} FROM \"{InspectionRoleName}\"",
        ];

        foreach (string statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Refuses to hand over a container in which the revocation did not remove exactly one
    /// privilege and nothing else.
    /// </summary>
    /// <remarks>
    /// The messages are deliberately neutral: no connection string, password, host, port,
    /// container detail, SQL, database name, role name or function name.
    /// </remarks>
    private async Task VerifyRevocationAsync(CancellationToken cancellationToken)
    {
        (bool tableSize, bool indexesSize, bool totalRelationSize) =
            await ReadEffectiveFunctionPrivilegesAsync(cancellationToken);

        if (totalRelationSize)
        {
            throw new InvalidOperationException("The function revocation did not take effect.");
        }

        if (!tableSize || !indexesSize)
        {
            throw new InvalidOperationException("The function revocation removed more than it should have.");
        }

        if (await ReadIsSuperuserAsync(cancellationToken))
        {
            throw new InvalidOperationException("The inspection role unexpectedly holds a privilege bypass.");
        }

        if ((await ReadRoleMembershipsAsync(cancellationToken)).Count != 0)
        {
            throw new InvalidOperationException("The inspection role unexpectedly holds a role membership.");
        }

        if (!await ReadEffectiveCatalogTablePrivilegeAsync(cancellationToken))
        {
            throw new InvalidOperationException("The required catalog access did not survive the revocation.");
        }
    }
}

/// <summary>
/// The required-function permission suite: its own collection, its own single fixture, no
/// parallelism.
/// </summary>
/// <remarks>
/// Kept separate from every other collection because it mutates a built-in function's ACL, and so
/// that running any suite in isolation never starts another suite's container.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlRequiredFunctionRevokedSuite : ICollectionFixture<PostgreSqlRequiredFunctionRevokedFixture>
{
    public const string Name = "PostgreSqlRequiredFunctionRevoked";
}
