using DbHealthInspector.PostgreSql.Connections;

namespace DbHealthInspector.UnitTests.Connections;

public sealed class PostgreSqlConnectionFactoryCreateTests
{
    [Fact]
    public void Create_ThrowsArgumentNullException_WhenConnectionStringIsNull()
    {
        ArgumentNullException exception = Assert.Throws<ArgumentNullException>(
            () => PostgreSqlConnectionFactory.Create(null!));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void Create_ThrowsArgumentException_WhenConnectionStringIsWhitespaceOnly()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionFactory.Create("   "));

        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public void Create_ThrowsArgumentException_WhenSyntaxIsInvalid()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionFactory.Create("Host=localhost;Port=not-a-number"));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("connectionString", exception.ParamName);
        Assert.Null(exception.InnerException);
    }

    [Fact]
    public void Create_ThrowsArgumentException_WhenOptionsIsSpecified()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionFactory.Create("Host=localhost;Options=-c some_setting=value"));

        Assert.StartsWith(PostgreSqlConnectionStringPolicy.InvalidConnectionStringMessage, exception.Message, StringComparison.Ordinal);
        Assert.Equal("connectionString", exception.ParamName);
    }

    [Fact]
    public async Task Create_Succeeds_AndDerivesTheExpectedMetadataForANetworkHost()
    {
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(
            "Host=db.example.com;Port=6543;Database=testdb;Username=testuser;Password=testpass;SslMode=Require");

        Assert.Equal(PostgreSqlConnectionTargetKind.NetworkHost, factory.Metadata.TargetKind);
        Assert.Equal(6543, factory.Metadata.Port);
        Assert.Equal("Require", factory.Metadata.SslMode);
    }

    [Fact]
    public async Task Create_Succeeds_AndBuildsExactlyOneDataSourceWithoutOpeningAnyConnection()
    {
        // Build() is lazy: it must not itself attempt any network I/O, so pointing at an
        // unreachable host must not make Create() throw or block.
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(
            "Host=192.0.2.1;Port=5432;Database=testdb;Username=testuser;Password=testpass");

        Assert.Equal(PostgreSqlConnectionTargetKind.NetworkHost, factory.Metadata.TargetKind);
    }
}
