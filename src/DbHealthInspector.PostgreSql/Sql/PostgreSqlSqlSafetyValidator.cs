using System.Collections.ObjectModel;
using System.Text;

namespace DbHealthInspector.PostgreSql.Sql;

/// <summary>
/// The two-layer, fail-closed validator applied to every inventoried statement when the inventory
/// is constructed.
/// </summary>
/// <remarks>
/// <para>
/// <b>Layer 1 — lexical safety.</b> Defence in depth, <b>not</b> a PostgreSQL parser. It
/// recognises only the narrow shapes the product needs — the exact <c>SET TRANSACTION READ
/// ONLY</c> form and a conservatively classified <c>SELECT</c> — and rejects everything else,
/// including anything it cannot fully account for. No external parser, and no single regular
/// expression pretending to be one.
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
/// <para>
/// <b>Layer 2 — the frozen statement contract.</b> Layer 1 alone proves only that a statement is
/// <i>some</i> safely classified <c>SELECT</c>; on its own it would happily accept
/// <c>SELECT 1</c>, <c>SELECT version()</c> or a <c>SELECT</c> over a business table under a
/// capability command kind. Layer 2 closes that gap by resolving the declared
/// <see cref="PostgreSqlSqlStatementId"/> against a frozen table and requiring the command kind,
/// the exact SQL text and the exact ordered parameter declarations all to match
/// (GC-DHI-04C-C1, R1-01).
/// </para>
/// <para>
/// The division of labour is deliberate: <b>the command kind classifies the shape, the statement
/// ID freezes the only authorized SQL, and both must match.</b> A shared kind is therefore never
/// permission to run an arbitrary statement of that shape — C002 and C003 are both
/// <see cref="PostgreSqlSqlCommandKind.SelectCapabilityCheck"/>, yet neither can carry the other's
/// SQL.
/// </para>
/// <para>
/// There is no relaxed mode, no runtime registration, no test-only bypass and no fallback that
/// accepts a generic <c>SELECT</c> as an authorized definition.
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
    /// Validates <paramref name="definition"/> through both layers: first every structural, token,
    /// shape and placeholder rule, then the frozen statement contract. Throws on the first
    /// violation found; returns normally only for one of the seven canonical definitions.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="definition"/> is <see langword="null"/>.</exception>
    /// <exception cref="PostgreSqlSqlSafetyException">The statement violates a safety rule.</exception>
    internal static void Validate(PostgreSqlSqlStatementDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        ValidateLexicalSafety(definition);
        ValidateFrozenStatementContract(definition);
    }

    /// <summary>
    /// Layer 1. Proves the SQL is lexically safe and that its declared command kind and parameter
    /// declarations agree with what the scanner actually found.
    /// </summary>
    /// <exception cref="PostgreSqlSqlSafetyException">The statement violates a safety rule.</exception>
    private static void ValidateLexicalSafety(PostgreSqlSqlStatementDefinition definition)
    {
        ScanResult scan = Scan(definition.Sql);
        ValidateShape(definition, scan);
        ValidatePlaceholders(definition, scan);
    }

    /// <summary>
    /// Validates raw SQL text against layer 1's structural and token rules only. It takes no
    /// definition, resolves no statement id and cannot authorize anything: it exists so the
    /// scanner's prohibited classes can be exercised by mutating canonical SQL, and production
    /// always goes through <see cref="Validate(PostgreSqlSqlStatementDefinition)"/>, which also
    /// applies layer 2. Passing this method is therefore <b>not</b> sufficient for a statement to
    /// enter the inventory.
    /// </summary>
    /// <exception cref="PostgreSqlSqlSafetyException">The statement violates a safety rule.</exception>
    internal static void ValidateText(string? sql)
    {
        ScanResult scan = Scan(sql);
        _ = ClassifyShape(scan);
    }

    // --- Layer 2: the frozen statement contract ---------------------------------------------

    /// <summary>
    /// One authorized statement's complete frozen contract: the single command kind it may
    /// declare, the exact SQL text it must carry, and the exact declared parameter types by
    /// ascending position.
    /// </summary>
    private sealed class FrozenStatementContract
    {
        internal FrozenStatementContract(
            PostgreSqlSqlCommandKind kind,
            string sql,
            params PostgreSqlSqlParameterType[] parameters)
        {
            Kind = kind;
            Sql = sql;
            Parameters = Array.AsReadOnly(parameters);
        }

        internal PostgreSqlSqlCommandKind Kind { get; }

        internal string Sql { get; }

        /// <summary>Declared types by ascending position: index 0 is <c>$1</c>.</summary>
        internal ReadOnlyCollection<PostgreSqlSqlParameterType> Parameters { get; }
    }

    /// <summary>
    /// The complete frozen inventory contract — the only seven (id, kind, SQL, parameters)
    /// combinations that exist.
    /// </summary>
    /// <remarks>
    /// The SQL comes from <see cref="PostgreSqlSqlInventory"/>'s canonical <see langword="const"/>
    /// fields rather than being duplicated here, so the two can never drift apart. Because those
    /// are compile-time constants, reading them does not run the inventory's type initializer, and
    /// no initialization cycle exists between the two types even though the inventory's
    /// constructor calls this validator.
    /// </remarks>
    private static readonly Dictionary<PostgreSqlSqlStatementId, FrozenStatementContract> FrozenContracts = new()
    {
        [PostgreSqlSqlStatementId.SetTransactionReadOnly] = new(
            PostgreSqlSqlCommandKind.SetTransactionReadOnly,
            PostgreSqlSqlInventory.SetTransactionReadOnlySql),

        [PostgreSqlSqlStatementId.ApplyLocalTimeouts] = new(
            PostgreSqlSqlCommandKind.SelectConfiguration,
            PostgreSqlSqlInventory.ApplyLocalTimeoutsSql,
            PostgreSqlSqlParameterType.Int32,
            PostgreSqlSqlParameterType.Int32,
            PostgreSqlSqlParameterType.Int32),

        [PostgreSqlSqlStatementId.VerifySessionState] = new(
            PostgreSqlSqlCommandKind.SelectVerification,
            PostgreSqlSqlInventory.VerifySessionStateSql,
            PostgreSqlSqlParameterType.Int32,
            PostgreSqlSqlParameterType.Int32,
            PostgreSqlSqlParameterType.Int32),

        [PostgreSqlSqlStatementId.ReadServerIdentity] = new(
            PostgreSqlSqlCommandKind.SelectServerIdentity,
            PostgreSqlSqlInventory.ReadServerIdentitySql),

        [PostgreSqlSqlStatementId.CheckCatalogMetadataAccess] = new(
            PostgreSqlSqlCommandKind.SelectCapabilityCheck,
            PostgreSqlSqlInventory.CheckCatalogMetadataAccessSql),

        [PostgreSqlSqlStatementId.CheckUsageStatisticsAccess] = new(
            PostgreSqlSqlCommandKind.SelectCapabilityCheck,
            PostgreSqlSqlInventory.CheckUsageStatisticsAccessSql),

        [PostgreSqlSqlStatementId.ReadStatisticsReset] = new(
            PostgreSqlSqlCommandKind.SelectStatistics,
            PostgreSqlSqlInventory.ReadStatisticsResetSql),
    };

    /// <summary>
    /// Layer 2. Requires the whole (id, kind, SQL, parameters) tuple to be one of the seven frozen
    /// combinations. Every other combination — including a canonical SQL declared under the wrong
    /// kind, a canonical kind carrying different SQL, or a single added, removed or altered token
    /// — is rejected.
    /// </summary>
    /// <exception cref="PostgreSqlSqlSafetyException">
    /// The statement is not one of the seven canonical definitions.
    /// </exception>
    private static void ValidateFrozenStatementContract(PostgreSqlSqlStatementDefinition definition)
    {
        // An id or kind outside its enumeration can never resolve to a contract; rejecting it
        // explicitly keeps the failure a safety failure rather than a dictionary miss.
        if (!Enum.IsDefined(definition.Id) || !Enum.IsDefined(definition.Kind))
        {
            throw new PostgreSqlSqlSafetyException();
        }

        if (!FrozenContracts.TryGetValue(definition.Id, out FrozenStatementContract? contract))
        {
            // A statement id with no frozen contract is unauthorized by construction — including a
            // future enum member added without one.
            throw new PostgreSqlSqlSafetyException();
        }

        if (definition.Kind != contract.Kind)
        {
            throw new PostgreSqlSqlSafetyException();
        }

        // Ordinal: the authorized SQL is an exact byte sequence, not a culture-sensitive one.
        if (!string.Equals(definition.Sql, contract.Sql, StringComparison.Ordinal))
        {
            throw new PostgreSqlSqlSafetyException();
        }

        if (definition.Parameters.Count != contract.Parameters.Count)
        {
            throw new PostgreSqlSqlSafetyException();
        }

        for (var index = 0; index < contract.Parameters.Count; index++)
        {
            PostgreSqlSqlParameterDefinition declared = definition.Parameters[index];

            if (declared.Position != index + 1 || declared.Type != contract.Parameters[index])
            {
                throw new PostgreSqlSqlSafetyException();
            }
        }
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

        // Every kind other than the SET form is a SELECT. Layer 1 proves only that the statement
        // is a safely classified SELECT, so all it can require here is that the declared kind
        // agrees about which of the two families it belongs to. The narrower distinctions between
        // the SELECT kinds are enforced by layer 2, which binds each kind to one statement id and
        // one exact SQL text.
        bool agrees = definition.Kind switch
        {
            PostgreSqlSqlCommandKind.SetTransactionReadOnly => observed == PostgreSqlSqlCommandKind.SetTransactionReadOnly,
            PostgreSqlSqlCommandKind.SelectConfiguration
                or PostgreSqlSqlCommandKind.SelectVerification
                or PostgreSqlSqlCommandKind.SelectServerIdentity
                or PostgreSqlSqlCommandKind.SelectCapabilityCheck
                or PostgreSqlSqlCommandKind.SelectStatistics =>
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
