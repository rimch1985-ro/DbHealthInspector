namespace DbHealthInspector.PostgreSql.Connections;

/// <summary>
/// Sanitized, immutable connection metadata safe to surface outside the connection boundary.
/// </summary>
/// <remarks>
/// Deliberately a plain class, not a <see langword="record"/>: a record's compiler-generated
/// <see cref="object.ToString"/> would render every property — including this type's own
/// allowlisted values — as a structural string, which is more surface than intended even though
/// none of the current properties are individually secret. The inherited <see cref="object.ToString"/>
/// returns only the type name. This type intentionally exposes only <see cref="TargetKind"/>,
/// <see cref="Port"/>, <see cref="SslMode"/>, <see cref="Pooling"/> and
/// <see cref="ConnectionTimeoutSeconds"/> — never host, database name, username, password,
/// passfile, application name, search path, options, certificate paths, or the connection
/// string itself. See docs/design/postgresql-connection-boundary.md.
/// </remarks>
internal sealed class PostgreSqlConnectionMetadata
{
    /// <summary>
    /// The kind of target the connection points at.
    /// </summary>
    internal PostgreSqlConnectionTargetKind TargetKind { get; }

    /// <summary>
    /// The server port.
    /// </summary>
    internal int Port { get; }

    /// <summary>
    /// The negotiated SSL mode, as text.
    /// </summary>
    internal string SslMode { get; }

    /// <summary>
    /// Whether connection pooling is enabled.
    /// </summary>
    internal bool Pooling { get; }

    /// <summary>
    /// The connection establishment timeout, in seconds.
    /// </summary>
    internal int ConnectionTimeoutSeconds { get; }

    /// <summary>
    /// Creates connection metadata.
    /// </summary>
    internal PostgreSqlConnectionMetadata(
        PostgreSqlConnectionTargetKind targetKind,
        int port,
        string sslMode,
        bool pooling,
        int connectionTimeoutSeconds)
    {
        if (!Enum.IsDefined(targetKind))
        {
            throw new ArgumentOutOfRangeException(nameof(targetKind), targetKind, "Undefined target kind.");
        }

        if (port <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "Port must be positive.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sslMode, nameof(sslMode));

        if (connectionTimeoutSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(connectionTimeoutSeconds), connectionTimeoutSeconds, "Connection timeout cannot be negative.");
        }

        TargetKind = targetKind;
        Port = port;
        SslMode = sslMode;
        Pooling = pooling;
        ConnectionTimeoutSeconds = connectionTimeoutSeconds;
    }
}
