namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// A single ordered key part of an index: either a plain column or an expression, never both.
/// </summary>
/// <remarks>
/// Collation, operator class, sort direction and nulls ordering are modeled per key part —
/// matching PostgreSQL semantics, where each column of a multi-column index can carry distinct
/// values for each of these — rather than duplicated as ambiguous single values on
/// <see cref="IndexSnapshot"/>. See docs/design/core-domain-contracts.md.
/// </remarks>
public sealed record IndexKeyPartSnapshot
{
    /// <summary>
    /// The one-based ordinal position of this key part within the index.
    /// </summary>
    public int Position { get; }

    /// <summary>
    /// The referenced column name. Mutually exclusive with <see cref="Expression"/>. When
    /// provided, cannot be empty or whitespace-only.
    /// </summary>
    public string? ColumnName { get; }

    /// <summary>
    /// The key part's expression text. Mutually exclusive with <see cref="ColumnName"/>. When
    /// provided, cannot be empty or whitespace-only.
    /// </summary>
    public string? Expression { get; }

    /// <summary>
    /// The collation applied to this key part, when relevant. When provided, cannot be empty
    /// or whitespace-only.
    /// </summary>
    public string? Collation { get; }

    /// <summary>
    /// The operator class applied to this key part, when relevant. When provided, cannot be
    /// empty or whitespace-only.
    /// </summary>
    public string? OperatorClass { get; }

    /// <summary>
    /// The sort direction for this key part.
    /// </summary>
    public IndexSortDirection SortDirection { get; }

    /// <summary>
    /// Where null values sort for this key part.
    /// </summary>
    public IndexNullsOrdering NullsOrdering { get; }

    /// <summary>
    /// Creates an index key part snapshot.
    /// </summary>
    /// <param name="position">One-based ordinal position. Must be 1 or greater.</param>
    /// <param name="columnName">The column name. Exactly one of this and <paramref name="expression"/> must be non-blank.</param>
    /// <param name="expression">The expression text. Exactly one of this and <paramref name="columnName"/> must be non-blank.</param>
    /// <param name="collation">The collation, when relevant.</param>
    /// <param name="operatorClass">The operator class, when relevant.</param>
    /// <param name="sortDirection">The sort direction.</param>
    /// <param name="nullsOrdering">Where null values sort.</param>
    public IndexKeyPartSnapshot(
        int position,
        string? columnName,
        string? expression,
        string? collation,
        string? operatorClass,
        IndexSortDirection sortDirection,
        IndexNullsOrdering nullsOrdering)
    {
        if (position < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(position), position, "Key part position must start at 1.");
        }

        Position = position;

        ColumnName = Guard.AgainstEmptyOrWhiteSpace(columnName, nameof(columnName));
        Expression = Guard.AgainstEmptyOrWhiteSpace(expression, nameof(expression));

        if ((ColumnName is not null) == (Expression is not null))
        {
            throw new ArgumentException(
                "A key part must reference exactly one of column name or expression.");
        }

        Collation = Guard.AgainstEmptyOrWhiteSpace(collation, nameof(collation));
        OperatorClass = Guard.AgainstEmptyOrWhiteSpace(operatorClass, nameof(operatorClass));

        Guard.AgainstUndefinedEnum(sortDirection, nameof(sortDirection));
        SortDirection = sortDirection;
        Guard.AgainstUndefinedEnum(nullsOrdering, nameof(nullsOrdering));
        NullsOrdering = nullsOrdering;
    }
}
