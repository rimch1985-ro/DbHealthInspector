using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.PostgreSql.Capabilities;

/// <summary>
/// The complete, immutable outcome of one capability probe.
/// </summary>
/// <remarks>
/// <para>
/// Everything it exposes is either an existing Core model or a plain number: no Npgsql type,
/// SQLSTATE, connection, transaction, command, connection string, raw SQL or session resource
/// crosses this boundary.
/// </para>
/// <para>
/// Deliberately a plain sealed class rather than a <see langword="record"/>: a record's generated
/// <see cref="object.ToString"/> would render the database name and current user structurally.
/// Those two values are authorized <i>result</i> metadata, but they must never reach an exception
/// message, a capability reason, a log or a test display name, so the inherited
/// <see cref="object.ToString"/> — which returns only the type name — is the safer default.
/// </para>
/// <para>
/// The constructor enforces the result's own invariants rather than trusting its caller: the
/// metadata must identify PostgreSQL, and the normalized version, major version and support
/// status must all be exactly what
/// <see cref="PostgreSqlServerVersionNormalizer"/> derives from
/// <see cref="ServerVersionNumber"/>. A result that exists at all is therefore internally
/// consistent no matter who built it (GC-DHI-04C-C1, R1-11). The derivation is delegated to the
/// normalizer and nothing derived from it is stored: there is exactly one implementation of the
/// version arithmetic.
/// </para>
/// </remarks>
internal sealed class PostgreSqlServerProbeResult
{
    private const string ForeignEngineMessage = "Probe metadata must identify PostgreSQL.";

    private const string InconsistentVersionMessage = "Probe version fields are inconsistent.";

    /// <summary>
    /// Engine, normalized version, database name and current user.
    /// </summary>
    internal DatabaseMetadata Metadata { get; }

    /// <summary>
    /// Exactly one state for every <see cref="CapabilityKind"/>.
    /// </summary>
    internal CapabilitySnapshot Capabilities { get; }

    /// <summary>
    /// The nullable UTC statistics-reset timestamp.
    /// </summary>
    internal StatisticsSnapshot Statistics { get; }

    /// <summary>
    /// The raw <c>server_version_num</c> the verdict was derived from.
    /// </summary>
    internal int ServerVersionNumber { get; }

    /// <summary>
    /// The numeric major version.
    /// </summary>
    internal int MajorVersion { get; }

    /// <summary>
    /// Whether <see cref="MajorVersion"/> is inside the supported range.
    /// </summary>
    internal PostgreSqlVersionSupportStatus VersionSupport { get; }

    internal PostgreSqlServerProbeResult(
        DatabaseMetadata metadata,
        CapabilitySnapshot capabilities,
        StatisticsSnapshot statistics,
        int serverVersionNumber,
        int majorVersion,
        PostgreSqlVersionSupportStatus versionSupport)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentNullException.ThrowIfNull(statistics);

        if (serverVersionNumber < 10000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(serverVersionNumber), "The encoded server version is not a value PostgreSQL could report.");
        }

        if (majorVersion < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(majorVersion), "The major version must be positive.");
        }

        if (!Enum.IsDefined(versionSupport))
        {
            throw new ArgumentOutOfRangeException(nameof(versionSupport), versionSupport, "Undefined support status.");
        }

        // Value equality on the Core contract: DatabaseEngine is a record, so this compares the
        // engine identity rather than the reference.
        if (metadata.Engine != DatabaseEngine.PostgreSql)
        {
            // The received engine is deliberately not named: the message is fixed.
            throw new ArgumentException(ForeignEngineMessage, nameof(metadata));
        }

        // Re-derived, never re-implemented. The encoding was already proven usable above, so
        // these calls cannot fail here.
        int expectedMajorVersion = PostgreSqlServerVersionNormalizer.MajorVersionOf(serverVersionNumber);
        string expectedEngineVersion = PostgreSqlServerVersionNormalizer.Normalize(serverVersionNumber);
        PostgreSqlVersionSupportStatus expectedSupport =
            PostgreSqlServerVersionNormalizer.SupportStatusOf(expectedMajorVersion);

        if (majorVersion != expectedMajorVersion
            || versionSupport != expectedSupport
            || !string.Equals(metadata.EngineVersion, expectedEngineVersion, StringComparison.Ordinal))
        {
            // No received or expected value is named: a version is server detail, and this
            // message is a surface a caller may render anywhere.
            throw new ArgumentException(InconsistentVersionMessage, nameof(serverVersionNumber));
        }

        Metadata = metadata;
        Capabilities = capabilities;
        Statistics = statistics;
        ServerVersionNumber = serverVersionNumber;
        MajorVersion = majorVersion;
        VersionSupport = versionSupport;
    }
}
