using System.Data;
using System.Runtime.ExceptionServices;
using Npgsql;
using NpgsqlTypes;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The production <see cref="IPostgreSqlStatementGateway"/>: bound to exactly one open
/// <see cref="NpgsqlConnection"/> and one live <see cref="NpgsqlTransaction"/>, it builds one
/// command per operation, binds positional parameters by declared type, runs it asynchronously
/// with the caller's token, and releases it.
/// </summary>
/// <remarks>
/// <para>
/// It never exposes the connection, the transaction or the command. It does not touch
/// <see cref="NpgsqlCommand.CommandTimeout"/> (the server-side transaction-local timeouts from
/// B002 are the single timeout authority), never prepares explicitly, never batches, never runs a
/// multi-statement command and performs no synchronous I/O — including no synchronous
/// <c>Dispose()</c> anywhere on any path.
/// </para>
/// <para>
/// Every stage that can fail captures its primary failure with
/// <see cref="ExceptionDispatchInfo"/> before releasing anything, so a disposal failure can never
/// replace a construction failure, an acquisition failure, an execution failure or a requested
/// cancellation (GC-DHI-04B-C2, R2-01).
/// </para>
/// </remarks>
internal sealed class NpgsqlStatementGateway : IPostgreSqlStatementGateway
{
    private readonly Func<PostgreSqlPreparedStatement, IPostgreSqlCommandHandle> _commandFactory;

    /// <summary>
    /// Creates the production gateway over a live connection and transaction.
    /// </summary>
    internal NpgsqlStatementGateway(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(transaction);

        _commandFactory = statement => new NpgsqlCommandHandle(statement, connection, transaction);
    }

    /// <summary>
    /// Creates a gateway over an explicit command factory. Production uses the connection and
    /// transaction constructor; unit tests supply a deterministic fake so the construction,
    /// acquisition and disposal lifecycles can be exercised without a server. The factory receives
    /// an already-resolved statement, so no caller can influence the command text.
    /// </summary>
    internal NpgsqlStatementGateway(Func<PostgreSqlPreparedStatement, IPostgreSqlCommandHandle> commandFactory)
    {
        ArgumentNullException.ThrowIfNull(commandFactory);

        _commandFactory = commandFactory;
    }

    public async ValueTask ExecuteNonQueryAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statement);

        IPostgreSqlCommandHandle command = await CreateCommandAsync(statement).ConfigureAwait(false);

        // Deliberately not `await using`: that compiles to a try/finally in which a disposal
        // failure would replace the execution failure.
        ExceptionDispatchInfo? primary = null;
        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Transparent capture: nothing is inspected, classified, sanitized or rewritten.
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        ExceptionDispatchInfo? disposal = await PostgreSqlAsyncCleanup
            .RunAllAsync(command.DisposeAsync)
            .ConfigureAwait(false);

        primary?.Throw();
        disposal?.Throw();
    }

    public async ValueTask<IPostgreSqlRowReader> ExecuteReaderAsync(PostgreSqlPreparedStatement statement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(statement);

        IPostgreSqlCommandHandle command = await CreateCommandAsync(statement).ConfigureAwait(false);

        IPostgreSqlRowSource? rows = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            rows = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // Transparent capture: nothing is inspected, classified, sanitized or rewritten.
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        if (primary is not null)
        {
            // Acquisition failed, so nothing downstream will ever own this command. It is
            // released asynchronously and exactly once, and its disposal failure is discarded
            // rather than allowed to replace the acquisition failure — which, on this path,
            // always exists.
            _ = await PostgreSqlAsyncCleanup.RunAllAsync(command.DisposeAsync).ConfigureAwait(false);
            primary.Throw();
        }

        // Ownership of both the rows and the command transfers to the returned reader, so the
        // command outlives this method exactly as long as the rows do.
        return new CommandBoundRowReader(rows!, command);
    }

    /// <summary>
    /// Builds one command and binds its parameters. A failure at any point releases the partially
    /// built command asynchronously and re-throws the construction failure with its original stack
    /// intact; the disposal failure can never take its place.
    /// </summary>
    private async ValueTask<IPostgreSqlCommandHandle> CreateCommandAsync(PostgreSqlPreparedStatement statement)
    {
        IPostgreSqlCommandHandle? command = null;
        ExceptionDispatchInfo? primary = null;
        try
        {
            command = _commandFactory(statement);

            foreach (PostgreSqlSqlParameterValue value in statement.Parameters)
            {
                command.AddParameter(value);
            }
        }
        catch (Exception exception)
        {
            // Transparent capture: nothing is inspected, classified, sanitized or rewritten.
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        if (primary is not null)
        {
            if (command is not null)
            {
                // Only when a command actually exists — a factory that threw leaves nothing to
                // release, and disposing null would itself be a defect.
                _ = await PostgreSqlAsyncCleanup.RunAllAsync(command.DisposeAsync).ConfigureAwait(false);
            }

            primary.Throw();
        }

        return command!;
    }

    /// <summary>
    /// The production command handle: a thin, owning wrapper over one
    /// <see cref="NpgsqlCommand"/>.
    /// </summary>
    private sealed class NpgsqlCommandHandle : IPostgreSqlCommandHandle
    {
        private readonly NpgsqlCommand _command;

        internal NpgsqlCommandHandle(PostgreSqlPreparedStatement statement, NpgsqlConnection connection, NpgsqlTransaction transaction)
        {
            // The command text always comes from the inventory-resolved statement.
            _command = new NpgsqlCommand(statement.CommandText, connection, transaction);
        }

        public void AddParameter(PostgreSqlSqlParameterValue value) => _command.Parameters.Add(CreateParameter(value));

        public async ValueTask ExecuteNonQueryAsync(CancellationToken cancellationToken) =>
            _ = await _command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        public async ValueTask<IPostgreSqlRowSource> ExecuteReaderAsync(CancellationToken cancellationToken)
        {
            NpgsqlDataReader reader = await _command
                .ExecuteReaderAsync(CommandBehavior.SingleResult, cancellationToken)
                .ConfigureAwait(false);

            return new NpgsqlRowSource(reader);
        }

        public async ValueTask DisposeAsync() => await _command.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The production row source: a thin wrapper over one <see cref="NpgsqlDataReader"/>.
    /// </summary>
    private sealed class NpgsqlRowSource : IPostgreSqlRowSource
    {
        private readonly NpgsqlDataReader _reader;

        internal NpgsqlRowSource(NpgsqlDataReader reader) => _reader = reader;

        public int FieldCount => _reader.FieldCount;

        public async ValueTask<bool> ReadAsync(CancellationToken cancellationToken) =>
            await _reader.ReadAsync(cancellationToken).ConfigureAwait(false);

        public bool IsNull(int ordinal) => _reader.IsDBNull(ordinal);

        public bool GetBoolean(int ordinal) => _reader.GetBoolean(ordinal);

        public string GetString(int ordinal) => _reader.GetString(ordinal);

        public int GetInt32(int ordinal) => _reader.GetInt32(ordinal);

        public long GetInt64(int ordinal) => _reader.GetInt64(ordinal);

        // timestamptz is read as a DateTimeOffset directly; Npgsql yields a zero offset for it,
        // and the executor rejects any other offset rather than normalising it silently.
        public DateTimeOffset GetDateTimeOffset(int ordinal) => _reader.GetFieldValue<DateTimeOffset>(ordinal);

        public async ValueTask DisposeAsync() => await _reader.DisposeAsync().ConfigureAwait(false);
    }

    private static NpgsqlParameter CreateParameter(PostgreSqlSqlParameterValue value) => value.Type switch
    {
        // Positional parameters: Npgsql binds unnamed parameters in the order they were added,
        // which the executor has already ordered by ascending position. No caller-controlled
        // parameter name exists, and no value is ever interpolated into the command text.
        PostgreSqlSqlParameterType.Int32 => new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Integer,
            Value = value.Int32Value,
        },

        // The bound payload is a fresh array built here from the already-copied, read-only
        // collection, so neither the caller nor the inventory can observe or mutate what Npgsql
        // receives. Element order is preserved exactly.
        PostgreSqlSqlParameterType.TextArray => new NpgsqlParameter
        {
            NpgsqlDbType = NpgsqlDbType.Array | NpgsqlDbType.Text,
            Value = value.TextArrayValue.ToArray(),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(value), value.Type, "Undefined parameter type."),
    };

    /// <summary>
    /// Couples a row source to the command that produced it, releasing both
    /// <b>independently</b>: the command is released even when releasing the rows fails, and only
    /// the first failure is surfaced (GC-DHI-04B-C1, F-09).
    /// </summary>
    private sealed class CommandBoundRowReader : IPostgreSqlRowReader
    {
        private readonly IPostgreSqlRowSource _rows;
        private readonly IPostgreSqlCommandHandle _command;

        internal CommandBoundRowReader(IPostgreSqlRowSource rows, IPostgreSqlCommandHandle command)
        {
            _rows = rows;
            _command = command;
        }

        public int FieldCount => _rows.FieldCount;

        public async ValueTask<bool> ReadAsync(CancellationToken cancellationToken) =>
            await _rows.ReadAsync(cancellationToken).ConfigureAwait(false);

        public bool IsNull(int ordinal) => _rows.IsNull(ordinal);

        public bool GetBoolean(int ordinal) => _rows.GetBoolean(ordinal);

        public string GetString(int ordinal) => _rows.GetString(ordinal);

        public int GetInt32(int ordinal) => _rows.GetInt32(ordinal);

        public long GetInt64(int ordinal) => _rows.GetInt64(ordinal);

        public DateTimeOffset GetDateTimeOffset(int ordinal) => _rows.GetDateTimeOffset(ordinal);

        public async ValueTask DisposeAsync()
        {
            ExceptionDispatchInfo? failure = await PostgreSqlAsyncCleanup
                .RunAllAsync(_rows.DisposeAsync, _command.DisposeAsync)
                .ConfigureAwait(false);

            failure?.Throw();
        }
    }
}
