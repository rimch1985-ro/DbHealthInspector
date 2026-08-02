using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sql.TestSupport;

namespace DbHealthInspector.UnitTests.Capabilities.TestSupport;

/// <summary>
/// Builds a real <see cref="PostgreSqlInspectionOperationExecutor"/> over a scripted gateway, so
/// the probe drives the genuine typed operations, the genuine inventory and the genuine shape
/// contracts — only the server's answers are scripted.
/// </summary>
/// <remarks>
/// No mocking library, no server, no threads and no sleeps. Every statement the probe issues is
/// recorded in order, so "C004 was not executed" is observable rather than assumed.
/// </remarks>
internal sealed class ProbeScript : IPostgreSqlStatementGateway
{
    private readonly Dictionary<PostgreSqlSqlStatementId, Func<FakeRowReader>> _rows = [];
    private readonly Dictionary<PostgreSqlSqlStatementId, Exception> _failures = [];
    private readonly Dictionary<PostgreSqlSqlStatementId, Action> _beforeStatement = [];

    internal List<PostgreSqlSqlStatementId> ExecutedIds { get; } = [];

    internal List<CancellationToken> Tokens { get; } = [];

    internal int CountOf(PostgreSqlSqlStatementId id) => ExecutedIds.Count(executed => executed == id);

    internal PostgreSqlInspectionOperationExecutor View() =>
        new(new PostgreSqlSqlExecutor(new PostgreSqlSqlInventory(), this));

    /// <summary>
    /// The default healthy server: PostgreSQL 18.4, catalog and statistics both readable, and a
    /// reset timestamp.
    /// </summary>
    internal static ProbeScript Healthy(
        int serverVersionNumber = 180004,
        string databaseName = "synthetic_db",
        string currentUser = "synthetic_role",
        bool catalogAvailable = true,
        bool statisticsAvailable = true,
        DateTimeOffset? statisticsReset = null)
    {
        var script = new ProbeScript();
        script.WithIdentity(serverVersionNumber, databaseName, currentUser);
        script.WithCatalogAccess(catalogAvailable);
        script.WithStatisticsAccess(statisticsAvailable);
        script.WithStatisticsReset(statisticsReset ?? new DateTimeOffset(2026, 8, 1, 9, 30, 0, TimeSpan.Zero));
        return script;
    }

    internal ProbeScript WithIdentity(int serverVersionNumber, string databaseName, string currentUser)
    {
        _rows[PostgreSqlSqlStatementId.ReadServerIdentity] =
            () => FakeRowReader.WithRows(3, [serverVersionNumber, databaseName, currentUser]);
        return this;
    }

    internal ProbeScript WithCatalogAccess(bool available)
    {
        _rows[PostgreSqlSqlStatementId.CheckCatalogMetadataAccess] = () => FakeRowReader.WithRows(1, [available]);
        return this;
    }

    internal ProbeScript WithStatisticsAccess(bool available)
    {
        _rows[PostgreSqlSqlStatementId.CheckUsageStatisticsAccess] = () => FakeRowReader.WithRows(1, [available]);
        return this;
    }

    internal ProbeScript WithStatisticsReset(DateTimeOffset? reset)
    {
        _rows[PostgreSqlSqlStatementId.ReadStatisticsReset] = () => FakeRowReader.WithRows(1, [reset]);
        return this;
    }

    internal ProbeScript WithRawRows(PostgreSqlSqlStatementId id, Func<FakeRowReader> rows)
    {
        _rows[id] = rows;
        return this;
    }

    internal ProbeScript FailingAt(PostgreSqlSqlStatementId id, Exception failure)
    {
        _failures[id] = failure;
        return this;
    }

    /// <summary>
    /// Runs a callback from inside the seam for a statement, immediately before its scripted
    /// outcome — used to cancel the caller's token from the stage under test.
    /// </summary>
    internal ProbeScript BeforeStatement(PostgreSqlSqlStatementId id, Action action)
    {
        _beforeStatement[id] = action;
        return this;
    }

    public ValueTask ExecuteNonQueryAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        Record(statement.Id, cancellationToken);
        return _failures.TryGetValue(statement.Id, out Exception? failure)
            ? ValueTask.FromException(failure)
            : ValueTask.CompletedTask;
    }

    public ValueTask<IPostgreSqlRowReader> ExecuteReaderAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        Record(statement.Id, cancellationToken);

        if (_failures.TryGetValue(statement.Id, out Exception? failure))
        {
            return ValueTask.FromException<IPostgreSqlRowReader>(failure);
        }

        return _rows.TryGetValue(statement.Id, out Func<FakeRowReader>? rows)
            ? ValueTask.FromResult<IPostgreSqlRowReader>(rows())
            : ValueTask.FromResult<IPostgreSqlRowReader>(FakeRowReader.Empty(1));
    }

    private void Record(PostgreSqlSqlStatementId id, CancellationToken cancellationToken)
    {
        if (_beforeStatement.TryGetValue(id, out Action? before))
        {
            before();
        }

        ExecutedIds.Add(id);
        Tokens.Add(cancellationToken);
    }
}
