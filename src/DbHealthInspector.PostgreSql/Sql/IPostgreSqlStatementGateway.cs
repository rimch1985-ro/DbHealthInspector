namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The narrow seam through which <see cref="PostgreSqlSqlExecutor"/> reaches Npgsql. Production
/// uses <see cref="NpgsqlStatementGateway"/>, which is bound to one connection and one
/// transaction; unit tests substitute a deterministic fake so the executor's resolution, binding
/// and row-shape rules can be proven without a live server.
/// </summary>
/// <remarks>
/// The gateway accepts only an already-resolved <see cref="PostgreSqlPreparedStatement"/>. It has
/// no overload taking SQL text, so no raw-SQL path exists even inside the assembly.
/// </remarks>
internal interface IPostgreSqlStatementGateway
{
    /// <summary>
    /// Runs a statement that returns no result set.
    /// </summary>
    ValueTask ExecuteNonQueryAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken);

    /// <summary>
    /// Runs a statement that returns rows. The caller owns and must dispose the returned reader.
    /// </summary>
    ValueTask<IPostgreSqlRowReader> ExecuteReaderAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken);
}

/// <summary>
/// One command, from construction to disposal. This is the seam that lets the gateway's lifecycle
/// rules — construction failure, reader-acquisition failure and asynchronous disposal failure — be
/// proven without a live server (GC-DHI-04B-C2, R2-01).
/// </summary>
/// <remarks>
/// A handle is created from an already-resolved <see cref="PostgreSqlPreparedStatement"/>, so
/// neither a caller nor an authorized operation can influence the command text: the production
/// factory reads it from the inventory-resolved statement and nowhere else.
/// </remarks>
internal interface IPostgreSqlCommandHandle : IAsyncDisposable
{
    /// <summary>
    /// Appends one positional parameter, in ascending position order.
    /// </summary>
    void AddParameter(PostgreSqlSqlParameterValue value);

    /// <summary>
    /// Runs the command expecting no result set.
    /// </summary>
    ValueTask ExecuteNonQueryAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs the command and acquires its rows. The caller owns the returned source.
    /// </summary>
    ValueTask<IPostgreSqlRowSource> ExecuteReaderAsync(CancellationToken cancellationToken);
}

/// <summary>
/// The rows produced by one command, independent of the command that produced them so that the
/// two can be released separately.
/// </summary>
internal interface IPostgreSqlRowSource : IAsyncDisposable
{
    /// <summary>The number of columns in the current result set.</summary>
    int FieldCount { get; }

    /// <summary>Advances to the next row, returning <see langword="false"/> when there are none.</summary>
    ValueTask<bool> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Whether the column at <paramref name="ordinal"/> is SQL NULL.</summary>
    bool IsNull(int ordinal);

    /// <summary>Reads the column at <paramref name="ordinal"/> as a boolean.</summary>
    bool GetBoolean(int ordinal);

    /// <summary>Reads the column at <paramref name="ordinal"/> as a string.</summary>
    string GetString(int ordinal);
}

/// <summary>
/// The minimal read surface the executor needs: enough to enforce row and column counts and to
/// project B003's five verification columns, and nothing more.
/// </summary>
internal interface IPostgreSqlRowReader : IAsyncDisposable
{
    /// <summary>
    /// The number of columns in the current result set.
    /// </summary>
    int FieldCount { get; }

    /// <summary>
    /// Advances to the next row, returning <see langword="false"/> when there are no more.
    /// </summary>
    ValueTask<bool> ReadAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Whether the column at <paramref name="ordinal"/> is SQL NULL.
    /// </summary>
    bool IsNull(int ordinal);

    /// <summary>
    /// Reads the column at <paramref name="ordinal"/> as a boolean.
    /// </summary>
    bool GetBoolean(int ordinal);

    /// <summary>
    /// Reads the column at <paramref name="ordinal"/> as a string.
    /// </summary>
    string GetString(int ordinal);
}
