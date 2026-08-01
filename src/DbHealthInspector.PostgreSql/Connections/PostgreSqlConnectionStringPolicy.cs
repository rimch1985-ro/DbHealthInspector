using Npgsql;

namespace DbHealthInspector.PostgreSql.Connections;

/// <summary>
/// Parses, validates and normalizes a connection string, and derives sanitized metadata from
/// it. The single authoritative place where the mandatory security overrides
/// (docs/design/postgresql-connection-boundary.md §7) are applied.
/// </summary>
internal static class PostgreSqlConnectionStringPolicy
{
    /// <summary>
    /// The fixed message used for every connection-string configuration failure: null/blank
    /// input, unparsable syntax, or a rejected <c>Options</c> value. Never accompanied by the
    /// original value or an inner exception.
    /// </summary>
    internal const string InvalidConnectionStringMessage = "The PostgreSQL connection string is invalid.";

    private const string RequiredApplicationName = "DbHealthInspector";

    /// <summary>
    /// Parses <paramref name="connectionString"/>, rejects a non-empty <c>Options</c> value, and
    /// applies the mandatory security overrides. Does not open a connection or execute SQL.
    /// </summary>
    /// <param name="connectionString">
    /// The connection string to parse. Cannot be <see langword="null"/>, empty or
    /// whitespace-only.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="connectionString"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="connectionString"/> is empty, whitespace-only, unparsable, or specifies a
    /// non-empty <c>Options</c> value.
    /// </exception>
    internal static NpgsqlConnectionStringBuilder ParseAndNormalize(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString));

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (Exception exception) when (IsExpectedConnectionStringParsingException(exception))
        {
            throw new ArgumentException(InvalidConnectionStringMessage, nameof(connectionString));
        }

        // Options carries arbitrary server-side session parameters (for example "-c
        // some_setting=value"); a future gate (GC-DHI-04B) must own every session/transaction
        // setting explicitly, so no caller-supplied Options value is accepted at all. The
        // rejected value itself is never included in the exception.
        if (!string.IsNullOrEmpty(builder.Options))
        {
            throw new ArgumentException(InvalidConnectionStringMessage, nameof(connectionString));
        }

        builder.PersistSecurityInfo = false;
        builder.IncludeErrorDetail = false;
        builder.LogParameters = false;
        builder.IncludeFailedBatchedCommand = false;
        builder.NoResetOnClose = false;
        builder.Enlist = false;
        builder.Multiplexing = false;
        builder.ApplicationName = RequiredApplicationName;

        return builder;
    }

    /// <summary>
    /// Derives sanitized metadata from an already-parsed, already-normalized builder.
    /// </summary>
    internal static PostgreSqlConnectionMetadata DeriveMetadata(NpgsqlConnectionStringBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return new PostgreSqlConnectionMetadata(
            DeriveTargetKind(builder.Host),
            builder.Port,
            builder.SslMode.ToString(),
            builder.Pooling,
            builder.Timeout);
    }

    private static PostgreSqlConnectionTargetKind DeriveTargetKind(string? host)
    {
        if (string.IsNullOrEmpty(host))
        {
            return PostgreSqlConnectionTargetKind.NetworkHost;
        }

        // Checked before comma detection: a host list of Unix-domain-socket directories (which
        // may itself contain commas) is still a Unix-domain-socket target, never MultiHost.
        if (host.StartsWith('/'))
        {
            return PostgreSqlConnectionTargetKind.UnixDomainSocket;
        }

        if (host.Contains(','))
        {
            return PostgreSqlConnectionTargetKind.MultiHost;
        }

        return PostgreSqlConnectionTargetKind.NetworkHost;
    }

    /// <summary>
    /// The exact, closed set of exception types <c>NpgsqlConnectionStringBuilder</c>'s
    /// constructor is known (by direct reproduction against Npgsql 10.0.3, covered by
    /// <c>PostgreSqlConnectionStringPolicyValidationTests</c>) to throw for genuinely invalid
    /// connection-string input: <see cref="ArgumentException"/> for a malformed value (bad
    /// syntax, a value of the wrong type, an unrecognized keyword) and
    /// <see cref="KeyNotFoundException"/> for input with no recognizable key/value pairs at all.
    /// Nothing outside this set is treated as an input-parsing failure — in particular, this
    /// helper is scoped to parsing only and is never used to guard <c>NpgsqlDataSourceBuilder
    /// .Build()</c> or connection opening, each of which has its own, separately scoped
    /// exception handling (docs/design/postgresql-connection-boundary.md §6).
    /// </summary>
    private static bool IsExpectedConnectionStringParsingException(Exception exception) =>
        exception is ArgumentException or KeyNotFoundException;
}
