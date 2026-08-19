using DbHealthInspector.PostgreSql.Snapshots;

namespace DbHealthInspector.UnitTests.Snapshots;

/// <summary>
/// The provider lifecycle contract (GC-DHI-04F §9–§10, corrected by C1 R1-01): disposal is one
/// logical operation spanning the capture drain <b>and</b> the resource release, and every caller
/// awaits that same operation and observes its same outcome.
/// </summary>
/// <remarks>
/// Every race below is driven by explicit task gates. There is no sleep, no polling and no timing
/// assumption anywhere in this suite.
/// </remarks>
public sealed class PostgreSqlSnapshotProviderLifecycleTests
{
    private const string ObjectName = "PostgreSqlDatabaseSnapshotProvider";

    /// <summary>Counts releases and can be held open or made to fail, deterministically.</summary>
    private sealed class ResourceRelease
    {
        private readonly TaskCompletionSource _allowed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int Count;

        internal Exception? Failure { get; set; }

        /// <summary>When true, the release blocks until <see cref="Allow"/> is called.</summary>
        internal bool Gated { get; init; }

        internal void Allow() => _allowed.TrySetResult();

        internal async ValueTask ReleaseAsync()
        {
            if (Gated)
            {
                await _allowed.Task;
            }

            _ = Interlocked.Increment(ref Count);

            if (Failure is { } failure)
            {
                throw failure;
            }
        }
    }

    // --- A: disposal waits for an admitted capture ------------------------------------------------

    [Fact]
    public async Task DisposalWaitsForAnAdmittedCapture_ThenReleasesTheResource()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var release = new ResourceRelease();

        lifecycle.Admit(ObjectName);

        ValueTask disposal = lifecycle.DisposeAsync(release.ReleaseAsync);
        Task disposalTask = disposal.AsTask();

        // The capture is still in flight, so nothing may have been released yet.
        Assert.False(disposalTask.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref release.Count));
        Assert.Equal(1, lifecycle.InFlightCount);

        lifecycle.Release();

        await disposalTask;

        Assert.Equal(1, Volatile.Read(ref release.Count));
        Assert.Equal(0, lifecycle.InFlightCount);
    }

    // --- B: disposal wins the race against admission ------------------------------------------------

    [Fact]
    public async Task WhenDisposalStartsFirst_ANewCaptureIsRejectedAndNothingIsOpened()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var release = new ResourceRelease();

        await lifecycle.DisposeAsync(release.ReleaseAsync);

        ObjectDisposedException exception = Assert.Throws<ObjectDisposedException>(() => lifecycle.Admit(ObjectName));

        Assert.Equal(ObjectName, exception.ObjectName);
        Assert.Equal(0, lifecycle.InFlightCount);
        Assert.Equal(1, Volatile.Read(ref release.Count));
    }

    // --- C: disposal waits for every admitted capture -----------------------------------------------

    [Fact]
    public async Task DisposalWaitsForEveryAdmittedCapture_NotJustTheFirst()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var release = new ResourceRelease();

        lifecycle.Admit(ObjectName);
        lifecycle.Admit(ObjectName);

        Task disposalTask = lifecycle.DisposeAsync(release.ReleaseAsync).AsTask();

        lifecycle.Release();

        // One capture remains, so the resource must still be held.
        Assert.False(disposalTask.IsCompleted);
        Assert.Equal(0, Volatile.Read(ref release.Count));
        Assert.Equal(1, lifecycle.InFlightCount);

        lifecycle.Release();

        await disposalTask;

        Assert.Equal(1, Volatile.Read(ref release.Count));
    }

    // --- D: concurrent disposers share one operation ------------------------------------------------

    [Fact]
    public async Task EveryConcurrentDisposer_AwaitsTheSameCompleteDisposal()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var release = new ResourceRelease { Gated = true };

        lifecycle.Admit(ObjectName);

        Task[] disposers =
        [
            lifecycle.DisposeAsync(release.ReleaseAsync).AsTask(),
            lifecycle.DisposeAsync(release.ReleaseAsync).AsTask(),
            lifecycle.DisposeAsync(release.ReleaseAsync).AsTask(),
        ];

        lifecycle.Release();

        // The release is gated open, so no disposer may have returned: this is the exact defect
        // R1-01 named -- a second caller returning while the first is still releasing.
        Assert.All(disposers, disposer => Assert.False(disposer.IsCompleted));

        release.Allow();
        await Task.WhenAll(disposers);

        // Released exactly once, by the first disposer only.
        Assert.Equal(1, Volatile.Read(ref release.Count));
    }

    [Fact]
    public async Task ASecondDisposer_NeverReturnsBeforeTheResourceIsReleased()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var release = new ResourceRelease { Gated = true };

        Task first = lifecycle.DisposeAsync(release.ReleaseAsync).AsTask();
        Task second = lifecycle.DisposeAsync(release.ReleaseAsync).AsTask();

        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);

        release.Allow();
        await Task.WhenAll(first, second);

        Assert.Equal(1, Volatile.Read(ref release.Count));
    }

    // --- E/F: a failing or cancelled capture still releases its lease ---------------------------------

    [Fact]
    public async Task AFailedCapture_StillReleasesItsLeaseSoDisposalCompletes()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var release = new ResourceRelease();

        lifecycle.Admit(ObjectName);

        try
        {
            throw new InvalidOperationException("capture failed");
        }
        catch (InvalidOperationException)
        {
            lifecycle.Release();
        }

        await lifecycle.DisposeAsync(release.ReleaseAsync);

        Assert.Equal(1, Volatile.Read(ref release.Count));
        Assert.Equal(0, lifecycle.InFlightCount);
    }

    [Fact]
    public async Task ACancelledCapture_StillReleasesItsLeaseSoDisposalCompletes()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var release = new ResourceRelease();

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        lifecycle.Admit(ObjectName);

        try
        {
            cts.Token.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            lifecycle.Release();
        }

        await lifecycle.DisposeAsync(release.ReleaseAsync);

        Assert.Equal(1, Volatile.Read(ref release.Count));
    }

    // --- Shared failure propagation -------------------------------------------------------------------

    [Fact]
    public async Task WhenTheResourceReleaseFails_EveryDisposerObservesTheSameFailure()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var failure = new InvalidOperationException("release failed");
        var release = new ResourceRelease { Gated = true, Failure = failure };

        Task[] disposers =
        [
            lifecycle.DisposeAsync(release.ReleaseAsync).AsTask(),
            lifecycle.DisposeAsync(release.ReleaseAsync).AsTask(),
            lifecycle.DisposeAsync(release.ReleaseAsync).AsTask(),
        ];

        release.Allow();

        foreach (Task disposer in disposers)
        {
            InvalidOperationException observed =
                await Assert.ThrowsAsync<InvalidOperationException>(() => disposer);

            // The very same exception instance, not a re-thrown copy per caller.
            Assert.Same(failure, observed);
        }

        // Attempted once. No caller retried it independently.
        Assert.Equal(1, Volatile.Read(ref release.Count));
    }

    [Fact]
    public async Task AFailedDisposal_IsNotRetriedByALaterCaller()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var failure = new InvalidOperationException("release failed");
        var release = new ResourceRelease { Failure = failure };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.DisposeAsync(release.ReleaseAsync).AsTask());

        // A later caller observes the recorded outcome rather than running the release again.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => lifecycle.DisposeAsync(release.ReleaseAsync).AsTask());

        Assert.Equal(1, Volatile.Read(ref release.Count));
    }

    [Fact]
    public async Task DisposalIsIdempotentAcrossManySequentialCalls()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var release = new ResourceRelease();

        for (var attempt = 0; attempt < 5; attempt++)
        {
            await lifecycle.DisposeAsync(release.ReleaseAsync);
        }

        Assert.Equal(1, Volatile.Read(ref release.Count));
        Assert.True(lifecycle.IsDisposalStarted);
    }

    // --- Bounded deterministic stress ------------------------------------------------------------------

    [Fact]
    public async Task UnderManyConcurrentDisposers_TheResourceIsReleasedExactlyOnce()
    {
        var lifecycle = new PostgreSqlSnapshotProviderLifecycle();
        var release = new ResourceRelease { Gated = true };

        lifecycle.Admit(ObjectName);
        lifecycle.Admit(ObjectName);

        Task[] disposers = [.. Enumerable.Range(0, 16)
            .Select(_ => lifecycle.DisposeAsync(release.ReleaseAsync).AsTask())];

        lifecycle.Release();
        lifecycle.Release();
        release.Allow();

        await Task.WhenAll(disposers);

        Assert.Equal(1, Volatile.Read(ref release.Count));
        Assert.All(disposers, disposer => Assert.True(disposer.IsCompletedSuccessfully));
    }
}
