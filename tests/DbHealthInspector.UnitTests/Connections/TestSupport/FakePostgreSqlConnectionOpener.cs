using DbHealthInspector.PostgreSql.Connections;
using Npgsql;

namespace DbHealthInspector.UnitTests.Connections.TestSupport;

/// <summary>
/// A deterministic, hand-written test double for <see cref="PostgreSqlConnectionOpener"/>. No
/// mocking library, no reflection into production internals, no network, no threads, no sleeps,
/// no real ports or DNS: it is a plain delegate target that records what it was called with and
/// either returns a caller-supplied <see cref="NpgsqlConnection"/> or throws a caller-supplied
/// exception.
/// </summary>
internal sealed class FakePostgreSqlConnectionOpener
{
    private readonly NpgsqlConnection? _connectionToReturn;
    private Exception? _exceptionToThrow;
    private readonly Action? _beforeReturnOrThrow;

    private FakePostgreSqlConnectionOpener(NpgsqlConnection? connectionToReturn, Exception? exceptionToThrow, Action? beforeReturnOrThrow)
    {
        _connectionToReturn = connectionToReturn;
        _exceptionToThrow = exceptionToThrow;
        _beforeReturnOrThrow = beforeReturnOrThrow;
    }

    internal int CallCount { get; private set; }

    internal CancellationToken LastCancellationToken { get; private set; }

    /// <summary>
    /// Creates an opener that succeeds, returning <paramref name="connection"/> (or a fresh,
    /// never-opened <see cref="NpgsqlConnection"/> if none is supplied).
    /// </summary>
    internal static FakePostgreSqlConnectionOpener ReturningConnection(NpgsqlConnection? connection = null, Action? beforeReturn = null) =>
        new(connection ?? new NpgsqlConnection("Host=unused-fake-opener-connection"), null, beforeReturn);

    /// <summary>
    /// Creates an opener that throws <paramref name="exception"/>. <paramref name="beforeThrow"/>,
    /// when supplied, runs immediately before the throw — used to simulate a caller cancelling
    /// its token while the (fake) open attempt is in flight.
    /// </summary>
    internal static FakePostgreSqlConnectionOpener Throwing(Exception exception, Action? beforeThrow = null) =>
        new(null, exception, beforeThrow);

    /// <summary>
    /// The delegate to pass to <c>PostgreSqlConnectionFactory.Create(connectionString, opener)</c>.
    /// </summary>
    internal PostgreSqlConnectionOpener AsDelegate => InvokeAsync;

    private ValueTask<NpgsqlConnection> InvokeAsync(NpgsqlDataSource dataSource, CancellationToken cancellationToken)
    {
        CallCount++;
        _ = dataSource;
        LastCancellationToken = cancellationToken;

        _beforeReturnOrThrow?.Invoke();

        if (_exceptionToThrow is Exception exception)
        {
            _exceptionToThrow = null;
            throw exception;
        }

        return ValueTask.FromResult(_connectionToReturn!);
    }
}
