using DbHealthInspector.PostgreSql.Connections;
using DbHealthInspector.UnitTests.Connections.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Connections;

/// <summary>
/// Drives a synthetic <see cref="NpgsqlException"/> carrying a sensitive marker all the way
/// through the real production path — fake opener, then
/// <see cref="PostgreSqlConnectionFactory.OpenConnectionAsync"/>'s own
/// <c>catch (NpgsqlException)</c> clause, then sanitization — and confirms the marker survives
/// nowhere in the result. This exercises the actual catch clause, not just the
/// <see cref="PostgreSqlConnectionFactory.SanitizeOpenFailure"/> helper in isolation.
/// </summary>
public sealed class PostgreSqlConnectionFactoryNpgsqlExceptionSanitizationTests
{
    private const string ValidConnectionString = "Host=localhost;Database=testdb;Username=testuser;Password=testpass";
    private const string SecretMarker = "synthetic-npgsql-secret-04a";

    [Fact]
    public async Task SyntheticNpgsqlException_MarkerInMessage_NeverSurvivesSanitization()
    {
        var original = new NpgsqlException($"connection failed: {SecretMarker}");
        var opener = FakePostgreSqlConnectionOpener.Throwing(original);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        PostgreSqlConnectionException sanitized = await Assert.ThrowsAsync<PostgreSqlConnectionException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Equal("The PostgreSQL connection could not be opened.", sanitized.Message);
        Assert.DoesNotContain(SecretMarker, sanitized.Message);
        Assert.DoesNotContain(SecretMarker, sanitized.ToString());
        Assert.DoesNotContain(SecretMarker, sanitized.StackTrace ?? string.Empty);
        Assert.DoesNotContain(SecretMarker, factory.Metadata.ToString());
        Assert.Null(sanitized.InnerException);
        Assert.Empty(sanitized.Data);
        Assert.Equal(1, opener.CallCount);
    }

    [Fact]
    public async Task SyntheticNpgsqlException_MarkerInData_NeverSurvivesSanitization()
    {
        var original = new NpgsqlException("connection failed");
        original.Data["detail"] = SecretMarker;
        var opener = FakePostgreSqlConnectionOpener.Throwing(original);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        PostgreSqlConnectionException sanitized = await Assert.ThrowsAsync<PostgreSqlConnectionException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Empty(sanitized.Data);
        Assert.DoesNotContain(SecretMarker, sanitized.ToString());
    }

    [Fact]
    public async Task SyntheticNpgsqlException_MarkerInInnerException_NeverSurvivesSanitization()
    {
        var innerException = new InvalidOperationException(SecretMarker);
        var original = new NpgsqlException("connection failed", innerException);
        var opener = FakePostgreSqlConnectionOpener.Throwing(original);
        await using PostgreSqlConnectionFactory factory = PostgreSqlConnectionFactory.Create(ValidConnectionString, opener.AsDelegate);

        PostgreSqlConnectionException sanitized = await Assert.ThrowsAsync<PostgreSqlConnectionException>(
            () => factory.OpenConnectionAsync(TestContext.Current.CancellationToken).AsTask());

        Assert.Null(sanitized.InnerException);
        Assert.DoesNotContain(SecretMarker, sanitized.ToString());
    }
}
