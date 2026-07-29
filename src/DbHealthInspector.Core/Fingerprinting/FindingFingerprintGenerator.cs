using System.Security.Cryptography;
using System.Text;

using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.Core.Fingerprinting;

/// <summary>
/// Computes a stable, deterministic <see cref="FindingFingerprint"/> from
/// <see cref="FindingFingerprintInput"/>.
/// </summary>
/// <remarks>
/// <para>
/// The same logical finding keeps the same fingerprint even when its evidence order, current
/// object size, estimated rows, message, recommendation, severity, confidence or rule
/// implementation version change. Only the finding's logical identity — format version, engine,
/// finding code, object reference and <see cref="Findings.FingerprintParticipation.Include"/>
/// evidence — participates. See docs/design/core-domain-contracts.md for the full rationale and
/// the golden vector this algorithm must keep reproducing.
/// </para>
/// <para>
/// The canonical byte representation is never exposed publicly: every string field is written
/// with an explicit presence marker and length prefix instead of a delimiter, so no input value
/// can forge a collision by embedding a separator character.
/// </para>
/// </remarks>
public static class FindingFingerprintGenerator
{
    /// <summary>
    /// Computes the fingerprint for <paramref name="input"/>.
    /// </summary>
    public static FindingFingerprint Generate(FindingFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            WriteField(writer, FindingFingerprintInput.CurrentFormatVersion);
            WriteField(writer, input.Engine.Name);
            WriteField(writer, input.Code.Value);
            WriteField(writer, input.ObjectReference.ObjectType.ToString());
            WriteField(writer, input.ObjectReference.SchemaName);
            WriteField(writer, input.ObjectReference.ParentObjectName);
            WriteField(writer, input.ObjectReference.ObjectName);

            EvidenceItem[] includedEvidence = [.. input.Evidence
                .Where(item => item.FingerprintParticipation == FingerprintParticipation.Include)
                .OrderBy(item => item.Key, StringComparer.Ordinal)
                .ThenBy(item => item.Value, StringComparer.Ordinal)
                .ThenBy(item => item.Unit, StringComparer.Ordinal)];

            writer.Write(includedEvidence.Length);
            foreach (EvidenceItem item in includedEvidence)
            {
                WriteField(writer, item.Key);
                WriteField(writer, item.Value);
                WriteField(writer, item.Unit);
            }
        }

        byte[] hash = SHA256.HashData(buffer.ToArray());
        return FindingFingerprint.FromHash(hash);
    }

    /// <summary>
    /// Encodes one canonical field through the exact same field-canonicalization logic
    /// <see cref="Generate"/> uses (<see cref="WriteField"/>), returning the raw canonical
    /// bytes for a single field value. This internal canonicalization operation makes it
    /// possible to verify, at the byte level, that <see langword="null"/> and
    /// <see cref="string.Empty"/> canonicalize
    /// differently — a property no public domain type can otherwise demonstrate, because every
    /// public optional string rejects empty/whitespace-only values (see
    /// docs/design/core-domain-contracts.md §3, §11). Internal, not part of the public API;
    /// visible to <c>DbHealthInspector.UnitTests</c> only via
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute"/>.
    /// </summary>
    internal static byte[] EncodeCanonicalField(string? value)
    {
        using var buffer = new MemoryStream();
        using (var writer = new BinaryWriter(buffer, Encoding.UTF8, leaveOpen: true))
        {
            WriteField(writer, value);
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Writes one canonical field: a one-byte presence marker (<c>0</c> for
    /// <see langword="null"/>, <c>1</c> otherwise), followed for present values by a
    /// four-byte length prefix and the UTF-8 bytes of the value normalized to Unicode Form C.
    /// This distinguishes <see langword="null"/> from an empty string and makes the field
    /// self-delimiting, so no field value can be crafted to collide with a neighboring field.
    /// </summary>
    private static void WriteField(BinaryWriter writer, string? value)
    {
        if (value is null)
        {
            writer.Write((byte)0);
            return;
        }

        string normalized = value.Normalize(NormalizationForm.FormC);
        byte[] bytes = Encoding.UTF8.GetBytes(normalized);
        writer.Write((byte)1);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}
