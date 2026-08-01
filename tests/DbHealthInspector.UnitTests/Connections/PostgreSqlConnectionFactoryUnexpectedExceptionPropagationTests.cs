using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.UnitTests.Connections.TestSupport;

namespace DbHealthInspector.UnitTests.Connections;

/// <summary>
/// Confirms that <see cref="PostgreSqlConnectionFactory.OpenConnectionAsync"/> only ever
/// sanitizes an <see cref="Npgsql.NpgsqlException"/> or an unrelated
/// <see cref="OperationCanceledException"/> (GC-DHI-04A-C1, F-01): every other exception type the
/// opener might throw — representing a programming defect or a lifecycle violation, not an
/// ordinary connection failure — must propagate with its original type, message and identity
/// completely unchanged, exactly once, with no sanitization and no retry.
/// </summary>
public sealed class PostgreSqlConnectionFactoryUnexpectedExceptionPropagationTests
{
    private const string ValidConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass";

    [Fact]
    public async Task InvalidOperationException_PropagatesUnchanged()
    {
        var original = new InvalidOperationException("synthetic invalid operation");
        var opener = FakePostgreSqlConnectionOpener.Throwing(original);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        InvalidOperationException thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
        Assert.Equal(1, opener.CallCount);
    }

    [Fact]
    public async Task ObjectDisposedException_PropagatesUnchanged()
    {
        var original = new ObjectDisposedException("synthetic-disposed-object");
        var opener = FakePostgreSqlConnectionOpener.Throwing(original);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        ObjectDisposedException thrown = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
        Assert.Equal(1, opener.CallCount);
    }

    [Fact]
    public async Task ArgumentException_PropagatesUnchanged()
    {
        var original = new ArgumentException("synthetic argument failure", "someParameter");
        var opener = FakePostgreSqlConnectionOpener.Throwing(original);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        ArgumentException thrown = await Assert.ThrowsAsync<ArgumentException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
        Assert.Equal(1, opener.CallCount);
    }

    [Fact]
    public async Task NullReferenceException_PropagatesUnchanged()
    {
        // NullReferenceException is reserved for the runtime (CA2201) and must not be
        // constructed directly; trigger a genuine one instead of faking its shape.
        NullReferenceException original;
        try
        {
            string? nullString = null;
            _ = nullString!.Length;
            throw new InvalidOperationException("unreachable: the line above always throws");
        }
        catch (NullReferenceException runtimeException)
        {
            original = runtimeException;
        }

        var opener = FakePostgreSqlConnectionOpener.Throwing(original);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        NullReferenceException thrown = await Assert.ThrowsAsync<NullReferenceException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
        Assert.Equal(1, opener.CallCount);
    }

    [Fact]
    public async Task TimeoutException_PropagatesUnchanged()
    {
        var original = new TimeoutException("synthetic timeout");
        var opener = FakePostgreSqlConnectionOpener.Throwing(original);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        TimeoutException thrown = await Assert.ThrowsAsync<TimeoutException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Same(original, thrown);
        Assert.Equal(1, opener.CallCount);
    }
}
