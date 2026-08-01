using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.UnitTests.Connections.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Connections;

/// <summary>
/// Covers <see cref="PostgreSqlConnectionFactory.OpenConnectionAsync"/>'s cancellation semantics
/// and the small internal helpers it is built from. Every case uses
/// <see cref="FakePostgreSqlConnectionOpener"/> — no real socket, port, DNS lookup, or
/// PostgreSQL server is involved anywhere in this file; a real-server open test remains
/// deferred to GC-DHI-04B.
/// </summary>
public sealed class PostgreSqlConnectionFactoryOpenCancellationTests
{
    private const string ValidConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass";

    // --- Full cancellation matrix (GC-DHI-04A-C1 §10) ----------------------------------------

    [Fact]
    public async Task TokenAlreadyCanceledBeforeCalling_ThrowsOceAndNeverInvokesTheOpener()
    {
        var opener = FakePostgreSqlConnectionOpener.ReturningConnection();
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => factory.OpenConnectionAsync(cts.Token).AsTask());

        Assert.Equal(0, opener.CallCount);
    }

    [Fact]
    public async Task OpenerThrowsOceCarryingTheSameToken_Propagates()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var opener = FakePostgreSqlConnectionOpener.Throwing(new OperationCanceledException(cts.Token));
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => factory.OpenConnectionAsync(cts.Token).AsTask());

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Equal(1, opener.CallCount);
    }

    [Fact]
    public async Task OpenerThrowsOceCarryingCancellationTokenNone_Sanitizes()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var opener = FakePostgreSqlConnectionOpener.Throwing(new OperationCanceledException(CancellationToken.None));
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        PostgreSqlConnectionException exception = await Assert.ThrowsAsync<PostgreSqlConnectionException>(
            () => factory.OpenConnectionAsync(cts.Token).AsTask());

        Assert.Equal("The PostgreSQL connection could not be opened.", exception.Message);
    }

    [Fact]
    public async Task OpenerThrowsOceCarryingAnUnrelatedUncanceledToken_Sanitizes()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var unrelated = new CancellationTokenSource();
        var opener = FakePostgreSqlConnectionOpener.Throwing(new OperationCanceledException(unrelated.Token));
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        await Assert.ThrowsAsync<PostgreSqlConnectionException>(() => factory.OpenConnectionAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task OpenerThrowsOceCarryingAnUnrelatedAlreadyCanceledToken_Sanitizes()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using var unrelated = new CancellationTokenSource();
        unrelated.Cancel();
        var opener = FakePostgreSqlConnectionOpener.Throwing(new OperationCanceledException(unrelated.Token));
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        await Assert.ThrowsAsync<PostgreSqlConnectionException>(() => factory.OpenConnectionAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task CallerTokenCanceledBeforeSanitizingNpgsqlException_CancellationDominates()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var opener = FakePostgreSqlConnectionOpener.Throwing(new NpgsqlException("synthetic failure"), beforeThrow: cts.Cancel);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        await Assert.ThrowsAsync<OperationCanceledException>(() => factory.OpenConnectionAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task CallerTokenCanceledBeforeSanitizingUnrelatedOce_CancellationDominates()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var opener = FakePostgreSqlConnectionOpener.Throwing(new OperationCanceledException(CancellationToken.None), beforeThrow: cts.Cancel);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        await Assert.ThrowsAsync<OperationCanceledException>(() => factory.OpenConnectionAsync(cts.Token).AsTask());
    }

    [Fact]
    public async Task BothTokensAreCancellationTokenNone_NoAssociation_Sanitizes()
    {
        var opener = FakePostgreSqlConnectionOpener.Throwing(new OperationCanceledException(CancellationToken.None));
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        await Assert.ThrowsAsync<PostgreSqlConnectionException>(() => factory.OpenConnectionAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task PassesTheExactRequestedTokenToTheOpener()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var opener = FakePostgreSqlConnectionOpener.ReturningConnection();
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        await using NpgsqlConnection connection = await factory.OpenConnectionAsync(cts.Token);

        Assert.Equal(cts.Token, opener.LastCancellationToken);
    }

    [Fact]
    public async Task Succeeds_ReturnsExactlyWhatTheOpenerReturned()
    {
        using var returned = new NpgsqlConnection("Host=unused-success-marker");
        var opener = FakePostgreSqlConnectionOpener.ReturningConnection(returned);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        NpgsqlConnection connection = await factory.OpenConnectionAsync(TestContext.Current.CancellationToken);

        Assert.Same(returned, connection);
    }

    // --- IsRequestedCancellation --------------------------------------------------------------

    [Fact]
    public void IsRequestedCancellation_ReturnsTrue_WhenTheRequestedTokenIsAlreadyCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var exception = new OperationCanceledException(CancellationToken.None);

        Assert.True(PostgreSqlConnectionFactory.IsRequestedCancellation(exception, cts.Token));
    }

    [Fact]
    public void IsRequestedCancellation_ReturnsTrue_WhenTheExceptionTokenEqualsTheRequestedTokenBeforeEitherIsCanceled()
    {
        using var cts = new CancellationTokenSource();
        var exception = new OperationCanceledException(cts.Token);

        Assert.True(PostgreSqlConnectionFactory.IsRequestedCancellation(exception, cts.Token));
    }

    [Fact]
    public void IsRequestedCancellation_ReturnsFalse_WhenTheExceptionCarriesCancellationTokenNoneAndTheRequestedTokenIsUncanceled()
    {
        using var cts = new CancellationTokenSource();
        var exception = new OperationCanceledException(CancellationToken.None);

        Assert.False(PostgreSqlConnectionFactory.IsRequestedCancellation(exception, cts.Token));
    }

    [Fact]
    public void IsRequestedCancellation_ReturnsFalse_WhenTheExceptionCarriesAnUnrelatedUncanceledToken()
    {
        using var cts = new CancellationTokenSource();
        using var unrelated = new CancellationTokenSource();
        var exception = new OperationCanceledException(unrelated.Token);

        Assert.False(PostgreSqlConnectionFactory.IsRequestedCancellation(exception, cts.Token));
    }

    [Fact]
    public void IsRequestedCancellation_ReturnsFalse_WhenBothTokensAreCancellationTokenNone()
    {
        // CancellationToken.None == CancellationToken.None is structurally true, but neither is
        // cancelable, so the CanBeCanceled guard must prevent this from counting as association.
        var exception = new OperationCanceledException(CancellationToken.None);

        Assert.False(PostgreSqlConnectionFactory.IsRequestedCancellation(exception, CancellationToken.None));
    }

    // --- SanitizeOrThrowIfCanceled / SanitizeOpenFailure --------------------------------------

    [Fact]
    public void SanitizeOrThrowIfCanceled_ThrowsOperationCanceledException_WhenTheRequestedTokenIsCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(
            () => PostgreSqlConnectionFactory.SanitizeOrThrowIfCanceled(new NpgsqlException("boom"), cts.Token));
    }

    [Fact]
    public void SanitizeOrThrowIfCanceled_ReturnsASanitizedException_WhenTheRequestedTokenIsNotCanceled()
    {
        using var cts = new CancellationTokenSource();

        PostgreSqlConnectionException exception = PostgreSqlConnectionFactory.SanitizeOrThrowIfCanceled(
            new NpgsqlException("boom"), cts.Token);

        Assert.Equal("The PostgreSQL connection could not be opened.", exception.Message);
    }

    [Fact]
    public void SanitizeOpenFailure_NeverRetainsTheOriginalMessageTypeOrData()
    {
        const string sensitiveMessage = "password authentication failed for user \"MARKERSANITIZEUSER\"";
        var original = new NpgsqlException(sensitiveMessage);
        original.Data["host"] = "prod-db.internal";

        PostgreSqlConnectionException sanitized = PostgreSqlConnectionFactory.SanitizeOpenFailure(original);

        Assert.Equal("The PostgreSQL connection could not be opened.", sanitized.Message);
        Assert.DoesNotContain("MARKERSANITIZEUSER", sanitized.Message);
        Assert.Null(sanitized.InnerException);
        Assert.Empty(sanitized.Data);
        Assert.IsNotType<NpgsqlException>(sanitized);
    }
}
