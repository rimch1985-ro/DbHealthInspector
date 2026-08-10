using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.UnitTests.Sql.TestSupport;

/// <summary>
/// A deterministic <see cref="IPostgreSqlCommandHandle"/> double for exercising the gateway's
/// command lifecycle: construction, parameter binding, reader acquisition and asynchronous
/// disposal. No mocking library, no server, no threads and no sleeps.
/// </summary>
internal sealed class FakeCommandHandle : IPostgreSqlCommandHandle
{
    private readonly Exception? _addParameterFailure;
    private readonly Exception? _executeNonQueryFailure;
    private readonly Exception? _acquireFailure;
    private readonly Exception? _disposeFailure;
    private readonly FakeRowSource? _rows;

    private FakeCommandHandle(
        Exception? addParameterFailure,
        Exception? executeNonQueryFailure,
        Exception? acquireFailure,
        Exception? disposeFailure,
        FakeRowSource? rows)
    {
        _addParameterFailure = addParameterFailure;
        _executeNonQueryFailure = executeNonQueryFailure;
        _acquireFailure = acquireFailure;
        _disposeFailure = disposeFailure;
        _rows = rows;
    }

    internal int AddParameterCount { get; private set; }

    internal int ExecuteNonQueryCount { get; private set; }

    internal int AcquireCount { get; private set; }

    /// <summary>How many times disposal was attempted. Must never exceed one on any path.</summary>
    internal int DisposeCount { get; private set; }

    internal List<CancellationToken> Tokens { get; } = [];

    internal FakeRowSource? Rows => _rows;

    internal static FakeCommandHandle Succeeding(FakeRowSource? rows = null, Exception? disposeFailure = null) =>
        new(null, null, null, disposeFailure, rows ?? FakeRowSource.SingleRow(3));

    internal static FakeCommandHandle FailingToBind(Exception failure, Exception? disposeFailure = null) =>
        new(failure, null, null, disposeFailure, null);

    internal static FakeCommandHandle FailingToAcquire(Exception failure, Exception? disposeFailure = null) =>
        new(null, null, failure, disposeFailure, null);

    internal static FakeCommandHandle FailingNonQuery(Exception failure, Exception? disposeFailure = null) =>
        new(null, failure, null, disposeFailure, null);

    public void AddParameter(PostgreSqlSqlParameterValue value)
    {
        AddParameterCount++;
        if (_addParameterFailure is not null)
        {
            throw _addParameterFailure;
        }
    }

    public ValueTask ExecuteNonQueryAsync(CancellationToken cancellationToken)
    {
        ExecuteNonQueryCount++;
        Tokens.Add(cancellationToken);

        return _executeNonQueryFailure is null
            ? ValueTask.CompletedTask
            : ValueTask.FromException(_executeNonQueryFailure);
    }

    public ValueTask<IPostgreSqlRowSource> ExecuteReaderAsync(CancellationToken cancellationToken)
    {
        AcquireCount++;
        Tokens.Add(cancellationToken);

        return _acquireFailure is not null
            ? ValueTask.FromException<IPostgreSqlRowSource>(_acquireFailure)
            : ValueTask.FromResult<IPostgreSqlRowSource>(_rows ?? FakeRowSource.SingleRow(3));
    }

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return _disposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(_disposeFailure);
    }
}

/// <summary>
/// A scripted <see cref="IPostgreSqlRowSource"/> whose disposal can be made to fail independently
/// of the command's.
/// </summary>
internal sealed class FakeRowSource : IPostgreSqlRowSource
{
    private readonly IReadOnlyList<object?[]> _rows;
    private readonly Exception? _disposeFailure;
    private int _index = -1;

    private FakeRowSource(IReadOnlyList<object?[]> rows, int fieldCount, Exception? disposeFailure)
    {
        _rows = rows;
        FieldCount = fieldCount;
        _disposeFailure = disposeFailure;
    }

    internal int DisposeCount { get; private set; }

    public int FieldCount { get; }

    internal static FakeRowSource SingleRow(int fieldCount, Exception? disposeFailure = null)
    {
        object?[] row = new object?[fieldCount];
        for (var index = 0; index < fieldCount; index++)
        {
            row[index] = fieldCount == 5 && index == 1 ? "repeatable read" : (object)true;
        }

        return new FakeRowSource([row], fieldCount, disposeFailure);
    }

    internal static FakeRowSource Empty(int fieldCount, Exception? disposeFailure = null) =>
        new([], fieldCount, disposeFailure);

    /// <summary>
    /// Scripts explicit rows, so a caller can place a value whose CLR type is not the one the
    /// statement promises and drive the typed-read seam through the real gateway.
    /// </summary>
    internal static FakeRowSource WithRows(int fieldCount, params object?[][] rows) =>
        new(rows, fieldCount, null);

    public ValueTask<bool> ReadAsync(CancellationToken cancellationToken)
    {
        _index++;
        return ValueTask.FromResult(_index < _rows.Count);
    }

    public bool IsNull(int ordinal) => _rows[_index][ordinal] is null;

    public bool GetBoolean(int ordinal) => (bool)_rows[_index][ordinal]!;

    public string GetString(int ordinal) => (string)_rows[_index][ordinal]!;

    public int GetInt32(int ordinal) => (int)_rows[_index][ordinal]!;

    public long GetInt64(int ordinal) => (long)_rows[_index][ordinal]!;

    public DateTimeOffset GetDateTimeOffset(int ordinal) => (DateTimeOffset)_rows[_index][ordinal]!;

    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return _disposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(_disposeFailure);
    }
}
