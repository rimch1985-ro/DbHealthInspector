using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.UnitTests.TestSupport;

/// <summary>
/// A small, configurable <see cref="IDatabaseSnapshotProvider"/> fake for orchestration tests.
/// </summary>
internal sealed class FakeSnapshotProvider : IDatabaseSnapshotProvider
{
    private readonly Func<CancellationToken, Task<DatabaseSnapshot>> _capture;

    public int CallCount { get; private set; }

    public CancellationToken? LastReceivedToken { get; private set; }

    private FakeSnapshotProvider(Func<CancellationToken, Task<DatabaseSnapshot>> capture)
    {
        _capture = capture;
    }

    public static FakeSnapshotProvider Returning(DatabaseSnapshot snapshot) =>
        new(_ => Task.FromResult(snapshot));

    /// <summary>
    /// Simulates a badly-behaved provider returning <see langword="null"/> despite the
    /// non-nullable interface signature.
    /// </summary>
    public static FakeSnapshotProvider ReturningNull() =>
        new(_ => Task.FromResult<DatabaseSnapshot>(null!));

    public static FakeSnapshotProvider Throwing(Exception exception) =>
        new(_ => throw exception);

    /// <summary>
    /// Throws an <see cref="OperationCanceledException"/> carrying the token it receives.
    /// </summary>
    public static FakeSnapshotProvider Canceling() =>
        new(cancellationToken => throw new OperationCanceledException(cancellationToken));

    /// <summary>
    /// Cancels <paramref name="tokenSource"/> as a side effect, then returns
    /// <paramref name="snapshot"/> normally (without itself throwing), so the orchestrator's own
    /// post-snapshot cancellation check is what is being exercised, not the provider's.
    /// </summary>
    public static FakeSnapshotProvider CancelingSourceThenReturning(
        CancellationTokenSource tokenSource, DatabaseSnapshot snapshot) =>
        new(_ =>
        {
            tokenSource.Cancel();
            return Task.FromResult(snapshot);
        });

    public Task<DatabaseSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        CallCount++;
        LastReceivedToken = cancellationToken;
        return _capture(cancellationToken);
    }
}
