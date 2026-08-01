using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sql.TestSupport;

namespace DbHealthInspector.UnitTests.Sessions.TestSupport;

/// <summary>
/// The stages of a session scope, recorded in the order the runner drove them.
/// </summary>
internal enum SessionScopeStep
{
    OpenConnection,
    BeginTransaction,
    CreateExecutor,
    Rollback,
    DisposeTransaction,
    DisposeConnection,
}

/// <summary>
/// A deterministic <see cref="IPostgreSqlInspectionSessionScope"/> double: records every step in
/// order, can be scripted to fail at any single step, and hands the runner a <b>real</b>
/// <see cref="PostgreSqlSqlExecutor"/> backed by a scripted gateway so the genuine B001 → B002 →
/// B003 code path executes. No server, socket, thread or sleep is involved.
/// </summary>
internal sealed class FakeInspectionSessionScope : IPostgreSqlInspectionSessionScope, IPostgreSqlInspectionSessionScopeFactory
{
    private readonly Dictionary<SessionScopeStep, Exception> _failures = [];
    private readonly Dictionary<SessionScopeStep, Action> _beforeStep = [];
    private readonly ScriptedStatementGateway _gateway;

    internal FakeInspectionSessionScope(ScriptedStatementGateway? gateway = null)
    {
        _gateway = gateway ?? ScriptedStatementGateway.HealthySession();
    }

    internal List<SessionScopeStep> Steps { get; } = [];

    internal ScriptedStatementGateway Gateway => _gateway;

    internal FakeInspectionSessionScope FailingAt(SessionScopeStep step, Exception failure)
    {
        _failures[step] = failure;
        return this;
    }

    /// <summary>
    /// Runs a callback from inside a lifecycle step, immediately before that step's scripted
    /// outcome — used to cancel the caller's token from the stage under test rather than before
    /// the runner is ever entered (GC-DHI-04B-C1, F-08).
    /// </summary>
    internal FakeInspectionSessionScope BeforeStep(SessionScopeStep step, Action action)
    {
        _beforeStep[step] = action;
        return this;
    }

    public IPostgreSqlInspectionSessionScope Create() => this;

    /// <summary>The exact token the runner handed to <c>OpenConnectionAsync</c>.</summary>
    internal CancellationToken OpenConnectionCancellationToken { get; private set; }

    /// <summary>The exact token the runner handed to <c>BeginTransactionAsync</c>.</summary>
    internal CancellationToken BeginTransactionCancellationToken { get; private set; }

    public ValueTask OpenConnectionAsync(CancellationToken cancellationToken)
    {
        OpenConnectionCancellationToken = cancellationToken;
        return Record(SessionScopeStep.OpenConnection);
    }

    public ValueTask BeginTransactionAsync(CancellationToken cancellationToken)
    {
        BeginTransactionCancellationToken = cancellationToken;
        return Record(SessionScopeStep.BeginTransaction);
    }

    public PostgreSqlSqlExecutor CreateExecutor()
    {
        Steps.Add(SessionScopeStep.CreateExecutor);
        if (_failures.TryGetValue(SessionScopeStep.CreateExecutor, out Exception? failure))
        {
            throw failure;
        }

        return new PostgreSqlSqlExecutor(new PostgreSqlSqlInventory(), _gateway);
    }

    public ValueTask RollbackAsync() => Record(SessionScopeStep.Rollback);

    public ValueTask DisposeTransactionAsync() => Record(SessionScopeStep.DisposeTransaction);

    public ValueTask DisposeConnectionAsync() => Record(SessionScopeStep.DisposeConnection);

    private ValueTask Record(SessionScopeStep step)
    {
        if (_beforeStep.TryGetValue(step, out Action? before))
        {
            before();
        }

        Steps.Add(step);
        return _failures.TryGetValue(step, out Exception? failure)
            ? ValueTask.FromException(failure)
            : ValueTask.CompletedTask;
    }

    /// <summary>
    /// Whether every cleanup step ran, regardless of which of them failed.
    /// </summary>
    internal bool AllCleanupStepsAttempted =>
        Steps.Contains(SessionScopeStep.Rollback)
            && Steps.Contains(SessionScopeStep.DisposeTransaction)
            && Steps.Contains(SessionScopeStep.DisposeConnection);

    /// <summary>
    /// Whether the transaction was released before the connection.
    /// </summary>
    internal bool TransactionDisposedBeforeConnection =>
        Steps.IndexOf(SessionScopeStep.DisposeTransaction) >= 0
            && Steps.IndexOf(SessionScopeStep.DisposeTransaction) < Steps.IndexOf(SessionScopeStep.DisposeConnection);
}

/// <summary>
/// A gateway double that answers per statement id, so a single named statement can be made to
/// fail or to return a specific verification row while the others behave normally.
/// </summary>
internal sealed class ScriptedStatementGateway : IPostgreSqlStatementGateway
{
    private readonly Dictionary<PostgreSqlSqlStatementId, Exception> _failures = [];
    private readonly Dictionary<PostgreSqlSqlStatementId, Action> _beforeStatement = [];
    private Func<FakeRowReader>? _verificationRow;
    private Action? _beforeVerification;

    internal List<PostgreSqlSqlStatementId> ExecutedIds { get; } = [];

    internal List<CancellationToken> Tokens { get; } = [];

    internal static ScriptedStatementGateway HealthySession() => new();

    internal ScriptedStatementGateway FailingAt(PostgreSqlSqlStatementId id, Exception failure)
    {
        _failures[id] = failure;
        return this;
    }

    internal ScriptedStatementGateway WithVerificationState(
        bool isReadOnly = true,
        string isolationLevel = "repeatable read",
        bool statementMatches = true,
        bool lockMatches = true,
        bool idleMatches = true)
    {
        _verificationRow = () => FakeRowReader.VerificationRow(isReadOnly, isolationLevel, statementMatches, lockMatches, idleMatches);
        return this;
    }

    internal ScriptedStatementGateway WithNoVerificationRow()
    {
        _verificationRow = () => FakeRowReader.Empty(5);
        return this;
    }

    /// <summary>
    /// Runs a callback immediately before B003 executes — used to cancel the caller's token
    /// mid-initialization without any timing dependency.
    /// </summary>
    internal ScriptedStatementGateway BeforeVerification(Action action)
    {
        _beforeVerification = action;
        return this;
    }

    /// <summary>
    /// Runs a callback from inside the seam for a specific statement, immediately before that
    /// statement's scripted outcome. This is how a cancellation is raised from the stage under
    /// test rather than before the runner is ever entered (GC-DHI-04B-C1, F-08).
    /// </summary>
    internal ScriptedStatementGateway BeforeStatement(PostgreSqlSqlStatementId id, Action action)
    {
        _beforeStatement[id] = action;
        return this;
    }

    public ValueTask ExecuteNonQueryAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        InvokeBeforeStatement(statement.Id);

        ExecutedIds.Add(statement.Id);
        Tokens.Add(cancellationToken);

        return _failures.TryGetValue(statement.Id, out Exception? failure)
            ? ValueTask.FromException(failure)
            : ValueTask.CompletedTask;
    }

    public ValueTask<IPostgreSqlRowReader> ExecuteReaderAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        if (statement.Id == PostgreSqlSqlStatementId.VerifySessionState)
        {
            _beforeVerification?.Invoke();
        }

        InvokeBeforeStatement(statement.Id);

        ExecutedIds.Add(statement.Id);
        Tokens.Add(cancellationToken);

        if (_failures.TryGetValue(statement.Id, out Exception? failure))
        {
            return ValueTask.FromException<IPostgreSqlRowReader>(failure);
        }

        IPostgreSqlRowReader reader = statement.Id switch
        {
            PostgreSqlSqlStatementId.ApplyLocalTimeouts => FakeRowReader.ConfigurationRow(),
            PostgreSqlSqlStatementId.VerifySessionState => (_verificationRow ?? (() => FakeRowReader.VerificationRow()))(),
            _ => FakeRowReader.Empty(),
        };

        return ValueTask.FromResult(reader);
    }

    private void InvokeBeforeStatement(PostgreSqlSqlStatementId id)
    {
        if (_beforeStatement.TryGetValue(id, out Action? action))
        {
            action();
        }
    }
}
