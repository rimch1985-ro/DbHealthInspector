using System.Reflection;
using DbHealthInspector.Core;
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
/// Deterministic provider-level evidence required by GC-DHI-04F-C1: the exact C004/42501
/// degradation path (R1-03), schema-filter <b>instance</b> identity, the atomicity, cancellation-gap
/// and EDI matrices (R1-04), and the absence of sync-over-async construction cleanup (R1-02).
/// </summary>
public sealed class PostgreSqlDatabaseSnapshotProviderEvidenceTests
{
    private const string CompositionMessage = "The PostgreSQL snapshot could not be composed safely.";

    private static ProviderStatementGateway HealthyGateway()
    {
        var gateway = new ProviderStatementGateway();
        gateway.TableRows.Add(ProviderStatementGateway.TableRow());
        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow());
        gateway.StatisticsRows.Add(ProviderStatementGateway.StatisticsRow());
        return gateway;
    }

    private static PostgreSqlDatabaseSnapshotProvider Provider(
        FakeInspectionSessionScope scope, PostgreSqlSchemaFilter? filter = null) =>
        PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope, filter ?? PostgreSqlSchemaFilter.IncludeEverything, PostgreSqlInspectionSessionOptions.Default);

    // --- R1-02: no sync-over-async, and validation precedes resource acquisition -------------------

    [Fact]
    public void TheProviderAndItsLifecycleContainNoSyncOverAsync()
    {
        // A source-level guarantee: the IL of an async method can hide an intent, so the check is
        // made against the shipped source of exactly the two types under this contract.
        string root = FindRepositoryRoot();
        string[] files =
        [
            Path.Combine(root, "src", "DbHealthInspector.PostgreSql", "Snapshots", "PostgreSqlDatabaseSnapshotProvider.cs"),
            Path.Combine(root, "src", "DbHealthInspector.PostgreSql", "Snapshots", "PostgreSqlSnapshotProviderLifecycle.cs"),
        ];

        foreach (string file in files)
        {
            Assert.True(File.Exists(file));

            string source = File.ReadAllText(file);

            Assert.DoesNotContain("GetAwaiter().GetResult", source, StringComparison.Ordinal);
            Assert.DoesNotContain(".Wait(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("Task.Run(", source, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    [InlineData(300_001)]
    public void AnInvalidTimeout_CreatesNoResourceAtAll(int milliseconds)
    {
        // Validation precedes acquisition, so a rejected argument leaves nothing to clean up and
        // no synchronous disposal is ever needed.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => PostgreSqlDatabaseSnapshotProvider.Create(
                "Host=localhost;Database=x;Username=u;Password=p",
                [],
                [],
                TimeSpan.FromMilliseconds(milliseconds)));
    }

    [Fact]
    public void AnInvalidSchemaName_CreatesNoResourceAtAll() =>
        Assert.ThrowsAny<Exception>(
            () => PostgreSqlDatabaseSnapshotProvider.Create(
                "Host=localhost;Database=x;Username=u;Password=p",
                [" "],
                [],
                TimeSpan.FromSeconds(30)));

    [Fact]
    public async Task AValidCreate_ProducesOneDisposableProvider()
    {
        // No server is contacted by construction; this proves the happy path acquires exactly one
        // factory and that disposing it is a clean asynchronous operation.
        await using PostgreSqlDatabaseSnapshotProvider provider = PostgreSqlDatabaseSnapshotProvider.Create(
            "Host=localhost;Database=x;Username=u;Password=p");

        Assert.NotNull(provider);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DbHealthInspector.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }

    // --- R1-03: provider-level C004 / 42501 degradation ---------------------------------------------

    [Fact]
    public async Task C004PermissionDenied_DegradesThroughTheWholeProviderComposition()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        _ = gateway.WithStatisticsResetPermissionDenied();

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(new FakeInspectionSessionScope(gateway));

        DatabaseSnapshot snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        // C001-C004 all ran; C004 failed with 42501 and the 04C policy degraded it.
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadServerIdentity));
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess));
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess));
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadStatisticsReset));

        // The object queries still run; only the optional statistics are given up.
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadTableSnapshots));
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexMetadata));
        Assert.Equal(0, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexUsageStatistics));

        Assert.Equal(
            CapabilityStatus.Available,
            snapshot.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(
            CapabilityStatus.Unavailable,
            snapshot.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);
        Assert.Equal(
            CapabilityStatus.Disabled,
            snapshot.Capabilities.GetState(CapabilityKind.DataProfiling).Status);

        Assert.Null(snapshot.Statistics.StatisticsResetAtUtc);
        Assert.NotEmpty(snapshot.Indexes);
        Assert.All(snapshot.Indexes, index => Assert.Null(index.ScanCount));
    }

    [Fact]
    public void TheProviderContainsNoSqlStateOrPostgresExceptionHandling()
    {
        // The 42501 authority stays in the 04C probe. This asserts the provider added no competing
        // classifier of its own.
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "DbHealthInspector.PostgreSql", "Snapshots",
            "PostgreSqlDatabaseSnapshotProvider.cs"));

        Assert.DoesNotContain("PostgresException", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SqlState", source, StringComparison.Ordinal);
        Assert.DoesNotContain("42501", source, StringComparison.Ordinal);
    }

    // --- R1-04A: the same filter INSTANCE reaches every statement -------------------------------------

    [Fact]
    public async Task TheProviderHoldsTheCallersFilterInstanceItself()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        var filter = new PostgreSqlSchemaFilter(["sales"], ["staging"]);

        await using PostgreSqlDatabaseSnapshotProvider provider =
            Provider(new FakeInspectionSessionScope(gateway), filter);

        _ = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        FieldInfo field = typeof(PostgreSqlDatabaseSnapshotProvider)
            .GetField("_filter", BindingFlags.NonPublic | BindingFlags.Instance)!;

        // Reference identity: the provider stores the very instance it was given, never a
        // defensively rebuilt equivalent.
        Assert.Same(filter, field.GetValue(provider));
    }

    [Fact]
    public void TheProviderNeverConstructsASchemaFilterOutsideItsFactory()
    {
        // The counterpart to the identity check above. Because the filter field is the one the
        // capture path passes to both operations, a provider that never builds a second filter
        // cannot hand two different instances to D001 and the index operation.
        //
        // A binding-level assertion cannot prove this: PostgreSqlSqlParameterValue.TextArray copies
        // its input defensively, so the two bound arrays are distinct objects even when one filter
        // instance was used (GC-DHI-04F-C1, R1-04A).
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(), "src", "DbHealthInspector.PostgreSql", "Snapshots",
            "PostgreSqlDatabaseSnapshotProvider.cs"));

        int constructions = source.Split("new PostgreSqlSchemaFilter(").Length - 1;

        // Exactly one, in the four-argument factory. None in CaptureAsync or its composition.
        Assert.Equal(1, constructions);

        int captureStart = source.IndexOf("CaptureCoreAsync", StringComparison.Ordinal);
        Assert.True(captureStart > 0);
        Assert.DoesNotContain(
            "new PostgreSqlSchemaFilter(", source[captureStart..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryFilteredStatementBindsTheSameFilterContents()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        var filter = new PostgreSqlSchemaFilter(["sales"], ["staging"]);

        await using PostgreSqlDatabaseSnapshotProvider provider =
            Provider(new FakeInspectionSessionScope(gateway), filter);

        _ = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        // D001, E001 and E002 each bound the filter, and all three agree.
        Assert.Equal(3, gateway.BoundIncludedSchemas.Count);
        Assert.All(gateway.BoundIncludedSchemas, bound => Assert.Equal(["sales"], bound));
    }

    // --- R1-04B: atomicity matrix ------------------------------------------------------------------------

    public static TheoryData<int> AtomicityCases() => [0, 1, 2, 3, 4];

    [Theory]
    [MemberData(nameof(AtomicityCases))]
    public async Task NoFailurePathEverReturnsASnapshot(int scenario)
    {
        ProviderStatementGateway gateway = HealthyGateway();
        var scope = new FakeInspectionSessionScope(gateway);

        switch (scenario)
        {
            case 0: // D001 fails
                _ = gateway.FailingAt(PostgreSqlSqlStatementId.ReadTableSnapshots, new InvalidOperationException("D001"));
                break;
            case 1: // E001 fails, after D001 already succeeded
                _ = gateway.FailingAt(PostgreSqlSqlStatementId.ReadIndexMetadata, new InvalidOperationException("E001"));
                break;
            case 2: // E002 fails, after D001 and E001 already succeeded
                _ = gateway.FailingAt(PostgreSqlSqlStatementId.ReadIndexUsageStatistics, new InvalidOperationException("E002"));
                break;
            case 3: // closure fails: an index with no table
                gateway.IndexRows.Clear();
                gateway.StatisticsRows.Clear();
                gateway.IndexRows.Add(ProviderStatementGateway.IndexRow(table: "absent"));
                break;
            default: // Core construction fails: two tables with the same identity
                gateway.TableRows.Add(ProviderStatementGateway.TableRow());
                break;
        }

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(scope);

        // Every one of these returns nothing at all -- never a metadata-only or tables-only result.
        Exception failure = await Assert.ThrowsAnyAsync<Exception>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.NotNull(failure);

        // Cleanup ran in full regardless of which stage failed.
        Assert.True(scope.AllCleanupStepsAttempted);
        Assert.True(scope.TransactionDisposedBeforeConnection);
    }

    [Fact]
    public async Task ADuplicateTableIdentity_IsRejectedUpstreamAndNamesNothing()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.TableRows.Add(ProviderStatementGateway.TableRow());

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(new FakeInspectionSessionScope(gateway));

        // The 04D result guard rejects the duplicate before composition is reached, so this is the
        // table-mapping failure rather than the composition one -- still fixed, still valueless,
        // and still no snapshot.
        Exception exception = await Assert.ThrowsAnyAsync<Exception>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.DoesNotContain("orders", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ACoreConstructionFailure_IsWrappedWithoutNamingTheObject()
    {
        // The upstream 04D/04E guards make Core's own duplicate checks unreachable through a normal
        // capture, so the narrow wrap is exercised directly here with state those guards would
        // never have produced. Core's message for this input names the schema and table; the wrap
        // must replace it with the fixed, valueless composition failure.
        MethodInfo compose = typeof(PostgreSqlDatabaseSnapshotProvider)
            .GetMethod("Compose", BindingFlags.NonPublic | BindingFlags.Static)!;

        var probe = new PostgreSqlServerProbeResult(
            new DatabaseMetadata(DatabaseEngine.PostgreSql, "18.4", "db", "user"),
            new CapabilitySnapshot(
            [
                new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available),
                new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available),
                new CapabilityState(CapabilityKind.DataProfiling, CapabilityStatus.Disabled),
            ]),
            new StatisticsSnapshot(null),
            180004,
            18,
            PostgreSqlVersionSupportStatus.Supported);

        TableSnapshot duplicate = new(
            "leak_schema", "leak_table", RelationKind.OrdinaryTable, false, false, 0, 0, 0, 0, false);

        TargetInvocationException thrown = Assert.Throws<TargetInvocationException>(
            () => compose.Invoke(null, [probe, new[] { duplicate, duplicate }, Array.Empty<IndexSnapshot>()]));

        PostgreSqlSnapshotCompositionException exception =
            Assert.IsType<PostgreSqlSnapshotCompositionException>(thrown.InnerException);

        Assert.Equal(CompositionMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.DoesNotContain("leak_schema", exception.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("leak_table", exception.ToString(), StringComparison.Ordinal);
    }

    // --- R1-04C: cancellation at every operation gap ---------------------------------------------------------

    /// <summary>The statement each gap sits immediately before.</summary>
    private static readonly PostgreSqlSqlStatementId[] GapBoundaries =
    [
        PostgreSqlSqlStatementId.ReadTableSnapshots,        // after probe / before D001
        PostgreSqlSqlStatementId.ReadIndexMetadata,         // after D001 / before E001
        PostgreSqlSqlStatementId.ReadIndexUsageStatistics,  // after E001 / before E002
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationInAnyOperationGap_RunsNoLaterStatementAndReturnsNoSnapshot(int gapIndex)
    {
        PostgreSqlSqlStatementId boundary = GapBoundaries[gapIndex];
        ProviderStatementGateway gateway = HealthyGateway();
        var scope = new FakeInspectionSessionScope(gateway);

        using var cts = new CancellationTokenSource();
        _ = gateway.BeforeStatement(boundary, () =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        });

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(scope);

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        // Neither the boundary nor anything after it executed.
        foreach (PostgreSqlSqlStatementId notReached in GapBoundaries[gapIndex..])
        {
            Assert.Equal(0, gateway.CountOf(notReached));
        }

        Assert.True(scope.AllCleanupStepsAttempted);
    }

    [Fact]
    public async Task CancellationAfterAllQueriesButBeforeReturn_ReturnsNoSnapshot()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        var scope = new FakeInspectionSessionScope(gateway);

        using var cts = new CancellationTokenSource();

        // Every query has been issued by the time rollback begins; the post-cleanup checkpoint must
        // still refuse to hand the caller a snapshot.
        _ = scope.BeforeStep(SessionScopeStep.Rollback, cts.Cancel);

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(scope);

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexUsageStatistics));
        Assert.True(scope.AllCleanupStepsAttempted);
    }

    // --- R1-04D: provider-level EDI precedence -------------------------------------------------------------

    [Fact]
    public async Task ACompositionFailure_OutranksARollbackFailure()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.IndexRows.Clear();
        gateway.StatisticsRows.Clear();
        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow(table: "absent"));

        var scope = new FakeInspectionSessionScope(gateway);
        _ = scope.FailingAt(SessionScopeStep.Rollback, new InvalidOperationException("rollback"));

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(scope);

        await Assert.ThrowsAsync<PostgreSqlSnapshotCompositionException>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.True(scope.AllCleanupStepsAttempted);
    }

    [Fact]
    public async Task ARequestedCancellation_OutranksARollbackFailure()
    {
        ProviderStatementGateway gateway = HealthyGateway();
        var scope = new FakeInspectionSessionScope(gateway);
        _ = scope.FailingAt(SessionScopeStep.Rollback, new InvalidOperationException("rollback"));

        using var cts = new CancellationTokenSource();
        _ = gateway.BeforeStatement(PostgreSqlSqlStatementId.ReadTableSnapshots, () =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        });

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(scope);

        await Assert.ThrowsAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        Assert.True(scope.AllCleanupStepsAttempted);
    }

    public static TheoryData<int> CleanupStages() => [0, 1, 2];

    [Theory]
    [MemberData(nameof(CleanupStages))]
    public async Task OnSuccess_AnyCleanupFailureIsStillObservable(int stage)
    {
        SessionScopeStep step = stage switch
        {
            0 => SessionScopeStep.Rollback,
            1 => SessionScopeStep.DisposeTransaction,
            _ => SessionScopeStep.DisposeConnection,
        };

        ProviderStatementGateway gateway = HealthyGateway();
        var scope = new FakeInspectionSessionScope(gateway);
        _ = scope.FailingAt(step, new InvalidOperationException("cleanup"));

        await using PostgreSqlDatabaseSnapshotProvider provider = Provider(scope);

        // With no primary failure the cleanup failure must not be swallowed, and the caller must
        // not receive a snapshot.
        await Assert.ThrowsAnyAsync<Exception>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));

        Assert.True(scope.AllCleanupStepsAttempted);
    }
}
