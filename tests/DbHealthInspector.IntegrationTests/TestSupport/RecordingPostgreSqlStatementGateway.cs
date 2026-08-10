using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.IntegrationTests.TestSupport;

/// <summary>
/// A passive, <b>test-only</b> observer wrapped around the real
/// <see cref="NpgsqlStatementGateway"/>, so a server-backed test can assert which statements a
/// probe actually executed against PostgreSQL and what the server actually answered — rather than
/// inferring it from the final result (GC-DHI-04C-C1, R1-06).
/// </summary>
/// <remarks>
/// <para>
/// It delegates every execution verbatim to the gateway it wraps, returns exactly the rows that
/// gateway produced, and forwards disposal unchanged. It changes no value, reorders nothing,
/// suppresses no exception and injects no failure: removing it would leave the observed sequence
/// identical.
/// </para>
/// <para>
/// It accepts only an already-resolved <see cref="PostgreSqlPreparedStatement"/> — the same narrow
/// seam production uses — so it opens no raw-SQL path, and it exposes no connection, transaction,
/// command or connection string. This type lives only in the IntegrationTests assembly and is
/// never referenced by the product.
/// </para>
/// </remarks>
internal sealed class RecordingPostgreSqlStatementGateway : IPostgreSqlStatementGateway
{
    private readonly IPostgreSqlStatementGateway _inner;
    private readonly List<PostgreSqlSqlStatementId> _executed = [];

    internal RecordingPostgreSqlStatementGateway(IPostgreSqlStatementGateway inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        _inner = inner;
    }

    /// <summary>
    /// Every statement handed to the gateway, in execution order. Recorded before delegating, so a
    /// statement that failed still counts as attempted — which makes "C004 was never executed" a
    /// genuinely strong claim.
    /// </summary>
    /// <remarks>Returns a copy: a caller can never mutate the recorded sequence.</remarks>
    internal IReadOnlyList<PostgreSqlSqlStatementId> ExecutedStatements => _executed.ToArray();

    /// <summary>
    /// The boolean C003 actually read from the server, or <see langword="null"/> when C003 never
    /// ran. This is the server's own answer, observed at the row seam, not a value inferred from
    /// the composed capability result.
    /// </summary>
    internal bool? ObservedUsageStatisticsAvailable { get; private set; }

    public ValueTask ExecuteNonQueryAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statement);

        _executed.Add(statement.Id);
        return _inner.ExecuteNonQueryAsync(statement, cancellationToken);
    }

    public async ValueTask<IPostgreSqlRowReader> ExecuteReaderAsync(
        PostgreSqlPreparedStatement statement,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statement);

        _executed.Add(statement.Id);

        IPostgreSqlRowReader reader = await _inner.ExecuteReaderAsync(statement, cancellationToken);

        // Only C003's answer is observed. Every other statement is handed back its own reader
        // untouched, so no extra wrapper sits in the path of the statements under test.
        return statement.Id == PostgreSqlSqlStatementId.CheckUsageStatisticsAccess
            ? new RecordingPostgreSqlRowReader(reader, value => ObservedUsageStatisticsAvailable = value)
            : reader;
    }
}

/// <summary>
/// A passive, test-only observer around one real <see cref="IPostgreSqlRowReader"/>: it reports
/// the boolean read from ordinal 0 and otherwise behaves exactly like the reader it wraps,
/// including disposal.
/// </summary>
internal sealed class RecordingPostgreSqlRowReader : IPostgreSqlRowReader
{
    private readonly IPostgreSqlRowReader _inner;
    private readonly Action<bool> _observeBoolean;

    internal RecordingPostgreSqlRowReader(IPostgreSqlRowReader inner, Action<bool> observeBoolean)
    {
        _inner = inner;
        _observeBoolean = observeBoolean;
    }

    public int FieldCount => _inner.FieldCount;

    public ValueTask<bool> ReadAsync(CancellationToken cancellationToken) => _inner.ReadAsync(cancellationToken);

    public bool IsNull(int ordinal) => _inner.IsNull(ordinal);

    public bool GetBoolean(int ordinal)
    {
        bool value = _inner.GetBoolean(ordinal);

        if (ordinal == 0)
        {
            _observeBoolean(value);
        }

        // The observed value is returned exactly as the server produced it.
        return value;
    }

    public string GetString(int ordinal) => _inner.GetString(ordinal);

    public int GetInt32(int ordinal) => _inner.GetInt32(ordinal);

    public long GetInt64(int ordinal) => _inner.GetInt64(ordinal);

    public DateTimeOffset GetDateTimeOffset(int ordinal) => _inner.GetDateTimeOffset(ordinal);

    // Disposal is forwarded unchanged: the wrapper owns nothing of its own, so it must neither
    // swallow a disposal failure nor dispose twice.
    public ValueTask DisposeAsync() => _inner.DisposeAsync();
}
