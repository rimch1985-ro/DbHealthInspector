using System.Runtime.ExceptionServices;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The one shared mechanism for running release steps so that a failure in any of them can never
/// hide the failure that actually mattered.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is one of exactly six authorised <i>transparent</i> <c>catch (Exception)</c> sites</b>
/// (GC-DHI-04B-C1/C2, F-01/F-09) — the others are in
/// <c>PostgreSqlInspectionSessionRunner.RunAsync</c>,
/// <c>PostgreSqlSqlExecutor.ReadSingleRowAsync</c>, and the command construction, reader
/// acquisition and non-query execution paths of <c>NpgsqlStatementGateway</c>. All six exist
/// solely to capture, never to
/// classify or sanitize: inside the catch nothing is inspected, nothing is rewritten, and no type,
/// message, stack trace or instance identity is changed. The captured exception is re-thrown later
/// through <see cref="ExceptionDispatchInfo.Throw()"/>, which preserves the original stack trace.
/// No classification path uses a catch-all: every stage that sanitizes uses typed catches.
/// </para>
/// <para>
/// Without this, ordinary C# semantics would silently lose the primary failure: in a
/// <c>try/finally</c> — including the one <c>await using</c> compiles to — an exception raised
/// while releasing resources replaces the exception that was already propagating.
/// </para>
/// </remarks>
internal static class PostgreSqlAsyncCleanup
{
    /// <summary>
    /// Runs every step in order, always attempting all of them, and returns the <b>first</b>
    /// failure captured (or <see langword="null"/> when all succeeded). Later failures are
    /// deliberately dropped rather than aggregated: they are consequences of the first, and
    /// attaching them to <see cref="Exception.Data"/> or an inner exception would widen the
    /// sanitized surface this boundary exists to keep narrow.
    /// </summary>
    internal static async ValueTask<ExceptionDispatchInfo?> RunAllAsync(params Func<ValueTask>[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        ExceptionDispatchInfo? first = null;
        foreach (Func<ValueTask> step in steps)
        {
            try
            {
                await step().ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                // Transparent capture only — see the remarks above.
                first ??= ExceptionDispatchInfo.Capture(exception);
            }
        }

        return first;
    }
}
