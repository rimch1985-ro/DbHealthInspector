using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sql.TestSupport;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// The frozen shape contracts for C001–C004 (GC-DHI-04C §14): exact row count, exact column
/// count, exact nullability, and reader cleanup on every failure.
/// </summary>
public sealed class PostgreSqlSqlExecutorCapabilityShapeTests
{
    private static PostgreSqlSqlExecutor Executor(FakeStatementGateway gateway) => new(new PostgreSqlSqlInventory(), gateway);

    private static readonly DateTimeOffset UtcReset = new(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);

    // --- C001 ------------------------------------------------------------------------------------

    [Fact]
    public async Task C001_ProjectsAllThreeColumns()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(
            FakeRowReader.WithRows(3, [180004, "db", "user"]));

        PostgreSqlServerIdentity identity = await Executor(gateway).ReadServerIdentityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(180004, identity.ServerVersionNumber);
        Assert.Equal("db", identity.DatabaseName);
        Assert.Equal("user", identity.CurrentUser);
    }

    [Fact]
    public async Task C001_RejectsZeroRows()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.Empty(3));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadServerIdentityAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task C001_RejectsASecondRow()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(
            FakeRowReader.WithRows(3, [180004, "db", "user"], [180004, "db", "user"]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadServerIdentityAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public async Task C001_RejectsWrongColumnCount(int fieldCount)
    {
        object?[] row = Enumerable.Range(0, fieldCount).Select(index => (object?)(index == 0 ? 180004 : "value")).ToArray();
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(fieldCount, row));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadServerIdentityAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task C001_RejectsNullInAnyColumn(int nullOrdinal)
    {
        object?[] row = [180004, "db", "user"];
        row[nullOrdinal] = null;
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(3, row));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadServerIdentityAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task C001_DisposesTheReaderOnShapeFailure()
    {
        var reader = FakeRowReader.Empty(3);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadServerIdentityAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    // --- C002 and C003 -------------------------------------------------------------------------------

    public static TheoryData<string> BooleanChecks() =>
    [
        nameof(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess),
        nameof(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess),
    ];

    private static ValueTask<bool> RunBooleanCheckAsync(PostgreSqlSqlExecutor executor, string idName, CancellationToken cancellationToken) =>
        Enum.Parse<PostgreSqlSqlStatementId>(idName) == PostgreSqlSqlStatementId.CheckCatalogMetadataAccess
            ? executor.CheckCatalogMetadataAccessAsync(cancellationToken)
            : executor.CheckUsageStatisticsAccessAsync(cancellationToken);

    [Theory]
    [MemberData(nameof(BooleanChecks))]
    public async Task BooleanChecks_ReturnTrue(string idName)
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [true]));

        Assert.True(await RunBooleanCheckAsync(Executor(gateway), idName, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(BooleanChecks))]
    public async Task BooleanChecks_ReturnFalse(string idName)
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [false]));

        Assert.False(await RunBooleanCheckAsync(Executor(gateway), idName, TestContext.Current.CancellationToken));
    }

    [Theory]
    [MemberData(nameof(BooleanChecks))]
    public async Task BooleanChecks_RejectNull(string idName)
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [(object?)null]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunBooleanCheckAsync(Executor(gateway), idName, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [MemberData(nameof(BooleanChecks))]
    public async Task BooleanChecks_RejectZeroRows(string idName)
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.Empty(1));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunBooleanCheckAsync(Executor(gateway), idName, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [MemberData(nameof(BooleanChecks))]
    public async Task BooleanChecks_RejectASecondRow(string idName)
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [true], [true]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunBooleanCheckAsync(Executor(gateway), idName, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [MemberData(nameof(BooleanChecks))]
    public async Task BooleanChecks_RejectWrongColumnCount(string idName)
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(2, [true, true]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunBooleanCheckAsync(Executor(gateway), idName, TestContext.Current.CancellationToken).AsTask());
    }

    // --- C004 ------------------------------------------------------------------------------------------

    [Fact]
    public async Task C004_ReturnsAUtcTimestamp()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [UtcReset]));

        DateTimeOffset? value = await Executor(gateway).ReadStatisticsResetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(UtcReset, value);
        Assert.Equal(TimeSpan.Zero, value!.Value.Offset);
    }

    [Fact]
    public async Task C004_AcceptsNullAsAValidAnswer()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [(object?)null]));

        Assert.Null(await Executor(gateway).ReadStatisticsResetAsync(TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-5)]
    public async Task C004_RejectsANonZeroOffsetInsteadOfNormalizingIt(int offsetHours)
    {
        var shifted = new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.FromHours(offsetHours));
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [shifted]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadStatisticsResetAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task C004_RejectsZeroRows()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.Empty(1));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadStatisticsResetAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task C004_RejectsASecondRow()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [UtcReset], [UtcReset]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadStatisticsResetAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task C004_RejectsWrongColumnCount()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(2, [UtcReset, UtcReset]));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadStatisticsResetAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task C004_DisposesTheReaderOnANonZeroOffsetFailure()
    {
        var shifted = new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.FromHours(3));
        var reader = FakeRowReader.WithRows(1, [shifted]);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => Executor(gateway).ReadStatisticsResetAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    // --- Token forwarding and resolution ------------------------------------------------------------------

    [Fact]
    public async Task EveryCapabilityStatement_ForwardsTheExactTokenAndTakesNoParameters()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        FakeStatementGateway identityGateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(3, [180004, "db", "user"]));
        await Executor(identityGateway).ReadServerIdentityAsync(cts.Token);
        Assert.Equal(cts.Token, Assert.Single(identityGateway.Tokens));
        Assert.Empty(Assert.Single(identityGateway.Executed).Parameters);

        FakeStatementGateway catalogGateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [true]));
        await Executor(catalogGateway).CheckCatalogMetadataAccessAsync(cts.Token);
        Assert.Equal(cts.Token, Assert.Single(catalogGateway.Tokens));
        Assert.Empty(Assert.Single(catalogGateway.Executed).Parameters);

        FakeStatementGateway statisticsGateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [true]));
        await Executor(statisticsGateway).CheckUsageStatisticsAccessAsync(cts.Token);
        Assert.Equal(cts.Token, Assert.Single(statisticsGateway.Tokens));
        Assert.Empty(Assert.Single(statisticsGateway.Executed).Parameters);

        FakeStatementGateway resetGateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(1, [UtcReset]));
        await Executor(resetGateway).ReadStatisticsResetAsync(cts.Token);
        Assert.Equal(cts.Token, Assert.Single(resetGateway.Tokens));
        Assert.Empty(Assert.Single(resetGateway.Executed).Parameters);
    }
}
