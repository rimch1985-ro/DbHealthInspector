using DbHealthInspector.PostgreSql.Sessions;

namespace DbHealthInspector.UnitTests.Sessions;

/// <summary>
/// The timeout policy frozen by GC-DHI-04B §6: exact defaults, exact bounds, the strict
/// lock &lt; statement relation, and rejection of every malformed value.
/// </summary>
public sealed class PostgreSqlInspectionSessionOptionsTests
{
    private static PostgreSqlInspectionSessionOptions Create(
        TimeSpan? statement = null, TimeSpan? @lock = null, TimeSpan? idle = null) =>
        new(statement ?? TimeSpan.FromSeconds(30), @lock ?? TimeSpan.FromSeconds(5), idle ?? TimeSpan.FromSeconds(60));

    // --- Defaults ---------------------------------------------------------------------------

    [Fact]
    public void Default_UsesTheFrozenAdapterDefaults()
    {
        PostgreSqlInspectionSessionOptions options = PostgreSqlInspectionSessionOptions.Default;

        Assert.Equal(TimeSpan.FromSeconds(30), options.StatementTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), options.LockTimeout);
        Assert.Equal(TimeSpan.FromSeconds(60), options.IdleInTransactionTimeout);
    }

    [Fact]
    public void Default_ExposesTheMatchingMillisecondValues()
    {
        PostgreSqlInspectionSessionOptions options = PostgreSqlInspectionSessionOptions.Default;

        Assert.Equal(30_000, options.StatementTimeoutMilliseconds);
        Assert.Equal(5_000, options.LockTimeoutMilliseconds);
        Assert.Equal(60_000, options.IdleInTransactionTimeoutMilliseconds);
    }

    [Fact]
    public void Default_IsStableAcrossReads()
    {
        Assert.Same(PostgreSqlInspectionSessionOptions.Default, PostgreSqlInspectionSessionOptions.Default);
    }

    // --- Exact bounds -----------------------------------------------------------------------

    [Fact]
    public void Constructor_AcceptsEveryExactMinimum()
    {
        // The minimum statement timeout (100 ms) must still leave room for a strictly smaller
        // lock timeout, and 50 ms is the lock minimum, so this is the tightest legal triple.
        PostgreSqlInspectionSessionOptions options = Create(
            TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(250));

        Assert.Equal(100, options.StatementTimeoutMilliseconds);
        Assert.Equal(50, options.LockTimeoutMilliseconds);
        Assert.Equal(250, options.IdleInTransactionTimeoutMilliseconds);
    }

    [Fact]
    public void Constructor_AcceptsEveryExactMaximum()
    {
        PostgreSqlInspectionSessionOptions options = Create(
            TimeSpan.FromMinutes(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(10));

        Assert.Equal(300_000, options.StatementTimeoutMilliseconds);
        Assert.Equal(30_000, options.LockTimeoutMilliseconds);
        Assert.Equal(600_000, options.IdleInTransactionTimeoutMilliseconds);
    }

    [Fact]
    public void Constructor_RejectsStatementTimeoutBelowMinimum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(statement: TimeSpan.FromMilliseconds(99), @lock: TimeSpan.FromMilliseconds(50)));

        Assert.Equal("statementTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsStatementTimeoutAboveMaximum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(statement: TimeSpan.FromMinutes(5) + TimeSpan.FromMilliseconds(1)));

        Assert.Equal("statementTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsLockTimeoutBelowMinimum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(@lock: TimeSpan.FromMilliseconds(49)));

        Assert.Equal("lockTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsLockTimeoutAboveMaximum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(statement: TimeSpan.FromMinutes(5), @lock: TimeSpan.FromSeconds(30) + TimeSpan.FromMilliseconds(1)));

        Assert.Equal("lockTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsIdleTimeoutBelowMinimum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(idle: TimeSpan.FromMilliseconds(249)));

        Assert.Equal("idleInTransactionTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsIdleTimeoutAboveMaximum()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(idle: TimeSpan.FromMinutes(10) + TimeSpan.FromMilliseconds(1)));

        Assert.Equal("idleInTransactionTimeout", exception.ParamName);
    }

    // --- Zero, negative, infinite, sub-millisecond, overflow ---------------------------------

    [Theory]
    [InlineData("statementTimeout")]
    [InlineData("lockTimeout")]
    [InlineData("idleInTransactionTimeout")]
    public void Constructor_RejectsZero(string paramName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateWithZeroFor(paramName));

        Assert.Equal(paramName, exception.ParamName);
    }

    [Theory]
    [InlineData("statementTimeout")]
    [InlineData("lockTimeout")]
    [InlineData("idleInTransactionTimeout")]
    public void Constructor_RejectsNegative(string paramName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateWithValueFor(paramName, TimeSpan.FromSeconds(-1)));

        Assert.Equal(paramName, exception.ParamName);
    }

    [Theory]
    [InlineData("statementTimeout")]
    [InlineData("lockTimeout")]
    [InlineData("idleInTransactionTimeout")]
    public void Constructor_RejectsInfinite(string paramName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateWithValueFor(paramName, Timeout.InfiniteTimeSpan));

        Assert.Equal(paramName, exception.ParamName);
    }

    [Theory]
    [InlineData("statementTimeout")]
    [InlineData("lockTimeout")]
    [InlineData("idleInTransactionTimeout")]
    public void Constructor_RejectsSubMillisecondPrecision(string paramName)
    {
        // 500.5 ms cannot be expressed as a whole number of milliseconds for set_config.
        var value = TimeSpan.FromTicks(TimeSpan.TicksPerMillisecond * 500 + 5_000);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateWithValueFor(paramName, value));

        Assert.Equal(paramName, exception.ParamName);
    }

    [Theory]
    [InlineData("statementTimeout")]
    [InlineData("lockTimeout")]
    [InlineData("idleInTransactionTimeout")]
    public void Constructor_RejectsValuesThatWouldOverflowMilliseconds(string paramName)
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => CreateWithValueFor(paramName, TimeSpan.MaxValue));

        Assert.Equal(paramName, exception.ParamName);
    }

    // --- Lock/statement relation --------------------------------------------------------------

    [Fact]
    public void Constructor_RejectsLockTimeoutEqualToStatementTimeout()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(statement: TimeSpan.FromSeconds(10), @lock: TimeSpan.FromSeconds(10)));

        Assert.Equal("lockTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_RejectsLockTimeoutGreaterThanStatementTimeout()
    {
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(statement: TimeSpan.FromSeconds(10), @lock: TimeSpan.FromSeconds(20)));

        Assert.Equal("lockTimeout", exception.ParamName);
    }

    [Fact]
    public void Constructor_AcceptsLockTimeoutOneMillisecondBelowStatementTimeout()
    {
        var statement = TimeSpan.FromSeconds(10);
        TimeSpan @lock = statement - TimeSpan.FromMilliseconds(1);

        PostgreSqlInspectionSessionOptions options = Create(statement, @lock);

        Assert.Equal(9_999, options.LockTimeoutMilliseconds);
    }

    // --- Immutability and message hygiene -----------------------------------------------------

    [Fact]
    public void PropertySurface_HasNoSetters()
    {
        System.Reflection.PropertyInfo[] properties = typeof(PostgreSqlInspectionSessionOptions)
            .GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.NotEmpty(properties);
        Assert.All(properties, property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void RejectionMessage_DoesNotIncludeTheOffendingValue()
    {
        // 123456789 ms is far above the maximum; the exception must identify which option was
        // wrong without echoing operational configuration back to the caller.
        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => Create(statement: TimeSpan.FromMilliseconds(123_456_789)));

        Assert.DoesNotContain("123456789", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789", exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.ActualValue);
    }

    private static PostgreSqlInspectionSessionOptions CreateWithZeroFor(string paramName) =>
        CreateWithValueFor(paramName, TimeSpan.Zero);

    private static PostgreSqlInspectionSessionOptions CreateWithValueFor(string paramName, TimeSpan value) => paramName switch
    {
        "statementTimeout" => Create(statement: value),
        "lockTimeout" => Create(@lock: value),
        "idleInTransactionTimeout" => Create(idle: value),
        _ => throw new ArgumentOutOfRangeException(nameof(paramName), paramName, "Unknown option."),
    };
}
