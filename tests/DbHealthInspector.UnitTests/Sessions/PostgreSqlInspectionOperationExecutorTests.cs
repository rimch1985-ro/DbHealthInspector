using System.Reflection;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sessions.TestSupport;
using DbHealthInspector.UnitTests.Sql.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Sessions;

/// <summary>
/// The authorized operation sees only typed C001–C004 methods (GC-DHI-04C §13). There is no
/// generic ID dispatch, no SQL string, and no way to name — let alone execute — B001–B003.
/// </summary>
public sealed class PostgreSqlInspectionOperationExecutorTests
{
    private static PostgreSqlInspectionOperationExecutor View(FakeStatementGateway gateway) =>
        new(new PostgreSqlSqlExecutor(new PostgreSqlSqlInventory(), gateway));

    // --- Typed operations resolve their fixed statements ------------------------------------------

    [Fact]
    public async Task ReadServerIdentityAsync_ResolvesC001()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(
            FakeRowReader.WithRows(3, [180004, "synthetic_db", "synthetic_role"]));

        PostgreSqlServerIdentity identity = await View(gateway).ReadServerIdentityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PostgreSqlSqlStatementId.ReadServerIdentity, Assert.Single(gateway.Executed).Id);
        Assert.Equal(180004, identity.ServerVersionNumber);
        Assert.Equal("synthetic_db", identity.DatabaseName);
        Assert.Equal("synthetic_role", identity.CurrentUser);
    }

    [Fact]
    public async Task CheckCatalogMetadataAccessAsync_ResolvesC002()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [true]));

        bool available = await View(gateway).CheckCatalogMetadataAccessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess, Assert.Single(gateway.Executed).Id);
        Assert.True(available);
    }

    [Fact]
    public async Task CheckUsageStatisticsAccessAsync_ResolvesC003()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [false]));

        bool available = await View(gateway).CheckUsageStatisticsAccessAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess, Assert.Single(gateway.Executed).Id);
        Assert.False(available);
    }

    [Fact]
    public async Task ReadStatisticsResetAsync_ResolvesC004()
    {
        var reset = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [reset]));

        DateTimeOffset? value = await View(gateway).ReadStatisticsResetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(PostgreSqlSqlStatementId.ReadStatisticsReset, Assert.Single(gateway.Executed).Id);
        Assert.Equal(reset, value);
    }

    [Fact]
    public async Task TypedOperationsForwardTheExactToken()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [true]));

        await View(gateway).CheckCatalogMetadataAccessAsync(cts.Token);

        Assert.Equal(cts.Token, Assert.Single(gateway.Tokens));
    }

    // --- Surface constraints -----------------------------------------------------------------------

    private static MethodInfo[] DeclaredMethods() => typeof(PostgreSqlInspectionOperationExecutor)
        .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .Where(method => method.DeclaringType == typeof(PostgreSqlInspectionOperationExecutor))
        .ToArray();

    [Fact]
    public void View_ExposesExactlyTheFourTypedOperations()
    {
        string[] names = DeclaredMethods().Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();

        Assert.Equal(
            [
                nameof(PostgreSqlInspectionOperationExecutor.CheckCatalogMetadataAccessAsync),
                nameof(PostgreSqlInspectionOperationExecutor.CheckUsageStatisticsAccessAsync),
                nameof(PostgreSqlInspectionOperationExecutor.ReadServerIdentityAsync),
                nameof(PostgreSqlInspectionOperationExecutor.ReadStatisticsResetAsync),
            ],
            names);
    }

    [Fact]
    public void View_HasNoGenericStatementIdDispatch()
    {
        // The 04B-era ExecuteAsync(statementId, parameters, token) is gone: B001–B003 cannot even
        // be named through this boundary any more.
        Assert.DoesNotContain(
            DeclaredMethods(),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(PostgreSqlSqlStatementId)));
    }

    [Fact]
    public void View_AcceptsNoRawSqlString()
    {
        Assert.DoesNotContain(
            DeclaredMethods(),
            method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)));
    }

    [Fact]
    public void View_AcceptsNoArbitraryParameterCollection()
    {
        Assert.DoesNotContain(
            DeclaredMethods(),
            method => method.GetParameters().Any(parameter =>
                parameter.ParameterType == typeof(IReadOnlyList<PostgreSqlSqlParameterValue>)));
    }

    [Fact]
    public void View_ExposesNoExecutorConnectionTransactionOrCommand()
    {
        Type[] forbidden =
        [
            typeof(PostgreSqlSqlExecutor), typeof(NpgsqlConnection), typeof(NpgsqlTransaction), typeof(NpgsqlCommand),
            typeof(IPostgreSqlStatementGateway), typeof(IPostgreSqlRowReader), typeof(IPostgreSqlCommandHandle),
        ];

        Assert.DoesNotContain(DeclaredMethods(), method => forbidden.Contains(method.ReturnType));

        PropertyInfo[] properties = typeof(PostgreSqlInspectionOperationExecutor)
            .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.Empty(properties);
    }

    [Fact]
    public async Task Runner_HandsTheCallbackTheRestrictedViewAndNotTheFullExecutor()
    {
        var scope = new FakeInspectionSessionScope();
        object? received = null;

        await new PostgreSqlInspectionSessionRunner(scope).RunAsync(
            PostgreSqlInspectionSessionOptions.Default,
            (view, _) =>
            {
                received = view;
                return ValueTask.FromResult(1);
            },
            TestContext.Current.CancellationToken);

        Assert.IsType<PostgreSqlInspectionOperationExecutor>(received);
        Assert.IsNotType<PostgreSqlSqlExecutor>(received);
    }

    [Fact]
    public async Task Runner_CallbackAddsNoStatementsBeyondB001ToB003()
    {
        var scope = new FakeInspectionSessionScope();

        await new PostgreSqlInspectionSessionRunner(scope).RunAsync(
            PostgreSqlInspectionSessionOptions.Default,
            (_, _) => ValueTask.FromResult(1),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
            ],
            scope.Gateway.ExecutedIds);
    }
}
