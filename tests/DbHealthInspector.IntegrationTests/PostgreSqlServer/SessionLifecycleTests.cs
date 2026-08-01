using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using Npgsql;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// Success, failure and cancellation through the real
/// <see cref="PostgreSqlInspectionSessionRunner"/> against PostgreSQL 18: every outcome must
/// release its resources, leave the pool usable and leave persistent state untouched.
/// </summary>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class SessionLifecycleTests
{
    private readonly PostgreSqlServerFixture _fixture;

    public SessionLifecycleTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private async Task AssertPersistentStateUnchangedAsync(CancellationToken cancellationToken)
    {
        Assert.Equal(PostgreSqlServerFixture.OriginalMarkerValue, await _fixture.ReadControlMarkerAsync(cancellationToken));
        Assert.Equal(1, await _fixture.ReadControlRowCountAsync(cancellationToken));

        (bool schemaExists, bool tableExists, long tableCount) = await _fixture.ReadSchemaShapeAsync(cancellationToken);
        Assert.True(schemaExists);
        Assert.True(tableExists);
        Assert.Equal(1, tableCount);
    }

    [Fact]
    public async Task Success_RunsTheOperationAndLeavesEverythingClean()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        // GC-DHI-04B inventories no functional statement, so the authorized operation returns a
        // synthetic result rather than querying anything.
        int result = await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default,
            (executor, _) =>
            {
                Assert.NotNull(executor);
                return ValueTask.FromResult(1234);
            },
            cancellationToken);

        Assert.Equal(1234, result);
        await AssertPersistentStateUnchangedAsync(cancellationToken);

        // The pool must remain usable straight afterwards.
        int second = await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default, (_, _) => ValueTask.FromResult(7), cancellationToken);
        Assert.Equal(7, second);
    }

    [Fact]
    public async Task Failure_PropagatesTheSyntheticErrorAndStillCleansUp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        var original = new InvalidOperationException("synthetic post-verification failure");

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync<int>(
                PostgreSqlInspectionSessionOptions.Default,
                (_, _) => throw original,
                cancellationToken).AsTask());

        // An unexpected exception is preserved exactly, never sanitized.
        Assert.Same(original, thrown);
        await AssertPersistentStateUnchangedAsync(cancellationToken);

        int afterwards = await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default, (_, _) => ValueTask.FromResult(9), cancellationToken);
        Assert.Equal(9, afterwards);
    }

    [Fact]
    public async Task Cancellation_PropagatesAndStillCleansUp()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync<int>(
                PostgreSqlInspectionSessionOptions.Default,
                (_, token) =>
                {
                    cts.Cancel();
                    token.ThrowIfCancellationRequested();
                    return ValueTask.FromResult(1);
                },
                cts.Token).AsTask());

        await AssertPersistentStateUnchangedAsync(cancellationToken);

        int afterwards = await runner.RunAsync(
            PostgreSqlInspectionSessionOptions.Default, (_, _) => ValueTask.FromResult(11), cancellationToken);
        Assert.Equal(11, afterwards);
    }

    [Fact]
    public async Task PreCanceledToken_NeverOpensAConnection()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => runner.RunAsync(PostgreSqlInspectionSessionOptions.Default, (_, _) => ValueTask.FromResult(1), cts.Token).AsTask());

        await AssertPersistentStateUnchangedAsync(cancellationToken);
    }

    [Fact]
    public async Task Session_LeavesNoOpenTransactionBehind()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(_fixture.InspectionConnectionString);
        var runner = new PostgreSqlInspectionSessionRunner(factory, PostgreSqlSqlInventory.Default);

        await runner.RunAsync(PostgreSqlInspectionSessionOptions.Default, (_, _) => ValueTask.FromResult(1), cancellationToken);

        // Observed through a separate administrative connection: no backend belonging to the
        // inspection role may still be inside a transaction.
        await using NpgsqlConnection admin = await _fixture.OpenAdminConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE usename = @role AND state IN ('idle in transaction', 'idle in transaction (aborted)')",
            admin);
        command.Parameters.AddWithValue("role", PostgreSqlServerFixture.InspectionRoleName);

        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(cancellationToken))!);
    }
}
