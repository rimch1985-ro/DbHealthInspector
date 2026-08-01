using System.Runtime.ExceptionServices;
using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.PostgreSql.Sql;
using Npgsql;

namespace DbHealthInspector.PostgreSql.Sessions;

/// <summary>
/// Runs one authorized operation inside a fully initialized, verified, read-only, rollback-only
/// PostgreSQL inspection session.
/// </summary>
/// <remarks>
/// <para>
/// The sequence is invariant: validate options, check cancellation, open, begin
/// <c>RepeatableRead</c>, B001, B002, B003, verify, invoke the operation exactly once, then roll
/// back and dispose the transaction before the connection. The operation receives only a
/// restricted <see cref="PostgreSqlInspectionOperationExecutor"/> — never the full executor, a
/// connection, a transaction or raw SQL — so it cannot re-run or undo the initialization the
/// runner just verified.
/// </para>
/// <para>
/// Each stage catches only what it expects: <see cref="NpgsqlException"/> and an
/// <see cref="OperationCanceledException"/> that is <b>not</b> associated with the requested
/// token. Everything else propagates untouched. The single transparent capture used to keep a
/// primary failure authoritative lives in <see cref="PostgreSqlAsyncCleanup"/> and in the one
/// documented capture below; neither classifies nor sanitizes.
/// </para>
/// </remarks>
internal sealed class PostgreSqlInspectionSessionRunner
{
    private readonly IPostgreSqlInspectionSessionScopeFactory _scopeFactory;

    /// <summary>
    /// Creates a runner over the GC-DHI-04A connection factory and the canonical SQL inventory.
    /// The runner does not own the factory and never disposes it.
    /// </summary>
    internal PostgreSqlInspectionSessionRunner(PostgreSqlConnectionFactory connectionFactory, PostgreSqlSqlInventory inventory)
        : this(new PostgreSqlInspectionSessionScopeFactory(connectionFactory, inventory))
    {
    }

    /// <summary>
    /// Creates a runner over an explicit scope factory. Production supplies
    /// <see cref="PostgreSqlInspectionSessionScopeFactory"/>; unit tests supply a deterministic
    /// fake so each stage can be made to fail in isolation.
    /// </summary>
    internal PostgreSqlInspectionSessionRunner(IPostgreSqlInspectionSessionScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);

        _scopeFactory = scopeFactory;
    }

    /// <summary>
    /// Initializes and verifies a session, runs <paramref name="operation"/> exactly once, and
    /// always finishes through rollback and disposal.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="operation"/> is <see langword="null"/>.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="PostgreSqlInspectionSessionException">An expected stage failure occurred.</exception>
    /// <exception cref="PostgreSqlConnectionException">Opening the connection failed; already sanitized by GC-DHI-04A.</exception>
    internal async ValueTask<TResult> RunAsync<TResult>(
        PostgreSqlInspectionSessionOptions options,
        Func<PostgreSqlInspectionOperationExecutor, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(operation);

        // Options were validated when they were constructed, so by the time one exists the policy
        // is already known-good; checking cancellation here keeps a pre-canceled caller from
        // reaching the server at all.
        cancellationToken.ThrowIfCancellationRequested();

        IPostgreSqlInspectionSessionScope scope = _scopeFactory.Create();

        ExceptionDispatchInfo? primary = null;
        TResult result = default!;
        try
        {
            result = await ExecuteSessionAsync(scope, options, operation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            // The single transparent capture in this type (GC-DHI-04B-C1, F-01). It does not
            // inspect, classify, sanitize or rewrite anything: it preserves the exception exactly
            // — same instance, same type, same message, same stack — so that the cleanup steps
            // below can all run without any of them being able to replace it.
            primary = ExceptionDispatchInfo.Capture(exception);
        }

        // Every step is attempted regardless of which earlier one failed, and the transaction is
        // always released before the connection.
        ExceptionDispatchInfo? cleanup = await PostgreSqlAsyncCleanup.RunAllAsync(
            scope.RollbackAsync,
            scope.DisposeTransactionAsync,
            scope.DisposeConnectionAsync).ConfigureAwait(false);

        // Requested cancellation and any primary failure outrank every cleanup outcome.
        primary?.Throw();

        if (cleanup is not null)
        {
            // Only an expected PostgreSQL failure becomes a sanitized CleanupFailed. Anything
            // else is a defect and is re-thrown exactly as it was captured.
            if (cleanup.SourceException is NpgsqlException)
            {
                throw new PostgreSqlInspectionSessionException(PostgreSqlInspectionSessionFailureKind.CleanupFailed);
            }

            cleanup.Throw();
        }

        return result;
    }

    private static async ValueTask<TResult> ExecuteSessionAsync<TResult>(
        IPostgreSqlInspectionSessionScope scope,
        PostgreSqlInspectionSessionOptions options,
        Func<PostgreSqlInspectionOperationExecutor, CancellationToken, ValueTask<TResult>> operation,
        CancellationToken cancellationToken)
    {
        // Open. A PostgreSqlConnectionException from GC-DHI-04A is already sanitized and
        // propagates unchanged — wrapping it again would add nothing but a second layer — and so
        // does the cancellation contract that boundary already froze.
        await scope.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

        // Every stage below uses only *typed* catches. There is deliberately no
        // `catch (Exception)` in any classification path: an unexpected type is never even
        // considered for sanitization, it simply never matches.
        const PostgreSqlInspectionSessionFailureKind initialization =
            PostgreSqlInspectionSessionFailureKind.InitializationFailed;

        try
        {
            await scope.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (IsUnrelatedCancellation(exception, cancellationToken))
        {
            throw Sanitize(exception, initialization, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw Sanitize(exception, initialization, cancellationToken);
        }

        PostgreSqlSqlExecutor executor = scope.CreateExecutor();

        try
        {
            // B001 must be the first statement executed inside the transaction.
            await executor.ExecuteSetTransactionReadOnlyAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (IsUnrelatedCancellation(exception, cancellationToken))
        {
            throw Sanitize(exception, initialization, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw Sanitize(exception, initialization, cancellationToken);
        }
        catch (PostgreSqlSqlResultShapeException exception)
        {
            throw Sanitize(exception, initialization, cancellationToken);
        }

        try
        {
            await executor.ApplyLocalTimeoutsAsync(
                options.StatementTimeoutMilliseconds,
                options.LockTimeoutMilliseconds,
                options.IdleInTransactionTimeoutMilliseconds,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (IsUnrelatedCancellation(exception, cancellationToken))
        {
            throw Sanitize(exception, initialization, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw Sanitize(exception, initialization, cancellationToken);
        }
        catch (PostgreSqlSqlResultShapeException exception)
        {
            throw Sanitize(exception, initialization, cancellationToken);
        }

        const PostgreSqlInspectionSessionFailureKind verification =
            PostgreSqlInspectionSessionFailureKind.VerificationFailed;

        PostgreSqlInspectionSessionState state;
        try
        {
            state = await executor.VerifySessionStateAsync(
                options.StatementTimeoutMilliseconds,
                options.LockTimeoutMilliseconds,
                options.IdleInTransactionTimeoutMilliseconds,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (IsUnrelatedCancellation(exception, cancellationToken))
        {
            throw Sanitize(exception, verification, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw Sanitize(exception, verification, cancellationToken);
        }
        catch (PostgreSqlSqlResultShapeException exception)
        {
            throw Sanitize(exception, verification, cancellationToken);
        }

        if (!state.IsVerified)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new PostgreSqlInspectionSessionException(verification);
        }

        var operationExecutor = new PostgreSqlInspectionOperationExecutor(executor);
        try
        {
            // Exactly one invocation, and only after every initialization and verification step
            // above has succeeded.
            return await operation(operationExecutor, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (IsUnrelatedCancellation(exception, cancellationToken))
        {
            throw Sanitize(exception, PostgreSqlInspectionSessionFailureKind.ExecutionFailed, cancellationToken);
        }
        catch (NpgsqlException exception)
        {
            throw Sanitize(exception, PostgreSqlInspectionSessionFailureKind.ExecutionFailed, cancellationToken);
        }
    }

    /// <summary>
    /// Whether an <see cref="OperationCanceledException"/> is <b>not</b> associated with the
    /// requested token, and may therefore be sanitized like any other expected stage failure.
    /// </summary>
    /// <remarks>
    /// Reuses the association rule frozen by GC-DHI-04A
    /// (<see cref="PostgreSqlConnectionFactory.IsRequestedCancellation"/>): a genuinely requested
    /// cancellation is never converted into a session failure, and two
    /// <see cref="CancellationToken.None"/> values never count as association.
    /// </remarks>
    internal static bool IsUnrelatedCancellation(OperationCanceledException exception, CancellationToken requestedToken) =>
        !PostgreSqlConnectionFactory.IsRequestedCancellation(exception, requestedToken);

    /// <summary>
    /// Converts an expected infrastructure failure into a sanitized session exception, after
    /// giving requested cancellation priority: the token is checked once more because it may have
    /// been canceled during the failed stage, and cancellation always outranks reporting a stage
    /// failure.
    /// </summary>
    /// <remarks>
    /// <paramref name="exception"/> is accepted only to prove the seam is wired to a real failure.
    /// Nothing about it — message, SQLSTATE, <c>Detail</c>, <c>Hint</c>, schema, table, column,
    /// constraint, stack trace or <see cref="Exception.Data"/> — is read or copied.
    /// </remarks>
    internal static PostgreSqlInspectionSessionException Sanitize(
        Exception exception, PostgreSqlInspectionSessionFailureKind failureKind, CancellationToken cancellationToken)
    {
        _ = exception;
        cancellationToken.ThrowIfCancellationRequested();
        return new PostgreSqlInspectionSessionException(failureKind);
    }
}
