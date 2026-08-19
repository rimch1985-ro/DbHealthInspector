using DbHealthInspector.Core.Inspections;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Indexes;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.PostgreSql.Snapshots;

/// <summary>
/// Captures one complete, engine-neutral <see cref="DatabaseSnapshot"/> from a PostgreSQL server.
/// </summary>
/// <remarks>
/// <para>
/// This is the only PostgreSQL type the package exports besides the assembly marker. It composes
/// the already-approved primitives — the connection factory, the verified session runner, the
/// capability probe, the table query and the composite index query — and adds orchestration only.
/// It defines no SQL of its own, and the productive inventory remains the same ten statements.
/// </para>
/// <para>
/// One capture uses one connection, one verified session and one <c>RepeatableRead</c>, read-only,
/// non-deferrable, rollback-only transaction. The caller receives a fully constructed snapshot or
/// an exception: there is no partial result, and nothing is published before rollback and cleanup
/// have completed.
/// </para>
/// <para>
/// One instance is safe for concurrent captures. The shared state is immutable apart from the
/// lifecycle counter: each admitted capture opens its own connection, transaction, executor and
/// local buffers. The caller owns the provider and must <see cref="DisposeAsync"/> it.
/// </para>
/// </remarks>
public sealed class PostgreSqlDatabaseSnapshotProvider : IDatabaseSnapshotProvider, IAsyncDisposable
{
    /// <summary>The idle-in-transaction timeout, fixed for every capture and never derived.</summary>
    private static readonly TimeSpan IdleInTransactionTimeout = TimeSpan.FromSeconds(60);

    /// <summary>The ceiling the derived lock timeout is capped at.</summary>
    private const int MaximumDerivedLockTimeoutMilliseconds = 5000;

    /// <summary>
    /// The connection factory this provider owns, or <see langword="null"/> when a test supplied
    /// its own scope factory. Retained so the production binding below stays inspectable.
    /// </summary>
    private readonly PostgreSqlConnectionFactory? _connectionFactory;

    /// <summary>
    /// The single asynchronous release of everything this provider owns.
    /// </summary>
    /// <remarks>
    /// For both public factories this is exactly
    /// <see cref="PostgreSqlConnectionFactory.DisposeAsync"/> over the factory created in
    /// <see cref="Create(string)"/>. A test may substitute its own observable release through
    /// <see cref="CreateForTesting"/>, which is what makes "disposal waits for the lease, then
    /// releases the owned resource" a measurement rather than an inference
    /// (GC-DHI-04F-C3, R3-01). Either way the delegate travels the same lifecycle path; there is
    /// no second disposal algorithm.
    /// </remarks>
    private readonly Func<ValueTask> _releaseResourceAsync;

    private readonly PostgreSqlInspectionSessionRunner _runner;
    private readonly PostgreSqlInspectionSessionOptions _options;
    private readonly PostgreSqlSchemaFilter _filter;

    /// <summary>
    /// Runs between composition and the final in-transaction cancellation checkpoint.
    /// </summary>
    /// <remarks>
    /// Always <see langword="null"/> in production: neither public <c>Create</c> overload can set
    /// it, so the field is inert unless a test supplied it through
    /// <see cref="CreateForTesting"/>. It exists because those two points are otherwise
    /// indistinguishable from outside — no statement runs between them — and the contract that a
    /// composed snapshot is still withheld when the caller cancels has to be provable, not assumed
    /// (GC-DHI-04F-C2, R1-04D).
    /// </remarks>
    private readonly Action? _afterCompose;

    private readonly PostgreSqlSnapshotProviderLifecycle _lifecycle = new();

    private PostgreSqlDatabaseSnapshotProvider(
        PostgreSqlConnectionFactory? connectionFactory,
        Func<ValueTask> releaseResourceAsync,
        PostgreSqlInspectionSessionRunner runner,
        PostgreSqlInspectionSessionOptions options,
        PostgreSqlSchemaFilter filter,
        Action? afterCompose = null)
    {
        _connectionFactory = connectionFactory;
        _releaseResourceAsync = releaseResourceAsync;
        _runner = runner;
        _options = options;
        _filter = filter;
        _afterCompose = afterCompose;
    }

    // --- Construction -------------------------------------------------------------------------

    /// <summary>
    /// Creates a provider that inspects every eligible schema using the adapter's default
    /// timeouts: 30 s statement, 5 s lock and 60 s idle-in-transaction.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is not usable.</exception>
    public static PostgreSqlDatabaseSnapshotProvider Create(string connectionString) =>
        Create(connectionString, PostgreSqlSchemaFilter.IncludeEverything, PostgreSqlInspectionSessionOptions.Default);

    /// <summary>
    /// Creates a provider restricted to the given schemas and statement timeout.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <param name="includedSchemas">
    /// Exact, case-sensitive schema names to inspect. Empty means every otherwise eligible schema.
    /// </param>
    /// <param name="excludedSchemas">
    /// Exact, case-sensitive schema names to skip. Empty means no caller exclusion. The permanent
    /// system-schema exclusions always apply and cannot be re-enabled by an include.
    /// </param>
    /// <param name="statementTimeout">
    /// The per-statement timeout. Must be a whole number of milliseconds between 100 ms and
    /// 5 minutes; it is validated exactly and never rounded, truncated or clamped. The lock timeout
    /// is derived from it and the idle-in-transaction timeout remains 60 seconds.
    /// </param>
    /// <exception cref="ArgumentNullException">A collection argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">An argument is not usable.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="statementTimeout"/> is out of range.</exception>
    public static PostgreSqlDatabaseSnapshotProvider Create(
        string connectionString,
        IReadOnlyCollection<string> includedSchemas,
        IReadOnlyCollection<string> excludedSchemas,
        TimeSpan statementTimeout)
    {
        ArgumentNullException.ThrowIfNull(includedSchemas);
        ArgumentNullException.ThrowIfNull(excludedSchemas);

        // Everything below runs before any external resource exists, so a rejected argument leaks
        // no data source, no connection and no server-side state.
        var filter = new PostgreSqlSchemaFilter([.. includedSchemas], [.. excludedSchemas]);
        PostgreSqlInspectionSessionOptions options = DeriveOptions(statementTimeout);

        return Create(connectionString, filter, options);
    }

    private static PostgreSqlDatabaseSnapshotProvider Create(
        string connectionString,
        PostgreSqlSchemaFilter filter,
        PostgreSqlInspectionSessionOptions options)
    {
        // Resolved before the factory exists. The inventory is a validated singleton whose first
        // access does real work, so touching it here keeps every fallible step ahead of the one
        // acquisition that would otherwise need cleanup.
        PostgreSqlSqlInventory inventory = PostgreSqlSqlInventory.Default;

        // The last fallible step, and deliberately so: everything after it is pure construction
        // that cannot fail in normal operation, so there is nothing to unwind and no synchronous
        // disposal of an asynchronous resource (GC-DHI-04F-C1, R1-02).
        //
        // The connection string reaches the approved 04A boundary and is never retained here, so
        // no field, message or Data entry of this type can carry it.
        PostgreSqlConnectionFactory connectionFactory = PostgreSqlConnectionFactory.Create(connectionString);
        var runner = new PostgreSqlInspectionSessionRunner(connectionFactory, inventory);

        // The owned release for every publicly created provider is the real factory's disposal.
        return new PostgreSqlDatabaseSnapshotProvider(
            connectionFactory,
            connectionFactory.DisposeAsync,
            runner,
            options,
            filter);
    }

    /// <summary>
    /// Creates a provider over an explicit scope factory. Test-only seam: the runner is built from
    /// a deterministic fake or a recording decorator, so no connection factory is owned.
    /// </summary>
    /// <param name="scopeFactory">Supplies each capture's session scope.</param>
    /// <param name="filter">The one immutable schema filter for this provider.</param>
    /// <param name="options">The session options each capture runs under.</param>
    /// <param name="afterCompose">
    /// Optional observation point invoked after composition and before the final in-transaction
    /// cancellation checkpoint. Test-only and inert when omitted.
    /// </param>
    /// <param name="releaseResourceAsync">
    /// The owned resource release this provider hands the lifecycle. Supplying an observable one
    /// lets a test watch the real ordering — lease release, then resource release, then disposal
    /// completion — which a no-op release cannot show. Defaults to a completed task.
    /// </param>
    internal static PostgreSqlDatabaseSnapshotProvider CreateForTesting(
        IPostgreSqlInspectionSessionScopeFactory scopeFactory,
        PostgreSqlSchemaFilter filter,
        PostgreSqlInspectionSessionOptions options,
        Action? afterCompose = null,
        Func<ValueTask>? releaseResourceAsync = null)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(options);

        return new PostgreSqlDatabaseSnapshotProvider(
            connectionFactory: null,
            releaseResourceAsync ?? (static () => ValueTask.CompletedTask),
            new PostgreSqlInspectionSessionRunner(scopeFactory),
            options,
            filter,
            afterCompose);
    }

    /// <summary>
    /// Derives the session options from one public statement timeout.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The statement timeout's own range and precision rules are enforced by
    /// <see cref="PostgreSqlInspectionSessionOptions"/>, which already rejects an infinite,
    /// non-positive, fractional-millisecond, too-short or too-long value with the exact semantics
    /// this API promises. Restating them here would create a second copy that could drift.
    /// </para>
    /// <para>
    /// The lock timeout is <c>min(5000, S / 2)</c> over the exact integer millisecond value, using
    /// non-negative integer division. For every accepted <c>S</c> in <c>[100, 300000]</c> that
    /// yields a whole number of milliseconds in <c>[50, 5000]</c> that is strictly less than
    /// <c>S</c>, so no separate lower clamp is needed and the session options' own
    /// lock-below-statement rule is satisfied by construction.
    /// </para>
    /// </remarks>
    private static PostgreSqlInspectionSessionOptions DeriveOptions(TimeSpan statementTimeout)
    {
        // Validated first, so the derivation below only ever runs on an accepted value. The
        // placeholder lock timeout is the smallest the options accept, which is therefore valid
        // for every acceptable statement timeout — the shortest of those is 100 ms, and 50 ms is
        // both within range and strictly shorter. That makes this construction a pure validator:
        // the statement timeout is the only argument that can make it fail, so the caller sees the
        // statement timeout's own error rather than one about a value they never supplied.
        var validated = new PostgreSqlInspectionSessionOptions(
            statementTimeout,
            PostgreSqlInspectionSessionOptions.MinimumLockTimeout,
            IdleInTransactionTimeout);

        int statementTimeoutMilliseconds = validated.StatementTimeoutMilliseconds;
        int lockTimeoutMilliseconds = Math.Min(
            MaximumDerivedLockTimeoutMilliseconds, statementTimeoutMilliseconds / 2);

        return new PostgreSqlInspectionSessionOptions(
            statementTimeout,
            TimeSpan.FromMilliseconds(lockTimeoutMilliseconds),
            IdleInTransactionTimeout);
    }

    // --- Capture ------------------------------------------------------------------------------

    /// <summary>
    /// Captures one complete snapshot.
    /// </summary>
    /// <param name="cancellationToken">Observed throughout; propagated unchanged.</param>
    /// <exception cref="ObjectDisposedException">Disposal has begun.</exception>
    /// <exception cref="OperationCanceledException">The caller cancelled.</exception>
    public async Task<DatabaseSnapshot> CaptureAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Atomic admission: rejects once disposal starts, and keeps the data source alive for as
        // long as this capture needs it.
        _lifecycle.Admit(nameof(PostgreSqlDatabaseSnapshotProvider));

        try
        {
            DatabaseSnapshot snapshot = await _runner.RunAsync(
                _options,
                (executor, token) => CaptureCoreAsync(executor, token),
                cancellationToken).ConfigureAwait(false);

            // Cleanup has completed by now. A caller who cancelled while the session was being
            // released still receives a cancellation rather than a snapshot.
            cancellationToken.ThrowIfCancellationRequested();

            return snapshot;
        }
        finally
        {
            // Non-cancelable and unconditional: a disposer waiting on the in-flight count must
            // never be stranded by a failure or a cancellation.
            _lifecycle.Release();
        }
    }

    /// <summary>
    /// The in-transaction composition, run exactly once inside the runner's callback.
    /// </summary>
    private async ValueTask<DatabaseSnapshot> CaptureCoreAsync(
        PostgreSqlInspectionOperationExecutor executor,
        CancellationToken cancellationToken)
    {
        // C001, then C002/C003/C004 only when the server is supported. The probe owns that
        // branching; this method never re-derives a version decision.
        PostgreSqlServerProbeResult probe = await PostgreSqlServerCapabilityProbe
            .ProbeAsync(executor, cancellationToken)
            .ConfigureAwait(false);

        if (probe.VersionSupport != PostgreSqlVersionSupportStatus.Supported)
        {
            // A complete unsupported-server snapshot, not a partial supported one: real metadata
            // and capabilities, and empty object collections because nothing was queried.
            return Compose(probe, [], []);
        }

        // On a supported server the catalog capability is required. The probe raises the existing
        // fixed failure, so D001, E001 and E002 are never reached.
        cancellationToken.ThrowIfCancellationRequested();

        PostgreSqlTableSnapshotQueryResult tables = await executor
            .ReadTableSnapshotsAsync(_filter, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        bool usageStatisticsAvailable =
            probe.Capabilities.GetState(CapabilityKind.UsageStatistics).Status == CapabilityStatus.Available;

        // The identical filter instance reaches both statements; it is never rebuilt per operation.
        PostgreSqlIndexSnapshotQueryResult indexes = await executor
            .ReadIndexSnapshotsAsync(_filter, usageStatisticsAvailable, cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        DatabaseSnapshot snapshot = Compose(probe, tables.Tables, indexes.Indexes);

        // Inert in production; see the field's remarks.
        _afterCompose?.Invoke();

        // Still inside the transaction: a caller who cancelled while the snapshot was being
        // assembled must not receive it. The snapshot exists at this point and is deliberately
        // discarded rather than returned.
        cancellationToken.ThrowIfCancellationRequested();

        return snapshot;
    }

    // --- Composition --------------------------------------------------------------------------

    /// <summary>
    /// Validates cross-object closure, derives schemas, materializes the frozen order and builds
    /// the snapshot.
    /// </summary>
    private static DatabaseSnapshot Compose(
        PostgreSqlServerProbeResult probe,
        IReadOnlyList<TableSnapshot> tables,
        IReadOnlyList<IndexSnapshot> indexes)
    {
        // Every index must belong to a table in this same capture. E001 reads pg_index.indrelid
        // from the same catalog snapshot under the same schema predicate as D001, so a missing
        // table means the two disagreed — inconsistent composition, never an index to drop.
        var tableIdentities = new HashSet<(string SchemaName, string TableName)>();
        foreach (TableSnapshot table in tables)
        {
            _ = tableIdentities.Add((table.SchemaName, table.TableName));
        }

        foreach (IndexSnapshot index in indexes)
        {
            if (!tableIdentities.Contains((index.SchemaName, index.TableName)))
            {
                throw new PostgreSqlSnapshotCompositionException();
            }
        }

        // Derived from the validated tables, never from new SQL. Closure above guarantees every
        // index schema is already a member.
        var schemaNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (TableSnapshot table in tables)
        {
            _ = schemaNames.Add(table.SchemaName);
        }

        // Materialized explicitly: hash-set or dictionary enumeration is never an output contract.
        string[] orderedSchemaNames = [.. schemaNames];
        Array.Sort(orderedSchemaNames, StringComparer.Ordinal);

        var schemas = new SchemaSnapshot[orderedSchemaNames.Length];
        for (var index = 0; index < orderedSchemaNames.Length; index++)
        {
            schemas[index] = new SchemaSnapshot(orderedSchemaNames[index]);
        }

        TableSnapshot[] orderedTables = [.. tables];
        Array.Sort(orderedTables, CompareTables);

        IndexSnapshot[] orderedIndexes = [.. indexes];
        Array.Sort(orderedIndexes, CompareIndexes);

        try
        {
            return new DatabaseSnapshot(
                probe.Metadata,
                schemas,
                orderedTables,
                orderedIndexes,
                probe.Capabilities,
                probe.Statistics);
        }
        catch (Exception exception) when (exception is ArgumentException or ArgumentOutOfRangeException)
        {
            // Core's guards are correct but their duplicate messages name the offending schema,
            // table or index. Any state reaching them here is one this adapter failed to reject
            // first, and it must still surface without those names. Deliberately narrow: a
            // cancellation, an Npgsql fault or any other exception is not caught.
            throw new PostgreSqlSnapshotCompositionException();
        }
    }

    private static int CompareTables(TableSnapshot left, TableSnapshot right)
    {
        int bySchema = string.CompareOrdinal(left.SchemaName, right.SchemaName);
        return bySchema != 0 ? bySchema : string.CompareOrdinal(left.TableName, right.TableName);
    }

    private static int CompareIndexes(IndexSnapshot left, IndexSnapshot right)
    {
        int bySchema = string.CompareOrdinal(left.SchemaName, right.SchemaName);
        if (bySchema != 0)
        {
            return bySchema;
        }

        int byTable = string.CompareOrdinal(left.TableName, right.TableName);
        return byTable != 0 ? byTable : string.CompareOrdinal(left.IndexName, right.IndexName);
    }

    // --- Disposal -----------------------------------------------------------------------------

    /// <summary>
    /// Prevents new captures, waits for admitted ones to finish without cancelling them, then
    /// releases the connection factory exactly once.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe under concurrent calls. Draining and releasing are one operation, so
    /// every caller returns only after the factory has actually been disposed and every caller
    /// observes the same outcome — including the same failure if disposing it threw.
    /// </remarks>
    public ValueTask DisposeAsync() => _lifecycle.DisposeAsync(_releaseResourceAsync);
}
