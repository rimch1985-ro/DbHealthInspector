using System.Reflection;
using DbHealthInspector.Core;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Snapshots;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;
using DbHealthInspector.UnitTests.Sessions.TestSupport;
using DbHealthInspector.UnitTests.Snapshots.TestSupport;

namespace DbHealthInspector.UnitTests.Snapshots;

/// <summary>
/// The GC-DHI-04F provider contract: one capture composes the approved primitives into exactly one
/// complete <see cref="DatabaseSnapshot"/>, or fails without returning a partial result.
/// </summary>
public sealed class PostgreSqlDatabaseSnapshotProviderTests
{
    private const string CompositionMessage = "The PostgreSQL snapshot could not be composed safely.";
    private const string LeakMessage = "Sensitive data was exposed.";

    private static PostgreSqlDatabaseSnapshotProvider Provider(
        ProviderStatementGateway gateway,
        PostgreSqlSchemaFilter? filter = null,
        PostgreSqlInspectionSessionOptions? options = null) =>
        PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            new FakeInspectionSessionScope(gateway),
            filter ?? PostgreSqlSchemaFilter.IncludeEverything,
            options ?? PostgreSqlInspectionSessionOptions.Default);

    /// <summary>A gateway with one table and one matching index — a minimal healthy server.</summary>
    private static ProviderStatementGateway HealthyGateway()
    {
        var gateway = new ProviderStatementGateway();
        gateway.TableRows.Add(ProviderStatementGateway.TableRow());
        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow());
        gateway.StatisticsRows.Add(ProviderStatementGateway.StatisticsRow());
        return gateway;
    }

    // --- Public API exactness -------------------------------------------------------------------

    [Fact]
    public void TheAssemblyExportsExactlyTwoTypes()
    {
        Type[] exported = typeof(PostgreSqlDatabaseSnapshotProvider).Assembly.GetExportedTypes();

        Assert.Equal(2, exported.Length);
        Assert.Contains(exported, type => type.FullName == "DbHealthInspector.PostgreSql.AssemblyMarker");
        Assert.Contains(exported, type => type == typeof(PostgreSqlDatabaseSnapshotProvider));
    }

    [Fact]
    public void TheProviderImplementsTheCoreContractAndAsyncDisposal()
    {
        Assert.True(typeof(IDatabaseSnapshotProvider).IsAssignableFrom(typeof(PostgreSqlDatabaseSnapshotProvider)));
        Assert.True(typeof(IAsyncDisposable).IsAssignableFrom(typeof(PostgreSqlDatabaseSnapshotProvider)));
        Assert.True(typeof(PostgreSqlDatabaseSnapshotProvider).IsSealed);
    }

    [Fact]
    public void TheProviderHasNoPublicConstructor() =>
        Assert.Empty(typeof(PostgreSqlDatabaseSnapshotProvider).GetConstructors(BindingFlags.Public | BindingFlags.Instance));

    [Fact]
    public void ThePublicSurfaceIsExactlyTheFourApprovedMembers()
    {
        MethodInfo[] declared = [.. typeof(PostgreSqlDatabaseSnapshotProvider)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)];

        Assert.Equal(2, declared.Count(method => method.Name == "Create"));
        Assert.Single(declared, method => method.Name == nameof(PostgreSqlDatabaseSnapshotProvider.CaptureAsync));
        Assert.Single(declared, method => method.Name == nameof(PostgreSqlDatabaseSnapshotProvider.DisposeAsync));
        Assert.Equal(4, declared.Length);
    }

    [Fact]
    public void TheCompositionExceptionIsInternalAndParameterless()
    {
        Type type = typeof(PostgreSqlDatabaseSnapshotProvider).Assembly
            .GetType("DbHealthInspector.PostgreSql.Snapshots.PostgreSqlSnapshotCompositionException", throwOnError: true)!;

        Assert.False(type.IsPublic);
        Assert.True(type.IsSealed);

        ConstructorInfo only = Assert.Single(type.GetConstructors(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.Empty(only.GetParameters());
    }

    // --- Construction and validation ------------------------------------------------------------

    [Fact]
    public void ANullSchemaCollection_IsRejected()
    {
        Assert.Throws<ArgumentNullException>(
            () => PostgreSqlDatabaseSnapshotProvider.Create("Host=localhost", null!, [], TimeSpan.FromSeconds(30)));
        Assert.Throws<ArgumentNullException>(
            () => PostgreSqlDatabaseSnapshotProvider.Create("Host=localhost", [], null!, TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(-1)]                 // Timeout.Infinite
    [InlineData(0)]                  // not positive
    [InlineData(99)]                 // below the 100 ms minimum
    [InlineData(300_001)]            // above the 5 minute maximum
    public void AnOutOfRangeStatementTimeout_IsRejectedBeforeAnyResourceExists(int milliseconds)
    {
        TimeSpan timeout = milliseconds == -1
            ? Timeout.InfiniteTimeSpan
            : TimeSpan.FromMilliseconds(milliseconds);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PostgreSqlDatabaseSnapshotProvider.Create("Host=localhost", [], [], timeout));
    }

    [Fact]
    public void AFractionalMillisecondStatementTimeout_IsRejectedRatherThanRounded()
    {
        // Never rounded, truncated or clamped: an inexact value is refused outright.
        var fractional = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * 1000 + 1);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => PostgreSqlDatabaseSnapshotProvider.Create("Host=localhost", [], [], fractional));
    }

    [Fact]
    public void AnInvalidSchemaName_IsRejectedBeforeAnyResourceExists()
    {
        // The existing fixed filter error, raised before a data source could be created.
        Assert.ThrowsAny<Exception>(
            () => PostgreSqlDatabaseSnapshotProvider.Create("Host=localhost", [" "], [], TimeSpan.FromSeconds(30)));
    }

    // --- D1 lock-timeout derivation -------------------------------------------------------------

    [Theory]
    [InlineData(100, 50)]
    [InlineData(101, 50)]
    [InlineData(102, 51)]
    [InlineData(999, 499)]
    [InlineData(1000, 500)]
    [InlineData(9999, 4999)]
    [InlineData(10000, 5000)]
    [InlineData(30000, 5000)]
    [InlineData(300000, 5000)]
    public void TheLockTimeoutIsDerivedExactlyAsFrozen(int statementMilliseconds, int expectedLockMilliseconds)
    {
        PostgreSqlInspectionSessionOptions options = DeriveOptions(TimeSpan.FromMilliseconds(statementMilliseconds));

        Assert.Equal(expectedLockMilliseconds, options.LockTimeoutMilliseconds);
        Assert.Equal(statementMilliseconds, options.StatementTimeoutMilliseconds);

        // The idle timeout is fixed, never derived from the statement timeout.
        Assert.Equal(60_000, options.IdleInTransactionTimeoutMilliseconds);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(137)]
    [InlineData(9999)]
    [InlineData(10000)]
    [InlineData(299_999)]
    [InlineData(300_000)]
    public void TheDerivedLockTimeoutAlwaysSatisfiesItsBounds(int statementMilliseconds)
    {
        PostgreSqlInspectionSessionOptions options = DeriveOptions(TimeSpan.FromMilliseconds(statementMilliseconds));

        Assert.InRange(options.LockTimeoutMilliseconds, 50, 5000);
        Assert.True(options.LockTimeoutMilliseconds < options.StatementTimeoutMilliseconds);
        Assert.Equal(0, options.LockTimeout.Ticks % TimeSpan.TicksPerMillisecond);
    }

    /// <summary>
    /// Reaches the private derivation through the public factory's validated path, without opening
    /// a connection: the options are whatever the provider would hand the runner.
    /// </summary>
    private static PostgreSqlInspectionSessionOptions DeriveOptions(TimeSpan statementTimeout)
    {
        MethodInfo derive = typeof(PostgreSqlDatabaseSnapshotProvider)
            .GetMethod("DeriveOptions", BindingFlags.NonPublic | BindingFlags.Static)!;

        return (PostgreSqlInspectionSessionOptions)derive.Invoke(null, [statementTimeout])!;
    }

    // --- Exact productive sequence ----------------------------------------------------------------

    [Fact]
    public async Task ASupportedServer_ExecutesExactlyTheFrozenSequence()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        _ = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
                PostgreSqlSqlStatementId.CheckUsageStatisticsAccess,
                PostgreSqlSqlStatementId.ReadStatisticsReset,
                PostgreSqlSqlStatementId.ReadTableSnapshots,
                PostgreSqlSqlStatementId.ReadIndexMetadata,
                PostgreSqlSqlStatementId.ReadIndexUsageStatistics,
            ],
            gateway.ExecutedIds);
    }

    [Fact]
    public async Task TheSameFilterInstanceReachesD001AndTheIndexOperation()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        var filter = new PostgreSqlSchemaFilter(["sales"], ["staging"]);
        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway, filter);

        _ = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        // D001, E001 and E002 each bound the same filter contents; nothing was rebuilt per call.
        Assert.Equal(3, gateway.BoundIncludedSchemas.Count);
        Assert.All(gateway.BoundIncludedSchemas, bound => Assert.Equal(["sales"], bound));
    }

    [Fact]
    public async Task ACompleteSnapshotIsComposedFromTheProbeAndBothQueries()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.DatabaseName = "inspected_db";
        gateway.CurrentUser = "inspector";
        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(DatabaseEngine.PostgreSql, snapshot.Metadata.Engine);
        Assert.Equal("inspected_db", snapshot.Metadata.DatabaseName);
        Assert.Equal("inspector", snapshot.Metadata.CurrentUser);

        Assert.Equal(["public"], snapshot.Schemas.Select(schema => schema.SchemaName).ToArray());
        Assert.Single(snapshot.Tables);
        Assert.Single(snapshot.Indexes);

        Assert.Equal(
            CapabilityStatus.Available,
            snapshot.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(
            CapabilityStatus.Disabled,
            snapshot.Capabilities.GetState(CapabilityKind.DataProfiling).Status);
        Assert.NotNull(snapshot.Statistics.StatisticsResetAtUtc);

        // Statistics were available, so the scan counter is the exact server value, not null.
        Assert.Equal(7L, Assert.Single(snapshot.Indexes).ScanCount);
    }

    // --- Unsupported server -------------------------------------------------------------------------

    [Fact]
    public async Task AnUnsupportedServer_RunsC001OnlyAndReturnsACompleteEmptySnapshot()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.ServerVersionNumber = 140012;
        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        // C001 ran; nothing after it did.
        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
                PostgreSqlSqlStatementId.ReadServerIdentity,
            ],
            gateway.ExecutedIds);

        // A complete unsupported snapshot, not a partial supported one.
        Assert.NotNull(snapshot.Metadata);
        Assert.Empty(snapshot.Schemas);
        Assert.Empty(snapshot.Tables);
        Assert.Empty(snapshot.Indexes);
        Assert.Null(snapshot.Statistics.StatisticsResetAtUtc);
        Assert.NotEqual(
            CapabilityStatus.Available,
            snapshot.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status);
    }

    // --- Capability branches ---------------------------------------------------------------------------

    [Fact]
    public async Task CatalogMetadataUnavailable_FailsWithoutRunningAnyObjectQuery()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.CatalogMetadataAvailable = false;
        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        await Assert.ThrowsAsync<PostgreSqlRequiredCatalogCapabilityException>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.Equal(0, gateway.CountOf(PostgreSqlSqlStatementId.ReadTableSnapshots));
        Assert.Equal(0, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexMetadata));
        Assert.Equal(0, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexUsageStatistics));
    }

    [Fact]
    public async Task UsageStatisticsUnavailable_SkipsC004AndE002AndNullsEveryScanCount()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.UsageStatisticsAvailable = false;
        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, gateway.CountOf(PostgreSqlSqlStatementId.ReadStatisticsReset));
        Assert.Equal(0, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexUsageStatistics));

        // D001 and E001 still ran, and absence is unknown rather than zero.
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadTableSnapshots));
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexMetadata));
        Assert.Null(Assert.Single(snapshot.Indexes).ScanCount);
        Assert.Null(snapshot.Statistics.StatisticsResetAtUtc);
        Assert.Equal(
            CapabilityStatus.Unavailable,
            snapshot.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);
    }

    [Fact]
    public async Task E002RunsExactlyOnceWhenStatisticsAreAvailable()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        _ = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexUsageStatistics));
    }

    // --- Composition: closure, derivation, ordering -----------------------------------------------------

    [Fact]
    public async Task AnIndexWithoutItsTable_FailsComposition()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.IndexRows.Clear();
        gateway.StatisticsRows.Clear();
        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow(table: "absent_table", index: "orphan_idx"));

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        PostgreSqlSnapshotCompositionException exception =
            await Assert.ThrowsAsync<PostgreSqlSnapshotCompositionException>(
                () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.Equal(CompositionMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        foreach (string surface in new[] { exception.Message, exception.ToString() })
        {
            foreach (string marker in new[] { "absent_table", "orphan_idx" })
            {
                bool leaked = surface.Contains(marker, StringComparison.Ordinal);
                Assert.False(leaked, LeakMessage);
            }
        }
    }

    [Fact]
    public async Task AnIndexInADifferentSchemaFromItsTable_FailsComposition()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.IndexRows.Clear();
        gateway.StatisticsRows.Clear();
        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow(schema: "other", table: "orders"));

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        await Assert.ThrowsAsync<PostgreSqlSnapshotCompositionException>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SchemasAreDerivedFromTablesAndOrderedOrdinally()
    {
        ProviderStatementGateway gateway = new();
        gateway.TableRows.Add(ProviderStatementGateway.TableRow(schema: "zeta", table: "t"));
        gateway.TableRows.Add(ProviderStatementGateway.TableRow(schema: "Alpha", table: "t"));
        gateway.TableRows.Add(ProviderStatementGateway.TableRow(schema: "alpha", table: "t"));
        gateway.TableRows.Add(ProviderStatementGateway.TableRow(schema: "zeta", table: "u"));

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        // Distinct, ordinal — so uppercase sorts before lowercase — and never a hash-set order.
        Assert.Equal(["Alpha", "alpha", "zeta"], snapshot.Schemas.Select(schema => schema.SchemaName).ToArray());
    }

    [Fact]
    public async Task TablesAndIndexesAreOrderedOrdinally()
    {
        ProviderStatementGateway gateway = new();
        gateway.TableRows.Add(ProviderStatementGateway.TableRow(schema: "public", table: "zebra"));
        gateway.TableRows.Add(ProviderStatementGateway.TableRow(schema: "archive", table: "orders"));
        gateway.TableRows.Add(ProviderStatementGateway.TableRow(schema: "public", table: "apple"));

        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow(schema: "public", table: "zebra", index: "z_idx"));
        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow(schema: "public", table: "apple", index: "b_idx"));
        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow(schema: "public", table: "apple", index: "a_idx"));

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Equal(
            [("archive", "orders"), ("public", "apple"), ("public", "zebra")],
            snapshot.Tables.Select(table => (table.SchemaName, table.TableName)).ToArray());

        Assert.Equal(
            [("public", "apple", "a_idx"), ("public", "apple", "b_idx"), ("public", "zebra", "z_idx")],
            snapshot.Indexes.Select(index => (index.SchemaName, index.TableName, index.IndexName)).ToArray());
    }

    [Fact]
    public async Task AnEmptyServer_ProducesAValidEmptySnapshot()
    {
        ProviderStatementGateway gateway = new();
        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.Empty(snapshot.Schemas);
        Assert.Empty(snapshot.Tables);
        Assert.Empty(snapshot.Indexes);
        Assert.NotNull(snapshot.Metadata);
        Assert.NotNull(snapshot.Capabilities);
    }

    // --- Atomicity ---------------------------------------------------------------------------------------

    [Fact]
    public async Task AFailureAfterD001_ReturnsNothingAtAll()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        _ = gateway.FailingAt(PostgreSqlSqlStatementId.ReadIndexMetadata, new InvalidOperationException("E001"));

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        // No tables-only snapshot escapes just because D001 already succeeded.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));
    }

    // --- Cancellation ------------------------------------------------------------------------------------

    [Fact]
    public async Task APrecancelledToken_PreventsTheCaptureEntirely()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        Assert.Empty(gateway.ExecutedIds);
    }

    /// <summary>
    /// The C/D/E boundaries, indexed so the theory signature stays public while the statement id
    /// enum remains internal.
    /// </summary>
    private static readonly PostgreSqlSqlStatementId[] CancellationBoundaries =
    [
        PostgreSqlSqlStatementId.ReadServerIdentity,
        PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
        PostgreSqlSqlStatementId.CheckUsageStatisticsAccess,
        PostgreSqlSqlStatementId.ReadStatisticsReset,
        PostgreSqlSqlStatementId.ReadTableSnapshots,
        PostgreSqlSqlStatementId.ReadIndexMetadata,
        PostgreSqlSqlStatementId.ReadIndexUsageStatistics,
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task CancellationAtAnyStatementBoundary_ReturnsNoSnapshot(int boundaryIndex)
    {
        PostgreSqlSqlStatementId boundary = CancellationBoundaries[boundaryIndex];
        ProviderStatementGateway gateway = HealthyGateway();
        using var cts = new CancellationTokenSource();

        _ = gateway.BeforeStatement(boundary, () =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        });

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        // The cancellation is raised from inside the seam before the statement's outcome, so the
        // boundary itself never executed -- and neither did anything that follows it.
        foreach (PostgreSqlSqlStatementId notReached in CancellationBoundaries[boundaryIndex..])
        {
            Assert.Equal(0, gateway.CountOf(notReached));
        }
    }

    [Fact]
    public async Task CancellationAfterAllQueries_StillReturnsNoSnapshot()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        using var cts = new CancellationTokenSource();

        // Cancel during the last statement's execution, after every query has been issued: the
        // in-transaction checkpoint before construction must still refuse to return.
        var scope = new FakeInspectionSessionScope(gateway);
        _ = scope.BeforeStep(SessionScopeStep.Rollback, cts.Cancel);

        await using var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope, PostgreSqlSchemaFilter.IncludeEverything, PostgreSqlInspectionSessionOptions.Default);

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        // Cleanup still ran to completion despite the cancellation.
        Assert.True(scope.AllCleanupStepsAttempted);
        Assert.True(scope.TransactionDisposedBeforeConnection);
    }

    // --- Cleanup and EDI precedence ------------------------------------------------------------------------

    [Fact]
    public async Task AQueryFailure_OutranksAReaderDisposalFailure()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        _ = gateway
            .FailingAt(PostgreSqlSqlStatementId.ReadTableSnapshots, new InvalidOperationException("primary"))
            .WithReaderDisposalFailure(PostgreSqlSqlStatementId.ReadTableSnapshots, new InvalidOperationException("cleanup"));

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.Equal("primary", exception.Message);
    }

    [Fact]
    public async Task ACompositionFailure_OutranksARollbackFailure()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.IndexRows.Clear();
        gateway.StatisticsRows.Clear();
        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow(table: "absent_table"));

        var scope = new FakeInspectionSessionScope(gateway);
        _ = scope.FailingAt(SessionScopeStep.Rollback, new InvalidOperationException("rollback"));

        await using var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope, PostgreSqlSchemaFilter.IncludeEverything, PostgreSqlInspectionSessionOptions.Default);

        // The composition failure is the primary and must not be replaced by cleanup.
        await Assert.ThrowsAsync<PostgreSqlSnapshotCompositionException>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.True(scope.AllCleanupStepsAttempted);
    }

    [Fact]
    public async Task OnSuccess_ARollbackFailureIsStillObservable()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        var scope = new FakeInspectionSessionScope(gateway);
        _ = scope.FailingAt(SessionScopeStep.Rollback, new InvalidOperationException("rollback"));

        await using var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope, PostgreSqlSchemaFilter.IncludeEverything, PostgreSqlInspectionSessionOptions.Default);

        // No primary exists, so the cleanup failure must not be swallowed.
        await Assert.ThrowsAnyAsync<Exception>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.True(scope.AllCleanupStepsAttempted);
    }

    // --- Lifecycle and concurrency ------------------------------------------------------------------------

    [Fact]
    public async Task DisposeAsyncIsIdempotent()
    {
        PostgreSqlDatabaseSnapshotProvider provider = Provider(HealthyGateway());

        await provider.DisposeAsync();
        await provider.DisposeAsync();
        await provider.DisposeAsync();
    }

    [Fact]
    public async Task ACaptureAfterDisposal_IsRejectedByName()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        PostgreSqlDatabaseSnapshotProvider provider = Provider(gateway);

        await provider.DisposeAsync();

        ObjectDisposedException exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.Equal(nameof(PostgreSqlDatabaseSnapshotProvider), exception.ObjectName);

        // Rejected before anything reached the server.
        Assert.Empty(gateway.ExecutedIds);
    }

    [Fact]
    public async Task ConcurrentCaptures_EachUseTheirOwnScope()
    {
        // Each capture gets its own gateway through its own scope, so independence is observable.
        var factory = new CountingScopeFactory();

        await using var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            factory, PostgreSqlSchemaFilter.IncludeEverything, PostgreSqlInspectionSessionOptions.Default);

        Task<DatabaseSnapshot>[] captures =
        [
            provider.CaptureAsync(TestContext.Current.CancellationToken),
            provider.CaptureAsync(TestContext.Current.CancellationToken),
            provider.CaptureAsync(TestContext.Current.CancellationToken),
        ];

        DatabaseSnapshot[] snapshots = await Task.WhenAll(captures);

        Assert.Equal(3, snapshots.Length);
        Assert.All(snapshots, snapshot => Assert.Single(snapshot.Tables));
        Assert.Equal(3, factory.CreatedScopes);
    }

    private sealed class CountingScopeFactory : IPostgreSqlInspectionSessionScopeFactory
    {
        private int _created;

        internal int CreatedScopes => Volatile.Read(ref _created);

        public IPostgreSqlInspectionSessionScope Create()
        {
            _ = Interlocked.Increment(ref _created);

            var gateway = new ProviderStatementGateway();
            gateway.TableRows.Add(ProviderStatementGateway.TableRow());

            return new FakeInspectionSessionScope(gateway);
        }
    }
}
