namespace DbHealthInspector.PostgreSql.Capabilities;

/// <summary>
/// The raw identity C001 recovers from the server: a machine-readable version plus the database
/// name and current user.
/// </summary>
/// <remarks>
/// <para>
/// An intermediate, immutable carrier between the executor and the probe. It holds no Npgsql
/// type, no SQLSTATE, no SQL and no session resource, and it deliberately carries nothing beyond
/// these three values — no host, port, session user, platform string or build string.
/// </para>
/// <para>
/// Deliberately a plain sealed class rather than a <see langword="record"/>: the database name and
/// current user are authorized <i>result</i> metadata but must never reach an exception message,
/// a capability reason, a log or a test display name, and a record's generated
/// <see cref="object.ToString"/> would render both structurally wherever the value happened to be
/// interpolated.
/// </para>
/// </remarks>
internal sealed class PostgreSqlServerIdentity
{
    /// <summary>
    /// The value of <c>server_version_num</c>: the single source of version truth.
    /// </summary>
    internal int ServerVersionNumber { get; }

    /// <summary>
    /// The current database name.
    /// </summary>
    internal string DatabaseName { get; }

    /// <summary>
    /// The current user.
    /// </summary>
    internal string CurrentUser { get; }

    internal PostgreSqlServerIdentity(int serverVersionNumber, string databaseName, string currentUser)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databaseName, nameof(databaseName));
        ArgumentException.ThrowIfNullOrWhiteSpace(currentUser, nameof(currentUser));

        ServerVersionNumber = serverVersionNumber;
        DatabaseName = databaseName;
        CurrentUser = currentUser;
    }
}
