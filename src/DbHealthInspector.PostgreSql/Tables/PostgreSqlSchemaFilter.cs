using System.Collections.ObjectModel;

namespace DbHealthInspector.PostgreSql.Tables;

/// <summary>
/// The immutable, closed schema filter bound to D001's two <c>text[]</c> parameters.
/// </summary>
/// <remarks>
/// <para>
/// Both lists hold exact schema names compared ordinally and case-sensitively. There is no
/// pattern, wildcard, regular expression, dynamic identifier or SQL fragment: a name only ever
/// travels as an element of a bound array, never as text spliced into a statement.
/// </para>
/// <para>
/// An empty include list means "every eligible non-system schema"; an empty exclude list means
/// "no additional exclusion". The mandatory system-schema exclusions live in D001 itself and
/// cannot be re-enabled from here — naming <c>pg_catalog</c> in the include list simply matches
/// nothing.
/// </para>
/// <para>
/// Both lists are copied on construction and sorted with <see cref="StringComparer.Ordinal"/>, so
/// a caller that mutates its own array afterwards cannot change this filter, and two filters built
/// from the same names always bind the same arrays in the same order.
/// </para>
/// </remarks>
internal sealed class PostgreSqlSchemaFilter
{
    /// <summary>
    /// The exact schema names to include, ordinally sorted. Empty means "no include restriction".
    /// </summary>
    internal ReadOnlyCollection<string> IncludedSchemas { get; }

    /// <summary>
    /// The exact schema names to exclude, ordinally sorted. Empty means "no extra exclusion".
    /// </summary>
    internal ReadOnlyCollection<string> ExcludedSchemas { get; }

    /// <summary>
    /// A filter that restricts nothing: every eligible non-system schema is inspected.
    /// </summary>
    internal static PostgreSqlSchemaFilter IncludeEverything { get; } = new([], []);

    /// <summary>
    /// Creates a filter from exact schema names.
    /// </summary>
    /// <exception cref="PostgreSqlSchemaFilterException">
    /// Either list is null, holds a null, empty, whitespace-only or NUL-containing name, repeats a
    /// name, or names the same schema in both lists.
    /// </exception>
    internal PostgreSqlSchemaFilter(IReadOnlyList<string> includedSchemas, IReadOnlyList<string> excludedSchemas)
    {
        // Deliberately not ArgumentNullException: a filter rejection must look the same to a
        // caller whatever was wrong with it, and must never name what was wrong.
        if (includedSchemas is null || excludedSchemas is null)
        {
            throw new PostgreSqlSchemaFilterException();
        }

        ReadOnlyCollection<string> included = Normalize(includedSchemas);
        ReadOnlyCollection<string> excluded = Normalize(excludedSchemas);

        // A name in both lists is a contradiction the adapter refuses to resolve silently.
        var includedSet = new HashSet<string>(included, StringComparer.Ordinal);
        foreach (string name in excluded)
        {
            if (includedSet.Contains(name))
            {
                throw new PostgreSqlSchemaFilterException();
            }
        }

        IncludedSchemas = included;
        ExcludedSchemas = excluded;
    }

    /// <summary>
    /// Validates every name, copies the list and sorts the copy ordinally.
    /// </summary>
    private static ReadOnlyCollection<string> Normalize(IReadOnlyList<string> names)
    {
        var copied = new string[names.Count];
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var index = 0; index < names.Count; index++)
        {
            string? name = names[index];

            if (string.IsNullOrWhiteSpace(name))
            {
                throw new PostgreSqlSchemaFilterException();
            }

            // A NUL can truncate an identifier inside the driver or the server.
            if (name.Contains('\0', StringComparison.Ordinal))
            {
                throw new PostgreSqlSchemaFilterException();
            }

            // Ordinal and case-sensitive: "Public" and "public" are different schemas in
            // PostgreSQL, so treating them as duplicates would be wrong.
            if (!seen.Add(name))
            {
                throw new PostgreSqlSchemaFilterException();
            }

            copied[index] = name;
        }

        Array.Sort(copied, StringComparer.Ordinal);
        return Array.AsReadOnly(copied);
    }
}
