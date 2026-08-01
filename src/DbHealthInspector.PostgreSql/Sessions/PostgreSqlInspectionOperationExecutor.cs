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
/// B001, B002 and B003 are session-initialization statements owned exclusively by the runner, so
/// every one of them is rejected here. Because GC-DHI-04B inventories no operational statement at
/// all, <see cref="ExecuteAsync"/> currently rejects every id; GC-DHI-04C is where operational
/// statements — and the dispatch that runs them through the bound executor — will be introduced.
/// </para>
/// <para>
/// The view exposes no connection, no transaction, no command, no raw SQL and no property or
/// method returning the executor it wraps. Rejection carries the fixed
/// <see cref="PostgreSqlSqlSafetyException"/> message and never renders the rejected id or any
/// SQL.
/// </para>
/// </remarks>
internal sealed class PostgreSqlInspectionOperationExecutor
{
    /// <summary>
    /// The statements the runner owns. None of them may be executed by an authorized operation.
    /// </summary>
    private static readonly PostgreSqlSqlStatementId[] SessionInitializationStatements =
    [
        PostgreSqlSqlStatementId.SetTransactionReadOnly,
        PostgreSqlSqlStatementId.ApplyLocalTimeouts,
        PostgreSqlSqlStatementId.VerifySessionState,
    ];

    private readonly PostgreSqlSqlExecutor _executor;

    internal PostgreSqlInspectionOperationExecutor(PostgreSqlSqlExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(executor);

        _executor = executor;
    }

    /// <summary>
    /// Whether <paramref name="statementId"/> is one of the runner-owned initialization
    /// statements.
    /// </summary>
    internal static bool IsSessionInitializationStatement(PostgreSqlSqlStatementId statementId) =>
        Array.IndexOf(SessionInitializationStatements, statementId) >= 0;

    /// <summary>
    /// Runs an authorized operational statement.
    /// </summary>
    /// <exception cref="PostgreSqlSqlSafetyException">
    /// Always in GC-DHI-04B: <paramref name="statementId"/> is either a runner-owned
    /// initialization statement or an id with no operational statement behind it.
    /// </exception>
    internal ValueTask ExecuteAsync(
        PostgreSqlSqlStatementId statementId,
        IReadOnlyList<PostgreSqlSqlParameterValue> parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        cancellationToken.ThrowIfCancellationRequested();

        // A permanent rule: initialization statements belong to the runner alone.
        if (IsSessionInitializationStatement(statementId))
        {
            throw new PostgreSqlSqlSafetyException();
        }

        return ExecuteOperationalAsync(_executor, statementId, parameters, cancellationToken);
    }

    /// <summary>
    /// Dispatches a non-initialization statement to the bound session executor.
    /// </summary>
    /// <remarks>
    /// GC-DHI-04B's inventory is frozen at three initialization statements, so there is currently
    /// no operational statement for the bound executor to run and every remaining id is unknown
    /// to this view. GC-DHI-04C replaces this body with real dispatch.
    /// </remarks>
    private static ValueTask ExecuteOperationalAsync(
        PostgreSqlSqlExecutor executor,
        PostgreSqlSqlStatementId statementId,
        IReadOnlyList<PostgreSqlSqlParameterValue> parameters,
        CancellationToken cancellationToken)
    {
        _ = executor;
        _ = statementId;
        _ = parameters;
        _ = cancellationToken;

        throw new PostgreSqlSqlSafetyException();
    }
}
