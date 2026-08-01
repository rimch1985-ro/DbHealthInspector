namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// One bound parameter value: an immutable, closed model carrying an exact position, an exact
/// declared type and the value itself. There is no mutable dictionary, no caller-controlled
/// parameter name and no <see cref="object"/>-typed payload, so a value can never be bound to
/// the wrong placeholder or silently converted.
/// </summary>
internal readonly struct PostgreSqlSqlParameterValue : IEquatable<PostgreSqlSqlParameterValue>
{
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
    /// <see cref="PostgreSqlSqlParameterType.Int32"/>, which is the only type GC-DHI-04B defines.
    /// </summary>
    internal int Int32Value { get; }

    private PostgreSqlSqlParameterValue(int position, PostgreSqlSqlParameterType type, int int32Value)
    {
        Position = position;
        Type = type;
        Int32Value = int32Value;
    }

    /// <summary>
    /// Creates an <see cref="PostgreSqlSqlParameterType.Int32"/> value for the given 1-based
    /// position.
    /// </summary>
    internal static PostgreSqlSqlParameterValue Int32(int position, int value)
    {
        if (position < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Parameter positions are 1-based.");
        }

        return new PostgreSqlSqlParameterValue(position, PostgreSqlSqlParameterType.Int32, value);
    }

    public bool Equals(PostgreSqlSqlParameterValue other) =>
        Position == other.Position && Type == other.Type && Int32Value == other.Int32Value;

    public override bool Equals(object? obj) => obj is PostgreSqlSqlParameterValue other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Position, Type, Int32Value);

    public static bool operator ==(PostgreSqlSqlParameterValue left, PostgreSqlSqlParameterValue right) => left.Equals(right);

    public static bool operator !=(PostgreSqlSqlParameterValue left, PostgreSqlSqlParameterValue right) => !left.Equals(right);
}
