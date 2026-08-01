using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.PostgreSql.Sessions;

/// <summary>
/// The resource lifecycle of one inspection session, expressed as discrete steps so the runner
/// can classify a failure by the stage that produced it and so unit tests can inject a failure at
/// any single step deterministically.
/// </summary>
/// <remarks>
/// Production is <see cref="PostgreSqlInspectionSessionScope"/>, which wraps the GC-DHI-04A
/// connection factory and real Npgsql transaction. This is an infrastructure seam, not a
/// test-only path: the runner has exactly one code path and always drives it through this
/// interface.
/// </remarks>
internal interface IPostgreSqlInspectionSessionScope
{
    /// <summary>
    /// Opens the connection through the GC-DHI-04A factory.
    /// </summary>
    ValueTask OpenConnectionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Begins the single transaction at <see cref="System.Data.IsolationLevel.RepeatableRead"/>.
    /// </summary>
    ValueTask BeginTransactionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Creates the executor bound to the open connection and live transaction.
    /// </summary>
    PostgreSqlSqlExecutor CreateExecutor();

    /// <summary>
    /// Rolls the transaction back explicitly. Always called with
    /// <see cref="CancellationToken.None"/> by the runner, so a canceled caller token can never
    /// prevent the rollback from being attempted.
    /// </summary>
    ValueTask RollbackAsync();

    /// <summary>
    /// Disposes the transaction. Always before <see cref="DisposeConnectionAsync"/>.
    /// </summary>
    ValueTask DisposeTransactionAsync();

    /// <summary>
    /// Disposes the connection, returning it to the pool.
    /// </summary>
    ValueTask DisposeConnectionAsync();
}

/// <summary>
/// Creates a fresh <see cref="IPostgreSqlInspectionSessionScope"/> per run.
/// </summary>
internal interface IPostgreSqlInspectionSessionScopeFactory
{
    /// <summary>
    /// Creates a scope. Allocates no server resource by itself; the scope's own steps do.
    /// </summary>
    IPostgreSqlInspectionSessionScope Create();
}
