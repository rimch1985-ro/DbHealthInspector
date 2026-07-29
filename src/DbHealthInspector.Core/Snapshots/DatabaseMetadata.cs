namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// Identity information about the inspected database.
/// </summary>
public sealed record DatabaseMetadata
{
    /// <summary>
    /// The database engine.
    /// </summary>
    public DatabaseEngine Engine { get; }

    /// <summary>
    /// The engine's reported version (for example <c>"18.4"</c>).
    /// </summary>
    public string EngineVersion { get; }

    /// <summary>
    /// The database name.
    /// </summary>
    public string DatabaseName { get; }

    /// <summary>
    /// The current session's user, when available. When provided, cannot be empty or
    /// whitespace-only.
    /// </summary>
    public string? CurrentUser { get; }

    /// <summary>
    /// Creates database metadata.
    /// </summary>
    /// <param name="engine">The database engine.</param>
    /// <param name="engineVersion">The engine version. Cannot be null, empty or whitespace.</param>
    /// <param name="databaseName">The database name. Cannot be null, empty or whitespace.</param>
    /// <param name="currentUser">
    /// The current session's user, when available. When provided, cannot be empty or whitespace-only.
    /// </param>
    public DatabaseMetadata(
        DatabaseEngine engine,
        string engineVersion,
        string databaseName,
        string? currentUser = null)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
        EngineVersion = Guard.AgainstNullOrWhiteSpace(engineVersion, nameof(engineVersion));
        DatabaseName = Guard.AgainstNullOrWhiteSpace(databaseName, nameof(databaseName));
        CurrentUser = Guard.AgainstEmptyOrWhiteSpace(currentUser, nameof(currentUser));
    }
}
