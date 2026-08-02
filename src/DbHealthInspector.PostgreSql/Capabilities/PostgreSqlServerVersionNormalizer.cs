using System.Globalization;

namespace DbHealthInspector.PostgreSql.Capabilities;

/// <summary>
/// Turns the integer <c>server_version_num</c> into a normalized version string, a major number
/// and a supported/unsupported verdict.
/// </summary>
/// <remarks>
/// <para>
/// The numeric setting is the <b>only</b> source. Nothing here parses <c>version()</c>, textual
/// <c>server_version</c>, a vendor package suffix, a platform string or a build string — those
/// carry distribution detail that is both unnecessary and undesirable to read, and their formats
/// are not stable across builds.
/// </para>
/// <para>
/// All formatting uses <see cref="CultureInfo.InvariantCulture"/>, so a machine configured for a
/// locale with different digit shapes or separators still produces <c>"18.4"</c>.
/// </para>
/// </remarks>
internal static class PostgreSqlServerVersionNormalizer
{
    /// <summary>The lowest major version this product supports.</summary>
    internal const int MinimumSupportedMajorVersion = 15;

    /// <summary>The highest major version this product supports.</summary>
    internal const int MaximumSupportedMajorVersion = 18;

    /// <summary>
    /// The encoding changed at PostgreSQL 10: from that release on, the number is
    /// <c>major * 10000 + minor</c>; before it, <c>major * 10000 + minor * 100 + patch</c>.
    /// </summary>
    private const int FirstTwoPartVersionNumber = 100000;

    /// <summary>
    /// Derives the major version from an encoded version number.
    /// </summary>
    /// <exception cref="PostgreSqlServerVersionException">
    /// <paramref name="serverVersionNumber"/> is zero, negative or structurally impossible.
    /// </exception>
    internal static int MajorVersionOf(int serverVersionNumber)
    {
        ValidateEncoding(serverVersionNumber);
        return serverVersionNumber / 10000;
    }

    /// <summary>
    /// Normalizes an encoded version number to its display form: <c>"major.minor"</c> for
    /// PostgreSQL 10 and later, <c>"major.minor.patch"</c> for anything older — the older form
    /// exists solely so an unsupported server can still be reported precisely.
    /// </summary>
    /// <exception cref="PostgreSqlServerVersionException">
    /// <paramref name="serverVersionNumber"/> is zero, negative or structurally impossible.
    /// </exception>
    internal static string Normalize(int serverVersionNumber)
    {
        ValidateEncoding(serverVersionNumber);

        int major = serverVersionNumber / 10000;

        if (serverVersionNumber >= FirstTwoPartVersionNumber)
        {
            int minor = serverVersionNumber % 10000;
            return string.Create(CultureInfo.InvariantCulture, $"{major}.{minor}");
        }

        int legacyMinor = serverVersionNumber / 100 % 100;
        int patch = serverVersionNumber % 100;
        return string.Create(CultureInfo.InvariantCulture, $"{major}.{legacyMinor}.{patch}");
    }

    /// <summary>
    /// Whether <paramref name="majorVersion"/> is inside the supported 15–18 range. Decided
    /// numerically; no text is ever compared.
    /// </summary>
    internal static PostgreSqlVersionSupportStatus SupportStatusOf(int majorVersion) =>
        majorVersion >= MinimumSupportedMajorVersion && majorVersion <= MaximumSupportedMajorVersion
            ? PostgreSqlVersionSupportStatus.Supported
            : PostgreSqlVersionSupportStatus.Unsupported;

    private static void ValidateEncoding(int serverVersionNumber)
    {
        // Zero and negatives cannot be an encoded version at all. A value below 10000 would imply
        // major 0, which PostgreSQL has never reported.
        if (serverVersionNumber < 10000)
        {
            throw new PostgreSqlServerVersionException();
        }
    }
}

/// <summary>
/// Raised when <c>server_version_num</c> is not a value PostgreSQL could have produced.
/// </summary>
/// <remarks>
/// Carries a fixed message and never the offending value: the reported version is server detail,
/// and this exception can cross into a session failure surface.
/// </remarks>
internal sealed class PostgreSqlServerVersionException : Exception
{
    private const string SanitizedMessage = "The PostgreSQL server version could not be interpreted.";

    /// <summary>
    /// Creates the sanitized version-mapping exception.
    /// </summary>
    internal PostgreSqlServerVersionException()
        : base(SanitizedMessage)
    {
    }
}
