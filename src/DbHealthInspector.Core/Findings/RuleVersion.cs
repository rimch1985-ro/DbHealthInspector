using System.Globalization;

namespace DbHealthInspector.Core.Findings;

/// <summary>
/// The version of a diagnostic rule's implementation, distinct from the tool version and from
/// the report schema version.
/// </summary>
/// <remarks>
/// A rule version tracks internal implementation changes to a single rule. It intentionally does
/// not participate in fingerprint computation: see
/// <see cref="Fingerprinting.FindingFingerprintGenerator"/> and
/// docs/design/core-domain-contracts.md for the rationale.
/// </remarks>
public sealed record RuleVersion
{
    /// <summary>
    /// The version number. Always a positive integer starting at 1.
    /// </summary>
    public int Value { get; }

    /// <summary>
    /// Creates a rule version.
    /// </summary>
    /// <param name="value">Must be a positive integer (1 or greater).</param>
    public RuleVersion(int value)
    {
        if (value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "Rule version must be a positive integer, starting at 1.");
        }

        Value = value;
    }

    /// <summary>
    /// The first rule version, <c>1</c>.
    /// </summary>
    public static RuleVersion Initial { get; } = new(1);

    /// <summary>
    /// Returns the version number as text.
    /// </summary>
    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
