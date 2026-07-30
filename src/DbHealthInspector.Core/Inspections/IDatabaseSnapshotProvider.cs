using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Core.Inspections;

/// <summary>
/// Captures a single, engine-neutral <see cref="DatabaseSnapshot"/> for an inspection.
/// </summary>
/// <remarks>
/// This contract carries no engine-specific concept: no Npgsql type, no connection string, no
/// SQL and no logging. A concrete PostgreSQL implementation belongs to
/// <c>DbHealthInspector.PostgreSql</c> in a future gate; this gate defines the contract only.
/// <see cref="InspectionOrchestrator"/> calls <see cref="CaptureAsync"/> exactly once per
/// inspection, passing through the same <see cref="CancellationToken"/> it received. Any
/// exception — including <see cref="OperationCanceledException"/> — thrown by an implementation
/// propagates unchanged: there is no meaningful partial inspection result without a snapshot.
/// </remarks>
public interface IDatabaseSnapshotProvider
{
    /// <summary>
    /// Captures a database snapshot.
    /// </summary>
    /// <param name="cancellationToken">
    /// The token to observe while capturing the snapshot. Implementations must propagate
    /// <see cref="OperationCanceledException"/> rather than translating it into another
    /// exception type or a null result.
    /// </param>
    Task<DatabaseSnapshot> CaptureAsync(CancellationToken cancellationToken);
}
