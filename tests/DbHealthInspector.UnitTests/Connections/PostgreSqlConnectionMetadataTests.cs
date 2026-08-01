using System.Reflection;
using DbHealthInspector.PostgreSql.Connections;
using Npgsql;

namespace DbHealthInspector.UnitTests.Connections;

public sealed class PostgreSqlConnectionMetadataTests
{
    // Full TargetKind matrix (GC-DHI-04A-C1 §13). Precedence: a host beginning with "/" is
    // classified as UnixDomainSocket before comma (multi-host) detection is ever considered —
    // so a Unix-socket directory list, which may itself contain commas, is still
    // UnixDomainSocket, never MultiHost.
    [Theory]
    [InlineData("db.example.com", nameof(PostgreSqlConnectionTargetKind.NetworkHost))] // network host
    [InlineData("localhost", nameof(PostgreSqlConnectionTargetKind.NetworkHost))] // network host
    [InlineData("192.168.1.1", nameof(PostgreSqlConnectionTargetKind.NetworkHost))] // IPv4
    [InlineData("::1", nameof(PostgreSqlConnectionTargetKind.NetworkHost))] // IPv6
    [InlineData("/var/run/postgresql", nameof(PostgreSqlConnectionTargetKind.UnixDomainSocket))] // one Unix socket directory
    [InlineData("host1,host2", nameof(PostgreSqlConnectionTargetKind.MultiHost))] // two network hosts
    [InlineData("host1,host2,host3", nameof(PostgreSqlConnectionTargetKind.MultiHost))] // two network hosts
    [InlineData("host1, host2", nameof(PostgreSqlConnectionTargetKind.MultiHost))] // two network hosts, whitespace after comma
    [InlineData("/var/run/postgresql,/tmp/pg", nameof(PostgreSqlConnectionTargetKind.UnixDomainSocket))] // multiple Unix socket directories
    public void DeriveMetadata_DerivesTheExpectedTargetKind(string host, string expectedName)
    {
        var expected = Enum.Parse<PostgreSqlConnectionTargetKind>(expectedName);
        var builder = new NpgsqlConnectionStringBuilder($"Host={host}");

        PostgreSqlConnectionMetadata metadata = PostgreSqlConnectionStringPolicy.DeriveMetadata(builder);

        Assert.Equal(expected, metadata.TargetKind);
    }

    [Fact]
    public void DeriveMetadata_DefaultsToNetworkHost_WhenNoHostIsSupplied()
    {
        var builder = new NpgsqlConnectionStringBuilder(string.Empty);

        PostgreSqlConnectionMetadata metadata = PostgreSqlConnectionStringPolicy.DeriveMetadata(builder);

        Assert.Equal(PostgreSqlConnectionTargetKind.NetworkHost, metadata.TargetKind);
    }

    [Fact]
    public void DeriveMetadata_ReportsThePort()
    {
        var builder = new NpgsqlConnectionStringBuilder("Host=localhost;Port=6543");

        PostgreSqlConnectionMetadata metadata = PostgreSqlConnectionStringPolicy.DeriveMetadata(builder);

        Assert.Equal(6543, metadata.Port);
    }

    [Fact]
    public void DeriveMetadata_ReportsTheSslMode()
    {
        var builder = new NpgsqlConnectionStringBuilder("Host=localhost;SslMode=Require");

        PostgreSqlConnectionMetadata metadata = PostgreSqlConnectionStringPolicy.DeriveMetadata(builder);

        Assert.Equal("Require", metadata.SslMode);
    }

    [Fact]
    public void DeriveMetadata_ReportsPoolingWhenDisabled()
    {
        var builder = new NpgsqlConnectionStringBuilder("Host=localhost;Pooling=false");

        PostgreSqlConnectionMetadata metadata = PostgreSqlConnectionStringPolicy.DeriveMetadata(builder);

        Assert.False(metadata.Pooling);
    }

    [Fact]
    public void DeriveMetadata_ReportsThePoolingDefault()
    {
        var builder = new NpgsqlConnectionStringBuilder("Host=localhost");

        PostgreSqlConnectionMetadata metadata = PostgreSqlConnectionStringPolicy.DeriveMetadata(builder);

        Assert.True(metadata.Pooling);
    }

    [Fact]
    public void DeriveMetadata_ReportsTheConnectionTimeoutInSeconds()
    {
        var builder = new NpgsqlConnectionStringBuilder("Host=localhost;Timeout=45");

        PostgreSqlConnectionMetadata metadata = PostgreSqlConnectionStringPolicy.DeriveMetadata(builder);

        Assert.Equal(45, metadata.ConnectionTimeoutSeconds);
    }

    private static PropertyInfo[] InstanceProperties() => typeof(PostgreSqlConnectionMetadata)
        .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    [Fact]
    public void PropertySurface_ExposesExactlyTheAllowlistedFiveProperties()
    {
        string[] names = InstanceProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        string[] expected = new[] { "ConnectionTimeoutSeconds", "Pooling", "Port", "SslMode", "TargetKind" };

        Assert.Equal(expected, names);
    }

    [Fact]
    public void PropertySurface_HasNoSetters()
    {
        Assert.All(InstanceProperties(), property => Assert.Null(property.SetMethod));
    }

    [Fact]
    public void PropertySurface_DoesNotExposeAConnectionStringProperty()
    {
        Assert.DoesNotContain(InstanceProperties(), property => property.Name == "ConnectionString");
    }

    [Fact]
    public void ToString_DoesNotRenderAnyPropertyValue()
    {
        var metadata = new PostgreSqlConnectionMetadata(PostgreSqlConnectionTargetKind.NetworkHost, 6543, "Require", true, 45);

        string rendered = metadata.ToString()!;

        Assert.Equal(typeof(PostgreSqlConnectionMetadata).ToString(), rendered);
        Assert.DoesNotContain("6543", rendered);
        Assert.DoesNotContain("Require", rendered);
    }

    [Fact]
    public void Constructor_ThrowsArgumentOutOfRangeException_WhenTargetKindIsUndefined()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PostgreSqlConnectionMetadata((PostgreSqlConnectionTargetKind)999, 5432, "Prefer", true, 15));
    }

    [Fact]
    public void Constructor_ThrowsArgumentOutOfRangeException_WhenPortIsNotPositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PostgreSqlConnectionMetadata(PostgreSqlConnectionTargetKind.NetworkHost, 0, "Prefer", true, 15));
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenSslModeIsBlank()
    {
        Assert.Throws<ArgumentException>(
            () => new PostgreSqlConnectionMetadata(PostgreSqlConnectionTargetKind.NetworkHost, 5432, "  ", true, 15));
    }

    [Fact]
    public void Constructor_ThrowsArgumentOutOfRangeException_WhenConnectionTimeoutSecondsIsNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PostgreSqlConnectionMetadata(PostgreSqlConnectionTargetKind.NetworkHost, 5432, "Prefer", true, -1));
    }
}
