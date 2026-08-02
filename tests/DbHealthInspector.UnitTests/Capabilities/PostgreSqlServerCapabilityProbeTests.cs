using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Capabilities.TestSupport;
using DbHealthInspector.UnitTests.Sql.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Capabilities;

/// <summary>
/// The probe's sequencing and capability composition (GC-DHI-04C §7–§10, §15).
/// </summary>
public sealed class PostgreSqlServerCapabilityProbeTests
{
    private static ValueTask<PostgreSqlServerProbeResult> ProbeAsync(ProbeScript script, CancellationToken cancellationToken) =>
        PostgreSqlServerCapabilityProbe.ProbeAsync(script.View(), cancellationToken);

    private static CapabilityState StateOf(PostgreSqlServerProbeResult result, CapabilityKind kind) =>
        result.Capabilities.GetState(kind);

    // --- Supported, everything available ------------------------------------------------------------

    [Fact]
    public async Task Supported_CatalogTrue_StatisticsTrue_WithTimestamp()
    {
        var reset = new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.Zero);
        ProbeScript script = ProbeScript.Healthy(statisticsReset: reset);

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        Assert.Equal(PostgreSqlVersionSupportStatus.Supported, result.VersionSupport);
        Assert.Equal(180004, result.ServerVersionNumber);
        Assert.Equal(18, result.MajorVersion);
        Assert.Equal("18.4", result.Metadata.EngineVersion);
        Assert.Equal(DatabaseEngine.PostgreSql, result.Metadata.Engine);
        Assert.Equal("synthetic_db", result.Metadata.DatabaseName);
        Assert.Equal("synthetic_role", result.Metadata.CurrentUser);
        Assert.Equal(reset, result.Statistics.StatisticsResetAtUtc);

        Assert.Equal(CapabilityStatus.Available, StateOf(result, CapabilityKind.CatalogMetadata).Status);
        Assert.Null(StateOf(result, CapabilityKind.CatalogMetadata).Reason);
        Assert.Equal(CapabilityStatus.Available, StateOf(result, CapabilityKind.UsageStatistics).Status);
        Assert.Null(StateOf(result, CapabilityKind.UsageStatistics).Reason);
        Assert.Equal(CapabilityStatus.Disabled, StateOf(result, CapabilityKind.DataProfiling).Status);
        Assert.Equal("Data profiling is disabled by product policy.", StateOf(result, CapabilityKind.DataProfiling).Reason);
    }

    [Fact]
    public async Task Supported_CatalogTrue_StatisticsTrue_WithNullTimestamp()
    {
        // A null stats_reset is a valid answer and must not make the capability unavailable.
        ProbeScript script = ProbeScript.Healthy().WithStatisticsReset(null);

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        Assert.Null(result.Statistics.StatisticsResetAtUtc);
        Assert.Equal(CapabilityStatus.Available, StateOf(result, CapabilityKind.UsageStatistics).Status);
        Assert.Null(StateOf(result, CapabilityKind.UsageStatistics).Reason);
    }

    [Fact]
    public async Task Supported_ExecutesC001ThroughC004InOrder()
    {
        ProbeScript script = ProbeScript.Healthy();

        await ProbeAsync(script, TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
                PostgreSqlSqlStatementId.CheckUsageStatisticsAccess,
                PostgreSqlSqlStatementId.ReadStatisticsReset,
            ],
            script.ExecutedIds);
    }

    // --- Supported, statistics unavailable ------------------------------------------------------------

    [Fact]
    public async Task Supported_StatisticsFalse_SkipsC004AndReportsUnavailable()
    {
        ProbeScript script = ProbeScript.Healthy(statisticsAvailable: false);

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        Assert.Equal(0, script.CountOf(PostgreSqlSqlStatementId.ReadStatisticsReset));
        Assert.Equal(CapabilityStatus.Available, StateOf(result, CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(CapabilityStatus.Unavailable, StateOf(result, CapabilityKind.UsageStatistics).Status);
        Assert.Equal("Usage statistics are unavailable for this inspection.", StateOf(result, CapabilityKind.UsageStatistics).Reason);
        Assert.Null(result.Statistics.StatisticsResetAtUtc);
        Assert.Equal(CapabilityStatus.Disabled, StateOf(result, CapabilityKind.DataProfiling).Status);
    }

    // --- Unsupported versions -------------------------------------------------------------------------

    [Theory]
    [InlineData(90624, "9.6.24", 9)]
    [InlineData(140000, "14.0", 14)]
    [InlineData(190000, "19.0", 19)]
    [InlineData(200003, "20.3", 20)]
    public async Task Unsupported_SkipsEveryCapabilityStatementAndDoesNotThrow(int versionNumber, string normalized, int major)
    {
        ProbeScript script = ProbeScript.Healthy(serverVersionNumber: versionNumber);

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        Assert.Equal(PostgreSqlVersionSupportStatus.Unsupported, result.VersionSupport);
        Assert.Equal(normalized, result.Metadata.EngineVersion);
        Assert.Equal(major, result.MajorVersion);

        // Only C001 ran.
        Assert.Equal([PostgreSqlSqlStatementId.ReadServerIdentity], script.ExecutedIds);

        const string expectedReason = "The PostgreSQL server version is outside the supported range.";
        Assert.Equal(CapabilityStatus.Unavailable, StateOf(result, CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(expectedReason, StateOf(result, CapabilityKind.CatalogMetadata).Reason);
        Assert.Equal(CapabilityStatus.Unavailable, StateOf(result, CapabilityKind.UsageStatistics).Status);
        Assert.Equal(expectedReason, StateOf(result, CapabilityKind.UsageStatistics).Reason);
        Assert.Equal(CapabilityStatus.Disabled, StateOf(result, CapabilityKind.DataProfiling).Status);
        Assert.Null(result.Statistics.StatisticsResetAtUtc);
    }

    [Fact]
    public async Task Unsupported_ReasonNeverNamesTheActualVersion()
    {
        ProbeScript script = ProbeScript.Healthy(serverVersionNumber: 90624);

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        foreach (CapabilityState state in result.Capabilities.States)
        {
            string reason = state.Reason ?? string.Empty;
            bool leaked = reason.Contains("9.6", StringComparison.Ordinal)
                || reason.Contains("90624", StringComparison.Ordinal)
                || reason.Contains("synthetic_db", StringComparison.Ordinal)
                || reason.Contains("synthetic_role", StringComparison.Ordinal);

            Assert.False(leaked, "A capability reason exposed server details.");
        }
    }

    // --- Required catalog unavailable ------------------------------------------------------------------

    [Fact]
    public async Task CatalogFalse_ThrowsFixedExceptionAndSkipsC003AndC004()
    {
        ProbeScript script = ProbeScript.Healthy(catalogAvailable: false);

        PostgreSqlRequiredCatalogCapabilityException exception =
            await Assert.ThrowsAsync<PostgreSqlRequiredCatalogCapabilityException>(
                () => ProbeAsync(script, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("Required PostgreSQL catalog metadata is unavailable.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        Assert.Equal(
            [PostgreSqlSqlStatementId.ReadServerIdentity, PostgreSqlSqlStatementId.CheckCatalogMetadataAccess],
            script.ExecutedIds);
    }

    [Fact]
    public void RequiredCatalogException_HasOnlyAParameterlessConstructor()
    {
        // Sanitization by construction: no overload can attach a message, inner exception or data.
        System.Reflection.ConstructorInfo[] constructors = typeof(PostgreSqlRequiredCatalogCapabilityException)
            .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        System.Reflection.ConstructorInfo constructor = Assert.Single(constructors);
        Assert.Empty(constructor.GetParameters());
    }

    // --- C004 42501 race --------------------------------------------------------------------------------

    private static PostgresException InsufficientPrivilege() =>
        new("permission denied for view pg_stat_database", "ERROR", "ERROR", "42501");

    [Fact]
    public async Task C004InsufficientPrivilege_DegradesStatisticsOnly()
    {
        ProbeScript script = ProbeScript.Healthy()
            .FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, InsufficientPrivilege());

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityStatus.Available, StateOf(result, CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(CapabilityStatus.Unavailable, StateOf(result, CapabilityKind.UsageStatistics).Status);
        Assert.Equal("Usage statistics are unavailable for this inspection.", StateOf(result, CapabilityKind.UsageStatistics).Reason);
        Assert.Null(result.Statistics.StatisticsResetAtUtc);
        Assert.Equal(CapabilityStatus.Disabled, StateOf(result, CapabilityKind.DataProfiling).Status);
        Assert.Equal(1, script.CountOf(PostgreSqlSqlStatementId.ReadStatisticsReset));
    }

    [Theory]
    [InlineData("42P01")]
    [InlineData("57014")]
    [InlineData("25006")]
    [InlineData("58000")]
    public async Task C004NonInsufficientPrivilege_PropagatesInsteadOfDegrading(string sqlState)
    {
        var failure = new PostgresException("synthetic", "ERROR", "ERROR", sqlState);
        ProbeScript script = ProbeScript.Healthy().FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, failure);

        Exception? thrown = await Record.ExceptionAsync(() => ProbeAsync(script, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, thrown);
    }

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.ReadServerIdentity))]
    [InlineData(nameof(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess))]
    [InlineData(nameof(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess))]
    public async Task InsufficientPrivilegeAtAnyOtherStage_NeverDegrades(string idName)
    {
        // Only C004 may degrade. The same SQLSTATE anywhere else is an ordinary failure.
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        PostgresException failure = InsufficientPrivilege();
        ProbeScript script = ProbeScript.Healthy().FailingAt(id, failure);

        Exception? thrown = await Record.ExceptionAsync(() => ProbeAsync(script, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task UnexpectedExceptionDuringC004_PropagatesUnchanged()
    {
        var failure = new InvalidOperationException("synthetic unexpected");
        ProbeScript script = ProbeScript.Healthy().FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, failure);

        Exception? thrown = await Record.ExceptionAsync(() => ProbeAsync(script, TestContext.Current.CancellationToken).AsTask());

        Assert.Same(failure, thrown);
    }

    // --- Capability snapshot completeness -----------------------------------------------------------------

    public static TheoryData<string> AllScenarios() => ["Healthy", "StatisticsFalse", "Unsupported", "PrivilegeRace"];

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public async Task EveryScenario_ProducesExactlyThreeCapabilityStates(string scenario)
    {
        ProbeScript script = scenario switch
        {
            "Healthy" => ProbeScript.Healthy(),
            "StatisticsFalse" => ProbeScript.Healthy(statisticsAvailable: false),
            "Unsupported" => ProbeScript.Healthy(serverVersionNumber: 140000),
            "PrivilegeRace" => ProbeScript.Healthy().FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, InsufficientPrivilege()),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario."),
        };

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Capabilities.States.Count);
        Assert.Equal(
            Enum.GetValues<CapabilityKind>().OrderBy(kind => kind).ToArray(),
            result.Capabilities.States.Select(state => state.Kind).OrderBy(kind => kind).ToArray());

        // An Available capability never carries a reason.
        Assert.All(
            result.Capabilities.States.Where(state => state.Status == CapabilityStatus.Available),
            state => Assert.Null(state.Reason));
    }

    [Theory]
    [MemberData(nameof(AllScenarios))]
    public async Task DataProfiling_IsAlwaysDisabledByPolicy(string scenario)
    {
        ProbeScript script = scenario switch
        {
            "Healthy" => ProbeScript.Healthy(),
            "StatisticsFalse" => ProbeScript.Healthy(statisticsAvailable: false),
            "Unsupported" => ProbeScript.Healthy(serverVersionNumber: 140000),
            "PrivilegeRace" => ProbeScript.Healthy().FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, InsufficientPrivilege()),
            _ => throw new ArgumentOutOfRangeException(nameof(scenario), scenario, "Unknown scenario."),
        };

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        CapabilityState profiling = StateOf(result, CapabilityKind.DataProfiling);
        Assert.Equal(CapabilityStatus.Disabled, profiling.Status);
        Assert.Equal("Data profiling is disabled by product policy.", profiling.Reason);
    }

    // --- Argument validation -----------------------------------------------------------------------------

    [Fact]
    public async Task ProbeAsync_RejectsNullExecutor()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => PostgreSqlServerCapabilityProbe.ProbeAsync(null!, TestContext.Current.CancellationToken).AsTask());
    }

    // --- Result immutability ------------------------------------------------------------------------------

    [Fact]
    public void Result_HasNoSetters()
    {
        System.Reflection.PropertyInfo[] properties = typeof(PostgreSqlServerProbeResult)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public async Task Result_ToStringRendersNoIdentity()
    {
        ProbeScript script = ProbeScript.Healthy(databaseName: "MARKERDB", currentUser: "MARKERUSER");

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        string rendered = result.ToString()!;
        Assert.Equal(typeof(PostgreSqlServerProbeResult).ToString(), rendered);
    }

    [Fact]
    public async Task Identity_ToStringRendersNoValues()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(
            FakeRowReader.WithRows(3, [180004, "MARKERDB", "MARKERUSER"]));
        var executor = new PostgreSqlSqlExecutor(new PostgreSqlSqlInventory(), gateway);

        PostgreSqlServerIdentity identity = await executor.ReadServerIdentityAsync(TestContext.Current.CancellationToken);

        Assert.Equal(typeof(PostgreSqlServerIdentity).ToString(), identity.ToString());
    }
}
