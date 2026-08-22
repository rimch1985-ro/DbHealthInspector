namespace DbHealthInspector.Cli;

/// <summary>
/// Every message the CLI writes to the user. All are fixed strings authored here; the CLI never
/// relays text it did not write (GC-DHI-05B_DEFINITION.md §12).
/// </summary>
internal static class CliMessages
{
    internal const string NoConnectionProvided =
        "No PostgreSQL connection was provided.";

    internal const string NoConnectionHint =
        "Supply one with --connection, with --connection-env <NAME>, "
        + "or by setting the DBHEALTH_CONNECTION environment variable.";

    internal const string NamedEnvironmentVariableUnavailable =
        "The environment variable named by --connection-env is not set or is empty.";

    internal const string InvalidThresholdValue =
        "A diagnostic threshold value is invalid.";

    internal const string InvalidThresholdHint =
        "Each threshold must be a whole number greater than zero and within range.";

    internal const string InvalidConnectionConfiguration =
        "The PostgreSQL connection configuration is invalid.";

    internal const string InspectionCancelled =
        "The inspection was cancelled.";

    internal const string InspectionFailed =
        "The PostgreSQL inspection could not be completed.";

    /// <summary>
    /// Replaces System.CommandLine's own parse diagnostics, which echo unmatched tokens
    /// verbatim. A mistyped option name turns the following token into an unmatched token, so a
    /// connection string — password included — would be printed. See §12.2: the connection
    /// string must never reach the console.
    /// </summary>
    internal const string InvalidCommandLine =
        "The command line could not be understood.";

    internal const string InvalidCommandLineHint =
        "Run 'dbhealth inspect postgresql --help' to see the available options.";

    internal const string NoFindings =
        "No health issues were detected by the enabled diagnostics.";

    internal const string NoFindingsCaveat =
        "This does not guarantee the database has no other problems.";

    internal const string DiagnosticExecutionFailed =
        "One or more diagnostics failed to execute; the result is incomplete.";
}
