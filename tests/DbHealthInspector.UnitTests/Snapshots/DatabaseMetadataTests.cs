using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.UnitTests.Snapshots;

public sealed class DatabaseMetadataTests
{
    [Fact]
    public void Constructor_AllowsAKnownCurrentUser()
    {
        var metadata = new DatabaseMetadata(DatabaseEngine.PostgreSql, "18.4", "demo_business", "dbhealth");

        Assert.Equal(DatabaseEngine.PostgreSql, metadata.Engine);
        Assert.Equal("18.4", metadata.EngineVersion);
        Assert.Equal("demo_business", metadata.DatabaseName);
        Assert.Equal("dbhealth", metadata.CurrentUser);
    }

    [Fact]
    public void Constructor_AllowsAnUnknownCurrentUser()
    {
        var metadata = new DatabaseMetadata(DatabaseEngine.PostgreSql, "18.4", "demo_business");

        Assert.Null(metadata.CurrentUser);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankCurrentUser(string currentUser)
    {
        Assert.Throws<ArgumentException>(() =>
            new DatabaseMetadata(DatabaseEngine.PostgreSql, "18.4", "demo_business", currentUser));
    }

    [Fact]
    public void Constructor_RejectsNullEngine()
    {
        Assert.Throws<ArgumentNullException>(() => new DatabaseMetadata(null!, "18.4", "demo_business"));
    }

    [Fact]
    public void Constructor_RejectsNullEngineVersion()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseMetadata(DatabaseEngine.PostgreSql, null!, "demo_business"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankEngineVersion(string version)
    {
        Assert.Throws<ArgumentException>(() =>
            new DatabaseMetadata(DatabaseEngine.PostgreSql, version, "demo_business"));
    }

    [Fact]
    public void Constructor_RejectsNullDatabaseName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseMetadata(DatabaseEngine.PostgreSql, "18.4", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankDatabaseName(string databaseName)
    {
        Assert.Throws<ArgumentException>(() =>
            new DatabaseMetadata(DatabaseEngine.PostgreSql, "18.4", databaseName));
    }
}
