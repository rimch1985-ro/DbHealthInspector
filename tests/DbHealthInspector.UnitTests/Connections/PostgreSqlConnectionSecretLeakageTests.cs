using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.UnitTests.Connections.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Connections;

/// <summary>
/// Verifies that synthetic secret markers placed in every sensitive connection-string field
/// never survive into sanitized metadata or a sanitized exception.
/// </summary>
/// <remarks>
/// The open-failure half of each check uses <see cref="FakePostgreSqlConnectionOpener"/>,
/// configured to throw an <see cref="NpgsqlException"/> whose message embeds the marker — a
/// stronger check than merely observing that an ordinary failure happens not to mention it: it
/// proves sanitization strips the marker even when the (simulated) driver failure explicitly
/// contains it. No real socket, port, DNS lookup, or PostgreSQL server is involved. <c>Host</c>
/// is excluded from the open-failure half of the check: <see cref="PostgreSqlConnectionMetadata"/>
/// never retains the raw host string in the first place (see
/// docs/design/postgresql-connection-boundary.md), so the metadata/ToString check alone is
/// sufficient for it.
/// </remarks>
public sealed class PostgreSqlConnectionSecretLeakageTests
{
    private const string BaseConnectionString = "Host=localhost";

    private static void AssertMarkerAbsentFromMetadata(string connectionString, string marker)
    {
        NpgsqlConnectionStringBuilder builder = PostgreSqlConnectionStringPolicy.ParseAndNormalize(connectionString);
        PostgreSqlConnectionMetadata metadata = PostgreSqlConnectionStringPolicy.DeriveMetadata(builder);

        Assert.DoesNotContain(marker, metadata.ToString());
        Assert.DoesNotContain(marker, metadata.SslMode);
    }

    private static async Task AssertMarkerAbsentFromOpenFailureAsync(string connectionString, string marker, CancellationToken cancellationToken)
    {
        var opener = FakePostgreSqlConnectionOpener.Throwing(new NpgsqlException($"connection failed near: {marker}"));
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(connectionString, opener.AsDelegate);

        PostgreSqlConnectionException exception = await Assert.ThrowsAsync<PostgreSqlConnectionException>(
            () => factory.OpenConnectionAsync(cancellationToken).AsTask());

        Assert.Equal("The PostgreSQL connection could not be opened.", exception.Message);
        Assert.DoesNotContain(marker, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    [Fact]
    public void Host_NeverAppearsInMetadataOrItsToString()
    {
        const string marker = "MARKERHOSTVALUE";

        AssertMarkerAbsentFromMetadata($"Host={marker}.invalid-test-domain", marker);
    }

    [Fact]
    public async Task Database_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERDATABASEVALUE";
        string connectionString = $"{BaseConnectionString};Database={marker}";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Username_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERUSERNAMEVALUE";
        string connectionString = $"{BaseConnectionString};Username={marker}";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Password_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERPASSWORDVALUE";
        string connectionString = $"{BaseConnectionString};Password={marker}";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Passfile_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERPASSFILEVALUE";
        string connectionString = $"{BaseConnectionString};Passfile=/nonexistent/{marker}";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ApplicationName_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERAPPNAMEVALUE";
        string connectionString = $"{BaseConnectionString};Application Name={marker}";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SearchPath_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERSEARCHPATHVALUE";
        string connectionString = $"{BaseConnectionString};SearchPath={marker}";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SslPassword_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERSSLPASSWORDVALUE";
        string connectionString = $"{BaseConnectionString};SslPassword={marker}";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SslCertificatePath_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERSSLCERTVALUE";
        string connectionString = $"{BaseConnectionString};SslCertificate=/nonexistent/{marker}.pem";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task SslKeyPath_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERSSLKEYVALUE";
        string connectionString = $"{BaseConnectionString};SslKey=/nonexistent/{marker}.pem";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RootCertificatePath_NeverAppearsInMetadataOrTheSanitizedException()
    {
        const string marker = "MARKERROOTCERTVALUE";
        string connectionString = $"{BaseConnectionString};RootCertificate=/nonexistent/{marker}.pem";

        AssertMarkerAbsentFromMetadata(connectionString, marker);
        await AssertMarkerAbsentFromOpenFailureAsync(connectionString, marker, TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Options_NeverAppearsInTheRejectionException()
    {
        const string marker = "MARKEROPTIONSVALUE";

        ArgumentException exception = Assert.Throws<ArgumentException>(
            () => PostgreSqlConnectionStringPolicy.ParseAndNormalize($"Host=localhost;Options=-c {marker}=1"));

        Assert.DoesNotContain(marker, exception.Message);
    }
}
