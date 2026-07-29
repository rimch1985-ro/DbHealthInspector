using System.Text.RegularExpressions;

namespace DbHealthInspector.Core.Findings;

/// <summary>
/// A stable, public finding identifier in the form <c>DBH</c> followed by exactly three digits
/// (for example <c>DBH001</c>).
/// </summary>
/// <remarks>
/// Finding codes are public contract identifiers. Per <c>AGENTS.md</c>, they must never be
/// reused for a different meaning once published. This type is a class-based record rather than
/// a record struct specifically so that <see langword="default"/> cannot silently produce an
/// unvalidated instance; every instance is guaranteed to have passed <see cref="Pattern"/>.
/// </remarks>
public sealed record FindingCode : IComparable<FindingCode>
{
    // A plain compiled Regex is used instead of [GeneratedRegex] because the project prohibits
    // source generators; see docs/design/core-domain-contracts.md.
    private static readonly Regex Pattern = new("^DBH[0-9]{3}$", RegexOptions.Compiled);

    /// <summary>
    /// The stable code text, for example <c>DBH001</c>.
    /// </summary>
    public string Value { get; }

    /// <summary>
    /// Creates a finding code.
    /// </summary>
    /// <param name="value">
    /// Must be non-null, non-blank and match <c>DBH</c> followed by exactly three digits.
    /// </param>
    public FindingCode(string value)
    {
        Guard.AgainstNullOrWhiteSpace(value, nameof(value));
        if (!Pattern.IsMatch(value))
        {
            throw new ArgumentException(
                "Finding code must match the pattern 'DBH' followed by exactly three digits.",
                nameof(value));
        }

        Value = value;
    }

    /// <summary>
    /// Compares two finding codes using ordinal text comparison.
    /// </summary>
    public int CompareTo(FindingCode? other) =>
        other is null ? 1 : string.CompareOrdinal(Value, other.Value);

    /// <summary>
    /// Returns whether <paramref name="left"/> sorts before <paramref name="right"/>.
    /// </summary>
    public static bool operator <(FindingCode? left, FindingCode? right) =>
        left is null ? right is not null : left.CompareTo(right) < 0;

    /// <summary>
    /// Returns whether <paramref name="left"/> sorts before or equal to <paramref name="right"/>.
    /// </summary>
    public static bool operator <=(FindingCode? left, FindingCode? right) =>
        left is null || left.CompareTo(right) <= 0;

    /// <summary>
    /// Returns whether <paramref name="left"/> sorts after <paramref name="right"/>.
    /// </summary>
    public static bool operator >(FindingCode? left, FindingCode? right) =>
        left is not null && left.CompareTo(right) > 0;

    /// <summary>
    /// Returns whether <paramref name="left"/> sorts after or equal to <paramref name="right"/>.
    /// </summary>
    public static bool operator >=(FindingCode? left, FindingCode? right) =>
        left is null ? right is null : left.CompareTo(right) >= 0;

    /// <summary>
    /// Returns the stable code text.
    /// </summary>
    public override string ToString() => Value;
}
