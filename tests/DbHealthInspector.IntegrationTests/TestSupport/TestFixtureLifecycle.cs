using System.Runtime.ExceptionServices;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// The shared, <b>test-only</b> lifecycle contract every container fixture initializes through:
/// initialization is bounded by its own deadline, and a failure part-way through always releases
/// what was already started without ever replacing the failure that explains it
/// (GC-DHI-04C-C1, R1-05).
/// </summary>
/// <remarks>
/// <para>
/// Without this, a fixture that started its container and then failed during administrative setup
/// would depend on the runner eventually disposing it. Here, cleanup is attempted immediately, on
/// its own bounded budget, and its outcome — success, failure or timeout — never changes what the
/// caller sees.
/// </para>
/// <para>
/// This type lives only in the IntegrationTests assembly. It is deliberately delegate-driven so
/// every branch can be proven with fakes, without provoking a destructive failure against real
/// Docker.
/// </para>
/// </remarks>
internal static class TestFixtureLifecycle
{
    /// <summary>
    /// The independent budget for a whole fixture initialization: container start, administrative
    /// setup, role/schema/table creation, ACL changes and privilege verification.
    /// </summary>
    internal static readonly TimeSpan InitializationDeadline = TimeSpan.FromSeconds(120);

    /// <summary>
    /// The budget for releasing a partially started fixture after initialization failed.
    /// </summary>
    internal static readonly TimeSpan CleanupDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// The budget every server-backed permission-loss test body gets, excluding fixture
    /// initialization.
    /// </summary>
    internal static readonly TimeSpan TestDeadline = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Runs <paramref name="initialize"/> under its own deadline. On any failure,
    /// <paramref name="cleanup"/> is attempted immediately under a separate deadline and the
    /// original failure is re-thrown with its stack intact.
    /// </summary>
    /// <param name="initialize">The full initialization, which must honour the token it is given.</param>
    /// <param name="cleanup">
    /// The release path. Must tolerate being called when nothing, or only part, of the
    /// initialization completed, and must be safe to call again from normal disposal.
    /// </param>
    /// <param name="cancellationToken">The runner's token, which the deadline is linked to.</param>
    /// <param name="initializationDeadline">Overrides <see cref="InitializationDeadline"/>.</param>
    /// <param name="cleanupDeadline">Overrides <see cref="CleanupDeadline"/>.</param>
    internal static async ValueTask InitializeGuardedAsync(
        Func<CancellationToken, ValueTask> initialize,
        Func<ValueTask> cleanup,
        CancellationToken cancellationToken,
        TimeSpan? initializationDeadline = null,
        TimeSpan? cleanupDeadline = null)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        ArgumentNullException.ThrowIfNull(cleanup);

        // Linked, so the runner can still stop the suite early, but with an independent ceiling so
        // a runner that never cancels cannot leave initialization running forever.
        using var initializationScope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        initializationScope.CancelAfter(initializationDeadline ?? InitializationDeadline);

        ExceptionDispatchInfo? primary;
        try
        {
            await initialize(initializationScope.Token);
            return;
        }
        catch (Exception exception)
        {
            // Transparent capture only: the failure is not inspected, classified, sanitized,
            // wrapped or rewritten, and a deadline surfaces as the framework's own neutral
            // OperationCanceledException rather than a message this helper invents.
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        await AttemptBoundedCleanupAsync(cleanup, cleanupDeadline ?? CleanupDeadline);

        primary.Throw();
    }

    /// <summary>
    /// Attempts cleanup within a bounded time. A cleanup failure is discarded and a cleanup that
    /// overruns is abandoned — in both cases the caller's primary failure is what surfaces, and
    /// nothing about the container reaches the caller.
    /// </summary>
    private static async ValueTask AttemptBoundedCleanupAsync(Func<ValueTask> cleanup, TimeSpan deadline)
    {
        Task cleanupTask;
        try
        {
            cleanupTask = cleanup().AsTask();
        }
        catch (Exception)
        {
            // Thrown synchronously before returning a task. Discarded: see below.
            return;
        }

        // Whatever happens, the abandoned task's failure is observed so it can never resurface as
        // an unobserved task exception in an unrelated test.
        _ = cleanupTask.ContinueWith(
            static task => _ = task.Exception,
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        Task completed = await Task.WhenAny(cleanupTask, Task.Delay(deadline));

        if (!ReferenceEquals(completed, cleanupTask))
        {
            // Overran its budget. Abandoned rather than awaited, so a hung release cannot hold the
            // suite open; the primary failure is still the one reported.
            return;
        }

        try
        {
            await cleanupTask;
        }
        catch (Exception)
        {
            // Discarded on purpose. A cleanup failure explains nothing about why initialization
            // failed, and it must never replace, wrap or decorate the primary failure — it is not
            // attached as an inner exception and nothing is added to Data either.
        }
    }
}
