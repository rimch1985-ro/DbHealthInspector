using System.CommandLine;
using DbHealthInspector.Cli;
using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Rules;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.UnitTests.Cli.TestSupport;

/// <summary>
/// Runs the real CLI command tree with captured writers and a substitutable inspection executor,
/// so exit codes, message text and redaction are exercised through the exact production path.
/// </summary>
internal sealed class CliHarness
{
    private readonly Dictionary<string, string?> _environment = new(StringComparer.Ordinal);

    /// <summary>The thresholds the executor was invoked with, or null if it never ran.</summary>
    internal DiagnosticThresholds? ObservedThresholds { get; private set; }

    /// <summary>The connection string the executor was invoked with, or null if it never ran.</summary>
    internal string? ObservedConnectionString { get; private set; }

    internal string Output { get; private set; } = string.Empty;

    internal string Error { get; private set; } = string.Empty;

    internal string All => Output + Error;

    /// <summary>What the executor should do. Defaults to a healthy, zero-finding inspection.</summary>
    internal Func<CancellationToken, Task<InspectionResult>> Behavior { get; set; } =
        _ => Task.FromResult(Inspections.Healthy());

    /// <summary>
    /// Runs the genuine PostgreSQL executor instead of <see cref="Behavior"/>, so a test can
    /// exercise the real provider-creation boundary. No connection is opened when the
    /// configuration is rejected before any I/O.
    /// </summary>
    internal bool UseProductionExecutor { get; init; }

    internal CliHarness WithEnvironment(string name, string? value)
    {
        _environment[name] = value;
        return this;
    }

    internal async Task<int> RunAsync(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        InspectionExecutor executor = UseProductionExecutor
            ? InspectPostgreSqlCommand.ProductionExecutor
            : (connectionString, thresholds, cancellationToken) =>
            {
                ObservedConnectionString = connectionString;
                ObservedThresholds = thresholds;
                return Behavior(cancellationToken);
            };

        RootCommand root = CommandLineApplication.BuildRootCommand(
            executor,
            name => _environment.TryGetValue(name, out string? value) ? value : null);

        int exitCode = await CommandLineApplication.RunAsync(
            root, args, output, error, TestContext.Current.CancellationToken);

        Output = output.ToString();
        Error = error.ToString();
        return exitCode;
    }

    /// <summary>
    /// Builds real <see cref="InspectionResult"/> values by running the genuine orchestrator over
    /// a synthetic snapshot, so the CLI is always rendering output the production pipeline
    /// actually produces.
    /// </summary>
    internal static class Inspections
    {
        private sealed class FixedProvider(DatabaseSnapshot snapshot) : IDatabaseSnapshotProvider
        {
            public Task<DatabaseSnapshot> CaptureAsync(CancellationToken cancellationToken) =>
                Task.FromResult(snapshot);
        }

        internal static InspectionResult Run(DatabaseSnapshot snapshot)
        {
            var orchestrator = new InspectionOrchestrator(
                new FixedProvider(snapshot), ApprovedDiagnostics.CreateRegistrations());

            return orchestrator.InspectAsync(CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>A database with nothing wrong with it: zero findings.</summary>
        internal static InspectionResult Healthy() =>
            Run(Rules.TestSupport.DiagnosticSnapshotBuilder.Snapshot(
                tables: [Rules.TestSupport.DiagnosticSnapshotBuilder.Table("orders", hasPrimaryKey: true)],
                indexes: [Rules.TestSupport.DiagnosticSnapshotBuilder.Index("idx_orders_customer")]));

        /// <summary>An ordinary table with no primary key: one DBH001 Warning.</summary>
        internal static InspectionResult WithWarning() =>
            Run(Rules.TestSupport.DiagnosticSnapshotBuilder.Snapshot(
                tables: [Rules.TestSupport.DiagnosticSnapshotBuilder.Table("audit_log", hasPrimaryKey: false)]));

        /// <summary>An invalid index: one DBH005 Critical.</summary>
        internal static InspectionResult WithCritical() =>
            Run(Rules.TestSupport.DiagnosticSnapshotBuilder.Snapshot(
                indexes:
                [
                    Rules.TestSupport.DiagnosticSnapshotBuilder.Index("idx_broken", isValid: false),
                ]));

        /// <summary>A large table: one DBH002 Info, and nothing more severe.</summary>
        internal static InspectionResult WithInfoOnly() =>
            Run(Rules.TestSupport.DiagnosticSnapshotBuilder.Snapshot(
                tables:
                [
                    Rules.TestSupport.DiagnosticSnapshotBuilder.Table(
                        "orders", hasPrimaryKey: true, estimatedRowCount: 5_000_000),
                ]));

        /// <summary>Usage statistics unavailable: DBH004 is skipped, nothing else is affected.</summary>
        internal static InspectionResult WithStatisticsUnavailable() =>
            Run(Rules.TestSupport.DiagnosticSnapshotBuilder.Snapshot(
                tables: [Rules.TestSupport.DiagnosticSnapshotBuilder.Table("orders", hasPrimaryKey: true)],
                indexes: [Rules.TestSupport.DiagnosticSnapshotBuilder.Index("idx_orders_customer")],
                usageStatistics: CapabilityStatus.Unavailable));
    }
}
