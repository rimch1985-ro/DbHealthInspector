namespace DbHealthInspector.PostgreSql.Sessions;

/// <summary>
/// The immutable, validated timeout policy for one inspection session. Validated entirely in the
/// constructor, so an instance that exists at all is one the runner can apply without further
/// checks — and validation therefore always happens before a connection is opened.
/// </summary>
/// <remarks>
/// <para>
/// Bounds are the single normative set frozen by GC-DHI-04B §6. These are adapter defaults, not
/// final CLI defaults.
/// </para>
/// <para>
/// Rejection messages deliberately never include the offending value: options can carry
/// operational detail, and these exceptions are the one place option data could otherwise escape
/// toward a caller. The <c>ParamName</c> identifies which option was wrong, which is enough to
/// act on.
/// </para>
/// </remarks>
internal sealed class PostgreSqlInspectionSessionOptions
{
    internal static readonly TimeSpan MinimumStatementTimeout = TimeSpan.FromMilliseconds(100);
    internal static readonly TimeSpan MaximumStatementTimeout = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan MinimumLockTimeout = TimeSpan.FromMilliseconds(50);
    internal static readonly TimeSpan MaximumLockTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan MinimumIdleInTransactionTimeout = TimeSpan.FromMilliseconds(250);
    internal static readonly TimeSpan MaximumIdleInTransactionTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The frozen adapter defaults: 30 s statement, 5 s lock, 60 s idle-in-transaction.
    /// </summary>
    internal static PostgreSqlInspectionSessionOptions Default { get; } = new(
        TimeSpan.FromSeconds(30),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(60));

    /// <summary>
    /// Maximum time a single statement may run.
    /// </summary>
    internal TimeSpan StatementTimeout { get; }

    /// <summary>
    /// Maximum time a statement may wait for a lock. Strictly less than
    /// <see cref="StatementTimeout"/> so a lock wait always surfaces as a lock timeout rather
    /// than being masked by the statement timeout.
    /// </summary>
    internal TimeSpan LockTimeout { get; }

    /// <summary>
    /// Maximum time the transaction may sit idle before PostgreSQL terminates it.
    /// </summary>
    internal TimeSpan IdleInTransactionTimeout { get; }

    /// <summary>
    /// <see cref="StatementTimeout"/> in whole milliseconds, as bound to B002/B003 <c>$1</c>.
    /// </summary>
    internal int StatementTimeoutMilliseconds { get; }

    /// <summary>
    /// <see cref="LockTimeout"/> in whole milliseconds, as bound to B002/B003 <c>$2</c>.
    /// </summary>
    internal int LockTimeoutMilliseconds { get; }

    /// <summary>
    /// <see cref="IdleInTransactionTimeout"/> in whole milliseconds, as bound to B002/B003
    /// <c>$3</c>.
    /// </summary>
    internal int IdleInTransactionTimeoutMilliseconds { get; }

    internal PostgreSqlInspectionSessionOptions(
        TimeSpan statementTimeout,
        TimeSpan lockTimeout,
        TimeSpan idleInTransactionTimeout)
    {
        StatementTimeoutMilliseconds = Validate(
            statementTimeout, MinimumStatementTimeout, MaximumStatementTimeout, nameof(statementTimeout));
        LockTimeoutMilliseconds = Validate(
            lockTimeout, MinimumLockTimeout, MaximumLockTimeout, nameof(lockTimeout));
        IdleInTransactionTimeoutMilliseconds = Validate(
            idleInTransactionTimeout, MinimumIdleInTransactionTimeout, MaximumIdleInTransactionTimeout, nameof(idleInTransactionTimeout));

        if (lockTimeout >= statementTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(lockTimeout), "The lock timeout must be strictly shorter than the statement timeout.");
        }

        StatementTimeout = statementTimeout;
        LockTimeout = lockTimeout;
        IdleInTransactionTimeout = idleInTransactionTimeout;
    }

    private static int Validate(TimeSpan value, TimeSpan minimum, TimeSpan maximum, string paramName)
    {
        if (value == Timeout.InfiniteTimeSpan)
        {
            throw new ArgumentOutOfRangeException(paramName, "An infinite timeout is not allowed.");
        }

        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(paramName, "The timeout must be positive.");
        }

        if (value.Ticks % TimeSpan.TicksPerMillisecond != 0)
        {
            throw new ArgumentOutOfRangeException(paramName, "The timeout must be a whole number of milliseconds.");
        }

        if (value < minimum)
        {
            throw new ArgumentOutOfRangeException(paramName, "The timeout is below the allowed minimum.");
        }

        if (value > maximum)
        {
            throw new ArgumentOutOfRangeException(paramName, "The timeout is above the allowed maximum.");
        }

        // The range check above already bounds the value far below int.MaxValue milliseconds;
        // the checked conversion is defence in depth so that a future bound change can never
        // silently wrap.
        try
        {
            return checked((int)(value.Ticks / TimeSpan.TicksPerMillisecond));
        }
        catch (OverflowException)
        {
            throw new ArgumentOutOfRangeException(paramName, "The timeout cannot be expressed in milliseconds.");
        }
    }
}
