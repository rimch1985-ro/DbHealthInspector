using System.Text.RegularExpressions;

namespace DbHealthInspector.Core.Fingerprinting;

/// <summary>
/// A stable finding fingerprint, formatted as <c>sha256:</c> followed by 64 lowercase
/// hexadecimal characters.
/// </summary>
/// <remarks>
/// This type validates format only. It does not recompute or verify that the value matches any
/// particular finding; production fingerprints are produced by
/// <see cref="FindingFingerprintGenerator"/>. The validating constructor exists so a fingerprint
/// read back from a future persisted report can be represented as the same value object.
/// </remarks>
public sealed record FindingFingerprint
{
    private static readonly Regex Format = new("^sha256:[0-9a-f]{64}$", RegexOptions.Compiled);

    /// <summary>
    /// The fingerprint text, for example <c>sha256:</c> followed by 64 lowercase hex characters.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a fingerprint from an already-formatted value.
    /// </summary>
    /// <param name="value">Must match <c>sha256:</c> followed by exactly 64 lowercase hexadecimal characters.</param>
    public FindingFingerprint(string value)
    {
        Guard.AgainstNullOrWhiteSpace(value, nameof(value));
        if (!Format.IsMatch(value))
        {
            throw new ArgumentException(
                "Fingerprint must be formatted as 'sha256:' followed by 64 lowercase hexadecimal characters.",
                nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Builds a fingerprint from a raw SHA-256 hash. Used only by
    /// <see cref="FindingFingerprintGenerator"/>; the canonical bytes that produced
    /// <paramref name="hash"/> are never exposed.
    /// </summary>
    internal static FindingFingerprint FromHash(ReadOnlySpan<byte> hash) =>
        new($"sha256:{Convert.ToHexStringLower(hash)}");

    /// <summary>
    /// Returns the fingerprint text.
    /// </summary>
    public override string ToString() => Value;
}
