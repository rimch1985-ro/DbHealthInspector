using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.PostgreSql.Sessions;

/// <summary>
/// The restricted view handed to an authorized operation. It is deliberately <b>not</b> a
/// <see cref="PostgreSqlSqlExecutor"/>: the callback must not be able to re-run the statements
/// that established and verified the session.
/// </summary>
/// <remarks>
/// <para>
/// GC-DHI-04B-C1 (F-02). Giving the callback the full executor let it re-issue
/// <c>SET TRANSACTION READ ONLY</c>, change the transaction-local timeouts after they had already
/// been verified, or repeat the verification query — each of which would let the callback move
/// the session out from under the guarantees the runner just established.
/// </para>
/// <para>
/// GC-DHI-04C replaces the previous generic, ID-dispatching method with one <b>typed</b> method
/// per authorized operation. B001–B003 are therefore not merely rejected at run time: there is no
/// longer any surface through which a caller could name them at all, and no overload accepts a
/// statement ID, a SQL string or arbitrary parameters.
/// </para>
/// <para>
/// The view exposes no connection, no transaction, no command, no raw SQL and no property or
/// method returning the executor it wraps.
/// </para>
/// </remarks>
internal sealed class PostgreSqlInspectionOperationExecutor
{
    private readonly PostgreSqlSqlExecutor _executor;

    internal PostgreSqlInspectionOperationExecutor(PostgreSqlSqlExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        _executor = executor;
    }

    /// <summary>
    /// C001 — reads the server's numeric version, database name and current user.
    /// </summary>
    internal ValueTask<PostgreSqlServerIdentity> ReadServerIdentityAsync(CancellationToken cancellationToken) =>
        _executor.ReadServerIdentityAsync(cancellationToken);

    /// <summary>
    /// C002 — checks whether the required catalog-metadata allowlist is readable.
    /// </summary>
    internal ValueTask<bool> CheckCatalogMetadataAccessAsync(CancellationToken cancellationToken) =>
        _executor.CheckCatalogMetadataAccessAsync(cancellationToken);

    /// <summary>
    /// C003 — checks whether the optional usage-statistics views are readable.
    /// </summary>
    internal ValueTask<bool> CheckUsageStatisticsAccessAsync(CancellationToken cancellationToken) =>
        _executor.CheckUsageStatisticsAccessAsync(cancellationToken);

    /// <summary>
    /// C004 — reads the nullable statistics-reset timestamp.
    /// </summary>
    internal ValueTask<DateTimeOffset?> ReadStatisticsResetAsync(CancellationToken cancellationToken) =>
        _executor.ReadStatisticsResetAsync(cancellationToken);
}
