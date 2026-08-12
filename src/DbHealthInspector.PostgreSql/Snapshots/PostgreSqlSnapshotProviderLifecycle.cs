namespace DbHealthInspector.PostgreSql.Snapshots;

/// <summary>
/// The admission lease that coordinates concurrent captures with one asynchronous disposal.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately explicit rather than a race delegated to the data source: a capture is either
/// admitted before disposal begins — in which case disposal waits for it — or rejected outright.
/// There is no window in which a capture proceeds against a disposed resource, and an admitted
/// capture is never cancelled by disposal.
/// </para>
/// <para>
/// Disposal is a <b>single logical operation</b> that spans draining the in-flight captures and
/// releasing the owned resource. Every caller of <see cref="DisposeAsync"/> awaits that one
/// operation and observes its one outcome, so a second caller can never return while the first is
/// still releasing the resource, and a release failure is seen by all of them rather than by
/// whichever caller happened to run it (GC-DHI-04F-C1, R1-01).
/// </para>
/// <para>
/// Not a semaphore around captures: captures never wait for one another, only disposal waits for
/// captures. The lock guards a small counter and two fields, is never held across an <c>await</c>,
/// and nothing spins, blocks or runs sync-over-async.
/// </para>
/// </remarks>
internal sealed class PostgreSqlSnapshotProviderLifecycle
{
    private readonly Lock _gate = new();

    /// <summary>Completes when every admitted capture has released its lease.</summary>
    private readonly TaskCompletionSource _drained =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>
    /// Completes when the whole disposal — drain <b>and</b> resource release — has finished, with
    /// the outcome every disposer observes.
    /// </summary>
    private readonly TaskCompletionSource _disposed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _inFlight;
    private bool _disposalStarted;

    /// <summary>
    /// Whether disposal has begun. Once true, no further capture is admitted.
    /// </summary>
    internal bool IsDisposalStarted
    {
        get
        {
            lock (_gate)
            {
                return _disposalStarted;
            }
        }
    }

    /// <summary>The number of admitted captures that have not yet released their lease.</summary>
    internal int InFlightCount
    {
        get
        {
            lock (_gate)
            {
                return _inFlight;
            }
        }
    }

    /// <summary>
    /// Admits one capture.
    /// </summary>
    /// <remarks>
    /// The admission decision and the counter increment happen under one lock, so a capture is
    /// either fully admitted before disposal starts or rejected: there is no interleaving in which
    /// disposal observes an in-flight count that misses an admitted capture.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">Disposal has already begun.</exception>
    internal void Admit(string objectName)
    {
        lock (_gate)
        {
            // CA1513 suggests ObjectDisposedException.ThrowIf, but each of its overloads derives
            // the object name from Type.FullName. The observable name here is frozen as the simple
            // type name, so the explicit constructor is the required form rather than a style
            // choice.
#pragma warning disable CA1513
            if (_disposalStarted)
            {
                throw new ObjectDisposedException(objectName);
            }
#pragma warning restore CA1513

            _inFlight++;
        }
    }

    /// <summary>
    /// Releases one admitted capture's lease. Never throws and never cancels.
    /// </summary>
    internal void Release()
    {
        bool drained;

        lock (_gate)
        {
            _inFlight--;

            // Signalled only when a disposer is already waiting, and only by the capture that
            // empties the queue, so the drain completes exactly once.
            drained = _inFlight == 0 && _disposalStarted;
        }

        if (drained)
        {
            _ = _drained.TrySetResult();
        }
    }

    /// <summary>
    /// Performs the one disposal operation, or awaits the one already in progress.
    /// </summary>
    /// <param name="releaseResourceAsync">
    /// Releases the owned resource. Invoked <b>exactly once</b>, by the first disposer only, and
    /// only after every admitted capture has released its lease.
    /// </param>
    /// <remarks>
    /// Idempotent and safe under concurrent calls. Every caller awaits the same completion, so all
    /// of them return only after the resource has actually been released and all of them observe
    /// the same failure if releasing it threw.
    /// </remarks>
    internal async ValueTask DisposeAsync(Func<ValueTask> releaseResourceAsync)
    {
        bool firstDisposer;
        bool alreadyDrained;

        lock (_gate)
        {
            firstDisposer = !_disposalStarted;
            _disposalStarted = true;
            alreadyDrained = _inFlight == 0;
        }

        if (alreadyDrained)
        {
            // Nothing in flight, so no releasing capture will raise the drain signal.
            _ = _drained.TrySetResult();
        }

        if (firstDisposer)
        {
            try
            {
                await _drained.Task.ConfigureAwait(false);
                await releaseResourceAsync().ConfigureAwait(false);

                _ = _disposed.TrySetResult();
            }
            catch (Exception exception)
            {
                // Published to every disposer, not just this one. The release is not retried by
                // anybody else: the outcome recorded here is final.
                _ = _disposed.TrySetException(exception);
            }
        }

        // Including the first disposer: awaiting the shared completion is what makes every caller
        // observe the same outcome rather than only the one that ran the release.
        await _disposed.Task.ConfigureAwait(false);
    }
}
