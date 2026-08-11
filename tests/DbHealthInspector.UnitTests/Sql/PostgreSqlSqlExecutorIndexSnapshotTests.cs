using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Indexes;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;
using DbHealthInspector.UnitTests.Sql.TestSupport;

namespace DbHealthInspector.UnitTests.Sql;

/// <summary>
/// The E001/E002 multirecord contract (GC-DHI-04E §11, §12, §20, §27, §28): exactly thirty-one
/// columns per E001 row and four per E002 row, grouping by index identity, statistics merged only
/// when the capability allows it, no partial collection on failure, and cleanup that never masks
/// the primary failure.
/// </summary>
public sealed class PostgreSqlSqlExecutorIndexSnapshotTests
{
    private const int IndexFieldCount = 31;
    private const int StatisticsFieldCount = 4;
    private const string MappingMessage = "The PostgreSQL index metadata row is invalid.";
    private const string StatisticsMessage = "The PostgreSQL index usage statistics row is invalid.";
    private const string LeakMessage = "Sensitive data was exposed.";

    private static PostgreSqlSqlExecutor Executor(FakeStatementGateway gateway) =>
        new(new PostgreSqlSqlInventory(), gateway);

    private static PostgreSqlSchemaFilter NoFilter => PostgreSqlSchemaFilter.IncludeEverything;

    /// <summary>One well-formed E001 key-attribute row, in the frozen thirty-one column order.</summary>
    private static object?[] IndexRow(
        string schema = "public",
        string table = "orders",
        string index = "orders_a_idx",
        string accessMethod = "btree",
        string relationKind = "i",
        bool isIndexPartition = false,
        int attributeCount = 1,
        int keyAttributeCount = 1,
        int position = 1,
        bool isKey = true,
        string? columnName = "a",
        string? expression = null,
        string? collationSchema = null,
        string? collationName = null,
        string? opclassSchema = "pg_catalog",
        string? opclassName = "text_ops",
        string?[]? opclassOptions = null,
        bool? orderable = true,
        bool? ascending = true,
        bool? descending = false,
        bool? nullsFirst = false,
        bool? nullsLast = true,
        string? predicate = null,
        bool isUnique = false,
        bool? nullsNotDistinct = null,
        bool isPrimaryKey = false,
        bool backsConstraint = false,
        bool isValid = true,
        bool isReady = true,
        bool isLive = true,
        long sizeBytes = 8192) =>
        [
            schema, table, index, accessMethod, relationKind, isIndexPartition,
            attributeCount, keyAttributeCount, position, isKey, columnName, expression,
            collationSchema, collationName, opclassSchema, opclassName, opclassOptions,
            orderable, ascending, descending, nullsFirst, nullsLast, predicate,
            isUnique, nullsNotDistinct, isPrimaryKey, backsConstraint, isValid, isReady, isLive,
            sizeBytes,
        ];

    private static object?[] StatisticsRow(
        string schema = "public",
        string table = "orders",
        string index = "orders_a_idx",
        long scanCount = 7L) =>
        [schema, table, index, scanCount];

    private static ValueTask<PostgreSqlIndexSnapshotQueryResult> RunAsync(
        FakeStatementGateway gateway,
        bool usageStatisticsAvailable,
        CancellationToken cancellationToken) =>
        Executor(gateway).ReadIndexSnapshotsAsync(NoFilter, usageStatisticsAvailable, cancellationToken);

    private static FakeStatementGateway Gateway(FakeRowReader indexes, FakeRowReader? statistics = null) =>
        statistics is null
            ? FakeStatementGateway.Succeeding(indexes)
            : FakeStatementGateway.Succeeding(indexes, statistics);

    // --- Statement selection and parameters -----------------------------------------------------

    [Fact]
    public async Task WithoutStatistics_OnlyE001Executes()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount));

        await RunAsync(gateway, usageStatisticsAvailable: false, TestContext.Current.CancellationToken);

        PostgreSqlPreparedStatement executed = Assert.Single(gateway.Executed);
        Assert.Equal(PostgreSqlSqlStatementId.ReadIndexMetadata, executed.Id);
        Assert.Equal(1, gateway.ReaderCallCount);
        Assert.Equal(0, gateway.NonQueryCallCount);
    }

    [Fact]
    public async Task WithStatistics_E002ExecutesExactlyOnceAfterE001()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount),
            FakeRowReader.WithRows(StatisticsFieldCount));

        await RunAsync(gateway, usageStatisticsAvailable: true, TestContext.Current.CancellationToken);

        Assert.Equal(
            [PostgreSqlSqlStatementId.ReadIndexMetadata, PostgreSqlSqlStatementId.ReadIndexUsageStatistics],
            gateway.Executed.Select(statement => statement.Id).ToArray());
        Assert.Equal(2, gateway.ReaderCallCount);
    }

    [Fact]
    public async Task BothStatements_BindTheSameTwoTextArrays()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount),
            FakeRowReader.WithRows(StatisticsFieldCount));
        var filter = new PostgreSqlSchemaFilter(["sales", "public"], ["staging"]);

        await Executor(gateway).ReadIndexSnapshotsAsync(filter, true, TestContext.Current.CancellationToken);

        foreach (PostgreSqlPreparedStatement statement in gateway.Executed)
        {
            Assert.Equal(2, statement.Parameters.Count);
            Assert.Equal(PostgreSqlSqlParameterType.TextArray, statement.Parameters[0].Type);
            Assert.Equal(["public", "sales"], statement.Parameters[0].TextArrayValue);
            Assert.Equal(["staging"], statement.Parameters[1].TextArrayValue);
        }
    }

    [Fact]
    public async Task TheCommandTextIsAlwaysTheInventoryText()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount),
            FakeRowReader.WithRows(StatisticsFieldCount));

        await Executor(gateway).ReadIndexSnapshotsAsync(
            new PostgreSqlSchemaFilter(["public"], []), true, TestContext.Current.CancellationToken);

        Assert.Equal(PostgreSqlSqlInventory.ReadIndexMetadataSql, gateway.Executed[0].CommandText);
        Assert.Equal(PostgreSqlSqlInventory.ReadIndexUsageStatisticsSql, gateway.Executed[1].CommandText);
        Assert.DoesNotContain("public", gateway.Executed[0].CommandText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANullFilter_IsRejectedBeforeAnythingRuns()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount));

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => Executor(gateway).ReadIndexSnapshotsAsync(null!, false, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(0, gateway.ReaderCallCount);
    }

    // --- Grouping and EOF -----------------------------------------------------------------------

    [Fact]
    public async Task ZeroRows_IsAValidEmptyResult()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, false, TestContext.Current.CancellationToken);

        Assert.Empty(result.Indexes);
    }

    [Fact]
    public async Task OneIndexOfSeveralAttributes_BecomesOneSnapshot()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(attributeCount: 3, keyAttributeCount: 2, position: 1, columnName: "a"),
            IndexRow(attributeCount: 3, keyAttributeCount: 2, position: 2, columnName: "b"),
            IndexRow(attributeCount: 3, keyAttributeCount: 2, position: 3, isKey: false, columnName: "c",
                opclassSchema: null, opclassName: null, orderable: null, ascending: null,
                descending: null, nullsFirst: null, nullsLast: null)));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, false, TestContext.Current.CancellationToken);

        IndexSnapshot snapshot = Assert.Single(result.Indexes);
        Assert.Equal(2, snapshot.KeyParts.Count);
        Assert.Equal(["c"], snapshot.IncludedColumns.ToArray());
    }

    [Fact]
    public async Task SeveralIndexes_AreGroupedAtEachIdentityTransition()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(index: "i_one"),
            IndexRow(index: "i_two", attributeCount: 2, keyAttributeCount: 2, position: 1, columnName: "a"),
            IndexRow(index: "i_two", attributeCount: 2, keyAttributeCount: 2, position: 2, columnName: "b"),
            IndexRow(index: "i_three")));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, false, TestContext.Current.CancellationToken);

        Assert.Equal(
            ["i_one", "i_three", "i_two"],
            result.Indexes.Select(index => index.IndexName).ToArray());
        Assert.Equal(2, result.Indexes.Single(index => index.IndexName == "i_two").KeyParts.Count);
    }

    [Fact]
    public async Task TheFinalGroupIsClosedAtEndOfRows()
    {
        // The last index has no following identity change; EOF must still finalise it.
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(index: "i_one"),
            IndexRow(index: "i_last", attributeCount: 2, keyAttributeCount: 2, position: 1, columnName: "a"),
            IndexRow(index: "i_last", attributeCount: 2, keyAttributeCount: 2, position: 2, columnName: "b")));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, false, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Indexes.Count);
        Assert.Equal(2, result.Indexes.Single(index => index.IndexName == "i_last").KeyParts.Count);
    }

    [Fact]
    public async Task AMalformedFinalGroupAtEndOfRows_FailsTheWholeOperation()
    {
        // Claims two attributes but only one row arrives before EOF: never silently truncated.
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(index: "i_good"),
            IndexRow(index: "i_truncated", attributeCount: 2, keyAttributeCount: 2, position: 1)));

        await Assert.ThrowsAsync<PostgreSqlIndexSnapshotMappingException>(
            () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ANonContiguousIndexGroup_IsRejected()
    {
        // E001 orders by identity, so a repeat means the grouping assumption itself is broken.
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(index: "i_a"),
            IndexRow(index: "i_b"),
            IndexRow(index: "i_a")));

        await Assert.ThrowsAsync<PostgreSqlIndexSnapshotMappingException>(
            () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task IndexesInDifferentSchemas_AreDistinctGroups()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(schema: "one", index: "same"),
            IndexRow(schema: "two", index: "same")));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, false, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Indexes.Count);
    }

    // --- Shape --------------------------------------------------------------------------------

    [Theory]
    [InlineData(30)]
    [InlineData(32)]
    [InlineData(1)]
    public async Task AWrongE001FieldCount_IsRejected(int fieldCount)
    {
        object?[] row = [.. Enumerable.Range(0, fieldCount).Select(_ => (object?)"value")];
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(fieldCount, row));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());
    }

    /// <summary>
    /// A plausible but wrong CLR type for each E001 ordinal: text where a number or boolean is
    /// promised and vice versa, and a non-array where <c>text[]</c> is promised.
    /// </summary>
    private static object WrongTypedValueFor(int ordinal) => ordinal switch
    {
        0 or 1 or 2 or 3 or 4 => 42L,
        5 or 9 or 17 or 18 or 19 or 20 or 21 or 23 or 24 or 25 or 26 or 27 or 28 or 29 => "not-a-boolean",
        6 or 7 or 8 => "not-an-int32",
        10 or 11 or 12 or 13 or 14 or 15 or 22 => 42L,
        16 => "not-a-string-array",
        _ => "not-a-bigint",
    };

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
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(16)]
    [InlineData(17)]
    [InlineData(18)]
    [InlineData(19)]
    [InlineData(20)]
    [InlineData(21)]
    [InlineData(22)]
    [InlineData(23)]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    public async Task AWrongClrTypeInAnyE001Ordinal_SurfacesAsTheFixedMappingError(int ordinal)
    {
        object?[] row = IndexRow(opclassOptions: ["opt"]);
        row[ordinal] = WrongTypedValueFor(ordinal);

        var reader = FakeRowReader.WithRows(IndexFieldCount, row);
        FakeStatementGateway gateway = Gateway(reader);

        // Exact-type assertion: no InvalidCastException may escape.
        PostgreSqlIndexSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexSnapshotMappingException>(
                () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.True(reader.Disposed);
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
    [InlineData(23)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(28)]
    [InlineData(29)]
    [InlineData(30)]
    public async Task ANullInAnyRequiredE001Ordinal_IsRejected(int ordinal)
    {
        object?[] row = IndexRow();
        row[ordinal] = null;

        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount, row));

        Exception failure = await Assert.ThrowsAnyAsync<Exception>(
            () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());

        // A required NULL is a bad row either way; what matters is that it never maps.
        Assert.True(
            failure is PostgreSqlIndexSnapshotMappingException or PostgreSqlSqlResultShapeException,
            "A required null produced an unexpected failure type.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task AWrongClrTypeAtAnyRowPosition_ReturnsNoPartialCollection(int badRowIndex)
    {
        object?[][] rows =
        [
            IndexRow(index: "i_first"),
            IndexRow(index: "i_middle"),
            IndexRow(index: "i_last"),
        ];
        rows[badRowIndex][30] = "not-a-bigint";

        var reader = FakeRowReader.WithRows(IndexFieldCount, rows);
        FakeStatementGateway gateway = Gateway(reader);

        PostgreSqlIndexSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexSnapshotMappingException>(
                () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);
        Assert.True(reader.Disposed);
        Assert.DoesNotContain("i_first", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStringArrayColumn_IsReadThroughTheTypedSeam()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount, IndexRow(opclassOptions: ["values_per_range=32"])));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, false, TestContext.Current.CancellationToken);

        Assert.Equal(
            "\"pg_catalog\".\"text_ops\"|options[1;19:values_per_range=32]",
            Assert.Single(Assert.Single(result.Indexes).KeyParts).OperatorClass);
    }

    [Fact]
    public async Task ANullElementInAStringArray_IsRejected()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount, IndexRow(opclassOptions: ["ok", null])));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());
    }

    // --- E002 shape and merge -------------------------------------------------------------------

    [Fact]
    public async Task AMatchingStatisticsRow_SuppliesTheExactScanCount()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow()),
            FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(scanCount: 1234L)));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, true, TestContext.Current.CancellationToken);

        Assert.Equal(1234L, Assert.Single(result.Indexes).ScanCount);
    }

    [Fact]
    public async Task AZeroScanCount_IsARealValueNotUnknown()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow()),
            FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(scanCount: 0L)));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, true, TestContext.Current.CancellationToken);

        IndexSnapshot snapshot = Assert.Single(result.Indexes);
        Assert.NotNull(snapshot.ScanCount);
        Assert.Equal(0L, snapshot.ScanCount);
    }

    [Fact]
    public async Task WhenStatisticsAreUnavailable_EveryScanCountIsNullAndNeverZero()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount, IndexRow()));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, usageStatisticsAvailable: false, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Indexes).ScanCount);
        Assert.Equal(1, gateway.ReaderCallCount);
    }

    [Fact]
    public async Task APhysicalIndexWithNoStatisticsRow_ReportsNullNotZero()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow(index: "i_unmeasured")),
            FakeRowReader.WithRows(StatisticsFieldCount));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, true, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Indexes).ScanCount);
    }

    [Fact]
    public async Task AVirtualIndex_ReportsNullEvenWhenStatisticsAreAvailable()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow(index: "i_root", relationKind: "I", sizeBytes: 0L)),
            FakeRowReader.WithRows(StatisticsFieldCount));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, true, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(result.Indexes).ScanCount);
    }

    [Fact]
    public async Task AStatisticsRowMatchingAVirtualIndex_IsRejected()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow(index: "i_root", relationKind: "I", sizeBytes: 0L)),
            FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(index: "i_root")));

        await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task AStatisticsRowWithNoMatchingIndex_IsRejected()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow(index: "i_known")),
            FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(index: "i_unknown")));

        await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ADuplicateStatisticsIdentity_IsRejected()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow()),
            FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(scanCount: 1L), StatisticsRow(scanCount: 2L)));

        // No last-write-wins.
        await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());
    }

    // --- R1-01: global final-index duplicate identity -------------------------------------------

    [Fact]
    public async Task AnAdjacentDuplicateFinalIndexIdentity_IsRejected()
    {
        // Same schema and index name in consecutive groups, differing only by table.
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(schema: "a", table: "t1", index: "shared"),
            IndexRow(schema: "a", table: "t2", index: "shared")));

        PostgreSqlIndexSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexSnapshotMappingException>(
                () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
    }

    [Fact]
    public async Task AnInterleavedDuplicateFinalIndexIdentity_IsRejected()
    {
        // The escape R1-01 named: the two colliding groups are not neighbours, so a check that
        // only compares a group with its predecessor lets this through. Every raw identity here is
        // distinct -- (a,t1,shared), (a,t2,other), (a,t3,shared) -- yet the first and last produce
        // the same final (schema, index) identity.
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(schema: "a", table: "t1", index: "shared"),
            IndexRow(schema: "a", table: "t2", index: "other"),
            IndexRow(schema: "a", table: "t3", index: "shared")));

        PostgreSqlIndexSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexSnapshotMappingException>(
                () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        foreach (string surface in new[] { exception.Message, exception.ToString() })
        {
            foreach (string marker in new[] { "shared", "other", "t1", "t3" })
            {
                bool leaked = surface.Contains(marker, StringComparison.Ordinal);
                Assert.False(leaked, LeakMessage);
            }
        }
    }

    [Fact]
    public async Task TheSameIndexNameInDifferentSchemas_IsAllowed()
    {
        // An index name is unique within its schema, so this is a legitimate catalog.
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(schema: "s1", table: "t", index: "same"),
            IndexRow(schema: "s2", table: "t", index: "same")));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, false, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Indexes.Count);
    }

    [Theory]
    [InlineData("a", "A", "idx", "idx")]
    [InlineData("a", "a", "idx", "IDX")]
    public async Task FinalIdentityComparisonIsOrdinalAndCaseSensitive(
        string firstSchema, string secondSchema, string firstIndex, string secondIndex)
    {
        // Case-different schema or index names are distinct identities, never folded together.
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(schema: firstSchema, table: "t1", index: firstIndex),
            IndexRow(schema: secondSchema, table: "t2", index: secondIndex)));

        PostgreSqlIndexSnapshotQueryResult result =
            await RunAsync(gateway, false, TestContext.Current.CancellationToken);

        Assert.Equal(2, result.Indexes.Count);
    }

    [Fact]
    public async Task AnInterleavedDuplicateFinalIdentity_OutranksAnE001DisposalFailure()
    {
        // Proves the global duplicate check runs while the E001 reader is still open: if it ran
        // after the read, the disposal failure would have replaced it.
        var reader = FakeRowReader.WithRows(
            IndexFieldCount,
            IndexRow(schema: "a", table: "t1", index: "shared"),
            IndexRow(schema: "a", table: "t2", index: "other"),
            IndexRow(schema: "a", table: "t3", index: "shared"));
        reader.DisposeFailure = new InvalidOperationException("disposal");
        FakeStatementGateway gateway = Gateway(reader);

        PostgreSqlIndexSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexSnapshotMappingException>(
                () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);
        Assert.True(reader.Disposed);
    }

    // --- R1-02: E002 reconciliation precedes reader disposal ------------------------------------

    [Fact]
    public async Task AnUnmatchedStatisticsRow_OutranksAnE002DisposalFailure()
    {
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(index: "i_unknown"));
        statistics.DisposeFailure = new InvalidOperationException("disposal");

        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow(index: "i_known")),
            statistics);

        // The contradiction is detected while the E002 reader is open, so it stays primary.
        PostgreSqlIndexUsageStatisticsMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
                () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(StatisticsMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.True(statistics.Disposed);
    }

    [Fact]
    public async Task AStatisticsRowMatchingAVirtualIndex_OutranksAnE002DisposalFailure()
    {
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(index: "i_root"));
        statistics.DisposeFailure = new InvalidOperationException("disposal");

        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow(index: "i_root", relationKind: "I", sizeBytes: 0L)),
            statistics);

        PostgreSqlIndexUsageStatisticsMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
                () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(StatisticsMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.True(statistics.Disposed);
    }

    [Fact]
    public async Task ADuplicateStatisticsIdentity_OutranksAnE002DisposalFailure()
    {
        var statistics = FakeRowReader.WithRows(
            StatisticsFieldCount, StatisticsRow(scanCount: 1L), StatisticsRow(scanCount: 2L));
        statistics.DisposeFailure = new InvalidOperationException("disposal");

        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow()),
            statistics);

        PostgreSqlIndexUsageStatisticsMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
                () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(StatisticsMessage, exception.Message);
        Assert.True(statistics.Disposed);
    }

    [Fact]
    public async Task ANegativeScanCount_OutranksAnE002DisposalFailure()
    {
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(scanCount: -1L));
        statistics.DisposeFailure = new InvalidOperationException("disposal");

        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow()),
            statistics);

        PostgreSqlIndexUsageStatisticsMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
                () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(StatisticsMessage, exception.Message);
        Assert.True(statistics.Disposed);
    }

    [Fact]
    public async Task AnE002DisposalFailureAloneStillPropagates()
    {
        // The converse of the four tests above: with no primary, the cleanup failure must remain
        // observable rather than being swallowed.
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow());
        statistics.DisposeFailure = new InvalidOperationException("disposal");

        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow()),
            statistics);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ANegativeScanCount_IsRejected()
    {
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow()),
            FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(scanCount: -1L)));

        await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    public async Task AWrongE002FieldCount_IsRejected(int fieldCount)
    {
        object?[] row = [.. Enumerable.Range(0, fieldCount).Select(_ => (object?)"value")];
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow()),
            FakeRowReader.WithRows(fieldCount, row));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task AWrongClrTypeInAnyE002Ordinal_SurfacesAsTheFixedStatisticsError(int ordinal)
    {
        object?[] row = StatisticsRow();
        row[ordinal] = ordinal == 3 ? "not-a-bigint" : 42L;

        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, row);
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount, IndexRow()), statistics);

        PostgreSqlIndexUsageStatisticsMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
                () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(StatisticsMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);
        Assert.True(statistics.Disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task ANullInAnyE002Ordinal_IsRejected(int ordinal)
    {
        object?[] row = StatisticsRow();
        row[ordinal] = null;

        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount, IndexRow()), FakeRowReader.WithRows(StatisticsFieldCount, row));

        await Assert.ThrowsAsync<PostgreSqlSqlResultShapeException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ABadStatisticsRowAtAnyPosition_FailsTheWholeOperation(int badRow)
    {
        object?[][] rows =
        [
            StatisticsRow(index: "i_a"),
            StatisticsRow(index: "i_b"),
            StatisticsRow(index: "i_c"),
        ];
        rows[badRow][3] = -5L;

        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(
                IndexFieldCount, IndexRow(index: "i_a"), IndexRow(index: "i_b"), IndexRow(index: "i_c")),
            FakeRowReader.WithRows(StatisticsFieldCount, rows));

        await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());
    }

    // --- Cancellation --------------------------------------------------------------------------

    [Fact]
    public async Task APrecancelledToken_PreventsBothStatementsEntirely()
    {
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount, IndexRow()));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => RunAsync(gateway, true, cts.Token).AsTask());

        Assert.Equal(0, gateway.ReaderCallCount);
    }

    [Fact]
    public async Task TheExactTokenReachesBothStatementsAndEveryRead()
    {
        var indexes = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow());
        FakeStatementGateway gateway = Gateway(indexes, statistics);
        using var cts = new CancellationTokenSource();

        await Executor(gateway).ReadIndexSnapshotsAsync(NoFilter, true, cts.Token);

        Assert.All(gateway.Tokens, token => Assert.Equal(cts.Token, token));
        Assert.All(indexes.ReadTokens, token => Assert.Equal(cts.Token, token));
        Assert.All(statistics.ReadTokens, token => Assert.Equal(cts.Token, token));
    }

    [Fact]
    public async Task CancellationWhileE001Executes_AcquiresNoReader()
    {
        var reader = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        FakeStatementGateway gateway = Gateway(reader);
        using var cts = new CancellationTokenSource();
        gateway.BeforeExecuteReader = () =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        };

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadIndexSnapshotsAsync(NoFilter, false, cts.Token).AsTask());

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.Null(gateway.LastReader);
        Assert.False(reader.Disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationAtAnyE001RowBoundary_ReturnsNoPartialCollection(int cancelBeforeRow)
    {
        var reader = FakeRowReader.WithRows(
            IndexFieldCount, IndexRow(index: "i_a"), IndexRow(index: "i_b"), IndexRow(index: "i_c"));
        FakeStatementGateway gateway = Gateway(reader);

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
            () => Executor(gateway).ReadIndexSnapshotsAsync(NoFilter, false, cts.Token).AsTask());

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task CancellationAtTheE001EndOfRowsRead_StillReleasesTheReader()
    {
        var reader = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        FakeStatementGateway gateway = Gateway(reader);

        using var cts = new CancellationTokenSource();
        reader.BeforeRead = index =>
        {
            if (index == 1)
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadIndexSnapshotsAsync(NoFilter, false, cts.Token).AsTask());

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task CancellationDuringE001ReaderDisposal_Surfaces()
    {
        using var cts = new CancellationTokenSource();
        var reader = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        reader.DisposeFailure = new OperationCanceledException(cts.Token);
        FakeStatementGateway gateway = Gateway(reader);

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task CancellationBetweenE001AndE002_RunsNoSecondStatement()
    {
        var indexes = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow());
        FakeStatementGateway gateway = Gateway(indexes, statistics);

        using var cts = new CancellationTokenSource();
        // Cancel as the last E001 read reports end-of-rows: E001 has finished, E002 has not begun.
        indexes.BeforeRead = index =>
        {
            if (index == 1)
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadIndexSnapshotsAsync(NoFilter, true, cts.Token).AsTask());

        Assert.Equal(1, gateway.ReaderCallCount);
        Assert.False(statistics.Disposed);
    }

    [Fact]
    public async Task CancellationWhileE002Executes_AcquiresNoSecondReader()
    {
        var indexes = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow());
        FakeStatementGateway gateway = Gateway(indexes, statistics);

        using var cts = new CancellationTokenSource();
        gateway.BeforeExecuteReader = () =>
        {
            if (gateway.ReaderCallCount == 2)
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadIndexSnapshotsAsync(NoFilter, true, cts.Token).AsTask());

        Assert.Equal(2, gateway.ReaderCallCount);
        Assert.True(indexes.Disposed);
        Assert.False(statistics.Disposed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CancellationAtAnyE002RowBoundary_ReturnsNoPartialCollection(int cancelBeforeRow)
    {
        var statistics = FakeRowReader.WithRows(
            StatisticsFieldCount,
            StatisticsRow(index: "i_a"), StatisticsRow(index: "i_b"), StatisticsRow(index: "i_c"));
        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(
                IndexFieldCount, IndexRow(index: "i_a"), IndexRow(index: "i_b"), IndexRow(index: "i_c")),
            statistics);

        using var cts = new CancellationTokenSource();
        statistics.BeforeRead = index =>
        {
            if (index == cancelBeforeRow)
            {
                cts.Cancel();
                cts.Token.ThrowIfCancellationRequested();
            }
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadIndexSnapshotsAsync(NoFilter, true, cts.Token).AsTask());

        Assert.True(statistics.Disposed);
    }

    [Fact]
    public async Task CancellationDuringE002ReaderDisposal_Surfaces()
    {
        using var cts = new CancellationTokenSource();
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow());
        statistics.DisposeFailure = new OperationCanceledException(cts.Token);
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount, IndexRow()), statistics);

        OperationCanceledException exception = await Assert.ThrowsAsync<OperationCanceledException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(cts.Token, exception.CancellationToken);
        Assert.True(statistics.Disposed);
    }

    [Fact]
    public async Task ACancellationIsNeverReclassifiedAsAMappingError()
    {
        var reader = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        FakeStatementGateway gateway = Gateway(reader);

        using var cts = new CancellationTokenSource();
        reader.BeforeRead = _ =>
        {
            cts.Cancel();
            cts.Token.ThrowIfCancellationRequested();
        };

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => Executor(gateway).ReadIndexSnapshotsAsync(NoFilter, false, cts.Token).AsTask());
    }

    // --- Cleanup precedence ----------------------------------------------------------------------

    [Fact]
    public async Task TheReaderIsDisposedOnSuccess()
    {
        var reader = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        FakeStatementGateway gateway = Gateway(reader);

        await RunAsync(gateway, false, TestContext.Current.CancellationToken);

        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task BothReadersAreDisposedOnSuccess()
    {
        var indexes = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow());
        FakeStatementGateway gateway = Gateway(indexes, statistics);

        await RunAsync(gateway, true, TestContext.Current.CancellationToken);

        Assert.True(indexes.Disposed);
        Assert.True(statistics.Disposed);
    }

    [Fact]
    public async Task ACleanupOnlyFailure_StillPropagates()
    {
        var reader = FakeRowReader.WithRows(IndexFieldCount, IndexRow());
        reader.DisposeFailure = new InvalidOperationException("disposal");
        FakeStatementGateway gateway = Gateway(reader);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task APrimaryFailureWinsOverADisposalFailure()
    {
        var reader = FakeRowReader.WithRows(IndexFieldCount, IndexRow(relationKind: "x"));
        reader.DisposeFailure = new InvalidOperationException("disposal");
        FakeStatementGateway gateway = Gateway(reader);

        PostgreSqlIndexSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexSnapshotMappingException>(
                () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(MappingMessage, exception.Message);
        Assert.True(reader.Disposed);
    }

    [Fact]
    public async Task AnE002PrimaryFailureWinsOverItsDisposalFailure()
    {
        var statistics = FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(scanCount: -1L));
        statistics.DisposeFailure = new InvalidOperationException("disposal");
        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount, IndexRow()), statistics);

        await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
            () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());

        Assert.True(statistics.Disposed);
    }

    // --- Leakage ---------------------------------------------------------------------------------

    [Fact]
    public async Task AFailureNamesNothingItRead()
    {
        const string marker = "sensitive-marker-04e-executor";

        object?[] row = IndexRow(
            schema: marker + "-schema",
            table: marker + "-table",
            index: marker + "-index",
            expression: marker + "-expression",
            predicate: marker + "-predicate",
            collationSchema: marker + "-collation",
            collationName: marker + "-collation-name",
            opclassSchema: marker + "-opclass",
            opclassName: marker + "-opclass-name",
            opclassOptions: [marker + "-option"]);
        row[30] = "not-a-bigint";

        FakeStatementGateway gateway = Gateway(FakeRowReader.WithRows(IndexFieldCount, row));

        PostgreSqlIndexSnapshotMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexSnapshotMappingException>(
                () => RunAsync(gateway, false, TestContext.Current.CancellationToken).AsTask());

        foreach (string surface in new[]
                 {
                     exception.Message,
                     exception.ToString(),
                     exception.StackTrace ?? string.Empty,
                 })
        {
            Assert.False(surface.Contains(marker, StringComparison.Ordinal), LeakMessage);
            Assert.False(surface.Contains("InvalidCast", StringComparison.Ordinal), LeakMessage);
        }
    }

    [Fact]
    public async Task AStatisticsFailureNamesNothingItRead()
    {
        const string marker = "sensitive-marker-04e-stats";

        FakeStatementGateway gateway = Gateway(
            FakeRowReader.WithRows(IndexFieldCount, IndexRow(index: marker + "-index")),
            FakeRowReader.WithRows(StatisticsFieldCount, StatisticsRow(index: marker + "-index", scanCount: -3L)));

        PostgreSqlIndexUsageStatisticsMappingException exception =
            await Assert.ThrowsAsync<PostgreSqlIndexUsageStatisticsMappingException>(
                () => RunAsync(gateway, true, TestContext.Current.CancellationToken).AsTask());

        Assert.Equal(StatisticsMessage, exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        foreach (string surface in new[] { exception.Message, exception.ToString() })
        {
            Assert.False(surface.Contains(marker, StringComparison.Ordinal), LeakMessage);
        }
    }

    [Fact]
    public void TheStatisticsExceptionHasNoMessageOrInnerConstructor()
    {
        System.Reflection.ConstructorInfo[] constructors = typeof(PostgreSqlIndexUsageStatisticsMappingException)
            .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        System.Reflection.ConstructorInfo only = Assert.Single(constructors);
        Assert.Empty(only.GetParameters());
    }
}
