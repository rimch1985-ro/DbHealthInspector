namespace DbHealthInspector.Cli;

/// <summary>
/// The outcome of resolving a connection string. On failure it carries only a fixed message —
/// never the value that was examined.
/// </summary>
internal sealed record ConnectionResolution
{
    private ConnectionResolution(string? connectionString, string? error, string? hint)
    {
        ConnectionString = connectionString;
        Error = error;
        Hint = hint;
    }

    /// <summary>The resolved connection string, or <see langword="null"/> on failure.</summary>
    internal string? ConnectionString { get; }

    /// <summary>The fixed failure message, or <see langword="null"/> on success.</summary>
    internal string? Error { get; }

    /// <summary>An optional fixed hint accompanying <see cref="Error"/>.</summary>
    internal string? Hint { get; }

    internal bool Succeeded => ConnectionString is not null;

    internal static ConnectionResolution Success(string connectionString) =>
        new(connectionString, null, null);

    internal static ConnectionResolution Failure(string error, string? hint = null) =>
        new(null, error, hint);

    /// <summary>
    /// The default environment variable consulted when neither option is supplied.
    /// </summary>
    internal const string DefaultEnvironmentVariable = "DBHEALTH_CONNECTION";

    /// <summary>
    /// Resolves a connection string from, in order: <c>--connection</c>, the variable named by
    /// <c>--connection-env</c>, then <c>DBHEALTH_CONNECTION</c>.
    /// </summary>
    /// <param name="connectionOption">The <c>--connection</c> value, or null when not supplied.</param>
    /// <param name="connectionEnvOption">The <c>--connection-env</c> value, or null when not supplied.</param>
    /// <param name="readEnvironmentVariable">Reads an environment variable by name.</param>
    /// <remarks>
    /// Each explicit source is terminal: supplying one and finding it unusable is a failure, not a
    /// reason to consult the next source. A user who names a variable is being specific, and
    /// silently inspecting whatever <c>DBHEALTH_CONNECTION</c> happens to point at could inspect
    /// the wrong database (§7.1).
    /// </remarks>
    internal static ConnectionResolution Resolve(
        string? connectionOption,
        string? connectionEnvOption,
        Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);

        if (connectionOption is not null)
        {
            return string.IsNullOrWhiteSpace(connectionOption)
                ? Failure(CliMessages.NoConnectionProvided, CliMessages.NoConnectionHint)
                : Success(connectionOption);
        }

        if (connectionEnvOption is not null)
        {
            if (string.IsNullOrWhiteSpace(connectionEnvOption))
            {
                return Failure(CliMessages.NamedEnvironmentVariableUnavailable);
            }

            string? named = readEnvironmentVariable(connectionEnvOption);
            return string.IsNullOrWhiteSpace(named)
                ? Failure(CliMessages.NamedEnvironmentVariableUnavailable)
                : Success(named);
        }

        string? fallback = readEnvironmentVariable(DefaultEnvironmentVariable);
        return string.IsNullOrWhiteSpace(fallback)
            ? Failure(CliMessages.NoConnectionProvided, CliMessages.NoConnectionHint)
            : Success(fallback);
    }
}
