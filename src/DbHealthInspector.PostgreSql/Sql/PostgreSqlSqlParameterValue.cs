using System.Collections.ObjectModel;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// One bound parameter value: an immutable, closed model carrying an exact position, an exact
/// declared type and the value itself. There is no mutable dictionary, no caller-controlled
/// parameter name and no <see cref="object"/>-typed payload, so a value can never be bound to
/// the wrong placeholder or silently converted.
/// </summary>
internal readonly struct PostgreSqlSqlParameterValue : IEquatable<PostgreSqlSqlParameterValue>
{
    private readonly ReadOnlyCollection<string>? _textArrayValue;

    /// <summary>
    /// The 1-based placeholder position this value binds to.
    /// </summary>
    internal int Position { get; }

    /// <summary>
    /// The declared type of this value.
    /// </summary>
    internal PostgreSqlSqlParameterType Type { get; }

    /// <summary>
    /// The 32-bit integer payload. Meaningful only when <see cref="Type"/> is
    /// <see cref="PostgreSqlSqlParameterType.Int32"/>.
    /// </summary>
    internal int Int32Value { get; }

    /// <summary>
    /// The ordered text payload. Meaningful only when <see cref="Type"/> is
    /// <see cref="PostgreSqlSqlParameterType.TextArray"/>, in which case it is never
    /// <see langword="null"/> and contains no <see langword="null"/> element.
    /// </summary>
    /// <remarks>
    /// Exposed as a <see cref="ReadOnlyCollection{T}"/> over a copy the caller never held, so
    /// neither the creator nor a consumer can mutate what will be bound.
    /// </remarks>
    internal ReadOnlyCollection<string> TextArrayValue =>
        _textArrayValue ?? throw new InvalidOperationException("This parameter value is not a text array.");

    private PostgreSqlSqlParameterValue(
        int position,
        PostgreSqlSqlParameterType type,
        int int32Value,
        ReadOnlyCollection<string>? textArrayValue)
    {
        Position = position;
        Type = type;
        Int32Value = int32Value;
        _textArrayValue = textArrayValue;
    }

    /// <summary>
    /// Creates an <see cref="PostgreSqlSqlParameterType.Int32"/> value for the given 1-based
    /// position.
    /// </summary>
    internal static PostgreSqlSqlParameterValue Int32(int position, int value)
    {
        ThrowIfPositionIsNotOneBased(position);

        return new PostgreSqlSqlParameterValue(position, PostgreSqlSqlParameterType.Int32, value, null);
    }

    /// <summary>
    /// Creates a <see cref="PostgreSqlSqlParameterType.TextArray"/> value for the given 1-based
    /// position. The supplied sequence is copied immediately, so a later mutation of the caller's
    /// array cannot change what is bound. An empty array is valid.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="values"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="values"/> contains a null element.</exception>
    internal static PostgreSqlSqlParameterValue TextArray(int position, IReadOnlyList<string> values)
    {
        ThrowIfPositionIsNotOneBased(position);
        ArgumentNullException.ThrowIfNull(values);

        var copied = new string[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            copied[index] = values[index]
                ?? throw new ArgumentException("A text array parameter cannot contain a null element.", nameof(values));
        }

        return new PostgreSqlSqlParameterValue(
            position, PostgreSqlSqlParameterType.TextArray, 0, Array.AsReadOnly(copied));
    }

    private static void ThrowIfPositionIsNotOneBased(int position)
    {
        if (position < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Parameter positions are 1-based.");
        }
    }

    public bool Equals(PostgreSqlSqlParameterValue other)
    {
        if (Position != other.Position || Type != other.Type)
        {
            return false;
        }

        return Type == PostgreSqlSqlParameterType.TextArray
            ? TextArraysAreEqual(_textArrayValue, other._textArrayValue)
            : Int32Value == other.Int32Value;
    }

    private static bool TextArraysAreEqual(ReadOnlyCollection<string>? left, ReadOnlyCollection<string>? right)
    {
        if (left is null || right is null)
        {
            return ReferenceEquals(left, right);
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (var index = 0; index < left.Count; index++)
        {
            // Ordinal: schema names are identifiers, never culture-sensitive text.
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is PostgreSqlSqlParameterValue other && Equals(other);

    public override int GetHashCode()
    {
        if (Type != PostgreSqlSqlParameterType.TextArray)
        {
            return HashCode.Combine(Position, Type, Int32Value);
        }

        var hash = new HashCode();
        hash.Add(Position);
        hash.Add(Type);

        if (_textArrayValue is { } values)
        {
            foreach (string value in values)
            {
                hash.Add(value, StringComparer.Ordinal);
            }
        }

        return hash.ToHashCode();
    }

    public static bool operator ==(PostgreSqlSqlParameterValue left, PostgreSqlSqlParameterValue right) => left.Equals(right);

    public static bool operator !=(PostgreSqlSqlParameterValue left, PostgreSqlSqlParameterValue right) => !left.Equals(right);
}
