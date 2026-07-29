using DbHealthInspector.Core.Fingerprinting;

namespace DbHealthInspector.UnitTests.Fingerprinting;

public sealed class FindingFingerprintTests
{
    // Exactly 64 lowercase hex characters (16 repeated 4 times); length is asserted below
    // rather than relied upon by eye.
    private const string ValidHex = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private const string ValidValue = "sha256:" + ValidHex;

    [Fact]
    public void Constructor_AcceptsAWellFormedValue()
    {
        Assert.Equal(64, ValidHex.Length);

        var fingerprint = new FindingFingerprint(ValidValue);

        Assert.Equal(ValidValue, fingerprint.Value);
        Assert.Equal(ValidValue, fingerprint.ToString());
    }

    [Fact]
    public void Constructor_RejectsNull()
    {
        Assert.Throws<ArgumentNullException>(() => new FindingFingerprint(null!));
    }

    public static TheoryData<string> MalformedNonNullValues()
    {
        return new TheoryData<string>
        {
            "",
            "   ",
            ValidHex, // Missing the "sha256:" prefix.
            "sha256:" + ValidHex[..^1], // 63 hex characters: one short.
            "sha256:" + ValidHex + "f", // 65 hex characters: one too many.
            "sha256:" + ValidHex[..^1] + "G", // Non-hex character.
            "sha256:" + ValidHex.ToUpperInvariant(), // Uppercase hex is rejected.
            "sha1:" + ValidHex, // Wrong algorithm prefix.
        };
    }

    [Theory]
    [MemberData(nameof(MalformedNonNullValues))]
    public void Constructor_RejectsMalformedNonNullValues(string value)
    {
        Assert.Throws<ArgumentException>(() => new FindingFingerprint(value));
    }

    [Fact]
    public void Equality_IsValueBased()
    {
        Assert.Equal(new FindingFingerprint(ValidValue), new FindingFingerprint(ValidValue));
    }
}
