using System.Reflection;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Capabilities.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Capabilities;

/// <summary>
/// Database name and current user are authorized <i>result</i> metadata, but they — and every
/// PostgreSQL detail — must never appear in an exception, a capability reason or any other
/// exposed surface (GC-DHI-04C §18).
/// </summary>
/// <remarks>
/// <para>
/// Two hygiene rules govern this file (GC-DHI-04C-C1, R1-13). Marker sets are produced fresh per
/// call rather than held in a shared mutable array, so no test can alter what another test checks.
/// </para>
/// <para>
/// And a leak is asserted through <see cref="Assert.False(bool, string)"/> with a fixed message
/// rather than <c>Assert.DoesNotContain</c>, because the latter prints the marker and the
/// surrounding surface on failure — which would put the very value under test into CI output. No
/// marker is used as theory data either, so none can reach a test display name.
/// </para>
/// </remarks>
public sealed class PostgreSqlServerCapabilityProbeLeakageTests
{
    private const string LeakMessage = "Sensitive data was exposed.";

    private const string DatabaseMarker = "marker-database-04c";
    private const string UserMarker = "marker-user-04c";
    private const string MessageMarker = "marker-message-04c";
    private const string DetailMarker = "marker-detail-04c";
    private const string HintMarker = "marker-hint-04c";
    private const string SchemaMarker = "marker-schema-04c";
    private const string TableMarker = "marker-table-04c";
    private const string ColumnMarker = "marker-column-04c";
    private const string ConstraintMarker = "marker-constraint-04c";
    private const string InternalQueryMarker = "marker-internalquery-04c";
    private const string WhereMarker = "marker-where-04c";
    private const string RoutineMarker = "marker-routine-04c";
    private const string SqlStateMarker = "42501";

    /// <summary>
    /// Every populated field of the synthetic PostgreSQL failure. An iterator over constants: no
    /// shared array exists to be mutated, and each caller gets its own sequence.
    /// </summary>
    private static IEnumerable<string> ServerMarkers()
    {
        yield return MessageMarker;
        yield return DetailMarker;
        yield return HintMarker;
        yield return SchemaMarker;
        yield return TableMarker;
        yield return ColumnMarker;
        yield return ConstraintMarker;
        yield return InternalQueryMarker;
        yield return WhereMarker;
        yield return RoutineMarker;
        yield return SqlStateMarker;
    }

    /// <summary>Identity plus the SQL shapes that must never be echoed back.</summary>
    private static IEnumerable<string> IdentityAndSqlMarkers()
    {
        yield return DatabaseMarker;
        yield return UserMarker;
        yield return "SELECT";
        yield return "pg_catalog";
        yield return "has_table_privilege";
    }

    /// <summary>
    /// Fails with a fixed message that names neither the marker nor the surface, so a failure
    /// reports only that something leaked.
    /// </summary>
    private static void AssertNoLeak(string surface, IEnumerable<string> markers)
    {
        foreach (string marker in markers)
        {
            bool leaked = surface.Contains(marker, StringComparison.OrdinalIgnoreCase);
            Assert.False(leaked, LeakMessage);
        }
    }

    private static void AssertExceptionLeaksNothing(Exception exception, IEnumerable<string> markers)
    {
        string[] snapshot = [.. markers];

        AssertNoLeak(exception.Message, snapshot);
        AssertNoLeak(exception.ToString(), snapshot);
        AssertNoLeak(exception.StackTrace ?? string.Empty, snapshot);

        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    private static PostgresException LoadedInsufficientPrivilege() =>
        new(
            messageText: MessageMarker,
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: SqlStateMarker,
            detail: DetailMarker,
            hint: HintMarker,
            position: 0,
            internalPosition: 0,
            internalQuery: InternalQueryMarker,
            where: WhereMarker,
            schemaName: SchemaMarker,
            tableName: TableMarker,
            columnName: ColumnMarker,
            dataTypeName: "text",
            constraintName: ConstraintMarker,
            file: "marker-file.c",
            line: "1",
            routine: RoutineMarker);

    [Fact]
    public async Task DegradedStatistics_LeakNoServerDetail()
    {
        ProbeScript script = ProbeScript.Healthy(databaseName: DatabaseMarker, currentUser: UserMarker)
            .FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, LoadedInsufficientPrivilege());

        PostgreSqlServerProbeResult result = await PostgreSqlServerCapabilityProbe
            .ProbeAsync(script.View(), TestContext.Current.CancellationToken);

        foreach (CapabilityState state in result.Capabilities.States)
        {
            string reason = state.Reason ?? string.Empty;

            AssertNoLeak(reason, ServerMarkers());

            // Identity is authorized result metadata but never belongs in a reason.
            AssertNoLeak(reason, IdentityAndSqlMarkers());
        }
    }

    [Fact]
    public async Task DegradedStatistics_DiscardTheOriginalExceptionEntirely()
    {
        PostgresException original = LoadedInsufficientPrivilege();
        ProbeScript script = ProbeScript.Healthy()
            .FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, original);

        PostgreSqlServerProbeResult result = await PostgreSqlServerCapabilityProbe
            .ProbeAsync(script.View(), TestContext.Current.CancellationToken);

        FieldInfo[] fields = typeof(PostgreSqlServerProbeResult)
            .GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        foreach (FieldInfo field in fields)
        {
            object? value = field.GetValue(result);

            // Nothing on the result may reference the discarded exception, in any field.
            Assert.False(ReferenceEquals(value, original), LeakMessage);

            // Nor may it hold a delegate, whose closure could carry the exception indirectly.
            Assert.False(value is Delegate, LeakMessage);
        }

        AssertNoLeak(result.ToString()!, ServerMarkers());
    }

    [Fact]
    public async Task RequiredCatalogException_LeaksNothing()
    {
        ProbeScript script = ProbeScript.Healthy(
            databaseName: DatabaseMarker, currentUser: UserMarker, catalogAvailable: false);

        PostgreSqlRequiredCatalogCapabilityException exception =
            await Assert.ThrowsAsync<PostgreSqlRequiredCatalogCapabilityException>(
                () => PostgreSqlServerCapabilityProbe.ProbeAsync(script.View(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("Required PostgreSQL catalog metadata is unavailable.", exception.Message);

        AssertExceptionLeaksNothing(exception, [.. IdentityAndSqlMarkers(), SqlStateMarker]);
    }

    [Fact]
    public async Task VersionException_LeaksNothing()
    {
        // An impossible encoding must not echo the offending value back either.
        ProbeScript script = ProbeScript.Healthy()
            .WithIdentity(serverVersionNumber: 9999, databaseName: DatabaseMarker, currentUser: UserMarker);

        PostgreSqlServerVersionException exception = await Assert.ThrowsAsync<PostgreSqlServerVersionException>(
            () => PostgreSqlServerCapabilityProbe.ProbeAsync(script.View(), TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("The PostgreSQL server version could not be interpreted.", exception.Message);

        AssertExceptionLeaksNothing(exception, [.. IdentityAndSqlMarkers(), "9999"]);
    }

    [Fact]
    public async Task IdentityIsReportedInMetadataButNotRenderedByToString()
    {
        ProbeScript script = ProbeScript.Healthy(databaseName: DatabaseMarker, currentUser: UserMarker);

        PostgreSqlServerProbeResult result = await PostgreSqlServerCapabilityProbe
            .ProbeAsync(script.View(), TestContext.Current.CancellationToken);

        // Authorized: available through the typed metadata.
        bool databasePreserved = string.Equals(DatabaseMarker, result.Metadata.DatabaseName, StringComparison.Ordinal);
        bool userPreserved = string.Equals(UserMarker, result.Metadata.CurrentUser, StringComparison.Ordinal);
        Assert.True(databasePreserved, "Authorized metadata was not preserved.");
        Assert.True(userPreserved, "Authorized metadata was not preserved.");

        // Not authorized: incidental rendering.
        AssertNoLeak(result.ToString()!, [DatabaseMarker, UserMarker]);
    }

    [Fact]
    public void CapabilityReasons_AreExactlyTheThreeFrozenStrings()
    {
        Assert.Equal(
            "The PostgreSQL server version is outside the supported range.",
            PostgreSqlServerCapabilityProbe.UnsupportedVersionReason);
        Assert.Equal(
            "Usage statistics are unavailable for this inspection.",
            PostgreSqlServerCapabilityProbe.UnavailableStatisticsReason);
        Assert.Equal(
            "Data profiling is disabled by product policy.",
            PostgreSqlServerCapabilityProbe.DisabledProfilingReason);
    }

    [Fact]
    public async Task StatisticsFalseAndPrivilegeRace_ProduceIndistinguishableReasons()
    {
        // A caller must not be able to tell which of the two happened: both are simply
        // "unavailable", so neither reveals the server's privilege timeline.
        ProbeScript declined = ProbeScript.Healthy(statisticsAvailable: false);
        ProbeScript raced = ProbeScript.Healthy()
            .FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, LoadedInsufficientPrivilege());

        PostgreSqlServerProbeResult declinedResult = await PostgreSqlServerCapabilityProbe
            .ProbeAsync(declined.View(), TestContext.Current.CancellationToken);
        PostgreSqlServerProbeResult racedResult = await PostgreSqlServerCapabilityProbe
            .ProbeAsync(raced.View(), TestContext.Current.CancellationToken);

        CapabilityState declinedState = declinedResult.Capabilities.GetState(CapabilityKind.UsageStatistics);
        CapabilityState racedState = racedResult.Capabilities.GetState(CapabilityKind.UsageStatistics);

        Assert.Equal(declinedState.Status, racedState.Status);
        Assert.Equal(declinedState.Reason, racedState.Reason);
    }
}
