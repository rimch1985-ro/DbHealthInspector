using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Capabilities;

namespace DbHealthInspector.UnitTests.Capabilities;

/// <summary>
/// The probe result's own invariants (GC-DHI-04C-C1, R1-11). It does not trust its caller: a
/// result that exists at all identifies PostgreSQL and carries version fields that are exactly
/// what the normalizer derives from <c>server_version_num</c>.
/// </summary>
public sealed class PostgreSqlServerProbeResultTests
{
    private const string ForeignEngineMessage = "Probe metadata must identify PostgreSQL.";
    private const string InconsistentVersionMessage = "Probe version fields are inconsistent.";

    private static CapabilitySnapshot Capabilities() => new(
    [
        new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available),
        new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available),
        new CapabilityState(CapabilityKind.DataProfiling, CapabilityStatus.Disabled, "Data profiling is disabled by product policy."),
    ]);

    private static StatisticsSnapshot Statistics() => new(null);

    private static DatabaseMetadata Metadata(DatabaseEngine engine, string engineVersion) =>
        new(engine, engineVersion, "inspected_database", "inspection_role");

    private static PostgreSqlServerProbeResult Create(
        DatabaseEngine engine,
        string engineVersion,
        int serverVersionNumber,
        int majorVersion,
        PostgreSqlVersionSupportStatus support) =>
        new(Metadata(engine, engineVersion), Capabilities(), Statistics(), serverVersionNumber, majorVersion, support);

    // --- Accepted -------------------------------------------------------------------------------

    [Fact]
    public void CoherentSupportedFields_AreAccepted()
    {
        PostgreSqlServerProbeResult result = Create(
            DatabaseEngine.PostgreSql, "18.4", 180004, 18, PostgreSqlVersionSupportStatus.Supported);

        Assert.Equal(180004, result.ServerVersionNumber);
        Assert.Equal(18, result.MajorVersion);
        Assert.Equal("18.4", result.Metadata.EngineVersion);
        Assert.Equal(PostgreSqlVersionSupportStatus.Supported, result.VersionSupport);
    }

    [Fact]
    public void CoherentUnsupportedFields_AreAccepted()
    {
        PostgreSqlServerProbeResult result = Create(
            DatabaseEngine.PostgreSql, "19.0", 190000, 19, PostgreSqlVersionSupportStatus.Unsupported);

        Assert.Equal(19, result.MajorVersion);
        Assert.Equal(PostgreSqlVersionSupportStatus.Unsupported, result.VersionSupport);
    }

    [Fact]
    public void ALegacyThreePartVersion_IsAcceptedWhenCoherent()
    {
        PostgreSqlServerProbeResult result = Create(
            DatabaseEngine.PostgreSql, "9.6.24", 90624, 9, PostgreSqlVersionSupportStatus.Unsupported);

        Assert.Equal("9.6.24", result.Metadata.EngineVersion);
    }

    // --- Engine ---------------------------------------------------------------------------------

    [Fact]
    public void ANonPostgreSqlEngine_IsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Create(
            new DatabaseEngine("SQL Server"), "18.4", 180004, 18, PostgreSqlVersionSupportStatus.Supported));

        Assert.StartsWith(ForeignEngineMessage, exception.Message, StringComparison.Ordinal);

        // The received engine is never named.
        bool leaked = exception.ToString().Contains("SQL Server", StringComparison.Ordinal);
        Assert.False(leaked, "Sensitive data was exposed.");
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    [Fact]
    public void AnEngineThatOnlyLooksLikePostgreSql_IsRejected()
    {
        // Value equality on the Core contract, so a differently-named engine cannot slip through.
        Assert.Throws<ArgumentException>(() => Create(
            new DatabaseEngine("postgresql"), "18.4", 180004, 18, PostgreSqlVersionSupportStatus.Supported));
    }

    [Fact]
    public void AnEquivalentEngineValue_IsAccepted()
    {
        // A distinct instance carrying the same canonical name is the same engine.
        PostgreSqlServerProbeResult result = Create(
            new DatabaseEngine("PostgreSQL"), "18.4", 180004, 18, PostgreSqlVersionSupportStatus.Supported);

        Assert.Equal(DatabaseEngine.PostgreSql, result.Metadata.Engine);
    }

    // --- Version coherence -----------------------------------------------------------------------

    [Fact]
    public void AWrongNormalizedVersion_IsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Create(
            DatabaseEngine.PostgreSql, "18.5", 180004, 18, PostgreSqlVersionSupportStatus.Supported));

        Assert.StartsWith(InconsistentVersionMessage, exception.Message, StringComparison.Ordinal);

        foreach (string value in new[] { "18.5", "18.4", "180004" })
        {
            bool leaked = exception.ToString().Contains(value, StringComparison.Ordinal);
            Assert.False(leaked, "Sensitive data was exposed.");
        }

        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    [Fact]
    public void ALegacyFormattedVersionForAModernServer_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => Create(
            DatabaseEngine.PostgreSql, "18.0.4", 180004, 18, PostgreSqlVersionSupportStatus.Supported));
    }

    [Fact]
    public void AWrongMajorVersion_IsRejected()
    {
        ArgumentException exception = Assert.Throws<ArgumentException>(() => Create(
            DatabaseEngine.PostgreSql, "18.4", 180004, 17, PostgreSqlVersionSupportStatus.Supported));

        Assert.StartsWith(InconsistentVersionMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AWrongSupportStatus_IsRejected()
    {
        // 18 is inside the supported range, so claiming it is unsupported is incoherent...
        Assert.Throws<ArgumentException>(() => Create(
            DatabaseEngine.PostgreSql, "18.4", 180004, 18, PostgreSqlVersionSupportStatus.Unsupported));

        // ...and so is the reverse.
        Assert.Throws<ArgumentException>(() => Create(
            DatabaseEngine.PostgreSql, "19.0", 190000, 19, PostgreSqlVersionSupportStatus.Supported));
    }

    [Theory]
    [InlineData(150000, 15)]
    [InlineData(160000, 16)]
    [InlineData(170000, 17)]
    [InlineData(180004, 18)]
    public void EverySupportedMajor_RequiresItsOwnCoherentFields(int serverVersionNumber, int majorVersion)
    {
        string normalized = PostgreSqlServerVersionNormalizer.Normalize(serverVersionNumber);

        // Coherent: accepted.
        Create(DatabaseEngine.PostgreSql, normalized, serverVersionNumber, majorVersion, PostgreSqlVersionSupportStatus.Supported);

        // One field off: rejected.
        Assert.Throws<ArgumentException>(() => Create(
            DatabaseEngine.PostgreSql, normalized, serverVersionNumber, majorVersion + 1, PostgreSqlVersionSupportStatus.Supported));
    }

    // --- Null and range guards --------------------------------------------------------------------

    [Fact]
    public void NullComponents_AreRejected()
    {
        Assert.Throws<ArgumentNullException>(() => new PostgreSqlServerProbeResult(
            null!, Capabilities(), Statistics(), 180004, 18, PostgreSqlVersionSupportStatus.Supported));

        Assert.Throws<ArgumentNullException>(() => new PostgreSqlServerProbeResult(
            Metadata(DatabaseEngine.PostgreSql, "18.4"), null!, Statistics(), 180004, 18, PostgreSqlVersionSupportStatus.Supported));

        Assert.Throws<ArgumentNullException>(() => new PostgreSqlServerProbeResult(
            Metadata(DatabaseEngine.PostgreSql, "18.4"), Capabilities(), null!, 180004, 18, PostgreSqlVersionSupportStatus.Supported));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(9999)]
    public void AnImpossibleEncodedVersion_IsRejected(int serverVersionNumber)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            DatabaseEngine.PostgreSql, "18.4", serverVersionNumber, 18, PostgreSqlVersionSupportStatus.Supported));
    }

    [Fact]
    public void AnUndefinedSupportStatus_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Create(
            DatabaseEngine.PostgreSql, "18.4", 180004, 18, (PostgreSqlVersionSupportStatus)999));
    }

    [Fact]
    public void TheResultStillRendersOnlyItsTypeName()
    {
        PostgreSqlServerProbeResult result = Create(
            DatabaseEngine.PostgreSql, "18.4", 180004, 18, PostgreSqlVersionSupportStatus.Supported);

        Assert.Equal(typeof(PostgreSqlServerProbeResult).ToString(), result.ToString());
    }

    [Fact]
    public void TheNormalizerResultIsNotStoredInAnAdditionalField()
    {
        // Six declared values, and no seventh field caching what the normalizer derived.
        System.Reflection.FieldInfo[] fields = typeof(PostgreSqlServerProbeResult).GetFields(
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        Assert.Equal(6, fields.Length);
    }
}
