using System.Collections.ObjectModel;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// A resolved, fully bound statement ready for the gateway to run: the inventory's exact SQL plus
/// the ordered, type-checked values for its positional placeholders.
/// </summary>
/// <remarks>
/// This is the boundary between the part of execution that is pure and directly testable
/// (resolving an ID, checking parameter count/order/type) and the part that genuinely requires a
/// live server (building and running an <c>NpgsqlCommand</c>). Tests assert against this object
/// to prove the command text came from the inventory and the parameters were bound in order.
/// </remarks>
internal sealed class PostgreSqlPreparedStatement
{
    /// <summary>
    /// The statement this was resolved from.
    /// </summary>
    internal PostgreSqlSqlStatementId Id { get; }

    /// <summary>
    /// The exact SQL text taken from the inventory definition. Never modified, never built by
    /// concatenation.
    /// </summary>
    internal string CommandText { get; }

    /// <summary>
    /// The bound values, ordered by ascending position.
    /// </summary>
    internal ReadOnlyCollection<PostgreSqlSqlParameterValue> Parameters { get; }

    internal PostgreSqlPreparedStatement(
        PostgreSqlSqlStatementId id,
        string commandText,
        IReadOnlyList<PostgreSqlSqlParameterValue> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(commandText, nameof(commandText));
        ArgumentNullException.ThrowIfNull(parameters);

        var copied = new PostgreSqlSqlParameterValue[parameters.Count];
        for (var index = 0; index < parameters.Count; index++)
        {
            copied[index] = parameters[index];
        }

        Id = id;
        CommandText = commandText;
        Parameters = Array.AsReadOnly(copied);
    }
}
