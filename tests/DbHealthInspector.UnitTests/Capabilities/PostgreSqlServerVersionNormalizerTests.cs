using System.Globalization;
using DbHealthInspector.PostgreSql.Capabilities;

namespace DbHealthInspector.UnitTests.Capabilities;

/// <summary>
/// Version normalization is numeric only (GC-DHI-04C §6): the encoded
/// <c>server_version_num</c> is the single source, and no textual version is ever parsed.
/// </summary>
public sealed class PostgreSqlServerVersionNormalizerTests
{
    [Theory]
    [InlineData(90624, "9.6.24", 9, nameof(PostgreSqlVersionSupportStatus.Unsupported))]
    [InlineData(150000, "15.0", 15, nameof(PostgreSqlVersionSupportStatus.Supported))]
    [InlineData(150016, "15.16", 15, nameof(PostgreSqlVersionSupportStatus.Supported))]
    [InlineData(180004, "18.4", 18, nameof(PostgreSqlVersionSupportStatus.Supported))]
    [InlineData(190000, "19.0", 19, nameof(PostgreSqlVersionSupportStatus.Unsupported))]
    public void FrozenVectors_NormalizeExactly(int versionNumber, string expectedNormalized, int expectedMajor, string expectedSupportName)
    {
        var expectedSupport = Enum.Parse<PostgreSqlVersionSupportStatus>(expectedSupportName);

        Assert.Equal(expectedNormalized, PostgreSqlServerVersionNormalizer.Normalize(versionNumber));
        Assert.Equal(expectedMajor, PostgreSqlServerVersionNormalizer.MajorVersionOf(versionNumber));
        Assert.Equal(expectedSupport, PostgreSqlServerVersionNormalizer.SupportStatusOf(expectedMajor));
    }

    [Theory]
    [InlineData(140000, 14, nameof(PostgreSqlVersionSupportStatus.Unsupported))]
    [InlineData(150000, 15, nameof(PostgreSqlVersionSupportStatus.Supported))]
    [InlineData(160000, 16, nameof(PostgreSqlVersionSupportStatus.Supported))]
    [InlineData(170000, 17, nameof(PostgreSqlVersionSupportStatus.Supported))]
    [InlineData(180000, 18, nameof(PostgreSqlVersionSupportStatus.Supported))]
    [InlineData(190000, 19, nameof(PostgreSqlVersionSupportStatus.Unsupported))]
    public void SupportedRange_IsExactlyFifteenThroughEighteen(int versionNumber, int expectedMajor, string expectedSupportName)
    {
        var expectedSupport = Enum.Parse<PostgreSqlVersionSupportStatus>(expectedSupportName);

        int major = PostgreSqlServerVersionNormalizer.MajorVersionOf(versionNumber);

        Assert.Equal(expectedMajor, major);
        Assert.Equal(expectedSupport, PostgreSqlServerVersionNormalizer.SupportStatusOf(major));
    }

    [Fact]
    public void SupportedRangeBounds_AreFifteenAndEighteen()
    {
        Assert.Equal(15, PostgreSqlServerVersionNormalizer.MinimumSupportedMajorVersion);
        Assert.Equal(18, PostgreSqlServerVersionNormalizer.MaximumSupportedMajorVersion);
    }

    [Fact]
    public void PreTenEncoding_UsesThreeParts()
    {
        // The three-part form exists only so a pre-10 server can still be described precisely
        // while being reported as unsupported.
        Assert.Equal("9.6.24", PostgreSqlServerVersionNormalizer.Normalize(90624));
        Assert.Equal("9.4.1", PostgreSqlServerVersionNormalizer.Normalize(90401));
    }

    [Fact]
    public void TenAndLater_UsesTwoParts()
    {
        Assert.Equal("10.0", PostgreSqlServerVersionNormalizer.Normalize(100000));
        Assert.Equal("10.23", PostgreSqlServerVersionNormalizer.Normalize(100023));
    }

    // --- Invalid encodings -----------------------------------------------------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-180004)]
    [InlineData(1)]
    [InlineData(9999)]
    public void InvalidEncodings_AreRejected(int versionNumber)
    {
        Assert.Throws<PostgreSqlServerVersionException>(() => PostgreSqlServerVersionNormalizer.Normalize(versionNumber));
        Assert.Throws<PostgreSqlServerVersionException>(() => PostgreSqlServerVersionNormalizer.MajorVersionOf(versionNumber));
    }

    [Fact]
    public void VersionException_CarriesAFixedMessageAndNoDetail()
    {
        PostgreSqlServerVersionException exception = Assert.Throws<PostgreSqlServerVersionException>(
            () => PostgreSqlServerVersionNormalizer.Normalize(4242));

        Assert.Equal("The PostgreSQL server version could not be interpreted.", exception.Message);
        bool leaked = exception.ToString().Contains("4242", StringComparison.Ordinal);
        Assert.False(leaked, "The exception exposed the received server version.");
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    // --- Culture independence ---------------------------------------------------------------------

    [Theory]
    [InlineData("de-DE")]
    [InlineData("fr-FR")]
    [InlineData("ar-SA")]
    [InlineData("tr-TR")]
    public void Normalization_IsCultureInvariant(string cultureName)
    {
        CultureInfo original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(cultureName);

            Assert.Equal("18.4", PostgreSqlServerVersionNormalizer.Normalize(180004));
            Assert.Equal("9.6.24", PostgreSqlServerVersionNormalizer.Normalize(90624));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Fact]
    public void SupportStatusEnum_DeclaresExactlyTwoMembers()
    {
        Assert.Equal(2, Enum.GetValues<PostgreSqlVersionSupportStatus>().Length);
    }
}
