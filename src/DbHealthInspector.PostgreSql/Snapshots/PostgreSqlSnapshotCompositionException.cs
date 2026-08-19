namespace DbHealthInspector.PostgreSql.Snapshots;

/// <summary>
/// Raised when the individually valid results of one capture cannot be assembled into a single
/// consistent <c>DatabaseSnapshot</c>.
/// </summary>
/// <remarks>
/// <para>
/// Carries a fixed message and never the offending object. A schema, table or index name is
/// customer structure, and this exception can cross into a caller's failure surface — so the only
/// information it conveys is that composition was refused. <c>InnerException</c> is always
/// <see langword="null"/> and <c>Data</c> is always empty.
/// </para>
/// <para>
/// Used for exactly two situations, both of which mean the adapter derived inconsistent data
/// rather than that the server misbehaved: an index that references no table in the same capture,
/// and an <see cref="ArgumentException"/> or <see cref="ArgumentOutOfRangeException"/> raised by
/// Core's final snapshot guards. The second case is wrapped deliberately narrowly: Core's duplicate
/// messages name the offending schema, table or index, and those names must not escape.
/// </para>
/// <para>
/// It is never used to classify a cancellation, an Npgsql failure, an out-of-memory condition or
/// any other programming fault. Those propagate unchanged rather than being disguised as a data
/// error.
/// </para>
/// </remarks>
internal sealed class PostgreSqlSnapshotCompositionException : Exception
{
    private const string SanitizedMessage = "The PostgreSQL snapshot could not be composed safely.";

    /// <summary>
    /// Creates the sanitized composition exception. There is deliberately no constructor taking a
    /// message or an inner exception, so no code path anywhere in the assembly can attach one.
    /// </summary>
    internal PostgreSqlSnapshotCompositionException()
        : base(SanitizedMessage)
    {
    }
}
