using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Sessions;
using Npgsql;

namespace DbHealthInspector.PostgreSql.Capabilities;

/// <summary>
/// Runs C001–C004 inside an already-verified GC-DHI-04B session and composes the server metadata,
/// capability snapshot and statistics snapshot the rest of the product consumes.
/// </summary>
/// <remarks>
/// <para>
/// The probe decides <b>what</b> to ask and <b>in which order</b>; it never decides how a
/// statement runs. It holds no connection, transaction or command, executes nothing outside the
/// four inventoried statements, and caches nothing between sessions.
/// </para>
/// <para>
/// Exactly one failure mode is a degradation rather than a failure: losing <c>SELECT</c> on the
/// statistics views between C003 and C004. Everything else — an unreadable catalog, a shape
/// violation, any other PostgreSQL error, a cancellation — either fails the probe outright or
/// propagates to the sanitized GC-DHI-04B boundary untouched.
/// </para>
/// </remarks>
internal sealed class PostgreSqlServerCapabilityProbe
{
    /// <summary>PostgreSQL <c>insufficient_privilege</c>.</summary>
    private const string InsufficientPrivilegeSqlState = "42501";

    /// <summary>
    /// The reason both capabilities carry when the server's major version is outside 15–18. It
    /// deliberately does not name the actual version: the version is server detail, and a reason
    /// string is a surface a caller may render anywhere.
    /// </summary>
    internal const string UnsupportedVersionReason = "The PostgreSQL server version is outside the supported range.";

    /// <summary>
    /// The reason usage statistics carry whenever they are unavailable — whether C003 said so up
    /// front or C004 lost the privilege in a race. One string for both, so the two cases are
    /// indistinguishable to a caller and neither reveals which happened.
    /// </summary>
    internal const string UnavailableStatisticsReason = "Usage statistics are unavailable for this inspection.";

    /// <summary>
    /// The reason data profiling always carries. This is product policy, not a server condition.
    /// </summary>
    internal const string DisabledProfilingReason = "Data profiling is disabled by product policy.";

    /// <summary>
    /// Probes the server through the restricted operation view.
    /// </summary>
    /// <remarks>
    /// Static because the probe holds no state at all: it caches nothing between sessions, owns
    /// no resource and reads only what the executor it is handed returns. Making that explicit is
    /// better than an instance that would merely look as though it had a lifetime.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="executor"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="PostgreSqlRequiredCatalogCapabilityException">
    /// The server is supported but its required catalog metadata is unreachable.
    /// </exception>
    internal static async ValueTask<PostgreSqlServerProbeResult> ProbeAsync(
        PostgreSqlInspectionOperationExecutor executor,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(executor);
        cancellationToken.ThrowIfCancellationRequested();

        PostgreSqlServerIdentity identity = await executor.ReadServerIdentityAsync(cancellationToken).ConfigureAwait(false);

        int majorVersion = PostgreSqlServerVersionNormalizer.MajorVersionOf(identity.ServerVersionNumber);
        string normalizedVersion = PostgreSqlServerVersionNormalizer.Normalize(identity.ServerVersionNumber);
        PostgreSqlVersionSupportStatus versionSupport = PostgreSqlServerVersionNormalizer.SupportStatusOf(majorVersion);

        var metadata = new DatabaseMetadata(
            DatabaseEngine.PostgreSql, normalizedVersion, identity.DatabaseName, identity.CurrentUser);

        if (versionSupport == PostgreSqlVersionSupportStatus.Unsupported)
        {
            // An unsupported major is a reported outcome, not an exception. Nothing further is
            // asked of a server whose catalog shape this product has not been validated against.
            return new PostgreSqlServerProbeResult(
                metadata,
                Compose(
                    catalog: new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Unavailable, UnsupportedVersionReason),
                    statistics: new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Unavailable, UnsupportedVersionReason)),
                new StatisticsSnapshot(null),
                identity.ServerVersionNumber,
                majorVersion,
                versionSupport);
        }

        bool catalogAvailable = await executor.CheckCatalogMetadataAccessAsync(cancellationToken).ConfigureAwait(false);
        if (!catalogAvailable)
        {
            // Required, so there is no partial result to return: C003 and C004 are never reached.
            throw new PostgreSqlRequiredCatalogCapabilityException();
        }

        bool statisticsAvailable = await executor.CheckUsageStatisticsAccessAsync(cancellationToken).ConfigureAwait(false);
        if (!statisticsAvailable)
        {
            // C004 is not executed: asking for a value the server just said is unreadable would
            // only produce an error to swallow.
            return new PostgreSqlServerProbeResult(
                metadata,
                Compose(
                    catalog: new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available),
                    statistics: new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Unavailable, UnavailableStatisticsReason)),
                new StatisticsSnapshot(null),
                identity.ServerVersionNumber,
                majorVersion,
                versionSupport);
        }

        DateTimeOffset? statisticsResetAtUtc;
        try
        {
            statisticsResetAtUtc = await executor.ReadStatisticsResetAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (PostgresException exception) when (IsInsufficientPrivilege(exception))
        {
            // The single authorized degradation: C003 said the statistics views were readable and
            // the privilege was withdrawn before C004 ran. Cancellation is re-checked first, so a
            // caller who asked to stop is never told "statistics unavailable" instead.
            cancellationToken.ThrowIfCancellationRequested();

            // The PostgreSQL exception is discarded entirely — not stored, not wrapped, not
            // logged, not copied into Data.
            return new PostgreSqlServerProbeResult(
                metadata,
                Compose(
                    catalog: new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available),
                    statistics: new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Unavailable, UnavailableStatisticsReason)),
                new StatisticsSnapshot(null),
                identity.ServerVersionNumber,
                majorVersion,
                versionSupport);
        }

        return new PostgreSqlServerProbeResult(
            metadata,
            Compose(
                catalog: new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available),
                statistics: new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available)),
            new StatisticsSnapshot(statisticsResetAtUtc),
            identity.ServerVersionNumber,
            majorVersion,
            versionSupport);
    }

    /// <summary>
    /// Whether a PostgreSQL failure is exactly <c>insufficient_privilege</c>. Scoped to C004: the
    /// SQLSTATE is read here, inside this one localized decision, and never escapes it.
    /// </summary>
    internal static bool IsInsufficientPrivilege(PostgresException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return string.Equals(exception.SqlState, InsufficientPrivilegeSqlState, StringComparison.Ordinal);
    }

    /// <summary>
    /// Builds the capability snapshot in the frozen order — catalog, statistics, profiling — with
    /// exactly one state per kind. Data profiling is always disabled by product policy, whatever
    /// the server would permit.
    /// </summary>
    private static CapabilitySnapshot Compose(CapabilityState catalog, CapabilityState statistics) =>
        new(
        [
            catalog,
            statistics,
            new CapabilityState(CapabilityKind.DataProfiling, CapabilityStatus.Disabled, DisabledProfilingReason),
        ]);
}
