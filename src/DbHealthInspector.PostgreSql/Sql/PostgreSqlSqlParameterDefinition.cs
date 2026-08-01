namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// Declares one positional parameter of an inventoried statement: its 1-based position (matching
/// the PostgreSQL <c>$n</c> placeholder) and its declared type.
/// </summary>
/// <remarks>
/// Deliberately a plain sealed class rather than a <see langword="record"/>: a record's generated
/// <see cref="object.ToString"/> would render parameter metadata structurally, which is exactly
/// the kind of incidental diagnostic surface the boundary avoids.
/// </remarks>
internal sealed class PostgreSqlSqlParameterDefinition
{
    /// <summary>
    /// The 1-based placeholder position. <c>$1</c> is position 1.
    /// </summary>
    internal int Position { get; }

    /// <summary>
    /// The declared parameter type. Bound values must match exactly; no implicit conversion is
    /// performed.
    /// </summary>
    internal PostgreSqlSqlParameterType Type { get; }

    /// <summary>
    /// A short, non-sensitive description of what the parameter carries. Never contains a bound
    /// value.
    /// </summary>
    internal string Meaning { get; }

    internal PostgreSqlSqlParameterDefinition(int position, PostgreSqlSqlParameterType type, string meaning)
    {
        if (position < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(position), position, "Parameter positions are 1-based.");
        }

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Undefined parameter type.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(meaning, nameof(meaning));

        Position = position;
        Type = type;
        Meaning = meaning;
    }
}
