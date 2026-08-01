using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.UnitTests.Connections.TestSupport;

namespace DbHealthInspector.UnitTests.Connections;

public sealed class PostgreSqlConnectionFactoryDisposalTests
{
    private const string ValidConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass";

    [Fact]
    public async Task DisposeAsync_FirstCall_DisposesTheUnderlyingDataSource()
    {
        PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString);

        await factory.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task DisposeAsync_SecondCall_DoesNotThrow()
    {
        PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString);

        await factory.DisposeAsync();
        await factory.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_ManySequentialCalls_DoNotThrow()
    {
        PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString);

        for (var i = 0; i < 10; i++)
        {
            await factory.DisposeAsync();
        }
    }

    [Fact]
    public async Task DisposeAsync_ConcurrentCalls_DoNotThrow()
    {
        PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString);

        Task[] disposals = Enumerable.Range(0, 8).Select(_ => factory.DisposeAsync().AsTask()).ToArray();

        await Task.WhenAll(disposals);
    }

    [Fact]
    public async Task Metadata_RemainsReadable_AfterDisposal()
    {
        PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(
            "Host=db.example.com;Port=6543;Database=testdb;Username=testuser;Password=testpass");

        await factory.DisposeAsync();

        Assert.Equal(PostgreSqlConnectionTargetKind.NetworkHost, factory.Metadata.TargetKind);
        Assert.Equal(6543, factory.Metadata.Port);
    }

    [Fact]
    public async Task OpenConnectionAsync_ThrowsObjectDisposedException_AfterDisposal()
    {
        PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString);
        await factory.DisposeAsync();

        ObjectDisposedException exception = await Assert.ThrowsAsync<ObjectDisposedException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(nameof(PostgreSqlConnectionFactory), exception.ObjectName);
    }

    [Fact]
    public async Task OpenConnectionAsync_AfterDisposal_NeverInvokesTheOpener()
    {
        var opener = FakePostgreSqlConnectionOpener.ReturningConnection();
        PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);
        await factory.DisposeAsync();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, opener.CallCount);
    }
}
