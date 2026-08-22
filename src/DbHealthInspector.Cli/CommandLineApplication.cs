using System.CommandLine;

namespace DbHealthInspector.Cli;

/// <summary>
/// Builds the command tree and runs it, forcing the frozen exit-code contract onto
/// System.CommandLine's own behavior.
/// </summary>
internal static class CommandLineApplication
{
    internal static RootCommand BuildRootCommand(
        InspectionExecutor executor, Func<string, string?> readEnvironmentVariable)
    {
        var root = new RootCommand(
            "DbHealth Inspector - read-only PostgreSQL metadata diagnostics.");

        var inspect = new Command("inspect", "Inspect a database and report health findings.");
        inspect.Subcommands.Add(new InspectPostgreSqlCommand(executor, readEnvironmentVariable).Build());
        root.Subcommands.Add(inspect);

        return root;
    }

    /// <summary>
    /// Parses and runs, returning an exit code that always obeys the frozen 0/1/2 contract.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Two System.CommandLine behaviors are deliberately overridden here.
    /// </para>
    /// <para>
    /// <b>Exit code.</b> Version 2.0.10 returns <c>1</c> for a parse error, which in this contract
    /// means "inspection succeeded and found something". A parse error must be
    /// <see cref="ExitCodes.Failure"/> instead.
    /// </para>
    /// <para>
    /// <b>Diagnostics suppression.</b> The default parse-error output echoes unmatched tokens
    /// verbatim. A mistyped option name — <c>--connectio</c> for <c>--connection</c> — turns the
    /// following argument into an unmatched token, so the connection string, password included,
    /// would be written to standard error. Empirically verified against 2.0.10. The gate
    /// definition forbids the connection string ever reaching the console, so this writes its own
    /// token-free message and points the user at <c>--help</c> instead.
    /// </para>
    /// </remarks>
    internal static async Task<int> RunAsync(
        RootCommand root,
        string[] args,
        TextWriter output,
        TextWriter error,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        ParseResult parseResult = root.Parse(args);

        if (parseResult.Errors.Count > 0)
        {
            error.WriteLine(CliMessages.InvalidCommandLine);
            error.WriteLine(CliMessages.InvalidCommandLineHint);
            return ExitCodes.Failure;
        }

        var configuration = new InvocationConfiguration
        {
            Output = output,
            Error = error,

            // The command handler maps every exception to a fixed message itself; the default
            // handler would print exception text.
            EnableDefaultExceptionHandler = false,
        };

        try
        {
            return await parseResult.InvokeAsync(configuration, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            error.WriteLine(CliMessages.InspectionCancelled);
            return ExitCodes.Failure;
        }
        catch (Exception)
        {
            error.WriteLine(CliMessages.InspectionFailed);
            return ExitCodes.Failure;
        }
    }
}
