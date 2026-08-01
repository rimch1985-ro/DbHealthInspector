using System.Collections.ObjectModel;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// One immutable, statically-declared statement in the productive inventory: its closed ID, its
/// command shape, its exact SQL text, its ordered parameter declarations and the security reason
/// it exists.
/// </summary>
/// <remarks>
/// Deliberately a plain sealed class rather than a <see langword="record"/>: a record's generated
/// <see cref="object.ToString"/> would render the SQL text and parameter list structurally, which
/// would turn any incidental interpolation of a definition into a SQL disclosure. The inherited
/// <see cref="object.ToString"/> returns only the type name.
/// </remarks>
internal sealed class PostgreSqlSqlStatementDefinition
{
    /// <summary>
    /// The closed identifier callers use to resolve this statement. The only lookup key.
    /// </summary>
    internal PostgreSqlSqlStatementId Id { get; }

    /// <summary>
    /// The command shape the safety validator proved for <see cref="Sql"/>.
    /// </summary>
    internal PostgreSqlSqlCommandKind Kind { get; }

    /// <summary>
    /// The exact, static SQL text. Never built by concatenation or interpolation of any
    /// caller-supplied value.
    /// </summary>
    internal string Sql { get; }

    /// <summary>
    /// The ordered parameter declarations, positions <c>$1..$n</c> ascending. Read-only.
    /// </summary>
    internal ReadOnlyCollection<PostgreSqlSqlParameterDefinition> Parameters { get; }

    /// <summary>
    /// Why this statement is allowed to exist at all. Documentation only; never emitted into an
    /// exception that crosses the boundary.
    /// </summary>
    internal string SecurityPurpose { get; }

    internal PostgreSqlSqlStatementDefinition(
        PostgreSqlSqlStatementId id,
        PostgreSqlSqlCommandKind kind,
        string sql,
        IReadOnlyList<PostgreSqlSqlParameterDefinition> parameters,
        string securityPurpose)
    {
        if (!Enum.IsDefined(id))
        {
            throw new ArgumentOutOfRangeException(nameof(id), id, "Undefined statement id.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Undefined command kind.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(sql, nameof(sql));
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityPurpose, nameof(securityPurpose));

        var copied = new PostgreSqlSqlParameterDefinition[parameters.Count];
        for (var index = 0; index < parameters.Count; index++)
        {
            PostgreSqlSqlParameterDefinition parameter = parameters[index]
                ?? throw new ArgumentException("Parameter declarations cannot be null.", nameof(parameters));

            if (parameter.Position != index + 1)
            {
                throw new ArgumentException(
                    "Parameter declarations must be ordered and consecutive starting at position 1.",
                    nameof(parameters));
            }

            copied[index] = parameter;
        }

        Id = id;
        Kind = kind;
        Sql = sql;
        Parameters = Array.AsReadOnly(copied);
        SecurityPurpose = securityPurpose;
    }
}
