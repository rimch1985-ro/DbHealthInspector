using System.CommandLine;
using DbHealthInspector.Cli;

using var cancellation = new CancellationTokenSource();

// Ctrl+C cancels the in-flight inspection rather than killing the process, so the provider's
// existing rollback and disposal run and no transaction is left open.
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

RootCommand root = CommandLineApplication.BuildRootCommand(
    InspectPostgreSqlCommand.ProductionExecutor,
    InspectPostgreSqlCommand.ProductionEnvironmentReader);

return await CommandLineApplication.RunAsync(
    root, args, Console.Out, Console.Error, cancellation.Token);
