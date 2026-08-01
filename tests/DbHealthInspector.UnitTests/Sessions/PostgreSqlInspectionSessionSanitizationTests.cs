using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Sessions.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Sessions;

/// <summary>
/// Every stage-specific sanitization must discard the original failure completely. A synthetic
/// <see cref="PostgresException"/> is built with a <b>different</b> marker in each sensitive
/// field — message, SQLSTATE, detail, hint, schema, table, column, constraint, <c>Data</c> and an
/// inner exception — and none of them may survive anywhere on the resulting session exception.
/// </summary>
public sealed class PostgreSqlInspectionSessionSanitizationTests
{
    private const string MessageMarker = "marker-message-04b";
    private const string SqlStateMarker = "P0001";
    private const string DetailMarker = "marker-detail-04b";
    private const string HintMarker = "marker-hint-04b";
    private const string SchemaMarker = "marker-schema-04b";
    private const string TableMarker = "marker-table-04b";
    private const string ColumnMarker = "marker-column-04b";
    private const string ConstraintMarker = "marker-constraint-04b";
    private const string DataMarker = "marker-data-04b";
    private const string InnerMarker = "marker-inner-04b";
    private const string InternalQueryMarker = "marker-internalquery-04b";
    private const string WhereMarker = "marker-where-04b";
    private const string RoutineMarker = "marker-routine-04b";

    private static readonly string[] AllMarkers =
    [
        MessageMarker, DetailMarker, HintMarker, SchemaMarker, TableMarker, ColumnMarker,
        ConstraintMarker, DataMarker, InnerMarker, InternalQueryMarker, WhereMarker, RoutineMarker,
    ];

    /// <summary>
    /// A PostgreSQL failure carrying a distinct synthetic marker in every field the driver can
    /// populate.
    /// </summary>
    private static PostgresException LoadedPostgresException()
    {
        var exception = new PostgresException(
            messageText: MessageMarker,
            severity: "ERROR",
            invariantSeverity: "ERROR",
            sqlState: SqlStateMarker,
            detail: DetailMarker,
            hint: HintMarker,
            position: 0,
            internalPosition: 0,
            internalQuery: InternalQueryMarker,
            where: WhereMarker,
            schemaName: SchemaMarker,
            tableName: TableMarker,
            columnName: ColumnMarker,
            dataTypeName: "text",
            constraintName: ConstraintMarker,
            file: "marker-file.c",
            line: "1",
            routine: RoutineMarker);

        exception.Data["marker"] = DataMarker;
        return exception;
    }

    private static void AssertFullySanitized(PostgreSqlInspectionSessionException sanitized, PostgreSqlInspectionSessionFailureKind expectedKind)
    {
        Assert.Equal(expectedKind, sanitized.FailureKind);
        Assert.Equal(PostgreSqlInspectionSessionException.MessageFor(expectedKind), sanitized.Message);
        Assert.Null(sanitized.InnerException);
        Assert.Empty(sanitized.Data);

        string rendered = sanitized.ToString();
        string stackTrace = sanitized.StackTrace ?? string.Empty;

        foreach (string marker in AllMarkers)
        {
            Assert.DoesNotContain(marker, sanitized.Message, StringComparison.Ordinal);
            Assert.DoesNotContain(marker, rendered, StringComparison.Ordinal);
            Assert.DoesNotContain(marker, stackTrace, StringComparison.Ordinal);
        }

        // SQLSTATE must not survive either, in any casing.
        Assert.DoesNotContain(SqlStateMarker, rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(nameof(PostgreSqlSqlStatementId.SetTransactionReadOnly), nameof(PostgreSqlInspectionSessionFailureKind.InitializationFailed))]
    [InlineData(nameof(PostgreSqlSqlStatementId.ApplyLocalTimeouts), nameof(PostgreSqlInspectionSessionFailureKind.InitializationFailed))]
    [InlineData(nameof(PostgreSqlSqlStatementId.VerifySessionState), nameof(PostgreSqlInspectionSessionFailureKind.VerificationFailed))]
    public async Task InitializationAndVerificationFailures_AreFullySanitized(string idName, string kindName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        var expectedKind = Enum.Parse<PostgreSqlInspectionSessionFailureKind>(kindName);
        var scope = new FakeInspectionSessionScope(
            ScriptedStatementGateway.HealthySession().FailingAt(id, LoadedPostgresException()));

        PostgreSqlInspectionSessionException sanitized = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync(PostgreSqlInspectionSessionOptions.Default, (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken)
                .AsTask());

        AssertFullySanitized(sanitized, expectedKind);
    }

    [Fact]
    public async Task BeginTransactionFailure_IsFullySanitized()
    {
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.BeginTransaction, LoadedPostgresException());

        PostgreSqlInspectionSessionException sanitized = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync(PostgreSqlInspectionSessionOptions.Default, (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken)
                .AsTask());

        AssertFullySanitized(sanitized, PostgreSqlInspectionSessionFailureKind.InitializationFailed);
    }

    [Fact]
    public async Task OperationFailure_IsFullySanitized()
    {
        var scope = new FakeInspectionSessionScope();

        PostgreSqlInspectionSessionException sanitized = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync<int>(
                    PostgreSqlInspectionSessionOptions.Default,
                    (_, _) => throw LoadedPostgresException(),
                    TestContext.Current.CancellationToken)
                .AsTask());

        AssertFullySanitized(sanitized, PostgreSqlInspectionSessionFailureKind.ExecutionFailed);
    }

    [Fact]
    public async Task CleanupFailure_IsFullySanitized()
    {
        var scope = new FakeInspectionSessionScope()
            .FailingAt(SessionScopeStep.Rollback, LoadedPostgresException());

        PostgreSqlInspectionSessionException sanitized = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync(PostgreSqlInspectionSessionOptions.Default, (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken)
                .AsTask());

        AssertFullySanitized(sanitized, PostgreSqlInspectionSessionFailureKind.CleanupFailed);
    }

    [Fact]
    public async Task InnerExceptionMarker_NeverSurvives()
    {
        var inner = new InvalidOperationException(InnerMarker);
        var failure = new NpgsqlException(MessageMarker, inner);
        var scope = new FakeInspectionSessionScope(
            ScriptedStatementGateway.HealthySession().FailingAt(PostgreSqlSqlStatementId.SetTransactionReadOnly, failure));

        PostgreSqlInspectionSessionException sanitized = await Assert.ThrowsAsync<PostgreSqlInspectionSessionException>(
            () => new PostgreSqlInspectionSessionRunner(scope)
                .RunAsync(PostgreSqlInspectionSessionOptions.Default, (_, _) => ValueTask.FromResult(1), TestContext.Current.CancellationToken)
                .AsTask());

        AssertFullySanitized(sanitized, PostgreSqlInspectionSessionFailureKind.InitializationFailed);
    }

    [Fact]
    public void SessionState_NeverCarriesAServerMarker()
    {
        var state = new PostgreSqlInspectionSessionState(true, "repeatable read", true, true, true);

        Assert.Equal(typeof(PostgreSqlInspectionSessionState).ToString(), state.ToString());
        foreach (string marker in AllMarkers)
        {
            Assert.DoesNotContain(marker, state.ToString(), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Sanitize_DoesNotRetainTheOriginalExceptionAnywhere()
    {
        PostgresException original = LoadedPostgresException();

        PostgreSqlInspectionSessionException sanitized = PostgreSqlInspectionSessionRunner.Sanitize(
            original, PostgreSqlInspectionSessionFailureKind.ExecutionFailed, CancellationToken.None);

        AssertFullySanitized(sanitized, PostgreSqlInspectionSessionFailureKind.ExecutionFailed);

        // No field of the sanitized exception may reference the original instance.
        System.Reflection.FieldInfo[] fields = typeof(PostgreSqlInspectionSessionException)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(field => field.DeclaringType == typeof(PostgreSqlInspectionSessionException))
            .ToArray();

        Assert.DoesNotContain(fields, field => ReferenceEquals(field.GetValue(sanitized), original));
    }

    [Fact]
    public void SessionException_HasNoConstructorAcceptingAMessageOrInnerException()
    {
        // Sanitization is true by construction: there is no way, even inside this assembly, to
        // build one of these carrying caller-supplied text or a wrapped failure.
        System.Reflection.ConstructorInfo[] constructors = typeof(PostgreSqlInspectionSessionException)
            .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        System.Reflection.ConstructorInfo constructor = Assert.Single(constructors);
        System.Reflection.ParameterInfo parameter = Assert.Single(constructor.GetParameters());
        Assert.Equal(typeof(PostgreSqlInspectionSessionFailureKind), parameter.ParameterType);
    }

    [Fact]
    public void SessionException_DoesNotOverrideToString()
    {
        System.Reflection.MethodInfo? toString = typeof(PostgreSqlInspectionSessionException)
            .GetMethod(nameof(object.ToString), System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly);

        Assert.Null(toString);
    }

    [Theory]
    [InlineData(nameof(PostgreSqlInspectionSessionFailureKind.InitializationFailed), "The PostgreSQL inspection session could not be initialized.")]
    [InlineData(nameof(PostgreSqlInspectionSessionFailureKind.VerificationFailed), "The PostgreSQL inspection session could not be initialized.")]
    [InlineData(nameof(PostgreSqlInspectionSessionFailureKind.ExecutionFailed), "The PostgreSQL inspection operation failed.")]
    [InlineData(nameof(PostgreSqlInspectionSessionFailureKind.CleanupFailed), "The PostgreSQL inspection session could not be closed safely.")]
    public void FixedMessages_MatchTheFrozenContract(string kindName, string expected)
    {
        var kind = Enum.Parse<PostgreSqlInspectionSessionFailureKind>(kindName);

        Assert.Equal(expected, new PostgreSqlInspectionSessionException(kind).Message);
    }

    [Fact]
    public void VerificationFailed_ReusesTheInitializationMessageButKeepsItsOwnKind()
    {
        // Canonical contract (GC-DHI-04B-C1, F-07): the caller-visible text must not reveal that
        // the session reached the state-verification stage, while internal callers keep the
        // distinct kind.
        var verification = new PostgreSqlInspectionSessionException(PostgreSqlInspectionSessionFailureKind.VerificationFailed);
        var initialization = new PostgreSqlInspectionSessionException(PostgreSqlInspectionSessionFailureKind.InitializationFailed);

        Assert.Equal(initialization.Message, verification.Message);
        Assert.NotEqual(initialization.FailureKind, verification.FailureKind);
        Assert.DoesNotContain("verif", verification.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FailureKindEnum_DeclaresExactlyFourMembers()
    {
        Assert.Equal(4, Enum.GetValues<PostgreSqlInspectionSessionFailureKind>().Length);
    }
}
