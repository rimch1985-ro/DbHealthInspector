using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Sessions;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The central safety contract of GC-DHI-04B: after the exact production initialization sequence,
/// a persistent write by a role that <b>does</b> hold <c>UPDATE</c> must still fail with SQLSTATE
/// <c>25006</c> (<c>read_only_sql_transaction</c>) — proving read-only enforcement rather than a
/// missing privilege — and must leave the persistent state untouched.
/// </summary>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class WriteRejectionTests
{
    private readonly PostgreSqlServerFixture _fixture;

    public WriteRejectionTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task PersistentUpdate_FailsWithReadOnlyTransactionSqlState()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        string markerBefore = (await _fixture.ReadControlMarkerAsync(cancellationToken))!;
        long rowsBefore = await _fixture.ReadControlRowCountAsync(cancellationToken);
        (bool schemaBefore, bool tableBefore, long tableCountBefore) = await _fixture.ReadSchemaShapeAsync(cancellationToken);

        await using (TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString, PostgreSqlInspectionSessionOptions.Default, cancellationToken))
        {
            Assert.True(session.State.IsVerified);

            // Test-only SQL, built here and never present in the production inventory. The table
            // name comes from fixture constants, not from any caller-supplied value.
            await using NpgsqlCommand command = session.CreateTestOnlyCommand(
                $"UPDATE {PostgreSqlServerFixture.QualifiedControlTable} SET marker = 'changed' WHERE id = 1");

            PostgresException exception = await Assert.ThrowsAsync<PostgresException>(
                () => command.ExecuteNonQueryAsync(cancellationToken));

            // Inspecting SQLSTATE is legitimate here: this error never crosses the production
            // boundary, it is raised by a command this test owns.
            Assert.Equal("25006", exception.SqlState);
        }

        Assert.Equal(PostgreSqlServerFixture.OriginalMarkerValue, markerBefore);
        Assert.Equal(PostgreSqlServerFixture.OriginalMarkerValue, await _fixture.ReadControlMarkerAsync(cancellationToken));
        Assert.Equal(rowsBefore, await _fixture.ReadControlRowCountAsync(cancellationToken));
        Assert.Equal(1, rowsBefore);

        (bool schemaAfter, bool tableAfter, long tableCountAfter) = await _fixture.ReadSchemaShapeAsync(cancellationToken);
        Assert.True(schemaBefore && schemaAfter);
        Assert.True(tableBefore && tableAfter);
        Assert.Equal(tableCountBefore, tableCountAfter);
    }

    [Fact]
    public async Task InspectionRole_IsNotASuperuserButDoesHoldUpdate()
    {
        // Both halves matter: without UPDATE the write-rejection test would prove nothing, and a
        // superuser could bypass the very enforcement under test.
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using NpgsqlConnection connection = await _fixture.OpenAdminConnectionAsync(cancellationToken);

        await using var roleCommand = new NpgsqlCommand(
            "SELECT rolsuper, rolcreatedb, rolcreaterole, rolreplication, rolcanlogin FROM pg_roles WHERE rolname = @role",
            connection);
        roleCommand.Parameters.AddWithValue("role", PostgreSqlServerFixture.InspectionRoleName);

        await using (NpgsqlDataReader reader = await roleCommand.ExecuteReaderAsync(cancellationToken))
        {
            Assert.True(await reader.ReadAsync(cancellationToken));
            Assert.False(reader.GetBoolean(0), "The inspection role must not be a superuser.");
            Assert.False(reader.GetBoolean(1));
            Assert.False(reader.GetBoolean(2));
            Assert.False(reader.GetBoolean(3));
            Assert.True(reader.GetBoolean(4));
        }

        await using var grantCommand = new NpgsqlCommand(
            $"SELECT has_table_privilege(@role, '{PostgreSqlServerFixture.QualifiedControlTable}', 'UPDATE'), "
            + $"has_table_privilege(@role, '{PostgreSqlServerFixture.QualifiedControlTable}', 'SELECT')",
            connection);
        grantCommand.Parameters.AddWithValue("role", PostgreSqlServerFixture.InspectionRoleName);

        await using NpgsqlDataReader grants = await grantCommand.ExecuteReaderAsync(cancellationToken);
        Assert.True(await grants.ReadAsync(cancellationToken));
        Assert.True(grants.GetBoolean(0), "The inspection role must hold UPDATE so 25006 proves read-only enforcement.");
        Assert.True(grants.GetBoolean(1));
    }

    [Fact]
    public async Task VerifiedState_ReportsReadOnlyRepeatableReadAndMatchingTimeouts()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString, PostgreSqlInspectionSessionOptions.Default, cancellationToken);

        Assert.True(session.State.IsReadOnly);
        Assert.Equal("repeatable read", session.State.IsolationLevel);
        Assert.True(session.State.StatementTimeoutMatches);
        Assert.True(session.State.LockTimeoutMatches);
        Assert.True(session.State.IdleInTransactionTimeoutMatches);
        Assert.True(session.State.IsVerified);
    }
}
