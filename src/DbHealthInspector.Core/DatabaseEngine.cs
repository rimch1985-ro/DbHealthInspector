namespace DbHealthInspector.Core;

/// <summary>
/// Identifies the database engine a snapshot or finding originates from.
/// </summary>
/// <remarks>
/// Modeled as a small value object instead of an enumeration so that a future engine (for
/// example SQL Server, per a future ADR) can be introduced without redefining an existing
/// enumeration member and without coupling <c>DbHealthInspector.Core</c> to any adapter-specific
/// type. See ADR-0001 for the PostgreSQL-first decision this type supports.
/// </remarks>
public sealed record DatabaseEngine
{
    /// <summary>
    /// The canonical, human-readable engine name (for example <c>"PostgreSQL"</c>).
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a database engine identifier.
    /// </summary>
    /// <param name="name">The canonical engine name. Cannot be null, empty or whitespace.</param>
    public DatabaseEngine(string name)
    {
        Name = Guard.AgainstNullOrWhiteSpace(name, nameof(name));
    }

    /// <summary>
    /// The PostgreSQL engine identifier, the only engine supported in v0.1.0 per ADR-0001.
    /// </summary>
    public static DatabaseEngine PostgreSql { get; } = new("PostgreSQL");

    /// <summary>
    /// Returns the canonical engine name.
    /// </summary>
    public override string ToString() => Name;
}
