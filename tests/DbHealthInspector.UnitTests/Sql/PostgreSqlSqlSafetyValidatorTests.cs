using DbHealthInspector.PostgreSql.Sql;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// The fail-closed SQL safety validator (PG-06 foundation, GC-DHI-04B §9). Everything it does not
/// positively recognise as one of the two authorised shapes must be rejected.
/// </summary>
public sealed class PostgreSqlSqlSafetyValidatorTests
{
    private static void AssertRejected(string? sql) =>
        Assert.Throws<PostgreSqlSqlSafetyException>(() => PostgreSqlSqlSafetyValidator.ValidateText(sql));

    private static void AssertAccepted(string sql) =>
        PostgreSqlSqlSafetyValidator.ValidateText(sql);

    // --- Accepted shapes ---------------------------------------------------------------------

    [Fact]
    public void Accepts_B001Exactly()
    {
        AssertAccepted(PostgreSqlSqlInventory.SetTransactionReadOnlySql);
    }

    [Theory]
    [InlineData("set transaction read only")]
    [InlineData("Set Transaction Read Only")]
    [InlineData("SET   TRANSACTION\n  READ\tONLY")]
    public void Accepts_B001WithEquivalentCasingAndWhitespace(string sql)
    {
        // The canonical shape is compared after case and whitespace normalisation, so an
        // equivalent spelling is the same statement, not a different one.
        AssertAccepted(sql);
    }

    [Fact]
    public void Accepts_B002Exactly()
    {
        AssertAccepted(PostgreSqlSqlInventory.ApplyLocalTimeoutsSql);
    }

    [Fact]
    public void Accepts_B003Exactly()
    {
        AssertAccepted(PostgreSqlSqlInventory.VerifySessionStateSql);
    }

    [Fact]
    public void Accepts_ProhibitedWordInsideAStringLiteral()
    {
        AssertAccepted("SELECT 'this text mentions UPDATE and DROP'");
    }

    [Fact]
    public void Accepts_ProhibitedWordInsideAQuotedIdentifier()
    {
        AssertAccepted("SELECT 1 AS \"update\"");
    }

    [Fact]
    public void Accepts_IdentifierMerelyContainingAProhibitedSubstring()
    {
        // lock_timeout_matches contains "lock" but is a single identifier token, and B003 depends
        // on exactly this distinction.
        AssertAccepted("SELECT lock_timeout_matches, updated_at, dropped_count FROM_NOTHING");
    }

    [Fact]
    public void Accepts_EscapedQuoteInsideAStringLiteral()
    {
        AssertAccepted("SELECT 'it''s fine'");
    }

    // --- Structural rejections ----------------------------------------------------------------

    [Fact]
    public void Rejects_Null() => AssertRejected(null);

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Rejects_Blank(string sql) => AssertRejected(sql);

    [Fact]
    public void Rejects_ByteNul() => AssertRejected("SELECT 1\0");

    [Fact]
    public void Rejects_NulInsideAStringLiteral() => AssertRejected("SELECT 'a\0b'");

    [Fact]
    public void Rejects_LineComment() => AssertRejected("SELECT 1 -- trailing comment");

    [Fact]
    public void Rejects_BlockCommentOpen() => AssertRejected("SELECT /* hidden */ 1");

    [Fact]
    public void Rejects_StrayBlockCommentClose() => AssertRejected("SELECT 1 */");

    [Fact]
    public void Rejects_DollarQuotedBlock() => AssertRejected("SELECT $$ arbitrary $$");

    [Fact]
    public void Rejects_TaggedDollarQuotedBlock() => AssertRejected("SELECT $tag$ arbitrary $tag$");

    [Fact]
    public void Rejects_BackslashCommand() => AssertRejected("\\dt");

    [Fact]
    public void Rejects_Semicolon() => AssertRejected("SELECT 1; SELECT 2");

    [Fact]
    public void Rejects_TrailingSemicolon() => AssertRejected("SELECT 1;");

    [Fact]
    public void Rejects_TwoStatements() => AssertRejected("SET TRANSACTION READ ONLY; SELECT 1");

    [Fact]
    public void Rejects_UnterminatedStringLiteral() => AssertRejected("SELECT 'unterminated");

    [Fact]
    public void Rejects_UnterminatedQuotedIdentifier() => AssertRejected("SELECT 1 AS \"unterminated");

    // --- Prohibited token classes ---------------------------------------------------------------

    [Theory]
    [InlineData("INSERT")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    [InlineData("MERGE")]
    [InlineData("CREATE")]
    [InlineData("ALTER")]
    [InlineData("DROP")]
    [InlineData("TRUNCATE")]
    [InlineData("VACUUM")]
    [InlineData("ANALYZE")]
    [InlineData("REINDEX")]
    [InlineData("GRANT")]
    [InlineData("REVOKE")]
    [InlineData("COPY")]
    [InlineData("CALL")]
    [InlineData("DO")]
    [InlineData("EXECUTE")]
    [InlineData("PREPARE")]
    [InlineData("DEALLOCATE")]
    [InlineData("LOCK")]
    [InlineData("CLUSTER")]
    [InlineData("CHECKPOINT")]
    [InlineData("REFRESH")]
    [InlineData("IMPORT")]
    [InlineData("REASSIGN")]
    [InlineData("SECURITY")]
    [InlineData("LISTEN")]
    [InlineData("NOTIFY")]
    [InlineData("UNLISTEN")]
    [InlineData("DISCARD")]
    [InlineData("RESET")]
    [InlineData("LOAD")]
    [InlineData("COMMENT")]
    public void Rejects_ProhibitedTokenInsideASelect(string prohibited)
    {
        AssertRejected($"SELECT 1 {prohibited} something");
    }

    [Theory]
    [InlineData("insert")]
    [InlineData("UpDaTe")]
    [InlineData("dRoP")]
    public void Rejects_ProhibitedTokenRegardlessOfCase(string prohibited)
    {
        AssertRejected($"SELECT 1 {prohibited} something");
    }

    [Theory]
    [InlineData("INSERT INTO t VALUES (1)")]
    [InlineData("UPDATE t SET a = 1")]
    [InlineData("DELETE FROM t")]
    [InlineData("DROP TABLE t")]
    [InlineData("CREATE TABLE t (a int)")]
    [InlineData("TRUNCATE t")]
    public void Rejects_StatementsThatDoNotStartWithSelectOrSet(string sql)
    {
        AssertRejected(sql);
    }

    // --- SELECT-specific rejections ---------------------------------------------------------------

    [Fact]
    public void Rejects_SelectInto() => AssertRejected("SELECT a INTO other FROM t");

    [Theory]
    [InlineData("SELECT a FROM t FOR UPDATE")]
    [InlineData("SELECT a FROM t FOR NO KEY UPDATE")]
    [InlineData("SELECT a FROM t FOR SHARE")]
    [InlineData("SELECT a FROM t FOR KEY SHARE")]
    public void Rejects_LockingSelects(string sql)
    {
        AssertRejected(sql);
    }

    // --- WITH / other leading forms ---------------------------------------------------------------

    [Fact]
    public void Rejects_WithSelect()
    {
        // GC-DHI-04B inventories no WITH statement, so the whole class is rejected rather than
        // shipping a premature CTE parser. A later gate needing WITH must revisit this.
        AssertRejected("WITH c AS (SELECT 1) SELECT * FROM c");
    }

    [Fact]
    public void Rejects_DataModifyingCte()
    {
        AssertRejected("WITH c AS (UPDATE t SET a = 1 RETURNING a) SELECT * FROM c");
    }

    [Theory]
    [InlineData("SHOW transaction_isolation")]
    [InlineData("VALUES (1)")]
    [InlineData("TABLE t")]
    [InlineData("EXPLAIN SELECT 1")]
    [InlineData("BEGIN")]
    [InlineData("COMMIT")]
    [InlineData("ROLLBACK")]
    public void Rejects_OtherLeadingForms(string sql)
    {
        AssertRejected(sql);
    }

    // --- SET-specific rejections ------------------------------------------------------------------

    [Theory]
    [InlineData("SET LOCAL statement_timeout = '1s'")]
    [InlineData("SET search_path = public")]
    [InlineData("SET TRANSACTION ISOLATION LEVEL SERIALIZABLE")]
    [InlineData("SET TRANSACTION READ WRITE")]
    [InlineData("SET TRANSACTION READ ONLY DEFERRABLE")]
    [InlineData("SET SESSION AUTHORIZATION postgres")]
    public void Rejects_EverySetFormOtherThanTheExactB001Shape(string sql)
    {
        AssertRejected(sql);
    }

    // --- Placeholder rules --------------------------------------------------------------------------

    // --- Pipe grammar: exactly `||` and nothing else (GC-DHI-04B-C1, F-03) ---------------------------

    [Fact]
    public void Accepts_ExactConcatenationOperator() => AssertAccepted("SELECT 'a' || 'b'");

    [Fact]
    public void Accepts_ConcatenationOperatorWithoutSurroundingSpaces() => AssertAccepted("SELECT 'a'||'b'");

    [Fact]
    public void Rejects_SinglePipe() => AssertRejected("SELECT 1 | 2");

    [Fact]
    public void Rejects_TriplePipe() => AssertRejected("SELECT 1 ||| 2");

    [Fact]
    public void Rejects_QuadruplePipe() => AssertRejected("SELECT 1 |||| 2");

    [Fact]
    public void Rejects_TrailingSinglePipe() => AssertRejected("SELECT 1 |");

    [Fact]
    public void Accepts_PipeInsideAStringLiteral() => AssertAccepted("SELECT '|'");

    [Fact]
    public void Accepts_PipeInsideAQuotedIdentifier() => AssertAccepted("SELECT 1 AS \"|\"");

    [Fact]
    public void Rejects_B002MutatedToASinglePipe()
    {
        AssertRejected(PostgreSqlSqlInventory.ApplyLocalTimeoutsSql.Replace("||", "|", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_B002MutatedToATriplePipe()
    {
        AssertRejected(PostgreSqlSqlInventory.ApplyLocalTimeoutsSql.Replace("||", "|||", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_B002WithADuplicatedConcatenationOperator()
    {
        // `||` doubled into `||||` is a four-pipe run, which the exact-pair rule rejects.
        AssertRejected(PostgreSqlSqlInventory.ApplyLocalTimeoutsSql.Replace("||", "||||", StringComparison.Ordinal));
    }

    [Fact]
    public void DocumentsTheLexicalBoundary_RemovingTheConcatenationOperatorIsNotALexicalError()
    {
        // Deleting the operator leaves `$1::text 'ms'`. Every token there is individually legal,
        // so this is a *grammatical* error, not a lexical one, and the validator — which is
        // explicitly not a PostgreSQL parser — cannot and does not claim to catch it.
        //
        // Nothing depends on it doing so: the inventory's SQL is a compile-time constant that no
        // caller can mutate, and the PostgreSQL 18 suite executes each statement for real, where
        // the server rejects a malformed statement outright. This test pins the boundary so a
        // future reader does not mistake the validator for a parser.
        string mutated = PostgreSqlSqlInventory.ApplyLocalTimeoutsSql.Replace("|| ", string.Empty, StringComparison.Ordinal);

        AssertAccepted(mutated);
    }

    // --- Placeholder rules --------------------------------------------------------------------------

    [Fact]
    public void Rejects_ZeroPlaceholder() => AssertRejected("SELECT $0");

    [Fact]
    public void Rejects_BareDollar() => AssertRejected("SELECT $");

    [Fact]
    public void Definition_RejectsFirstPlaceholderThatIsNotDollarOne()
    {
        AssertDefinitionRejected("SELECT $2", [new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "value")]);
    }

    [Fact]
    public void Definition_RejectsPlaceholderGap()
    {
        AssertDefinitionRejected(
            "SELECT $1, $3",
            [
                new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "first"),
                new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.Int32, "second"),
            ]);
    }

    [Fact]
    public void Definition_RejectsUndeclaredPlaceholder()
    {
        AssertDefinitionRejected(
            "SELECT $1, $2",
            [new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "only one declared")]);
    }

    [Fact]
    public void Definition_RejectsUnusedDeclaredParameter()
    {
        AssertDefinitionRejected(
            "SELECT $1",
            [
                new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "used"),
                new PostgreSqlSqlParameterDefinition(2, PostgreSqlSqlParameterType.Int32, "never used"),
            ]);
    }

    [Fact]
    public void Definition_AcceptsARepeatedPlaceholderThatIsDeclaredOnce()
    {
        // B002/B003 style: the same declared position may legitimately appear more than once in
        // the text, as long as the set of distinct positions matches the declarations exactly.
        var definition = new PostgreSqlSqlStatementDefinition(
            PostgreSqlSqlStatementId.VerifySessionState,
            PostgreSqlSqlCommandKind.SelectVerification,
            "SELECT $1, $1",
            [new PostgreSqlSqlParameterDefinition(1, PostgreSqlSqlParameterType.Int32, "used twice")],
            "test");

        PostgreSqlSqlSafetyValidator.Validate(definition);
    }

    [Fact]
    public void Definition_RejectsDeclaredKindThatDisagreesWithTheProvenShape()
    {
        // Declaring a SELECT as the SET form (or vice versa) must not pass.
        AssertDefinitionRejected("SELECT 1", [], PostgreSqlSqlCommandKind.SetTransactionReadOnly);
    }

    [Fact]
    public void Validate_ThrowsArgumentNullForNullDefinition()
    {
        Assert.Throws<ArgumentNullException>(() => PostgreSqlSqlSafetyValidator.Validate(null!));
    }

    [Fact]
    public void SafetyException_CarriesAFixedMessageAndNoDetail()
    {
        PostgreSqlSqlSafetyException exception = Assert.Throws<PostgreSqlSqlSafetyException>(
            () => PostgreSqlSqlSafetyValidator.ValidateText("SELECT 1 DROP MARKERSQLSECRET"));

        Assert.Equal("The PostgreSQL statement failed SQL safety validation.", exception.Message);
        Assert.DoesNotContain("MARKERSQLSECRET", exception.ToString(), StringComparison.Ordinal);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    private static void AssertDefinitionRejected(
        string sql,
        IReadOnlyList<PostgreSqlSqlParameterDefinition> parameters,
        PostgreSqlSqlCommandKind kind = PostgreSqlSqlCommandKind.SelectVerification)
    {
        var definition = new PostgreSqlSqlStatementDefinition(
            PostgreSqlSqlStatementId.VerifySessionState, kind, sql, parameters, "test");

        Assert.Throws<PostgreSqlSqlSafetyException>(() => PostgreSqlSqlSafetyValidator.Validate(definition));
    }
}
