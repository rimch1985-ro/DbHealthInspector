namespace DbHealthInspector.PostgreSql.Connections;

/// <summary>
/// The kind of target a connection points at, derived from the connection string's host value
/// without retaining the host itself.
/// </summary>
internal enum PostgreSqlConnectionTargetKind
{
    /// <summary>
    /// A single network host (the default when the host value is not a socket path or a
    /// comma-separated multi-host list).
    /// </summary>
    NetworkHost,

    /// <summary>
    /// A Unix-domain socket path (a host value starting with <c>/</c>).
    /// </summary>
    UnixDomainSocket,

    /// <summary>
    /// A comma-separated list of multiple hosts (Npgsql multi-host/failover configuration).
    /// </summary>
    MultiHost,
}
