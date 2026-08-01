using System.Text;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// A small, deterministic, fail-closed lexical validator applied to every inventoried statement
/// when the inventory is constructed.
/// </summary>
/// <remarks>
/// <para>
/// This is defence in depth, <b>not</b> a PostgreSQL parser. It recognises only the narrow shapes
/// GC-DHI-04B needs — the exact <c>SET TRANSACTION READ ONLY</c> form and a conservatively
/// classified <c>SELECT</c> — and rejects everything else, including anything it cannot fully
/// account for. No external parser, and no single regular expression pretending to be one.
/// </para>
/// <para>
/// It scans character by character, tracking whether it is inside a single-quoted string literal
/// (with PostgreSQL's doubled-quote escape) or a double-quoted identifier, so that a prohibited
/// word appearing only inside a literal or a quoted identifier — or merely as a substring of a
/// longer identifier such as <c>lock_timeout_matches</c> — is not mistaken for a prohibited
/// statement. Comments, dollar quotes, backslash commands and semicolons are rejected outright
/// rather than skipped, because no inventoried statement has any legitimate reason to contain
/// them.
/// </para>
/// </remarks>
internal static class PostgreSqlSqlSafetyValidator
{
    /// <summary>
    /// Words that must never appear as a complete token outside a string literal or quoted
    /// identifier. Ordinal, case-insensitive, whole-token comparison.
    /// </summary>
    private static readonly HashSet<string> ProhibitedTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "INSERT", "UPDATE", "DELETE", "MERGE", "CREATE", "ALTER", "DROP", "TRUNCATE", "VACUUM",
        "ANALYZE", "REINDEX", "GRANT", "REVOKE", "COPY", "CALL", "DO", "EXECUTE", "PREPARE",
        "DEALLOCATE", "LOCK", "CLUSTER", "CHECKPOINT", "REFRESH", "IMPORT", "REASSIGN",
        "SECURITY", "LISTEN", "NOTIFY", "UNLISTEN", "DISCARD", "RESET", "LOAD", "COMMENT",
    };

    /// <summary>
    /// The one and only authorised <c>SET</c> shape, already whitespace- and case-normalised.
    /// </summary>
    private const string CanonicalSetTransactionReadOnly = "SET TRANSACTION READ ONLY";

    /// <summary>
    /// Validates <paramref name="definition"/>'s SQL against every structural, token, shape and
    /// placeholder rule. Throws on the first violation found; returns normally when the statement
    /// is provably one of the authorised shapes.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="PostgreSqlSqlSafetyException">The statement violates a safety rule.</exception>
    internal static void Validate(PostgreSqlSqlStatementDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        ScanResult scan = Scan(definition.Sql);
        ValidateShape(definition, scan);
        ValidatePlaceholders(definition, scan);
    }

    /// <summary>
    /// Validates raw SQL text against the structural and token rules only, without a definition
    /// to compare placeholders or command kind against. Used by the safety-contract tests to
    /// exercise every prohibited class; production always goes through
    /// <see cref="Validate(PostgreSqlSqlStatementDefinition)"/>.
    /// </summary>
    /// <exception cref="PostgreSqlSqlSafetyException">The statement violates a safety rule.</exception>
    internal static void ValidateText(string? sql)
    {
        ScanResult scan = Scan(sql);
        _ = ClassifyShape(scan);
    }

    // --- Lexical scan ---------------------------------------------------------------------

    private sealed class ScanResult
    {
        internal List<string> Tokens { get; } = [];

        internal List<int> Placeholders { get; } = [];

        internal string Normalized { get; set; } = string.Empty;
    }

    private static ScanResult Scan(string? sql)
    {
        if (sql is null)
        {
            throw new PostgreSqlSqlSafetyException();
        }

        if (string.IsNullOrWhiteSpace(sql))
        {
            throw new PostgreSqlSqlSafetyException();
        }

        var result = new ScanResult();
        var normalized = new StringBuilder(sql.Length);
        var token = new StringBuilder();
        var index = 0;

        void FlushToken()
        {
            if (token.Length > 0)
            {
                result.Tokens.Add(token.ToString());
                token.Clear();
            }
        }

        void AppendNormalizedSeparator()
        {
            if (normalized.Length > 0 && normalized[^1] != ' ')
            {
                normalized.Append(' ');
            }
        }

        while (index < sql.Length)
        {
            char current = sql[index];

            // A byte NUL can truncate the statement inside the driver or the server.
            if (current == '\0')
            {
                throw new PostgreSqlSqlSafetyException();
            }

            // Line comment: never legitimate in an inventoried statement.
            if (current == '-' && index + 1 < sql.Length && sql[index + 1] == '-')
            {
                throw new PostgreSqlSqlSafetyException();
            }

            // Block comment open or a stray close.
            if (current == '/' && index + 1 < sql.Length && sql[index + 1] == '*')
            {
                throw new PostgreSqlSqlSafetyException();
            }

            if (current == '*' && index + 1 < sql.Length && sql[index + 1] == '/')
            {
                throw new PostgreSqlSqlSafetyException();
            }

            // Statement separator, including a merely trailing one.
            if (current == ';')
            {
                throw new PostgreSqlSqlSafetyException();
            }

            // psql backslash command.
            if (current == '\\')
            {
                throw new PostgreSqlSqlSafetyException();
            }

            // Single-quoted string literal, with the doubled-quote escape.
            if (current == '\'')
            {
                FlushToken();
                index = SkipStringLiteral(sql, index);
                AppendNormalizedSeparator();
                normalized.Append("''");
                AppendNormalizedSeparator();
                continue;
            }

            // Double-quoted identifier, with the doubled-quote escape.
            if (current == '"')
            {
                FlushToken();
                index = SkipQuotedIdentifier(sql, index);
                AppendNormalizedSeparator();
                normalized.Append("\"\"");
                AppendNormalizedSeparator();
                continue;
            }

            if (current == '$')
            {
                FlushToken();
                index = ReadPlaceholder(sql, index, result);
                AppendNormalizedSeparator();
                normalized.Append("$?");
                AppendNormalizedSeparator();
                continue;
            }

            // The only pipe form the inventory uses is the string-concatenation operator, and it
            // is recognised as an exact pair. A lone '|' (bitwise or) and any longer run such as
            // '|||' are rejected rather than silently accepted as punctuation
            // (GC-DHI-04B-C1, F-03).
            if (current == '|')
            {
                FlushToken();

                if (index + 1 >= sql.Length || sql[index + 1] != '|')
                {
                    throw new PostgreSqlSqlSafetyException();
                }

                if (index + 2 < sql.Length && sql[index + 2] == '|')
                {
                    throw new PostgreSqlSqlSafetyException();
                }

                normalized.Append("||");
                index += 2;
                continue;
            }

            if (char.IsWhiteSpace(current))
            {
                FlushToken();
                AppendNormalizedSeparator();
                index++;
                continue;
            }

            // Identifier / keyword / numeric body.
            if (char.IsLetterOrDigit(current) || current == '_')
            {
                token.Append(current);
                normalized.Append(char.ToUpperInvariant(current));
                index++;
                continue;
            }

            // Remaining punctuation legitimately used by the inventory: ( ) , . : * = + - /
            if (IsAllowedPunctuation(current))
            {
                FlushToken();
                normalized.Append(current);
                index++;
                continue;
            }

            // Anything else is an unknown form; fail closed.
            throw new PostgreSqlSqlSafetyException();
        }

        FlushToken();
        result.Normalized = CollapseWhitespace(normalized.ToString());
        return result;
    }

    private static bool IsAllowedPunctuation(char value) =>
        value is '(' or ')' or ',' or '.' or ':' or '*' or '=' or '+' or '-' or '/';

    private static int SkipStringLiteral(string sql, int index)
    {
        // sql[index] == '\''
        index++;
        while (index < sql.Length)
        {
            if (sql[index] == '\0')
            {
                throw new PostgreSqlSqlSafetyException();
            }

            if (sql[index] == '\'')
            {
                // A doubled quote is an escaped quote, not the terminator.
                if (index + 1 < sql.Length && sql[index + 1] == '\'')
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        // Unterminated literal.
        throw new PostgreSqlSqlSafetyException();
    }

    private static int SkipQuotedIdentifier(string sql, int index)
    {
        // sql[index] == '"'
        index++;
        while (index < sql.Length)
        {
            if (sql[index] == '\0')
            {
                throw new PostgreSqlSqlSafetyException();
            }

            if (sql[index] == '"')
            {
                if (index + 1 < sql.Length && sql[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        // Unterminated quoted identifier.
        throw new PostgreSqlSqlSafetyException();
    }

    private static int ReadPlaceholder(string sql, int index, ScanResult result)
    {
        // sql[index] == '$'. Only "$<digits>" is a placeholder; "$tag$" is a dollar-quoted
        // block and "$$" is an empty-tag dollar quote — both rejected.
        var cursor = index + 1;
        var digits = 0;
        while (cursor < sql.Length && char.IsAsciiDigit(sql[cursor]))
        {
            cursor++;
            digits++;
        }

        if (digits == 0)
        {
            // Dollar quote or bare '$'.
            throw new PostgreSqlSqlSafetyException();
        }

        // "$1$" would begin a dollar-quoted block with a numeric tag.
        if (cursor < sql.Length && sql[cursor] == '$')
        {
            throw new PostgreSqlSqlSafetyException();
        }

        var text = sql[(index + 1)..cursor];
        if (!int.TryParse(text, out int position) || position < 1)
        {
            // Includes "$0" and any leading-zero/overflow form.
            throw new PostgreSqlSqlSafetyException();
        }

        result.Placeholders.Add(position);
        return cursor;
    }

    private static string CollapseWhitespace(string value) =>
        string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    // --- Shape and token rules -------------------------------------------------------------

    private static PostgreSqlSqlCommandKind ClassifyShape(ScanResult scan)
    {
        if (scan.Tokens.Count == 0)
        {
            throw new PostgreSqlSqlSafetyException();
        }

        string first = scan.Tokens[0];

        if (string.Equals(first, "SET", StringComparison.OrdinalIgnoreCase))
        {
            // The only authorised SET is the exact B001 shape. "SET LOCAL ...", "SET
            // search_path ...", "SET TRANSACTION ISOLATION ..." and every other form fail here.
            if (!string.Equals(scan.Normalized, CanonicalSetTransactionReadOnly, StringComparison.Ordinal))
            {
                throw new PostgreSqlSqlSafetyException();
            }

            return PostgreSqlSqlCommandKind.SetTransactionReadOnly;
        }

        if (!string.Equals(first, "SELECT", StringComparison.OrdinalIgnoreCase))
        {
            // WITH (including any CTE, data-modifying or not), SHOW, VALUES, TABLE, EXPLAIN and
            // every other leading form are rejected. GC-DHI-04B inventories no WITH statement, so
            // rejecting the whole class keeps the validator fail-closed instead of shipping a
            // premature CTE parser. A later gate that needs WITH must revisit this decision.
            throw new PostgreSqlSqlSafetyException();
        }

        RejectProhibitedTokens(scan);
        RejectUnsafeSelectForms(scan);
        return PostgreSqlSqlCommandKind.SelectVerification;
    }

    private static void RejectProhibitedTokens(ScanResult scan)
    {
        foreach (string token in scan.Tokens)
        {
            if (ProhibitedTokens.Contains(token))
            {
                throw new PostgreSqlSqlSafetyException();
            }
        }
    }

    private static void RejectUnsafeSelectForms(ScanResult scan)
    {
        // SELECT ... INTO materialises a new table.
        for (var index = 0; index < scan.Tokens.Count; index++)
        {
            if (string.Equals(scan.Tokens[index], "INTO", StringComparison.OrdinalIgnoreCase))
            {
                throw new PostgreSqlSqlSafetyException();
            }
        }

        // Row-locking clauses take real locks and are never read-only in effect.
        if (ContainsSequence(scan.Normalized, "FOR UPDATE")
            || ContainsSequence(scan.Normalized, "FOR NO KEY UPDATE")
            || ContainsSequence(scan.Normalized, "FOR SHARE")
            || ContainsSequence(scan.Normalized, "FOR KEY SHARE"))
        {
            throw new PostgreSqlSqlSafetyException();
        }
    }

    private static bool ContainsSequence(string normalized, string sequence) =>
        normalized.Equals(sequence, StringComparison.Ordinal)
            || normalized.StartsWith(sequence + " ", StringComparison.Ordinal)
            || normalized.EndsWith(" " + sequence, StringComparison.Ordinal)
            || normalized.Contains(" " + sequence + " ", StringComparison.Ordinal);

    private static void ValidateShape(PostgreSqlSqlStatementDefinition definition, ScanResult scan)
    {
        PostgreSqlSqlCommandKind observed = ClassifyShape(scan);

        // SelectConfiguration and SelectVerification are both SELECTs; the validator proves only
        // that the statement is a safely classified SELECT, and the declared kind must agree that
        // it is a SELECT rather than the SET form.
        bool agrees = definition.Kind switch
        {
            PostgreSqlSqlCommandKind.SetTransactionReadOnly => observed == PostgreSqlSqlCommandKind.SetTransactionReadOnly,
            PostgreSqlSqlCommandKind.SelectConfiguration or PostgreSqlSqlCommandKind.SelectVerification =>
                observed == PostgreSqlSqlCommandKind.SelectVerification,
            _ => false,
        };

        if (!agrees)
        {
            throw new PostgreSqlSqlSafetyException();
        }
    }

    private static void ValidatePlaceholders(PostgreSqlSqlStatementDefinition definition, ScanResult scan)
    {
        var distinct = new SortedSet<int>(scan.Placeholders);

        // Every placeholder present must be declared, and every declaration must be used.
        if (distinct.Count != definition.Parameters.Count)
        {
            throw new PostgreSqlSqlSafetyException();
        }

        var expected = 1;
        foreach (int position in distinct)
        {
            // Consecutive from $1 with no gaps and no $0 (the scanner already rejected $0).
            if (position != expected)
            {
                throw new PostgreSqlSqlSafetyException();
            }

            expected++;
        }

        foreach (PostgreSqlSqlParameterDefinition parameter in definition.Parameters)
        {
            if (!distinct.Contains(parameter.Position))
            {
                throw new PostgreSqlSqlSafetyException();
            }
        }
    }
}
