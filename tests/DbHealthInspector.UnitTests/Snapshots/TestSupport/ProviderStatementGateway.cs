using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sql.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Snapshots.TestSupport;

/// <summary>
/// A deterministic gateway double that answers the <b>whole</b> productive surface — B001–B003,
/// C001–C004, D001 and E001/E002 — so the provider's real composition path runs end to end with no
/// server, socket, thread or sleep.
/// </summary>
/// <remarks>
/// Deliberately separate from the 04B <c>ScriptedStatementGateway</c>, which only scripts session
/// initialization: extending that one would have changed a seam every earlier gate depends on.
/// </remarks>
internal sealed class ProviderStatementGateway : IPostgreSqlStatementGateway
{
    private readonly Dictionary<PostgreSqlSqlStatementId, Exception> _failures = [];
    private readonly Dictionary<PostgreSqlSqlStatementId, Action> _beforeStatement = [];
    private readonly Dictionary<PostgreSqlSqlStatementId, Func<Task>> _beforeStatementAsync = [];
    private readonly Dictionary<PostgreSqlSqlStatementId, Action> _afterReaderDisposed = [];
    private readonly Dictionary<PostgreSqlSqlStatementId, Exception> _readerDisposalFailures = [];

    /// <summary>Every statement the provider actually executed, in order.</summary>
    internal List<PostgreSqlSqlStatementId> ExecutedIds { get; } = [];

    /// <summary>The exact token handed to each execution.</summary>
    internal List<CancellationToken> Tokens { get; } = [];

    /// <summary>Every schema filter value bound, so filter identity can be proven.</summary>
    internal List<IReadOnlyList<string>> BoundIncludedSchemas { get; } = [];

    internal int ServerVersionNumber { get; set; } = 180004;

    internal string DatabaseName { get; set; } = "synthetic_db";

    internal string CurrentUser { get; set; } = "synthetic_user";

    internal bool CatalogMetadataAvailable { get; set; } = true;

    internal bool UsageStatisticsAvailable { get; set; } = true;

    internal DateTimeOffset? StatisticsResetAtUtc { get; set; } = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>D001 rows, each already in the frozen ten-column order.</summary>
    internal List<object?[]> TableRows { get; } = [];

    /// <summary>E001 rows, each already in the frozen thirty-one-column order.</summary>
    internal List<object?[]> IndexRows { get; } = [];

    /// <summary>E002 rows, each already in the frozen four-column order.</summary>
    internal List<object?[]> StatisticsRows { get; } = [];

    internal ProviderStatementGateway FailingAt(PostgreSqlSqlStatementId id, Exception failure)
    {
        _failures[id] = failure;
        return this;
    }

    /// <summary>
    /// Makes C004 fail with the exact SQLSTATE the approved 04C degradation policy recognises, so
    /// the <b>provider-level</b> outcome of that path can be observed end to end.
    /// </summary>
    /// <remarks>
    /// The provider contains no <c>PostgresException</c> or SQLSTATE handling of its own; the
    /// authority remains <c>ProbeAsync</c>. This only reproduces what the server would send.
    /// </remarks>
    internal ProviderStatementGateway WithStatisticsResetPermissionDenied()
    {
        _failures[PostgreSqlSqlStatementId.ReadStatisticsReset] = InsufficientPrivilege();
        return this;
    }

    /// <summary>Builds a real <c>42501</c> <see cref="PostgresException"/>.</summary>
    private static PostgresException InsufficientPrivilege() =>
        new(
            messageText: "permission denied for view pg_stat_database",
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: "42501");

    /// <summary>
    /// Runs a callback from inside the seam immediately before a statement's scripted outcome, so
    /// a cancellation is raised from the stage under test rather than before the capture starts.
    /// </summary>
    internal ProviderStatementGateway BeforeStatement(PostgreSqlSqlStatementId id, Action action)
    {
        _beforeStatement[id] = action;
        return this;
    }

    /// <summary>
    /// Awaits a task from inside the seam before a statement runs, so a capture can be held
    /// genuinely in flight — with its lease still held — while another thread starts disposal.
    /// </summary>
    internal ProviderStatementGateway BeforeStatementAwait(PostgreSqlSqlStatementId id, Func<Task> gate)
    {
        _beforeStatementAsync[id] = gate;
        return this;
    }

    /// <summary>
    /// Runs a callback when a statement's reader is disposed — that is, once the whole operation
    /// has finished reading. Cancelling here lands the token between the last query and the
    /// provider's next checkpoint, which no "before statement" hook can express.
    /// </summary>
    internal ProviderStatementGateway AfterReaderDisposed(PostgreSqlSqlStatementId id, Action action)
    {
        _afterReaderDisposed[id] = action;
        return this;
    }

    /// <summary>Makes a specific statement's reader fail on disposal.</summary>
    internal ProviderStatementGateway WithReaderDisposalFailure(PostgreSqlSqlStatementId id, Exception failure)
    {
        _readerDisposalFailures[id] = failure;
        return this;
    }

    internal int CountOf(PostgreSqlSqlStatementId id) => ExecutedIds.Count(executed => executed == id);

    public async ValueTask ExecuteNonQueryAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        await InvokeBeforeAsync(statement.Id);
        InvokeBefore(statement.Id);
        Record(statement, cancellationToken);

        if (_failures.TryGetValue(statement.Id, out Exception? failure))
        {
            throw failure;
        }
    }

    public async ValueTask<IPostgreSqlRowReader> ExecuteReaderAsync(
        PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        await InvokeBeforeAsync(statement.Id);
        InvokeBefore(statement.Id);
        Record(statement, cancellationToken);

        if (_failures.TryGetValue(statement.Id, out Exception? failure))
        {
            throw failure;
        }

        FakeRowReader reader = BuildReader(statement.Id);

        if (_readerDisposalFailures.TryGetValue(statement.Id, out Exception? disposalFailure))
        {
            reader.DisposeFailure = disposalFailure;
        }

        return _afterReaderDisposed.TryGetValue(statement.Id, out Action? onDisposed)
            ? new DisposalObservingRowReader(reader, onDisposed)
            : reader;
    }

    private FakeRowReader BuildReader(PostgreSqlSqlStatementId id) => id switch
    {
        PostgreSqlSqlStatementId.ApplyLocalTimeouts => FakeRowReader.ConfigurationRow(),
        PostgreSqlSqlStatementId.VerifySessionState => FakeRowReader.VerificationRow(),
        PostgreSqlSqlStatementId.ReadServerIdentity =>
            FakeRowReader.WithRows(3, [ServerVersionNumber, DatabaseName, CurrentUser]),
        PostgreSqlSqlStatementId.CheckCatalogMetadataAccess =>
            FakeRowReader.WithRows(1, [CatalogMetadataAvailable]),
        PostgreSqlSqlStatementId.CheckUsageStatisticsAccess =>
            FakeRowReader.WithRows(1, [UsageStatisticsAvailable]),
        PostgreSqlSqlStatementId.ReadStatisticsReset =>
            FakeRowReader.WithRows(1, [StatisticsResetAtUtc]),
        PostgreSqlSqlStatementId.ReadTableSnapshots =>
            FakeRowReader.WithRows(10, [.. TableRows]),
        PostgreSqlSqlStatementId.ReadIndexMetadata =>
            FakeRowReader.WithRows(31, [.. IndexRows]),
        PostgreSqlSqlStatementId.ReadIndexUsageStatistics =>
            FakeRowReader.WithRows(4, [.. StatisticsRows]),
        _ => FakeRowReader.Empty(),
    };

    private void Record(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        ExecutedIds.Add(statement.Id);
        Tokens.Add(cancellationToken);

        if (statement.Id is PostgreSqlSqlStatementId.ReadTableSnapshots
            or PostgreSqlSqlStatementId.ReadIndexMetadata
            or PostgreSqlSqlStatementId.ReadIndexUsageStatistics)
        {
            BoundIncludedSchemas.Add(statement.Parameters[0].TextArrayValue);
        }
    }

    private void InvokeBefore(PostgreSqlSqlStatementId id)
    {
        if (_beforeStatement.TryGetValue(id, out Action? action))
        {
            action();
        }
    }

    private async Task InvokeBeforeAsync(PostgreSqlSqlStatementId id)
    {
        if (_beforeStatementAsync.TryGetValue(id, out Func<Task>? gate))
        {
            await gate();
        }
    }

    // --- Row builders -------------------------------------------------------------------------

    /// <summary>One well-formed D001 row in the frozen ten-column order.</summary>
    internal static object?[] TableRow(
        string schema = "public",
        string table = "orders",
        string relkind = "r",
        string persistence = "p",
        bool isPartition = false,
        long? estimate = 0L,
        long tableSize = 8192L,
        long indexSize = 0L,
        long totalSize = 8192L,
        bool hasPrimaryKey = false) =>
        [schema, table, relkind, persistence, isPartition, estimate, tableSize, indexSize, totalSize, hasPrimaryKey];

    /// <summary>One well-formed single-key E001 row in the frozen thirty-one-column order.</summary>
    internal static object?[] IndexRow(
        string schema = "public",
        string table = "orders",
        string index = "orders_a_idx",
        string accessMethod = "btree",
        string relationKind = "i",
        bool isIndexPartition = false,
        int attributeCount = 1,
        int keyAttributeCount = 1,
        int position = 1,
        bool isKey = true,
        string? columnName = "a",
        string? expression = null,
        string? collationSchema = null,
        string? collationName = null,
        string? opclassSchema = "pg_catalog",
        string? opclassName = "text_ops",
        string?[]? opclassOptions = null,
        bool? orderable = true,
        bool? ascending = true,
        bool? descending = false,
        bool? nullsFirst = false,
        bool? nullsLast = true,
        string? predicate = null,
        bool isUnique = false,
        bool? nullsNotDistinct = null,
        bool isPrimaryKey = false,
        bool backsConstraint = false,
        bool isValid = true,
        bool isReady = true,
        bool isLive = true,
        long sizeBytes = 8192L) =>
        [
            schema, table, index, accessMethod, relationKind, isIndexPartition,
            attributeCount, keyAttributeCount, position, isKey, columnName, expression,
            collationSchema, collationName, opclassSchema, opclassName, opclassOptions,
            orderable, ascending, descending, nullsFirst, nullsLast, predicate,
            isUnique, nullsNotDistinct, isPrimaryKey, backsConstraint, isValid, isReady, isLive,
            sizeBytes,
        ];

    /// <summary>One well-formed E002 row in the frozen four-column order.</summary>
    internal static object?[] StatisticsRow(
        string schema = "public",
        string table = "orders",
        string index = "orders_a_idx",
        long scanCount = 7L) =>
        [schema, table, index, scanCount];
}

/// <summary>
/// A passive reader decorator that reports when the reader it wraps is disposed — the moment the
/// operation owning it has finished reading. Changes no value and forwards disposal unchanged.
/// </summary>
internal sealed class DisposalObservingRowReader : IPostgreSqlRowReader
{
    private readonly IPostgreSqlRowReader _inner;
    private readonly Action _onDisposed;

    internal DisposalObservingRowReader(IPostgreSqlRowReader inner, Action onDisposed)
    {
        _inner = inner;
        _onDisposed = onDisposed;
    }

    public int FieldCount => _inner.FieldCount;

    public ValueTask<bool> ReadAsync(CancellationToken cancellationToken) => _inner.ReadAsync(cancellationToken);

    public bool IsNull(int ordinal) => _inner.IsNull(ordinal);

    public bool GetBoolean(int ordinal) => _inner.GetBoolean(ordinal);

    public string GetString(int ordinal) => _inner.GetString(ordinal);

    public int GetInt32(int ordinal) => _inner.GetInt32(ordinal);

    public long GetInt64(int ordinal) => _inner.GetInt64(ordinal);

    public string[] GetStringArray(int ordinal) => _inner.GetStringArray(ordinal);

    public DateTimeOffset GetDateTimeOffset(int ordinal) => _inner.GetDateTimeOffset(ordinal);

    public async ValueTask DisposeAsync()
    {
        await _inner.DisposeAsync();

        // After the inner disposal, so the observed point is genuinely "the operation is done".
        _onDisposed();
    }
}
