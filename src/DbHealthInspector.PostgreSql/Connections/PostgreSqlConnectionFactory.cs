using Npgsql;

namespace DbHealthInspector.PostgreSql.Connections;

/// <summary>
/// Owns a single <see cref="NpgsqlDataSource"/> built from an already-resolved, validated and
/// normalized connection string, and opens connections from it asynchronously.
/// </summary>
/// <remarks>
/// <para>
/// The factory owns the <see cref="NpgsqlDataSource"/>; the caller owns every
/// <see cref="NpgsqlConnection"/> returned by <see cref="OpenConnectionAsync"/> and is
/// responsible for disposing it. The original connection string is not retained in a separate
/// application field and is not exposed by the factory or metadata. A normalized
/// <see cref="NpgsqlConnectionStringBuilder"/> is used transiently to construct the data source
/// and derive <see cref="Metadata"/>; the data source necessarily retains private connection
/// configuration so that it can open connections.
/// </para>
/// <para>
/// See docs/design/postgresql-connection-boundary.md for the full design rationale, including
/// why the cancellation-association and exception-sanitization logic is split into small,
/// separately testable internal methods, and why connection opening goes through the
/// <see cref="PostgreSqlConnectionOpener"/> seam rather than calling
/// <see cref="NpgsqlDataSource.OpenConnectionAsync(CancellationToken)"/> directly.
/// </para>
/// </remarks>
internal sealed class PostgreSqlConnectionFactory : IAsyncDisposable
{
    private readonly PostgreSqlConnectionOpener _opener;
    private NpgsqlDataSource? _dataSource;

    /// <summary>
    /// Sanitized metadata about the connection target. Remains readable after disposal.
    /// </summary>
    internal PostgreSqlConnectionMetadata Metadata { get; }

    private PostgreSqlConnectionFactory(NpgsqlDataSource dataSource, PostgreSqlConnectionMetadata metadata, PostgreSqlConnectionOpener opener)
    {
        _dataSource = dataSource;
        Metadata = metadata;
        _opener = opener;
    }

    /// <summary>
    /// Validates, normalizes and parses <paramref name="connectionString"/>, then builds and
    /// owns a single <see cref="NpgsqlDataSource"/> from it, using the real
    /// <see cref="NpgsqlDataSourceConnectionOpener.Default"/> opener. Executes no SQL and opens
    /// no connection.
    /// </summary>
    /// <param name="connectionString">
    /// An already-resolved connection string. Cannot be <see langword="null"/>, empty or
    /// whitespace-only, must be syntactically valid, and cannot specify a non-empty
    /// <c>Options</c> value.
    /// </param>
    internal static PostgreSqlConnectionFactory Create(string connectionString) =>
        Create(connectionString, NpgsqlDataSourceConnectionOpener.Default);

    /// <summary>
    /// Same as <see cref="Create(string)"/>, but with an explicit <see cref="PostgreSqlConnectionOpener"/>.
    /// Production callers should use <see cref="Create(string)"/>; this overload exists so tests
    /// can substitute a deterministic fake opener instead of opening a real socket — see
    /// docs/design/postgresql-connection-boundary.md §5, §14.
    /// </summary>
    internal static PostgreSqlConnectionFactory Create(string connectionString, PostgreSqlConnectionOpener opener)
    {
        ArgumentNullException.ThrowIfNull(opener);

        NpgsqlConnectionStringBuilder builder = PostgreSqlConnectionStringPolicy.ParseAndNormalize(connectionString);
        PostgreSqlConnectionMetadata metadata = PostgreSqlConnectionStringPolicy.DeriveMetadata(builder);

        // No catch here: builder is already validated and normalized, so NpgsqlDataSourceBuilder
        // .Build() failing represents a broken invariant, defective internal configuration, or
        // Npgsql misuse — not invalid caller input — and must propagate with full fidelity
        // rather than being folded into the "invalid connection string" ArgumentException.
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(builder.ConnectionString);
        NpgsqlDataSource dataSource = dataSourceBuilder.Build();

        return new PostgreSqlConnectionFactory(dataSource, metadata, opener);
    }

    /// <summary>
    /// Opens a new connection asynchronously via the factory's <see cref="PostgreSqlConnectionOpener"/>.
    /// The returned connection belongs to the caller, which is responsible for disposing it.
    /// Executes no SQL, starts no transaction and changes no session state.
    /// </summary>
    /// <param name="cancellationToken">
    /// Checked before the open attempt starts and passed through unchanged to the opener.
    /// Requested cancellation always propagates and is never converted into a sanitized
    /// connection failure; see <see cref="IsRequestedCancellation"/>.
    /// </param>
    /// <exception cref="ObjectDisposedException">This factory has already been disposed.</exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellationToken"/> was canceled, or an
    /// <see cref="OperationCanceledException"/> genuinely associated with it was observed while
    /// opening.
    /// </exception>
    /// <exception cref="PostgreSqlConnectionException">
    /// Opening the connection failed with an <see cref="NpgsqlException"/>, or with an
    /// <see cref="OperationCanceledException"/> unrelated to the requested cancellation.
    /// </exception>
    internal async ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        NpgsqlDataSource dataSource = _dataSource
            ?? throw new ObjectDisposedException(nameof(PostgreSqlConnectionFactory));

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return await _opener(dataSource, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!IsRequestedCancellation(exception, cancellationToken))
        {
            // An OperationCanceledException genuinely associated with cancellationToken is never
            // caught by this filter at all — it propagates with full, untouched fidelity.
            // Everything reaching this block is an OCE unrelated to the requested cancellation,
            // handled identically to an ordinary NpgsqlException below.
            throw SanitizeOrThrowIfCanceled(exception, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            // The only exception type the opener is expected to use to report an ordinary,
            // recoverable connection failure. Anything else — ObjectDisposedException,
            // InvalidOperationException, ArgumentException, NullReferenceException, the
            // process-corruption types — is not caught here and propagates unchanged, since it
            // represents a defect or lifecycle violation, not an expected open failure.
            throw SanitizeOrThrowIfCanceled(exception, cancellationToken);
        }
    }

    /// <summary>
    /// Releases the owned <see cref="NpgsqlDataSource"/>. The first call disposes it; every
    /// later call is a no-op that neither disposes it again nor throws. Does not attempt to
    /// close any connection already returned to a caller by <see cref="OpenConnectionAsync"/>,
    /// and does not store any. Must not run concurrently with <see cref="OpenConnectionAsync"/>
    /// on the same instance; that ordering is intentionally left unspecified.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        NpgsqlDataSource? dataSource = Interlocked.Exchange(ref _dataSource, null);
        if (dataSource is not null)
        {
            await dataSource.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Determines whether <paramref name="exception"/> represents the cancellation of
    /// <paramref name="requestedToken"/> specifically, as opposed to an unrelated
    /// <see cref="OperationCanceledException"/> (for example one carrying
    /// <see cref="CancellationToken.None"/> or an entirely different token). Two conditions each
    /// independently establish association: <paramref name="requestedToken"/> is already
    /// canceled, or the exception's own token is exactly <paramref name="requestedToken"/> and
    /// both are cancelable. The <c>CanBeCanceled</c> checks specifically prevent
    /// <see cref="CancellationToken.None"/> compared against another
    /// <see cref="CancellationToken.None"/> from being treated as association, even though the
    /// two default tokens are structurally equal. Reached directly from
    /// <see cref="OpenConnectionAsync"/>'s catch filter, and exercised directly in tests for the
    /// same reason as <see cref="SanitizeOpenFailure"/>.
    /// </summary>
    internal static bool IsRequestedCancellation(OperationCanceledException exception, CancellationToken requestedToken)
    {
        if (requestedToken.IsCancellationRequested)
        {
            return true;
        }

        return requestedToken.CanBeCanceled
            && exception.CancellationToken.CanBeCanceled
            && exception.CancellationToken == requestedToken;
    }

    /// <summary>
    /// Applies the priority ordering <see cref="OpenConnectionAsync"/> requires once an
    /// exception has been caught and determined not to be, by itself, an
    /// <see cref="OperationCanceledException"/> associated with <paramref name="requestedToken"/>:
    /// requested cancellation is checked one more time — it may have become true during the
    /// failed open attempt — and takes priority over recording a sanitized failure. Internal,
    /// and exercised directly in tests for the same reason as <see cref="SanitizeOpenFailure"/>.
    /// </summary>
    internal static PostgreSqlConnectionException SanitizeOrThrowIfCanceled(Exception exception, CancellationToken requestedToken)
    {
        requestedToken.ThrowIfCancellationRequested();
        return SanitizeOpenFailure(exception);
    }

    /// <summary>
    /// Sanitizes any connection-open failure into a fixed, information-free
    /// <see cref="PostgreSqlConnectionException"/>. Real production code, reached from
    /// <see cref="OpenConnectionAsync"/> through <see cref="SanitizeOrThrowIfCanceled"/> — not a
    /// test-only branch. <paramref name="exception"/> is accepted only to prove the seam is
    /// wired to the real failure; nothing about it (message, type, stack trace, data) is copied
    /// into the result.
    /// </summary>
    internal static PostgreSqlConnectionException SanitizeOpenFailure(Exception exception)
    {
        _ = exception;
        return new PostgreSqlConnectionException();
    }
}
