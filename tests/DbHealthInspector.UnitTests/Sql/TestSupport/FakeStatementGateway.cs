using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.UnitTests.Sql.TestSupport;

/// <summary>
/// A deterministic <see cref="IPostgreSqlStatementGateway"/> double. No mocking library, no
/// network, no server, no threads and no sleeps: it records the prepared statement and token it
/// was handed, and returns a scripted reader or throws a scripted exception.
/// </summary>
internal sealed class FakeStatementGateway : IPostgreSqlStatementGateway
{
    private readonly Queue<FakeRowReader> _readers = new();
    private readonly Exception? _nonQueryFailure;
    private readonly Exception? _readerFailure;

    private FakeStatementGateway(Exception? nonQueryFailure, Exception? readerFailure)
    {
        _nonQueryFailure = nonQueryFailure;
        _readerFailure = readerFailure;
    }

    internal List<PostgreSqlPreparedStatement> Executed { get; } = [];

    internal List<CancellationToken> Tokens { get; } = [];

    internal int NonQueryCallCount { get; private set; }

    internal int ReaderCallCount { get; private set; }

    internal FakeRowReader? LastReader { get; private set; }

    /// <summary>
    /// Invoked at the start of <see cref="ExecuteReaderAsync"/>, before any reader is produced.
    /// Lets a test cancel the caller's token at the exact moment the command would be executing,
    /// with no sleep and no race.
    /// </summary>
    internal Action? BeforeExecuteReader { get; set; }

    internal static FakeStatementGateway Succeeding(params FakeRowReader[] readers)
    {
        var gateway = new FakeStatementGateway(null, null);
        foreach (FakeRowReader reader in readers)
        {
            gateway._readers.Enqueue(reader);
        }

        return gateway;
    }

    internal static FakeStatementGateway FailingNonQuery(Exception failure) => new(failure, null);

    internal static FakeStatementGateway FailingReader(Exception failure) => new(null, failure);

    public ValueTask ExecuteNonQueryAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        NonQueryCallCount++;
        Executed.Add(statement);
        Tokens.Add(cancellationToken);

        return _nonQueryFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(_nonQueryFailure);
    }

    public ValueTask<IPostgreSqlRowReader> ExecuteReaderAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        ReaderCallCount++;
        Executed.Add(statement);
        Tokens.Add(cancellationToken);

        // Runs after the call has been recorded but before a reader exists, so a test that
        // cancels here proves the command was reached and that no reader was ever acquired.
        BeforeExecuteReader?.Invoke();

        if (_readerFailure is not null)
        {
            return ValueTask.FromException<IPostgreSqlRowReader>(_readerFailure);
        }

        FakeRowReader reader = _readers.Count > 0 ? _readers.Dequeue() : FakeRowReader.Empty();
        LastReader = reader;
        return ValueTask.FromResult<IPostgreSqlRowReader>(reader);
    }
}

/// <summary>
/// A scripted <see cref="IPostgreSqlRowReader"/> over in-memory rows, so row-count, column-count
/// and NULL handling can be proven without a server.
/// </summary>
internal sealed class FakeRowReader : IPostgreSqlRowReader
{
    private readonly IReadOnlyList<object?[]> _rows;
    private int _index = -1;

    private FakeRowReader(IReadOnlyList<object?[]> rows, int fieldCount)
    {
        _rows = rows;
        FieldCount = fieldCount;
    }

    internal bool Disposed { get; private set; }

    internal List<CancellationToken> ReadTokens { get; } = [];

    /// <summary>
    /// When set, <see cref="DisposeAsync"/> throws this after recording that it ran, so the
    /// executor's "disposal must never mask the primary failure" contract can be exercised.
    /// </summary>
    internal Exception? DisposeFailure { get; set; }

    public int FieldCount { get; }

    internal static FakeRowReader Empty(int fieldCount = 3) => new([], fieldCount);

    internal static FakeRowReader WithRows(int fieldCount, params object?[][] rows) => new(rows, fieldCount);

    /// <summary>
    /// The shape B002 expects: one row of three columns.
    /// </summary>
    internal static FakeRowReader ConfigurationRow() => WithRows(3, ["30000ms", "5000ms", "60000ms"]);

    /// <summary>
    /// The shape B003 expects: one row of five columns.
    /// </summary>
    internal static FakeRowReader VerificationRow(
        bool isReadOnly = true,
        string isolationLevel = "repeatable read",
        bool statementMatches = true,
        bool lockMatches = true,
        bool idleMatches = true) =>
        WithRows(5, [isReadOnly, isolationLevel, statementMatches, lockMatches, idleMatches]);

    /// <summary>
    /// Invoked with the zero-based index of the row about to be produced, before the read
    /// advances. Lets a test cancel before the first row, between rows or during the last row
    /// without any sleep or race.
    /// </summary>
    internal Action<int>? BeforeRead { get; set; }

    public ValueTask<bool> ReadAsync(CancellationToken cancellationToken)
    {
        ReadTokens.Add(cancellationToken);
        BeforeRead?.Invoke(_index + 1);
        _index++;
        return ValueTask.FromResult(_index < _rows.Count);
    }

    public bool IsNull(int ordinal) => _rows[_index][ordinal] is null;

    public bool GetBoolean(int ordinal) => (bool)_rows[_index][ordinal]!;

    public string GetString(int ordinal) => (string)_rows[_index][ordinal]!;

    public int GetInt32(int ordinal) => (int)_rows[_index][ordinal]!;

    public long GetInt64(int ordinal) => (long)_rows[_index][ordinal]!;

    /// <summary>
    /// Mirrors the production seam: a wrong CLR type raises <see cref="InvalidCastException"/>, and
    /// the array is copied so a test can prove the caller never receives the scripted instance.
    /// </summary>
    public string[] GetStringArray(int ordinal)
    {
        string?[] raw = (string?[])_rows[_index][ordinal]!;

        var copy = new string[raw.Length];
        for (var index = 0; index < raw.Length; index++)
        {
            copy[index] = raw[index] ?? throw new PostgreSqlSqlResultShapeException();
        }

        return copy;
    }

    public DateTimeOffset GetDateTimeOffset(int ordinal) => (DateTimeOffset)_rows[_index][ordinal]!;

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
    }
}
