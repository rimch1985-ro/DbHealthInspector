namespace DbHealthInspector.PostgreSql.Sessions;

/// <summary>
/// The effective transaction state read back by B003, immediately before any authorized
/// operation is allowed to run.
/// </summary>
/// <remarks>
/// Deliberately a plain sealed class rather than a <see langword="record"/>: a record's generated
/// <see cref="object.ToString"/> would turn this into an incidental diagnostic surface. It
/// carries only the five verification outcomes — never a setting's raw text, a connection
/// detail, a database or role name, or anything else read from the server.
/// </remarks>
internal sealed class PostgreSqlInspectionSessionState
{
    /// <summary>
    /// The exact value PostgreSQL reports for <c>transaction_isolation</c> when the transaction
    /// was begun at <see cref="System.Data.IsolationLevel.RepeatableRead"/>. Lowercase with a
    /// single space; compared ordinally. Verified directly against PostgreSQL 18.4.
    /// </summary>
    internal const string RepeatableReadIsolationLevel = "repeatable read";

    /// <summary>
    /// Whether <c>transaction_read_only</c> is effectively on.
    /// </summary>
    internal bool IsReadOnly { get; }

    /// <summary>
    /// The effective <c>transaction_isolation</c> value.
    /// </summary>
    internal string IsolationLevel { get; }

    /// <summary>
    /// Whether the effective <c>statement_timeout</c> equals the configured value.
    /// </summary>
    internal bool StatementTimeoutMatches { get; }

    /// <summary>
    /// Whether the effective <c>lock_timeout</c> equals the configured value.
    /// </summary>
    internal bool LockTimeoutMatches { get; }

    /// <summary>
    /// Whether the effective <c>idle_in_transaction_session_timeout</c> equals the configured
    /// value.
    /// </summary>
    internal bool IdleInTransactionTimeoutMatches { get; }

    internal PostgreSqlInspectionSessionState(
        bool isReadOnly,
        string isolationLevel,
        bool statementTimeoutMatches,
        bool lockTimeoutMatches,
        bool idleInTransactionTimeoutMatches)
    {
        ArgumentNullException.ThrowIfNull(isolationLevel);

        IsReadOnly = isReadOnly;
        IsolationLevel = isolationLevel;
        StatementTimeoutMatches = statementTimeoutMatches;
        LockTimeoutMatches = lockTimeoutMatches;
        IdleInTransactionTimeoutMatches = idleInTransactionTimeoutMatches;
    }

    /// <summary>
    /// Whether every verified condition holds. Anything less blocks the authorized operation.
    /// </summary>
    internal bool IsVerified =>
        IsReadOnly
            && string.Equals(IsolationLevel, RepeatableReadIsolationLevel, StringComparison.Ordinal)
            && StatementTimeoutMatches
            && LockTimeoutMatches
            && IdleInTransactionTimeoutMatches;
}
