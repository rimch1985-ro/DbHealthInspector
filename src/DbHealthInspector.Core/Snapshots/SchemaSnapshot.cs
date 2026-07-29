namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// A single schema observed during an inspection.
/// </summary>
public sealed record SchemaSnapshot
{
    /// <summary>
    /// The schema name.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// Creates a schema snapshot.
    /// </summary>
    /// <param name="schemaName">The schema name. Cannot be null, empty or whitespace.</param>
    public SchemaSnapshot(string schemaName)
    {
        SchemaName = Guard.AgainstNullOrWhiteSpace(schemaName, nameof(schemaName));
    }
}
