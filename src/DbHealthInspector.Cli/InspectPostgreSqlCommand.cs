using System.CommandLine;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.Cli;

/// <summary>
/// The <c>dbhealth inspect postgresql</c> command: its options, and the handler that resolves
/// them, runs the inspection and maps the outcome to an exit code.
/// </summary>
internal sealed class InspectPostgreSqlCommand
{
    internal Option<string?> Connection { get; } =
        new("--connection")
        {
            Description =
                "PostgreSQL connection string. WARNING: a value passed on the command line may be "
                + "visible in shell history and in process listings. Prefer --connection-env or the "
                + "DBHEALTH_CONNECTION environment variable for anything carrying a password.",
        };

    internal Option<string?> ConnectionEnv { get; } =
        new("--connection-env")
        {
            Description =
                "Name of an environment variable holding the PostgreSQL connection string. "
                + "If the named variable is missing or empty the command fails; it does not fall "
                + "back to DBHEALTH_CONNECTION.",
        };

    internal Option<string?> LargeTableRowThreshold { get; } =
        new("--large-table-row-threshold")
        {
            Description =
                "DBH002 row threshold, as a whole number of rows greater than zero. Default 1000000.",
        };

    internal Option<string?> LargeTableSizeThresholdMb { get; } =
        new("--large-table-size-threshold-mb")
        {
            Description =
                "DBH002 size threshold. One unit is exactly 1048576 bytes (binary megabyte). "
                + "Must be a whole number greater than zero. Default 1024 (1073741824 bytes).",
        };

    internal Option<string?> UnusedIndexSizeThresholdMb { get; } =
        new("--unused-index-size-threshold-mb")
        {
            Description =
                "DBH004 minimum index size. One unit is exactly 1048576 bytes (binary megabyte). "
                + "Must be a whole number greater than zero. Default 10 (10485760 bytes).",
        };

    private readonly InspectionExecutor _executor;
    private readonly Func<string, string?> _readEnvironmentVariable;

    internal InspectPostgreSqlCommand(
        InspectionExecutor executor, Func<string, string?> readEnvironmentVariable)
    {
        ArgumentNullException.ThrowIfNull(executor);
        ArgumentNullException.ThrowIfNull(readEnvironmentVariable);
        _executor = executor;
        _readEnvironmentVariable = readEnvironmentVariable;
    }

    /// <summary>Builds the <c>postgresql</c> subcommand with its action bound.</summary>
    internal Command Build()
    {
        var command = new Command(
            "postgresql",
            "Inspect a PostgreSQL database and report its health findings. The inspection is "
            + "strictly read-only: it reads catalog metadata and statistics, and never modifies "
            + "the database.");

        command.Options.Add(Connection);
        command.Options.Add(ConnectionEnv);
        command.Options.Add(LargeTableRowThreshold);
        command.Options.Add(LargeTableSizeThresholdMb);
        command.Options.Add(UnusedIndexSizeThresholdMb);

        command.SetAction((ParseResult parseResult, CancellationToken cancellationToken) =>
            ExecuteAsync(
                parseResult,
                parseResult.InvocationConfiguration.Output,
                parseResult.InvocationConfiguration.Error,
                cancellationToken));

        return command;
    }

    /// <summary>
    /// Resolves the connection and thresholds, runs the inspection and maps the outcome to an
    /// exit code. Every failure path writes a fixed message and returns
    /// <see cref="ExitCodes.Failure"/>.
    /// </summary>
    internal async Task<int> ExecuteAsync(
        ParseResult parseResult, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parseResult);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ConnectionResolution connection = ConnectionResolution.Resolve(
            parseResult.GetValue(Connection),
            parseResult.GetValue(ConnectionEnv),
            _readEnvironmentVariable);

        if (!connection.Succeeded)
        {
            error.WriteLine(connection.Error);
            if (connection.Hint is { } hint)
            {
                error.WriteLine(hint);
            }

            return ExitCodes.Failure;
        }

        ThresholdResolution thresholds = ThresholdResolution.Resolve(
            parseResult.GetValue(LargeTableRowThreshold),
            parseResult.GetValue(LargeTableSizeThresholdMb),
            parseResult.GetValue(UnusedIndexSizeThresholdMb));

        if (!thresholds.Succeeded)
        {
            error.WriteLine(thresholds.Error);
            error.WriteLine(CliMessages.InvalidThresholdHint);
            return ExitCodes.Failure;
        }

        InspectionResult result;
        try
        {
            result = await _executor(
                connection.ConnectionString!, thresholds.Thresholds!, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine(CliMessages.InspectionCancelled);
            return ExitCodes.Failure;
        }
        catch (PostgreSqlConfigurationRejectedException)
        {
            // Raised only around provider creation, so this genuinely means the connection
            // configuration is unusable. An ArgumentException from anywhere later is an internal
            // defect and falls through to the generic mapping below (Codex R1-01).
            error.WriteLine(CliMessages.InvalidConnectionConfiguration);
            return ExitCodes.Failure;
        }
        catch (Exception)
        {
            // Total fallback. The exception's own text is never relayed: adapter exception types
            // are internal, so the CLI cannot distinguish a sanitized message from a raw Npgsql
            // one that may carry host, port, user or connection detail (§12.1).
            error.WriteLine(CliMessages.InspectionFailed);
            return ExitCodes.Failure;
        }

        if (!HasRequiredCatalogMetadata(result))
        {
            // The provider returns a complete, empty snapshot when the server version is
            // unsupported, and that would otherwise render as a clean zero-finding inspection —
            // telling the user their database is healthy when nothing was ever examined. The
            // required capability is the existing signal for this; no version is parsed here
            // (Codex R1-02).
            error.WriteLine(CliMessages.InspectionFailed);
            return ExitCodes.Failure;
        }

        InspectionRenderer.Render(output, result);

        if (result.HasErrors)
        {
            // A diagnostic failed to execute, so the result is not a trustworthy verdict.
            return ExitCodes.Failure;
        }

        return result.Summary.WarningFindings + result.Summary.CriticalFindings > 0
            ? ExitCodes.FindingsPresent
            : ExitCodes.Success;
    }

    /// <summary>
    /// Whether the inspection actually examined the catalog.
    /// </summary>
    /// <remarks>
    /// <see cref="CapabilityKind.CatalogMetadata"/> is the <b>required</b> capability: without it
    /// no relation or index was ever read, so an empty result means "not inspected", never
    /// "nothing wrong". This consumes the capability state the snapshot already carries — it does
    /// not re-probe, and it does not derive a second version-support policy.
    /// <para>
    /// <see cref="CapabilityKind.UsageStatistics"/> is deliberately <b>not</b> checked here. It is
    /// optional: losing it skips DBH004 and leaves the rest of the inspection valid, which must
    /// stay a successful run.
    /// </para>
    /// </remarks>
    private static bool HasRequiredCatalogMetadata(InspectionResult result) =>
        result.Snapshot.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status
            == CapabilityStatus.Available;

    /// <summary>The production executor, wired to the real PostgreSQL composition.</summary>
    internal static InspectionExecutor ProductionExecutor { get; } =
        static (connectionString, thresholds, cancellationToken) =>
            PostgreSqlInspectionExecution.ExecuteAsync(connectionString, thresholds, cancellationToken);

    /// <summary>Reads a real process environment variable.</summary>
    internal static Func<string, string?> ProductionEnvironmentReader { get; } =
        static name => Environment.GetEnvironmentVariable(name);
}
