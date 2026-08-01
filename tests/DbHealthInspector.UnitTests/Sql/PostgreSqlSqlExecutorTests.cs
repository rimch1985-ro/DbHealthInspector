using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sql.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// The executor resolves only inventoried IDs, binds parameters positionally and by declared
/// type, forwards the caller's exact token, enforces result shape and disposes its reader —
/// proven through a deterministic gateway double, with no server involved.
/// </summary>
public sealed class PostgreSqlSqlExecutorTests
{
    private static PostgreSqlSqlInventory Inventory() => new();

    private static PostgreSqlSqlExecutor Executor(FakeStatementGateway gateway) => new(Inventory(), gateway);

    // --- Resolution and command text ------------------------------------------------------------

    [Fact]
    public async Task B001_UsesTheInventoryCommandTextAndNoParameters()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding();

        await Executor(gateway).ExecuteSetTransactionReadOnlyAsync(TestContext.Current.CancellationToken);

        PostgreSqlPreparedStatement statement = Assert.Single(gateway.Executed);
        Assert.Equal(PostgreSqlSqlStatementId.SetTransactionReadOnly, statement.Id);
        Assert.Equal(PostgreSqlSqlInventory.SetTransactionReadOnlySql, statement.CommandText);
        Assert.Empty(statement.Parameters);
    }

    [Fact]
    public async Task B001_RunsAsANonQueryAndNeverOpensAReader()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding();

        await Executor(gateway).ExecuteSetTransactionReadOnlyAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, gateway.NonQueryCallCount);
        Assert.Equal(0, gateway.ReaderCallCount);
    }

    [Fact]
    public async Task B002_UsesTheInventoryCommandTextAndBindsParametersInOrder()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.ConfigurationRow());

        await Executor(gateway).ApplyLocalTimeoutsAsync(30_000, 5_000, 60_000, TestContext.Current.CancellationToken);

        PostgreSqlPreparedStatement statement = Assert.Single(gateway.Executed);
        Assert.Equal(PostgreSqlSqlInventory.ApplyLocalTimeoutsSql, statement.CommandText);
        Assert.Equal(
            [
                PostgreSqlSqlParameterValue.Int32(1, 30_000),
                PostgreSqlSqlParameterValue.Int32(2, 5_000),
                PostgreSqlSqlParameterValue.Int32(3, 60_000),
            ],
            statement.Parameters.ToArray());
    }

    [Fact]
    public async Task B003_UsesTheInventoryCommandTextAndBindsTheSameThreeParameters()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.VerificationRow());

        await Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken);

        PostgreSqlPreparedStatement statement = Assert.Single(gateway.Executed);
        Assert.Equal(PostgreSqlSqlInventory.VerifySessionStateSql, statement.CommandText);
        Assert.Equal([1, 2, 3], statement.Parameters.Select(parameter => parameter.Position).ToArray());
        Assert.All(statement.Parameters, parameter => Assert.Equal(PostgreSqlSqlParameterType.Int32, parameter.Type));
    }

    [Fact]
    public async Task ForwardsTheExactCancellationToken()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.VerificationRow());

        await Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, cts.Token);

        Assert.Equal(cts.Token, Assert.Single(gateway.Tokens));
        Assert.All(gateway.LastReader!.ReadTokens, token => Assert.Equal(cts.Token, token));
    }

    // --- Result shape -----------------------------------------------------------------------------

    [Fact]
    public async Task B002_RequiresOneRow()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.Empty());

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ApplyLocalTimeoutsAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task B002_RequiresThreeColumns()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(2, ["a", "b"]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ApplyLocalTimeoutsAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task B002_RejectsAnUnexpectedNullInAnyColumn(int nullOrdinal)
    {
        // GC-DHI-04B-C1, F-06: set_config never returns NULL for a successful assignment, so a
        // NULL means the server did not do what the statement asked.
        object?[] row = ["30000ms", "5000ms", "60000ms"];
        row[nullOrdinal] = null;
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(3, row));

        PostgreSqlSqlResultShapeException exception = await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ApplyLocalTimeoutsAsync(30_000, 5_000, 60_000, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("The PostgreSQL statement returned an unexpected result shape.", exception.Message);
        Assert.DoesNotContain(nullOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture), exception.Message, StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    [Fact]
    public async Task B002_DisposesTheReaderWhenANullColumnIsRejected()
    {
        var reader = FakeRowReader.WithRows(3, [null, "5000ms", "60000ms"]);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ApplyLocalTimeoutsAsync(30_000, 5_000, 60_000, TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task B002_RejectsASecondRow()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(
            FakeRowReader.WithRows(3, ["a", "b", "c"], ["d", "e", "f"]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ApplyLocalTimeoutsAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task B003_RequiresOneRow()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.Empty(5));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task B003_RequiresFiveColumns()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(4, [true, "repeatable read", true, true]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task B003_RejectsASecondRow()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(
            5,
            [true, "repeatable read", true, true, true],
            [true, "repeatable read", true, true, true]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task B003_RejectsAnUnexpectedNullInAnyColumn(int nullOrdinal)
    {
        object?[] row = [true, "repeatable read", true, true, true];
        row[nullOrdinal] = null;
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(5, row));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task B003_MapsAllFiveVerificationValues()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(
            FakeRowReader.VerificationRow(true, "repeatable read", true, false, true));

        PostgreSqlInspectionSessionState state = await Executor(gateway)
            .VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken);

        Assert.True(state.IsReadOnly);
        Assert.Equal("repeatable read", state.IsolationLevel);
        Assert.True(state.StatementTimeoutMatches);
        Assert.False(state.LockTimeoutMatches);
        Assert.True(state.IdleInTransactionTimeoutMatches);
        Assert.False(state.IsVerified);
    }

    // --- Reader disposal ---------------------------------------------------------------------------

    [Fact]
    public async Task DisposesTheReaderOnSuccess()
    {
        var reader = FakeRowReader.VerificationRow();
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken);

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task DisposesTheReaderWhenTheResultShapeIsRejected()
    {
        var reader = FakeRowReader.Empty(5);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).VerifySessionStateAsync(1_000, 500, 2_000, TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    // --- Parameter binding rules --------------------------------------------------------------------

    [Fact]
    public void Prepare_RejectsTooFewParameters()
    {
        Assert.Throws<PostgreSqlSqlParameterBindingException>(
            () => PostgreSqlSqlExecutor.Prepare(
                Inventory(),
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                [PostgreSqlSqlParameterValue.Int32(1, 1)]));
    }

    [Fact]
    public void Prepare_RejectsTooManyParameters()
    {
        Assert.Throws<PostgreSqlSqlParameterBindingException>(
            () => PostgreSqlSqlExecutor.Prepare(
                Inventory(),
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                [PostgreSqlSqlParameterValue.Int32(1, 1)]));
    }

    [Fact]
    public void Prepare_RejectsOutOfOrderPositions()
    {
        Assert.Throws<PostgreSqlSqlParameterBindingException>(
            () => PostgreSqlSqlExecutor.Prepare(
                Inventory(),
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                [
                    PostgreSqlSqlParameterValue.Int32(2, 1),
                    PostgreSqlSqlParameterValue.Int32(1, 2),
                    PostgreSqlSqlParameterValue.Int32(3, 3),
                ]));
    }

    [Fact]
    public void Prepare_RejectsAnUnknownStatementId()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PostgreSqlSqlExecutor.Prepare(Inventory(), (PostgreSqlSqlStatementId)999, []));
    }

    [Fact]
    public void ParameterBindingException_CarriesAFixedMessageAndNoDetail()
    {
        PostgreSqlSqlParameterBindingException exception = Assert.Throws<PostgreSqlSqlParameterBindingException>(
            () => PostgreSqlSqlExecutor.Prepare(
                Inventory(),
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                [PostgreSqlSqlParameterValue.Int32(1, 987654321)]));

        Assert.Equal("The PostgreSQL statement parameters did not match the statement definition.", exception.Message);
        Assert.DoesNotContain("987654321", exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    // --- Surface constraints -------------------------------------------------------------------------

    [Fact]
    public void Executor_ExposesNoRawSqlEntryPoint()
    {
        // No method may accept a SQL string, and none may expose a connection, transaction or
        // command — that is what keeps the authorized callback unable to run anything but
        // inventoried statements.
        System.Reflection.MethodInfo[] methods = typeof(PostgreSqlSqlExecutor)
            .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)
            .Where(method => method.DeclaringType == typeof(PostgreSqlSqlExecutor))
            .ToArray();

        Assert.DoesNotContain(methods, method => method.GetParameters().Any(parameter => parameter.ParameterType == typeof(string)));
        Assert.DoesNotContain(methods, method => method.ReturnType == typeof(NpgsqlCommand));
        Assert.DoesNotContain(methods, method => method.ReturnType == typeof(NpgsqlConnection));
        Assert.DoesNotContain(methods, method => method.ReturnType == typeof(NpgsqlTransaction));
    }

    [Fact]
    public void Executor_ExposesNoConnectionOrTransactionProperty()
    {
        System.Reflection.PropertyInfo[] properties = typeof(PostgreSqlSqlExecutor)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.Empty(properties);
    }

    [Fact]
    public void Executor_ImplementsNoDisposalBecauseItOwnsNoResource()
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(PostgreSqlSqlExecutor)));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(typeof(PostgreSqlSqlExecutor)));
    }
}
