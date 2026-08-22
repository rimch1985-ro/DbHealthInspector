namespace DbHealthInspector.Cli;

/// <summary>
/// Marks that the PostgreSQL provider rejected the connection configuration while it was being
/// created, as opposed to anything failing later during inspection.
/// </summary>
/// <remarks>
/// <para>
/// This type exists only to classify one boundary. Without it the command handler cannot tell an
/// <see cref="ArgumentException"/> thrown by <c>Create</c> — a genuinely invalid connection
/// configuration — from one thrown afterwards by diagnostic composition, orchestration or
/// rendering, which is an internal defect and must not be reported to the user as bad
/// configuration (Codex R1-01).
/// </para>
/// <para>
/// It deliberately carries a fixed message and <b>never</b> an inner exception: preserving the
/// original would put text outside this repository's control one dereference away from the
/// console, which §12.2 of the gate definition forbids.
/// </para>
/// </remarks>
internal sealed class PostgreSqlConfigurationRejectedException : Exception
{
    internal PostgreSqlConfigurationRejectedException()
        : base("The PostgreSQL connection configuration was rejected.")
    {
    }
}
