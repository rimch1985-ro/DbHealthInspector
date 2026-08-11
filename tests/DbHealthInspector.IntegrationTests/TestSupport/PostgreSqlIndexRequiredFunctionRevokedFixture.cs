using DbHealthInspector.PostgreSql.Sessions;
using Npgsql;
using Testcontainers.PostgreSql;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// A <b>dedicated, disposable</b> PostgreSQL 18 container in which the inspection role has lost
/// <c>EXECUTE</c> on exactly one of the four functions GC-DHI-04E's C002 expansion added, so the
/// required catalog capability genuinely fails and the index-snapshot operation is never offered
/// (GC-DHI-04E §23, "C002 required function").
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately a separate fixture and a separate container from
/// <see cref="PostgreSqlRequiredFunctionRevokedFixture"/> (GC-DHI-04D), which revokes
/// <c>pg_total_relation_size(regclass)</c> — a function the C002 baseline already required before
/// GC-DHI-04E existed. That fixture proves the pre-existing required-function path still works; it
/// does not prove that any of the four <b>new</b> functions GC-DHI-04E added actually controls the
/// capability. This fixture closes that gap by revoking
/// <c>pg_get_indexdef(oid, integer, boolean)</c> — the specific three-argument overload D1 (E001)
/// calls — and nothing else.
/// </para>
/// <para>
/// Revoking <c>EXECUTE</c> on a built-in function is a database-wide ACL change, so this fixture
/// never reuses another fixture's container: the mutation stays here and dies with it, and its
/// database, role and container names are all distinct from every other permission fixture so no
/// scenario can contaminate another.
/// </para>
/// <para>
/// Revoking from the role alone would prove nothing: <c>PUBLIC</c> holds <c>EXECUTE</c> on
/// <c>pg_get_indexdef</c> by default, so the fixture revokes from <c>PUBLIC</c> as well, grants the
/// role no membership, keeps it <c>NOSUPERUSER</c>, and verifies the resulting effective privileges
/// — including that every other GC-DHI-04D/04E required function survived — before any probe runs.
/// </para>
/// </remarks>
public sealed class PostgreSqlIndexRequiredFunctionRevokedFixture : IAsyncLifetime
{
    public const string DatabaseName = "dbhealth_index_function_revoked_test";
    public const string InspectionRoleName = "dbhealth_index_function_revoked_role";

    /// <summary>
    /// The exact overload E001 calls, and the only privilege this fixture removes.
    /// </summary>
    public const string RevokedFunction = "pg_catalog.pg_get_indexdef(oid, integer, boolean)";

    private const string AdminUser = "dbhealth_index_function_admin";
    private const string AdminPassword = "synthetic-index-function-admin-password";
    private const string InspectionPassword = "synthetic-index-function-inspection-password";

    private PostgreSqlContainer? _container;

    /// <summary>
    /// The connection string for the inspection role whose function privilege was revoked.
    /// </summary>
    public string InspectionConnectionString => BuildConnectionString(InspectionRoleName, InspectionPassword);

    /// <summary>
    /// The role's effective privileges captured <b>before</b> the revocation ran, so a test can
    /// assert the starting state positively rather than inferring it from the fixture having
    /// initialized (GC-DHI-04E-C1, R1-05).
    /// </summary>
    /// <remarks>
    /// Without this, "the selected function became unavailable" would rest on the assumption that
    /// it was available to begin with. A role that never had the privilege — or a container whose
    /// defaults differed — would produce exactly the same post-revocation readings.
    /// </remarks>
    public IndexFunctionPrivileges PrivilegesBeforeRevocation { get; private set; } =
        new(false, false, false, false, false, false, false);

    /// <summary>
    /// The productive C002 result observed <b>before</b> the revocation, through the real
    /// statement rather than a test-local reconstruction of it.
    /// </summary>
    public bool CatalogMetadataAvailableBeforeRevocation { get; private set; }

    /// <summary>Whether the role was a superuser before the revocation.</summary>
    public bool IsSuperuserBeforeRevocation { get; private set; }

    /// <summary>The role's memberships before the revocation.</summary>
    public IReadOnlyList<string> MembershipsBeforeRevocation { get; private set; } = [];

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

                // Three ordered stages, so the "before" state is a measurement rather than an
                // assumption: create the role, observe what it can actually do, and only then
                // remove exactly one privilege.
                await CreateInspectionRoleAsync(token);
                await CapturePreRevocationStateAsync(token);
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
    /// The role's effective <c>EXECUTE</c> privilege on the revoked overload plus every other
    /// function GC-DHI-04D/04E's C002 requires, as PostgreSQL itself computes it — direct grants,
    /// <c>PUBLIC</c> and memberships all included. Exactly one of these seven must read
    /// <see langword="false"/>.
    /// </summary>
    public async Task<IndexFunctionPrivileges> ReadEffectiveFunctionPrivilegesAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_table_size(regclass)', 'EXECUTE'),
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_indexes_size(regclass)', 'EXECUTE'),
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_total_relation_size(regclass)', 'EXECUTE'),
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_relation_size(regclass)', 'EXECUTE'),
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_get_indexdef(oid,integer,boolean)', 'EXECUTE'),
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_get_expr(pg_node_tree,oid,boolean)', 'EXECUTE'),
                pg_catalog.has_function_privilege(@role, 'pg_catalog.pg_index_column_has_property(regclass,integer,text)', 'EXECUTE')
            """,
            connection);
        command.Parameters.AddWithValue("role", InspectionRoleName);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new IndexFunctionPrivileges(
            TableSize: reader.GetBoolean(0),
            IndexesSize: reader.GetBoolean(1),
            TotalRelationSize: reader.GetBoolean(2),
            RelationSize: reader.GetBoolean(3),
            GetIndexDef: reader.GetBoolean(4),
            GetExpr: reader.GetBoolean(5),
            IndexColumnHasProperty: reader.GetBoolean(6));
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
            ?? throw new InvalidOperationException("The index required-function fixture has not been initialized.");

        return new NpgsqlConnectionStringBuilder
        {
            Host = container.Hostname,
            Port = container.GetMappedPublicPort(5432),
            Database = DatabaseName,
            Username = username,
            Password = password,
        }.ConnectionString;
    }

    private async Task CreateInspectionRoleAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);

        // Administrative fixture SQL. None of this exists in the productive inventory.
        string[] statements =
        [
            $"CREATE ROLE \"{InspectionRoleName}\" LOGIN NOSUPERUSER NOCREATEDB NOCREATEROLE NOREPLICATION PASSWORD '{InspectionPassword}'",
            $"GRANT CONNECT ON DATABASE \"{DatabaseName}\" TO \"{InspectionRoleName}\"",
        ];

        foreach (string statement in statements)
        {
            await using var command = new NpgsqlCommand(statement, connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Records what the freshly created role can do, before anything is taken away.
    /// </summary>
    /// <remarks>
    /// C002 is observed through the <b>productive</b> statement over a real verified session, not
    /// through a privilege query reassembled here: a test-local reconstruction could agree with
    /// itself while disagreeing with the statement the product actually runs.
    /// </remarks>
    private async Task CapturePreRevocationStateAsync(CancellationToken cancellationToken)
    {
        PrivilegesBeforeRevocation = await ReadEffectiveFunctionPrivilegesAsync(cancellationToken);
        IsSuperuserBeforeRevocation = await ReadIsSuperuserAsync(cancellationToken);
        MembershipsBeforeRevocation = await ReadRoleMembershipsAsync(cancellationToken);

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken);

        CatalogMetadataAvailableBeforeRevocation =
            await session.Operations.CheckCatalogMetadataAccessAsync(cancellationToken);

        // A fixture that starts from an already-broken state would make the whole comparison
        // meaningless, so the starting point is enforced here rather than merely reported.
        if (!CatalogMetadataAvailableBeforeRevocation)
        {
            throw new InvalidOperationException("The required catalog capability was already unavailable.");
        }

        IndexFunctionPrivileges before = PrivilegesBeforeRevocation;
        if (!before.TableSize
            || !before.IndexesSize
            || !before.TotalRelationSize
            || !before.RelationSize
            || !before.GetIndexDef
            || !before.GetExpr
            || !before.IndexColumnHasProperty)
        {
            throw new InvalidOperationException("A required function privilege was missing before the revocation.");
        }
    }

    private async Task RevokeFunctionExecuteAsync(CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = await OpenAdminConnectionAsync(cancellationToken);

        // PUBLIC holds EXECUTE on this function by default, so revoking only the role's direct
        // grant would leave an effective path open and the test would prove nothing. Naming the
        // exact three-argument overload keeps pg_get_indexdef(oid) — which this adapter never
        // calls — untouched.
        string[] statements =
        [
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
        IndexFunctionPrivileges privileges = await ReadEffectiveFunctionPrivilegesAsync(cancellationToken);

        if (privileges.GetIndexDef)
        {
            throw new InvalidOperationException("The function revocation did not take effect.");
        }

        if (!privileges.TableSize
            || !privileges.IndexesSize
            || !privileges.TotalRelationSize
            || !privileges.RelationSize
            || !privileges.GetExpr
            || !privileges.IndexColumnHasProperty)
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
/// The role's effective <c>EXECUTE</c> privilege on every function GC-DHI-04D and GC-DHI-04E's
/// C002 requires.
/// </summary>
public sealed record IndexFunctionPrivileges(
    bool TableSize,
    bool IndexesSize,
    bool TotalRelationSize,
    bool RelationSize,
    bool GetIndexDef,
    bool GetExpr,
    bool IndexColumnHasProperty);

/// <summary>
/// The index required-function permission suite: its own collection, its own single fixture, no
/// parallelism.
/// </summary>
/// <remarks>
/// Kept separate from every other collection — including GC-DHI-04D's
/// <see cref="PostgreSqlRequiredFunctionRevokedSuite"/> and GC-DHI-04C's
/// <see cref="PostgreSqlStatisticsRevokedSuite"/> — because it mutates a built-in function's ACL,
/// and so that running any suite in isolation never starts another suite's container.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PostgreSqlIndexRequiredFunctionRevokedSuite : ICollectionFixture<PostgreSqlIndexRequiredFunctionRevokedFixture>
{
    public const string Name = "PostgreSqlIndexRequiredFunctionRevoked";
}
