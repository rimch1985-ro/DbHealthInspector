using System.Reflection;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sessions.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Sessions;

/// <summary>
/// The authorized operation must not be able to re-run or undo the session initialization
/// (GC-DHI-04B-C1, F-02).
/// </summary>
public sealed class PostgreSqlInspectionOperationExecutorTests
{
    private static PostgreSqlInspectionOperationExecutor View() =>
        new(new PostgreSqlSqlExecutor(new PostgreSqlSqlInventory(), ScriptedStatementGateway.HealthySession()));

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.SetTransactionReadOnly))]
    [InlineData(nameof(PostgreSqlSqlStatementId.ApplyLocalTimeouts))]
    [InlineData(nameof(PostgreSqlSqlStatementId.VerifySessionState))]
    public async Task ExecuteAsync_RejectsEverySessionInitializationStatement(string idName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);

        await Assert.ThrowsAsync<PostgreSqlSqlSafetyException>(
            () => View().ExecuteAsync(id, [], TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.SetTransactionReadOnly))]
    [InlineData(nameof(PostgreSqlSqlStatementId.ApplyLocalTimeouts))]
    [InlineData(nameof(PostgreSqlSqlStatementId.VerifySessionState))]
    public void IsSessionInitializationStatement_IsTrueForAllThree(string idName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);

        Assert.True(PostgreSqlInspectionOperationExecutor.IsSessionInitializationStatement(id));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsAnUnknownId()
    {
        await Assert.ThrowsAsync<PostgreSqlSqlSafetyException>(
            () => View().ExecuteAsync((PostgreSqlSqlStatementId)999, [], TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_RejectionCarriesTheFixedMessageAndNoDetail()
    {
        PostgreSqlSqlSafetyException exception = await Assert.ThrowsAsync<PostgreSqlSqlSafetyException>(
            () => View().ExecuteAsync(PostgreSqlSqlStatementId.ApplyLocalTimeouts, [], TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("The PostgreSQL statement failed SQL safety validation.", exception.Message);
        Assert.DoesNotContain("ApplyLocalTimeouts", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("SET TRANSACTION", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNullParameters()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => View().ExecuteAsync(PostgreSqlSqlStatementId.VerifySessionState, null!, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ExecuteAsync_HonoursAPreCanceledToken()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => View().ExecuteAsync(PostgreSqlSqlStatementId.VerifySessionState, [], cts.Token).AsTask());
    }

    // --- Surface constraints --------------------------------------------------------------------

    [Fact]
    public void View_ExposesNoExecutorConnectionOrTransaction()
    {
        MemberInfo[] members =
        [
            .. typeof(PostgreSqlInspectionOperationExecutor).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance),
            .. typeof(PostgreSqlInspectionOperationExecutor).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => method.DeclaringType == typeof(PostgreSqlInspectionOperationExecutor)),
        ];

        Type[] forbidden = [typeof(PostgreSqlSqlExecutor), typeof(NpgsqlConnection), typeof(NpgsqlTransaction), typeof(NpgsqlCommand)];

        foreach (MemberInfo member in members)
        {
            Type? exposed = member switch
            {
                PropertyInfo property => property.PropertyType,
                MethodInfo method => method.ReturnType,
                _ => null,
            };

            Assert.DoesNotContain(forbidden, type => type == exposed);
        }
    }

    [Fact]
    public void View_AcceptsNoRawSqlString()
    {
        MethodInfo[] methods = typeof(PostgreSqlInspectionOperationExecutor)
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(PostgreSqlInspectionOperationExecutor))
            .ToArray();

        Assert.DoesNotContain(methods, method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)));
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

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.SetTransactionReadOnly))]
    [InlineData(nameof(PostgreSqlSqlStatementId.ApplyLocalTimeouts))]
    [InlineData(nameof(PostgreSqlSqlStatementId.VerifySessionState))]
    public async Task Runner_CallbackCannotReExecuteAnInitializationStatement(string idName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        var scope = new FakeInspectionSessionScope();

        await new PostgreSqlInspectionSessionRunner(scope).RunAsync(
            PostgreSqlInspectionSessionOptions.Default,
            async (view, token) =>
            {
                await Assert.ThrowsAsync<PostgreSqlSqlSafetyException>(() => view.ExecuteAsync(id, [], token).AsTask());
                return 1;
            },
            TestContext.Current.CancellationToken);

        // Only the runner's own three statements ran; the callback added none.
        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
            ],
            scope.Gateway.ExecutedIds);
    }
}
