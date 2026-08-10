using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;
using DbHealthInspector.UnitTests.Sql.TestSupport;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// D001's multirecord contract (GC-DHI-04D §11 and §20): exactly ten columns per row, only the
/// estimate nullable, zero rows valid, no partial collection on failure, and reader cleanup that
/// never masks the primary failure.
/// </summary>
public sealed class PostgreSqlSqlExecutorTableSnapshotTests
{
    private const int FieldCount = 10;

    private static PostgreSqlSqlExecutor Executor(FakeStatementGateway gateway) =>
        new(new PostgreSqlSqlInventory(), gateway);

    private static PostgreSqlSchemaFilter NoFilter => PostgreSqlSchemaFilter.IncludeEverything;

    /// <summary>One well-formed D001 row.</summary>
    private static object?[] Row(
        string schema = "public",
        string table = "orders",
        string relkind = "r",
        string persistence = "p",
        bool isPartition = false,
        long? estimate = 0L,
        long tableSize = 0L,
        long indexSize = 0L,
        long totalSize = 0L,
        bool hasPrimaryKey = false) =>
        [schema, table, relkind, persistence, isPartition, estimate, tableSize, indexSize, totalSize, hasPrimaryKey];

    private static ValueTask<PostgreSqlTableSnapshotQueryResult> RunAsync(
        FakeStatementGateway gateway, CancellationToken cancellationToken) =>
        Executor(gateway).ReadTableSnapshotsAsync(NoFilter, cancellationToken);

    // --- Row counts -------------------------------------------------------------------------------

    [Fact]
    public async Task ZeroRows_IsAValidResult()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount));

        PostgreSqlTableSnapshotQueryResult result = await RunAsync(gateway, TestContext.Current.CancellationToken);

        Assert.Empty(result.Tables);
    }

    [Fact]
    public async Task OneRow_IsProjectedAcrossAllTenOrdinals()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(
            FieldCount,
            Row("sales", "invoices", "r", "u", false, 1234L, 8192L, 16384L, 40960L, true)));

        PostgreSqlTableSnapshotQueryResult result = await RunAsync(gateway, TestContext.Current.CancellationToken);

        TableSnapshot table = Assert.Single(result.Tables);
        Assert.Equal("sales", table.SchemaName);
        Assert.Equal("invoices", table.TableName);
        Assert.Equal(RelationKind.OrdinaryTable, table.RelationKind);
        Assert.False(table.IsPartitionedRoot);
        Assert.False(table.IsPartition);
        Assert.Equal(1234L, table.EstimatedRowCount);
        Assert.Equal(8192L, table.TableSizeBytes);
        Assert.Equal(16384L, table.IndexSizeBytes);
        Assert.Equal(40960L, table.TotalSizeBytes);
        Assert.True(table.HasPrimaryKey);
    }

    [Fact]
    public async Task ManyRows_AreAllProjectedAndReordered()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(
            FieldCount,
            Row("public", "zebra"),
            Row("archive", "orders"),
            Row("public", "apple")));

        PostgreSqlTableSnapshotQueryResult result = await RunAsync(gateway, TestContext.Current.CancellationToken);

        Assert.Equal(
            [("archive", "orders"), ("public", "apple"), ("public", "zebra")],
            result.Tables.Select(table => (table.SchemaName, table.TableName)).ToArray());
    }

    // --- Field count and nullability ---------------------------------------------------------------

    [Theory]
    [InlineData(9)]
    [InlineData(11)]
    [InlineData(1)]
    public async Task AWrongFieldCount_IsRejected(int fieldCount)
    {
        object?[] row = [.. Enumerable.Range(0, fieldCount).Select(_ => (object?)"value")];
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(fieldCount, row));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task ANullInAnyRequiredColumn_IsRejected(int nullOrdinal)
    {
        object?[] row = Row();
        row[nullOrdinal] = null;
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount, row));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ANullEstimate_IsTheOnlyAcceptedNull()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(
            FieldCount, Row(estimate: null)));

        PostgreSqlTableSnapshotQueryResult result = await RunAsync(gateway, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Tables).EstimatedRowCount);
    }

    [Fact]
    public async Task ANullEstimateInOneRowDoesNotAffectAnother()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(
            FieldCount,
            Row("public", "known", estimate: 7L),
            Row("public", "unknown", estimate: null)));

        PostgreSqlTableSnapshotQueryResult result = await RunAsync(gateway, TestContext.Current.CancellationToken);

        Assert.Equal(7L, result.Tables[0].EstimatedRowCount);
        Assert.Null(result.Tables[1].EstimatedRowCount);
    }

    // --- Mapping failures abandon the whole read --------------------------------------------------------

    [Fact]
    public async Task ABadRowRejectsTheWholeResult_NotJustThatRow()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(
            FieldCount,
            Row("public", "good"),
            Row("public", "bad", relkind: "i"),
            Row("public", "alsogood")));

        await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ADuplicatePairRejectsTheWholeResult()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(
            FieldCount,
            Row("public", "orders"),
            Row("public", "orders")));

        await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ANegativeSizeRejectsTheWholeResult()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(
            FieldCount, Row(tableSize: -1L)));

        await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());
    }

    // --- Wrong CLR types are sanitized (R1-08, R1-20) -----------------------------------------------

    private const string MappingMessage = "The PostgreSQL table metadata row is invalid.";
    private const string LeakMessage = "Sensitive data was exposed.";

    /// <summary>
    /// A value whose CLR type matches no D001 column, so any ordinal it is placed in makes the
    /// reader's cast fail. Its <see cref="ToString"/> carries a marker, so the same value doubles
    /// as a leakage probe.
    /// </summary>
    private sealed class SensitiveValue
    {
        internal const string Marker = "sensitive-marker-c1-04d";

        public override string ToString() => Marker;
    }

    /// <summary>
    /// A plausible but wrong CLR type for <paramref name="ordinal"/>: a bigint where D001 promises
    /// text, and text where it promises boolean or bigint.
    /// </summary>
    private static object WrongTypedValueFor(int ordinal) => ordinal switch
    {
        0 or 1 or 2 or 3 => 42L,
        4 or 9 => "not-a-boolean",
        _ => "not-a-bigint",
    };

    private static object?[] RowWithWrongTypeAt(int ordinal, object? wrongValue = null)
    {
        object?[] row = Row();
        row[ordinal] = wrongValue ?? WrongTypedValueFor(ordinal);
        return row;
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public async Task AWrongClrTypeInAnyOrdinal_SurfacesAsTheFixedMappingError(int ordinal)
    {
        var reader = FakeRowReader.WithRows(FieldCount, RowWithWrongTypeAt(ordinal));
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        // Assert.ThrowsAsync is exact-type, so this alone proves no InvalidCastException escaped.
        PostgreSqlTableSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
                () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        // The reader was still released, and no partial collection escaped.
        Assert.True(reader.Disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(9)]
    public async Task AWrongClrType_NamesNeitherTheTypeNorTheValue(int ordinal)
    {
        var reader = FakeRowReader.WithRows(FieldCount, RowWithWrongTypeAt(ordinal, new SensitiveValue()));
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        PostgreSqlTableSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
                () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());

        foreach (string surface in new[]
                 {
                     exception.Message,
                     exception.ToString(),
                     exception.StackTrace ?? string.Empty,
                 })
        {
            Assert.False(surface.Contains(SensitiveValue.Marker, StringComparison.Ordinal), LeakMessage);
            Assert.False(surface.Contains(nameof(SensitiveValue), StringComparison.Ordinal), LeakMessage);
            Assert.False(surface.Contains("InvalidCast", StringComparison.Ordinal), LeakMessage);
        }

        Assert.Empty(exception.Data);
        Assert.Null(exception.InnerException);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AWrongClrTypeAtAnyRowPosition_ReturnsNoPartialCollection(int badRowIndex)
    {
        object?[][] rows =
        [
            Row("public", "first"),
            Row("public", "middle"),
            Row("public", "last"),
        ];
        rows[badRowIndex] = RowWithWrongTypeAt(6);

        var reader = FakeRowReader.WithRows(FieldCount, rows);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        PostgreSqlTableSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
                () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);

        // Nothing that mapped before the bad row survived: the method threw instead of returning.
        Assert.True(reader.Disposed);
        Assert.DoesNotContain("first", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWrongClrType_ReleasesBothTheReaderAndTheCommand()
    {
        // Composed over the real gateway so the command handle is the one the production code
        // releases: the executor disposes the reader, and the reader owns the command.
        var rows = FakeRowSource.WithRows(FieldCount, RowWithWrongTypeAt(6));
        FakeCommandHandle command = FakeCommandHandle.Succeeding(rows);
        var gateway = new NpgsqlStatementGateway(_ => command);
        var executor = new PostgreSqlSqlExecutor(new PostgreSqlSqlInventory(), gateway);

        await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
            () => executor.ReadTableSnapshotsAsync(NoFilter, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(1, command.DisposeCount);
        Assert.Equal(1, rows.DisposeCount);
    }

    [Fact]
    public async Task AWrongClrTypeAndAGoodRow_AreNotDistinguishableFromAnyOtherMappingRejection()
    {
        string[] messages =
        [
            (await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
                () => RunAsync(
                    FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount, RowWithWrongTypeAt(2))),
                    TestContext.Current.CancellationToken).AsTask())).Message,
            (await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
                () => RunAsync(
                    FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount, Row(relkind: "i"))),
                    TestContext.Current.CancellationToken).AsTask())).Message,
        ];

        Assert.All(messages, message => Assert.Equal(MappingMessage, message));
    }

    [Fact]
    public async Task ACancellationIsNeverReclassifiedAsAWrongTypeMappingError()
    {
        // The narrow catch must not absorb anything that is not a type mismatch.
        var reader = FakeRowReader.WithRows(FieldCount, Row());
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        using var cts = new CancellationTokenSource();
        reader.BeforeRead = _ =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadTableSnapshotsAsync(NoFilter, cts.Token).AsTask());
    }

    // --- Cleanup ------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheReaderIsDisposedOnSuccess()
    {
        var reader = FakeRowReader.WithRows(FieldCount, Row());
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await RunAsync(gateway, TestContext.Current.CancellationToken);

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task TheReaderIsDisposedOnAShapeFailure()
    {
        var reader = FakeRowReader.WithRows(3, ["a", "b", "c"]);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task TheReaderIsDisposedOnAMappingFailure()
    {
        var reader = FakeRowReader.WithRows(FieldCount, Row(relkind: "i"));
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task APrimaryFailureWinsOverADisposalFailure()
    {
        var reader = FakeRowReader.WithRows(FieldCount, Row(relkind: "i"));
        reader.DisposeFailure = new InvalidOperationException("disposal");
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task ADisposalFailureAloneStillPropagates()
    {
        var reader = FakeRowReader.WithRows(FieldCount, Row());
        reader.DisposeFailure = new InvalidOperationException("disposal");
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());
    }

    // --- Cancellation --------------------------------------------------------------------------------------

    [Fact]
    public async Task APrecancelledToken_PreventsD001Entirely()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount, Row()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => RunAsync(gateway, cts.Token).AsTask());

        Assert.Equal(0, gateway.ReaderCallCount);
    }

    [Fact]
    public async Task TheExactTokenReachesTheGatewayAndEveryRead()
    {
        var reader = FakeRowReader.WithRows(FieldCount, Row("a", "one"), Row("b", "two"));
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);
        using var cts = new CancellationTokenSource();

        await Executor(gateway).ReadTableSnapshotsAsync(NoFilter, cts.Token);

        Assert.All(gateway.Tokens, token => Assert.Equal(cts.Token, token));
        Assert.All(reader.ReadTokens, token => Assert.Equal(cts.Token, token));
        Assert.NotEmpty(reader.ReadTokens);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationAtAnyRowBoundary_ReturnsNoPartialCollection(int cancelBeforeRow)
    {
        var reader = FakeRowReader.WithRows(
            FieldCount, Row("a", "one"), Row("b", "two"), Row("c", "three"));
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        using var cts = new CancellationTokenSource();
        reader.BeforeRead = index =>
        {
            if (index == cancelBeforeRow)
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadTableSnapshotsAsync(NoFilter, cts.Token).AsTask());

        // No partial result escaped, and the reader was still released.
        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task CancellationDuringTheFinalRead_StillReleasesTheReader()
    {
        var reader = FakeRowReader.WithRows(FieldCount, Row("a", "one"));
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        using var cts = new CancellationTokenSource();
        reader.BeforeRead = index =>
        {
            // Index 1 is the read that would report end-of-rows.
            if (index == 1)
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadTableSnapshotsAsync(NoFilter, cts.Token).AsTask());

        Assert.True(reader.Disposed);
    }

    // --- Cancellation: command execution and reader disposal (R1-19) --------------------------------

    [Fact]
    public async Task CancellationWhileTheCommandExecutes_AcquiresNoReaderAndReturnsNothing()
    {
        var reader = FakeRowReader.WithRows(FieldCount, Row());
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        using var cts = new CancellationTokenSource();
        gateway.BeforeExecuteReader = () =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        };

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadTableSnapshotsAsync(NoFilter, cts.Token).AsTask());

        // The requested token is the one that surfaces.
        Assert.Equal(cts.Token, exception.CancellationToken);

        // D001 was reached, but no reader was ever handed out, so there was nothing to dispose
        // and no collection — partial or otherwise — to return.
        Assert.Equal(1, gateway.ReaderCallCount);
        Assert.Null(gateway.LastReader);
        Assert.False(reader.Disposed);
    }

    [Fact]
    public async Task CancellationDuringReaderDisposal_WithNoPrimary_SurfacesTheCancellation()
    {
        // Cleanup-only: every row mapped, so the disposal outcome is the only failure there is.
        using var cts = new CancellationTokenSource();
        var reader = FakeRowReader.WithRows(FieldCount, Row());
        reader.DisposeFailure = new OperationCanceledException(cts.Token);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task CancellationDuringReaderDisposal_NeverDisplacesAnExistingPrimary()
    {
        // A primary already exists, so the disposal cancellation must not replace it — and must
        // not be rewritten into a mapping error either.
        using var cts = new CancellationTokenSource();
        var reader = FakeRowReader.WithRows(FieldCount, Row(relkind: "i"));
        reader.DisposeFailure = new OperationCanceledException(cts.Token);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        PostgreSqlTableSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
                () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);
        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task AWrongTypePrimary_SurvivesADisposalCancellation()
    {
        using var cts = new CancellationTokenSource();
        var reader = FakeRowReader.WithRows(FieldCount, RowWithWrongTypeAt(7));
        reader.DisposeFailure = new OperationCanceledException(cts.Token);
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(reader);

        PostgreSqlTableSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlTableSnapshotMappingException>(
                () => RunAsync(gateway, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.True(reader.Disposed);
    }

    // --- Parameter binding ------------------------------------------------------------------------------------

    [Fact]
    public async Task TheFilterIsBoundAsExactlyTwoTextArrays()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount));
        var filter = new PostgreSqlSchemaFilter(["sales", "public"], ["staging"]);

        await Executor(gateway).ReadTableSnapshotsAsync(filter, TestContext.Current.CancellationToken);

        PostgreSqlPreparedStatement statement = Assert.Single(gateway.Executed);
        Assert.Equal(PostgreSqlSqlStatementId.ReadTableSnapshots, statement.Id);
        Assert.Equal(2, statement.Parameters.Count);

        Assert.Equal(1, statement.Parameters[0].Position);
        Assert.Equal(PostgreSqlSqlParameterType.TextArray, statement.Parameters[0].Type);
        Assert.Equal(["public", "sales"], statement.Parameters[0].TextArrayValue);

        Assert.Equal(2, statement.Parameters[1].Position);
        Assert.Equal(PostgreSqlSqlParameterType.TextArray, statement.Parameters[1].Type);
        Assert.Equal(["staging"], statement.Parameters[1].TextArrayValue);
    }

    [Fact]
    public async Task AnEmptyFilterStillBindsTwoEmptyArrays()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount));

        await Executor(gateway).ReadTableSnapshotsAsync(NoFilter, TestContext.Current.CancellationToken);

        PostgreSqlPreparedStatement statement = Assert.Single(gateway.Executed);
        Assert.Equal(2, statement.Parameters.Count);
        Assert.Empty(statement.Parameters[0].TextArrayValue);
        Assert.Empty(statement.Parameters[1].TextArrayValue);
    }

    [Fact]
    public async Task TheCommandTextIsAlwaysTheInventoryText()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount));

        await Executor(gateway).ReadTableSnapshotsAsync(
            new PostgreSqlSchemaFilter(["public"], []), TestContext.Current.CancellationToken);

        PostgreSqlPreparedStatement statement = Assert.Single(gateway.Executed);

        // No schema name was spliced into the SQL: the text is byte-identical to the inventory's.
        Assert.Equal(PostgreSqlSqlInventory.ReadTableSnapshotsSql, statement.CommandText);
        Assert.DoesNotContain("public", statement.CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANullFilter_IsRejectedBeforeAnythingRuns()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Executor(gateway).ReadTableSnapshotsAsync(null!, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, gateway.ReaderCallCount);
    }

    [Fact]
    public async Task D001IsTheOnlyStatementExecuted()
    {
        FakeStatementGateway gateway = FakeStatementGateway.Succeeding(FakeRowReader.WithRows(FieldCount));

        await Executor(gateway).ReadTableSnapshotsAsync(NoFilter, TestContext.Current.CancellationToken);

        Assert.Equal(0, gateway.NonQueryCallCount);
        Assert.Equal(1, gateway.ReaderCallCount);
    }
}
