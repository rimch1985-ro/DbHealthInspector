using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.PostgreSql.Snapshots;

namespace DbHealthInspector.Cli;

/// <summary>
/// Runs one inspection and returns its result. The single seam between the command handler and
/// PostgreSQL, so the handler's exit-code and redaction behavior can be tested deterministically
/// against the same code path production uses.
/// </summary>
internal delegate Task<InspectionResult> InspectionExecutor(
    string connectionString, DiagnosticThresholds thresholds, CancellationToken cancellationToken);

/// <summary>
/// The production composition: PostgreSQL snapshot provider, the approved diagnostics, and the
/// existing orchestrator.
/// </summary>
internal static class PostgreSqlInspectionExecution
{
    /// <summary>
    /// Captures one snapshot and evaluates DBH001-DBH005 through
    /// <see cref="InspectionOrchestrator"/>.
    /// </summary>
    /// <remarks>
    /// The orchestrator owns capability gating, rule ordering, failure isolation, finding
    /// validation, summary counts and overall risk. None of that is reimplemented here, and no
    /// rule is invoked directly. The default <c>Create</c> overload is used deliberately: every
    /// eligible user schema, permanent system-schema exclusions intact, existing session
    /// timeouts (§9, §10 of the gate definition).
    /// </remarks>
    internal static async Task<InspectionResult> ExecuteAsync(
        string connectionString, DiagnosticThresholds thresholds, CancellationToken cancellationToken)
    {
        PostgreSqlDatabaseSnapshotProvider provider = CreateProvider(connectionString);

        await using (provider.ConfigureAwait(false))
        {
            var orchestrator = new InspectionOrchestrator(
                provider, ApprovedDiagnostics.CreateRegistrations(thresholds));

            return await orchestrator.InspectAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Creates the provider, translating a configuration rejection at the narrowest possible
    /// boundary.
    /// </summary>
    /// <remarks>
    /// Only an <see cref="ArgumentException"/> raised by <c>Create</c> itself means "this
    /// connection configuration is unusable". The same exception type raised later — by
    /// diagnostic composition, by the orchestrator, or by rendering — is an internal defect, and
    /// telling the user their connection configuration is invalid would send them to fix
    /// something that is not broken. Translating here, around one statement, keeps the two cases
    /// distinguishable without widening any catch (Codex R1-01).
    /// </remarks>
    private static PostgreSqlDatabaseSnapshotProvider CreateProvider(string connectionString)
    {
        try
        {
            return PostgreSqlDatabaseSnapshotProvider.Create(connectionString);
        }
        catch (ArgumentException)
        {
            // Covers ArgumentOutOfRangeException and ArgumentNullException. The original is
            // deliberately not carried forward, in message or as an inner exception.
            throw new PostgreSqlConfigurationRejectedException();
        }
    }
}
