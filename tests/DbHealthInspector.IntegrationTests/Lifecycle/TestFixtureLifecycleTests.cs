using DbHealthInspector.IntegrationTests.TestSupport;

namespace DbHealthInspector.IntegrationTests.Lifecycle;

/// <summary>
/// The fixture lifecycle contract, proven deterministically with fakes: no Docker, no container,
/// no network, no sleeps used as synchronisation (GC-DHI-04C-C1, R1-05 and R1-07).
/// </summary>
/// <remarks>
/// These are deliberately <b>not</b> in the <c>PostgreSqlServer</c> category: forcing a real
/// container to fail part-way through initialization would be both destructive and unreliable,
/// and every branch is reachable through the helper's two delegates.
/// </remarks>
public sealed class TestFixtureLifecycleTests
{
    private static readonly TimeSpan ShortDeadline = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan GenerousDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// A stand-in for a container fixture: it records how far initialization got and how many
    /// times release was attempted.
    /// </summary>
    private sealed class FakeFixture
    {
        internal bool Started { get; private set; }

        internal int ReleaseCount { get; private set; }

        internal Exception? ReleaseFailure { get; set; }

        internal TaskCompletionSource? ReleaseGate { get; set; }

        internal ValueTask StartAsync()
        {
            Started = true;
            return ValueTask.CompletedTask;
        }

        internal async ValueTask ReleaseAsync()
        {
            ReleaseCount++;

            if (ReleaseGate is { } gate)
            {
                await gate.Task;
            }

            if (ReleaseFailure is { } failure)
            {
                throw failure;
            }
        }
    }

    private sealed class StartFailure : Exception
    {
        internal StartFailure()
            : base("start stage failed")
        {
        }
    }

    private sealed class SetupFailure : Exception
    {
        internal SetupFailure()
            : base("setup stage failed")
        {
        }
    }

    private sealed class VerificationFailure : Exception
    {
        internal VerificationFailure()
            : base("verification stage failed")
        {
        }
    }

    private sealed class CleanupFailure : Exception
    {
        internal CleanupFailure()
            : base("cleanup stage failed")
        {
        }
    }

    // --- The happy path -----------------------------------------------------------------------

    [Fact]
    public async Task SuccessfulInitialization_NeverAttemptsCleanup()
    {
        var fixture = new FakeFixture();

        await TestFixtureLifecycle.InitializeGuardedAsync(
            async _ => await fixture.StartAsync(),
            fixture.ReleaseAsync,
            TestContext.Current.CancellationToken,
            GenerousDeadline,
            GenerousDeadline);

        Assert.True(fixture.Started);
        Assert.Equal(0, fixture.ReleaseCount);
    }

    // --- Failure at each stage ----------------------------------------------------------------

    [Fact]
    public async Task StartFailure_BeforeTheContainerIsActive_SurfacesAndStillAttemptsCleanup()
    {
        var fixture = new FakeFixture();

        await Assert.ThrowsAsync<StartFailure>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                _ => throw new StartFailure(),
                fixture.ReleaseAsync,
                TestContext.Current.CancellationToken,
                GenerousDeadline,
                GenerousDeadline).AsTask());

        // Nothing started, but cleanup is still attempted: a release path must tolerate being
        // called when there is nothing to release.
        Assert.False(fixture.Started);
        Assert.Equal(1, fixture.ReleaseCount);
    }

    [Fact]
    public async Task SetupFailure_AfterStart_SurfacesAndReleasesImmediately()
    {
        var fixture = new FakeFixture();

        await Assert.ThrowsAsync<SetupFailure>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                async _ =>
                {
                    await fixture.StartAsync();
                    throw new SetupFailure();
                },
                fixture.ReleaseAsync,
                TestContext.Current.CancellationToken,
                GenerousDeadline,
                GenerousDeadline).AsTask());

        Assert.True(fixture.Started);
        Assert.Equal(1, fixture.ReleaseCount);
    }

    [Fact]
    public async Task VerificationFailure_SurfacesAndReleasesImmediately()
    {
        var fixture = new FakeFixture();

        await Assert.ThrowsAsync<VerificationFailure>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                async _ =>
                {
                    await fixture.StartAsync();
                    throw new VerificationFailure();
                },
                fixture.ReleaseAsync,
                TestContext.Current.CancellationToken,
                GenerousDeadline,
                GenerousDeadline).AsTask());

        Assert.True(fixture.Started);
        Assert.Equal(1, fixture.ReleaseCount);
    }

    // --- Cleanup never replaces the primary ---------------------------------------------------

    [Fact]
    public async Task CleanupFailure_NeverReplacesOrDecoratesThePrimaryFailure()
    {
        var fixture = new FakeFixture { ReleaseFailure = new CleanupFailure() };

        SetupFailure primary = await Assert.ThrowsAsync<SetupFailure>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                async _ =>
                {
                    await fixture.StartAsync();
                    throw new SetupFailure();
                },
                fixture.ReleaseAsync,
                TestContext.Current.CancellationToken,
                GenerousDeadline,
                GenerousDeadline).AsTask());

        Assert.Equal(1, fixture.ReleaseCount);
        Assert.Null(primary.InnerException);
        Assert.Empty(primary.Data);
    }

    [Fact]
    public async Task CleanupThatThrowsSynchronously_NeverReplacesThePrimaryFailure()
    {
        SetupFailure primary = await Assert.ThrowsAsync<SetupFailure>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                _ => throw new SetupFailure(),
                () => throw new CleanupFailure(),
                TestContext.Current.CancellationToken,
                GenerousDeadline,
                GenerousDeadline).AsTask());

        Assert.Null(primary.InnerException);
        Assert.Empty(primary.Data);
    }

    [Fact]
    public async Task CleanupThatOverrunsItsDeadline_IsAbandonedAndThePrimaryStillSurfaces()
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var fixture = new FakeFixture { ReleaseGate = gate, ReleaseFailure = new CleanupFailure() };

        try
        {
            SetupFailure primary = await Assert.ThrowsAsync<SetupFailure>(
                () => TestFixtureLifecycle.InitializeGuardedAsync(
                    async _ =>
                    {
                        await fixture.StartAsync();
                        throw new SetupFailure();
                    },
                    fixture.ReleaseAsync,
                    TestContext.Current.CancellationToken,
                    GenerousDeadline,
                    ShortDeadline).AsTask());

            // The helper returned without waiting for the hung release, and the primary is intact.
            Assert.Equal(1, fixture.ReleaseCount);
            Assert.Null(primary.InnerException);
            Assert.Empty(primary.Data);
        }
        finally
        {
            // Let the abandoned release finish and fail; the helper already observed it, so it can
            // never resurface as an unobserved task exception.
            gate.SetResult();
        }
    }

    [Fact]
    public async Task PrimaryFailure_KeepsItsOriginalStack()
    {
        var fixture = new FakeFixture();

        SetupFailure primary = await Assert.ThrowsAsync<SetupFailure>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                async _ =>
                {
                    await fixture.StartAsync();
                    throw new SetupFailure();
                },
                fixture.ReleaseAsync,
                TestContext.Current.CancellationToken,
                GenerousDeadline,
                GenerousDeadline).AsTask());

        // ExceptionDispatchInfo re-throws in place: the frame that actually threw is still there.
        Assert.NotNull(primary.StackTrace);
        Assert.Contains(nameof(PrimaryFailure_KeepsItsOriginalStack), primary.StackTrace, StringComparison.Ordinal);
    }

    // --- Deadlines -----------------------------------------------------------------------------

    [Fact]
    public async Task InitializationThatOverrunsItsDeadline_IsCanceledAndStillReleases()
    {
        var fixture = new FakeFixture();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                async token =>
                {
                    await fixture.StartAsync();

                    // Waits on the token rather than sleeping: the deadline is what ends this.
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                fixture.ReleaseAsync,
                TestContext.Current.CancellationToken,
                ShortDeadline,
                GenerousDeadline).AsTask());

        Assert.True(fixture.Started);
        Assert.Equal(1, fixture.ReleaseCount);
    }

    [Fact]
    public async Task InitializationDeadline_IsLinkedToTheRunnerToken()
    {
        var fixture = new FakeFixture();
        using var runner = new CancellationTokenSource();
        await runner.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                async token =>
                {
                    await fixture.StartAsync();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
                fixture.ReleaseAsync,
                runner.Token,
                GenerousDeadline,
                GenerousDeadline).AsTask());

        Assert.Equal(1, fixture.ReleaseCount);
    }

    [Fact]
    public void Deadlines_AreTheDocumentedBudgets()
    {
        Assert.Equal(TimeSpan.FromSeconds(120), TestFixtureLifecycle.InitializationDeadline);
        Assert.Equal(TimeSpan.FromSeconds(30), TestFixtureLifecycle.CleanupDeadline);
        Assert.Equal(TimeSpan.FromSeconds(30), TestFixtureLifecycle.TestDeadline);
    }

    // --- Argument contract ---------------------------------------------------------------------

    [Fact]
    public async Task NullDelegates_AreRejected()
    {
        var fixture = new FakeFixture();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                null!, fixture.ReleaseAsync, TestContext.Current.CancellationToken).AsTask());

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => TestFixtureLifecycle.InitializeGuardedAsync(
                _ => ValueTask.CompletedTask, null!, TestContext.Current.CancellationToken).AsTask());
    }
}
