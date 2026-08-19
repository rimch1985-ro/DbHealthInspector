using System.Reflection;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Snapshots;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;
using DbHealthInspector.UnitTests.Sessions.TestSupport;
using DbHealthInspector.UnitTests.Snapshots.TestSupport;

namespace DbHealthInspector.UnitTests.Snapshots;

/// <summary>
/// The provider-level races and checkpoints GC-DHI-04F-C2 closes: a capture genuinely in flight
/// against a concurrent disposal (R1-04A/B), the checkpoint after the last query but before
/// composition (R1-04C), the checkpoint after composition but before the callback returns
/// (R1-04D), and a cleanup failure that a later cancellation must not displace (R1-04E).
/// </summary>
/// <remarks>
/// Every race is driven by explicit task gates. There is no sleep, no polling and no timing
/// assumption in this suite.
/// </remarks>
public sealed class PostgreSqlDatabaseSnapshotProviderRaceTests
{
    private static ProviderStatementGateway HealthyGateway()
    {
        var gateway = new ProviderStatementGateway();
        gateway.TableRows.Add(ProviderStatementGateway.TableRow());
        gateway.IndexRows.Add(ProviderStatementGateway.IndexRow());
        gateway.StatisticsRows.Add(ProviderStatementGateway.StatisticsRow());
        return gateway;
    }

    /// <summary>Counts factory releases and can be gated open, exactly as the lifecycle suite does.</summary>
    private sealed class TrackedScopeFactory : IPostgreSqlInspectionSessionScopeFactory
    {
        private readonly FakeInspectionSessionScope _scope;

        internal TrackedScopeFactory(FakeInspectionSessionScope scope) => _scope = scope;

        internal int CreatedScopes { get; private set; }

        public IPostgreSqlInspectionSessionScope Create()
        {
            CreatedScopes++;
            return _scope;
        }
    }

    /// <summary>
    /// The ordered events a race must produce, recorded on one shared timeline.
    /// </summary>
    /// <remarks>
    /// There is deliberately no "lease released" marker. The lease is freed inside the provider's
    /// own <c>finally</c>, which runs before the capture's exception reaches the test, so any
    /// marker the test could record would land *after* the release had legitimately begun and
    /// would describe the recorder rather than the provider. The lease ordering is instead proven
    /// by the assertion that the release count is still zero while the capture is verifiably in
    /// flight — the release cannot start until the drain completes.
    /// </remarks>
    private enum RaceEvent
    {
        DisposeStarted,
        ResourceReleaseStarted,
        ResourceReleaseCompleted,
        DisposeCompleted,
    }

    /// <summary>
    /// The provider's owned resource, made observable: it records when its release starts and
    /// finishes, counts releases, and can be held open so the ordering is deterministic.
    /// </summary>
    private sealed class ObservableOwnedResource
    {
        private readonly TaskCompletionSource _mayComplete = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly List<RaceEvent> _timeline;
        private readonly Lock _gate;

        internal ObservableOwnedResource(List<RaceEvent> timeline, Lock gate)
        {
            _timeline = timeline;
            _gate = gate;
        }

        internal int ReleaseCount;

        /// <summary>Completes once the release has actually begun.</summary>
        internal Task ReleaseStarted => _started.Task;

        /// <summary>Lets a blocked release finish.</summary>
        internal void AllowReleaseCompletion() => _mayComplete.TrySetResult();

        internal async ValueTask ReleaseAsync()
        {
            _ = Interlocked.Increment(ref ReleaseCount);
            Record(RaceEvent.ResourceReleaseStarted);
            _started.TrySetResult();

            await _mayComplete.Task;

            Record(RaceEvent.ResourceReleaseCompleted);
        }

        internal void Record(RaceEvent step)
        {
            lock (_gate)
            {
                _timeline.Add(step);
            }
        }

        internal List<RaceEvent> Snapshot()
        {
            lock (_gate)
            {
                return [.. _timeline];
            }
        }
    }

    /// <summary>
    /// Asserts the one ordering a correct disposal must produce, and the three it must never.
    /// </summary>
    private static void AssertRaceOrdering(List<RaceEvent> timeline)
    {
        Assert.Equal(
            [
                RaceEvent.DisposeStarted,
                RaceEvent.ResourceReleaseStarted,
                RaceEvent.ResourceReleaseCompleted,
                RaceEvent.DisposeCompleted,
            ],
            timeline);

        // Restated as the impossibilities the gate names, so a future reordering fails loudly.
        Assert.True(
            timeline.IndexOf(RaceEvent.ResourceReleaseStarted) > timeline.IndexOf(RaceEvent.DisposeStarted),
            "The owned resource must not be released before disposal was requested.");
        Assert.True(
            timeline.IndexOf(RaceEvent.DisposeCompleted) > timeline.IndexOf(RaceEvent.ResourceReleaseCompleted),
            "DisposeAsync must not complete before the owned resource release did.");
        Assert.Single(timeline, step => step == RaceEvent.ResourceReleaseStarted);
    }

    // --- R1-04A: a failing capture racing disposal ------------------------------------------------

    [Fact]
    public async Task AFailingCaptureStillInFlight_HoldsTheOwnedResourceReleaseUntilItsLeaseIsFreed()
    {
        var timeline = new List<RaceEvent>();
        var gate = new Lock();
        var resource = new ObservableOwnedResource(timeline, gate);

        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new InvalidOperationException("D001 primary");

        ProviderStatementGateway gateway = HealthyGateway();
        _ = gateway
            .BeforeStatementAwait(PostgreSqlSqlStatementId.ReadTableSnapshots, async () =>
            {
                // The capture is genuinely in flight here, with its lease still held.
                admitted.TrySetResult();
                await proceed.Task;
            })
            .FailingAt(PostgreSqlSqlStatementId.ReadTableSnapshots, failure);

        var scope = new FakeInspectionSessionScope(gateway);
        var factory = new TrackedScopeFactory(scope);

        var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            factory,
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default,
            releaseResourceAsync: resource.ReleaseAsync);

        Task<DatabaseSnapshot> capture = provider.CaptureAsync(TestContext.Current.CancellationToken);
        await admitted.Task;

        resource.Record(RaceEvent.DisposeStarted);
        Task disposal = provider.DisposeAsync().AsTask();

        // Disposal has started but cannot proceed, and the owned resource is untouched.
        Assert.False(disposal.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref resource.ReleaseCount));

        // No new capture may be admitted from this moment on, and none reaches a scope.
        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => provider.CaptureAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, factory.CreatedScopes);

        proceed.TrySetResult();

        // The capture's own primary failure is preserved, not replaced by disposal.
        InvalidOperationException observed = await Assert.ThrowsAsync<InvalidOperationException>(() => capture);
        Assert.Same(failure, observed);

        // Only after the lease was freed does the owned release begin — and exactly once. The
        // zero count asserted above, taken while the capture was verifiably still in flight, is
        // what proves the release waited for the lease.

        await resource.ReleaseStarted;
        Assert.Equal(1, Volatile.Read(ref resource.ReleaseCount));

        // While that release is held open, disposal must still be pending.
        Assert.False(disposal.IsCompleted);

        resource.AllowReleaseCompletion();
        await disposal;
        resource.Record(RaceEvent.DisposeCompleted);

        Assert.Equal(1, Volatile.Read(ref resource.ReleaseCount));
        Assert.True(scope.AllCleanupStepsAttempted);
        AssertRaceOrdering(resource.Snapshot());
    }

    // --- R1-04B: a cancelled capture racing disposal ------------------------------------------------

    [Fact]
    public async Task ACancelledCaptureStillInFlight_HoldsTheOwnedResourceReleaseAndKeepsItsToken()
    {
        var timeline = new List<RaceEvent>();
        var gate = new Lock();
        var resource = new ObservableOwnedResource(timeline, gate);

        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var proceed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cts = new CancellationTokenSource();
        ProviderStatementGateway gateway = HealthyGateway();

        _ = gateway.BeforeStatementAwait(PostgreSqlSqlStatementId.ReadTableSnapshots, async () =>
        {
            admitted.TrySetResult();
            await proceed.Task;

            // Cancellation is requested by the caller, from inside the stage under test.
            cts.Token.ThrowIfCancellationRequested();
        });

        var scope = new FakeInspectionSessionScope(gateway);
        var factory = new TrackedScopeFactory(scope);

        var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            factory,
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default,
            releaseResourceAsync: resource.ReleaseAsync);

        Task<DatabaseSnapshot> capture = provider.CaptureAsync(cts.Token);
        await admitted.Task;

        resource.Record(RaceEvent.DisposeStarted);
        Task disposal = provider.DisposeAsync().AsTask();

        // Disposal does not cancel the admitted capture: it is still running, disposal waits, and
        // the owned resource has not been touched.
        Assert.False(disposal.IsCompleted);
        Assert.False(capture.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref resource.ReleaseCount));

        await cts.CancelAsync();
        proceed.TrySetResult();

        OperationCanceledException cancellation =
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => capture);

        // The caller's own token, not a substituted or linked one.
        Assert.Equal(cts.Token, cancellation.CancellationToken);

        // As above: the release could not have begun earlier, because the count was zero while the
        // capture still held its lease.
        await resource.ReleaseStarted;
        Assert.Equal(1, Volatile.Read(ref resource.ReleaseCount));
        Assert.False(disposal.IsCompleted);

        resource.AllowReleaseCompletion();
        await disposal;
        resource.Record(RaceEvent.DisposeCompleted);

        // No late admission slipped in behind the disposal.
        Assert.Equal(1, factory.CreatedScopes);
        Assert.Equal(1, Volatile.Read(ref resource.ReleaseCount));
        Assert.True(scope.AllCleanupStepsAttempted);
        AssertRaceOrdering(resource.Snapshot());
    }

    // --- R3-01: the public factories stay bound to the real owned resource -------------------------

    [Fact]
    public async Task APubliclyCreatedProvider_OwnsTheRealConnectionFactoryDisposal()
    {
        // No server is contacted by construction. This asserts that the delegate the lifecycle
        // will invoke is genuinely PostgreSqlConnectionFactory.DisposeAsync over the factory this
        // provider created — never the test double or a no-op.
        await using PostgreSqlDatabaseSnapshotProvider provider = PostgreSqlDatabaseSnapshotProvider.Create(
            "Host=localhost;Database=x;Username=u;Password=p");

        object? factory = typeof(PostgreSqlDatabaseSnapshotProvider)
            .GetField("_connectionFactory", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(provider);

        Assert.NotNull(factory);
        Assert.IsType<PostgreSqlConnectionFactory>(factory);

        var release = (Func<ValueTask>)typeof(PostgreSqlDatabaseSnapshotProvider)
            .GetField("_releaseResourceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(provider)!;

        // The delegate targets that same factory instance and is its DisposeAsync.
        Assert.Same(factory, release.Target);
        Assert.Equal(nameof(IAsyncDisposable.DisposeAsync), release.Method.Name);
    }

    [Fact]
    public void TheFourArgumentFactory_AlsoOwnsTheRealConnectionFactoryDisposal()
    {
        PostgreSqlDatabaseSnapshotProvider provider = PostgreSqlDatabaseSnapshotProvider.Create(
            "Host=localhost;Database=x;Username=u;Password=p", [], [], TimeSpan.FromSeconds(30));

        var release = (Func<ValueTask>)typeof(PostgreSqlDatabaseSnapshotProvider)
            .GetField("_releaseResourceAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(provider)!;

        Assert.IsType<PostgreSqlConnectionFactory>(release.Target);
        Assert.Equal(nameof(IAsyncDisposable.DisposeAsync), release.Method.Name);
    }

    // --- R1-04C: after the last query, before composition ---------------------------------------------

    [Fact]
    public async Task CancellationAsE002Completes_StopsBeforeComposition()
    {
        using var cts = new CancellationTokenSource();
        ProviderStatementGateway gateway = HealthyGateway();
        var composed = false;

        // E002 is the last query when statistics are available. Its reader disposal is the moment
        // the whole index operation has finished reading.
        _ = gateway.AfterReaderDisposed(PostgreSqlSqlStatementId.ReadIndexUsageStatistics, () => cts.Cancel());

        var scope = new FakeInspectionSessionScope(gateway);

        var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope,
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default,
            afterCompose: () => composed = true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        // Every query ran, and composition was never entered.
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadTableSnapshots));
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexMetadata));
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexUsageStatistics));
        Assert.False(composed);

        Assert.True(scope.AllCleanupStepsAttempted);
        Assert.True(scope.TransactionDisposedBeforeConnection);
    }

    [Fact]
    public async Task CancellationAsE001Completes_StopsBeforeComposition_WhenStatisticsAreUnavailable()
    {
        using var cts = new CancellationTokenSource();
        ProviderStatementGateway gateway = HealthyGateway();
        gateway.UsageStatisticsAvailable = false;
        var composed = false;

        // With statistics unavailable, E001 is the final query.
        _ = gateway.AfterReaderDisposed(PostgreSqlSqlStatementId.ReadIndexMetadata, () => cts.Cancel());

        var scope = new FakeInspectionSessionScope(gateway);

        var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope,
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default,
            afterCompose: () => composed = true);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexMetadata));
        Assert.Equal(0, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexUsageStatistics));
        Assert.False(composed);
        Assert.True(scope.AllCleanupStepsAttempted);
    }

    // --- R1-04D: after composition, before the callback returns -----------------------------------------

    [Fact]
    public async Task CancellationAfterComposition_DiscardsTheAlreadyBuiltSnapshot()
    {
        using var cts = new CancellationTokenSource();
        ProviderStatementGateway gateway = HealthyGateway();
        var composed = false;

        var scope = new FakeInspectionSessionScope(gateway);

        var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope,
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default,
            afterCompose: () =>
            {
                // Composition has already produced a snapshot at this point.
                composed = true;
                cts.Cancel();
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.CaptureAsync(cts.Token));

        // The three facts that make this checkpoint distinct from the previous one: composition
        // did run, a snapshot was built locally, and it was still not returned.
        Assert.True(composed);
        Assert.Equal(1, gateway.CountOf(PostgreSqlSqlStatementId.ReadIndexUsageStatistics));
        Assert.True(scope.AllCleanupStepsAttempted);
        Assert.True(scope.TransactionDisposedBeforeConnection);
    }

    [Fact]
    public async Task CancellationAfterComposition_IsCaughtInsideTheTransaction_NotOnlyAfterCleanup()
    {
        // Distinguishes the in-transaction checkpoint from the post-cleanup one, which the previous
        // test alone cannot: with the checkpoint present, the cancellation becomes the callback's
        // primary failure and therefore outranks the rollback failure. Were the checkpoint absent,
        // the callback would return normally and the rollback failure would surface instead.
        using var cts = new CancellationTokenSource();
        var cleanupFailure = new InvalidOperationException("rollback failed");

        ProviderStatementGateway gateway = HealthyGateway();
        var scope = new FakeInspectionSessionScope(gateway);
        _ = scope.FailingAt(SessionScopeStep.Rollback, cleanupFailure);

        var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope,
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default,
            afterCompose: cts.Cancel);

        Exception observed = await Assert.ThrowsAnyAsync<Exception>(
            () => provider.CaptureAsync(cts.Token));

        Assert.IsAssignableFrom<OperationCanceledException>(observed);
        Assert.NotSame(cleanupFailure, observed);
        Assert.True(scope.AllCleanupStepsAttempted);

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task WithoutCancellation_TheComposedSnapshotIsReturned()
    {
        // The control for the two tests above: the same seam runs, nothing cancels, and the
        // snapshot reaches the caller.
        ProviderStatementGateway gateway = HealthyGateway();
        var composed = false;

        await using var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            new FakeInspectionSessionScope(gateway),
            PostgreSqlSchemaFilter.IncludeEverything,
            PostgreSqlInspectionSessionOptions.Default,
            afterCompose: () => composed = true);

        DatabaseSnapshot snapshot = await provider.CaptureAsync(TestContext.Current.CancellationToken);

        Assert.True(composed);
        Assert.Single(snapshot.Tables);
        Assert.Single(snapshot.Indexes);
    }

    // --- R1-04E: a cleanup failure is not displaced by a later cancellation --------------------------------

    [Fact]
    public async Task ACleanupFailure_IsNotReplacedByACancellationRequestedAfterwards()
    {
        using var cts = new CancellationTokenSource();
        var cleanupFailure = new InvalidOperationException("rollback failed");

        ProviderStatementGateway gateway = HealthyGateway();
        var scope = new FakeInspectionSessionScope(gateway);

        // All product work succeeds; rollback then fails and becomes authoritative. The caller
        // cancels only afterwards, from a later cleanup step.
        _ = scope
            .FailingAt(SessionScopeStep.Rollback, cleanupFailure)
            .BeforeStep(SessionScopeStep.DisposeConnection, cts.Cancel);

        var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope, PostgreSqlSchemaFilter.IncludeEverything, PostgreSqlInspectionSessionOptions.Default);

        InvalidOperationException observed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CaptureAsync(cts.Token));

        // The cleanup failure that was captured first stays authoritative: the post-cleanup
        // checkpoint must not overwrite it with a cancellation.
        Assert.Same(cleanupFailure, observed);
        Assert.True(cts.IsCancellationRequested);
        Assert.True(scope.AllCleanupStepsAttempted);

        await provider.DisposeAsync();
    }

    [Fact]
    public async Task ATransactionDisposalFailure_IsNotReplacedByACancellationRequestedAfterwards()
    {
        using var cts = new CancellationTokenSource();
        var cleanupFailure = new InvalidOperationException("transaction disposal failed");

        ProviderStatementGateway gateway = HealthyGateway();
        var scope = new FakeInspectionSessionScope(gateway);

        _ = scope
            .FailingAt(SessionScopeStep.DisposeTransaction, cleanupFailure)
            .BeforeStep(SessionScopeStep.DisposeConnection, cts.Cancel);

        var provider = PostgreSqlDatabaseSnapshotProvider.CreateForTesting(
            scope, PostgreSqlSchemaFilter.IncludeEverything, PostgreSqlInspectionSessionOptions.Default);

        InvalidOperationException observed = await Assert.ThrowsAsync<InvalidOperationException>(
            () => provider.CaptureAsync(cts.Token));

        Assert.Same(cleanupFailure, observed);
        Assert.True(scope.AllCleanupStepsAttempted);

        await provider.DisposeAsync();
    }
}
