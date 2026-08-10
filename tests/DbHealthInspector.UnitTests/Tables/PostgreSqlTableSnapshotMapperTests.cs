using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.UnitTests.Tables;

/// <summary>
/// The D001 row-to-<see cref="TableSnapshot"/> mapping (GC-DHI-04D §12–§15): partition state takes
/// precedence over <c>relkind</c>, unknown values fail closed, and nothing is inferred.
/// </summary>
public sealed class PostgreSqlTableSnapshotMapperTests
{
    private const string LeakMessage = "Sensitive data was exposed.";

    private static TableSnapshot Map(
        string relationKind = "r",
        string persistence = "p",
        bool isPartition = false,
        string schemaName = "public",
        string tableName = "orders",
        long? estimatedRowCount = 0,
        long tableSizeBytes = 0,
        long indexSizeBytes = 0,
        long totalSizeBytes = 0,
        bool hasPrimaryKey = false) =>
        PostgreSqlTableSnapshotMapper.Map(
            schemaName, tableName, relationKind, persistence, isPartition,
            estimatedRowCount, tableSizeBytes, indexSizeBytes, totalSizeBytes, hasPrimaryKey);

    // --- Relation and partition mapping -----------------------------------------------------------

    [Theory]
    [InlineData("p")]
    [InlineData("u")]
    public void OrdinaryPermanentOrUnloggedTable_MapsToOrdinaryTable(string persistence)
    {
        TableSnapshot snapshot = Map(relationKind: "r", persistence: persistence);

        Assert.Equal(RelationKind.OrdinaryTable, snapshot.RelationKind);
        Assert.False(snapshot.IsPartitionedRoot);
        Assert.False(snapshot.IsPartition);
    }

    [Fact]
    public void OrdinaryTemporaryTable_MapsToTemporaryTable()
    {
        // Unreachable through a normal D001 result — pg_temp_* is excluded by the query — but the
        // branch exists so the mapping is complete and provable.
        TableSnapshot snapshot = Map(relationKind: "r", persistence: "t");

        Assert.Equal(RelationKind.TemporaryTable, snapshot.RelationKind);
        Assert.False(snapshot.IsPartitionedRoot);
        Assert.False(snapshot.IsPartition);
    }

    [Fact]
    public void PartitionedRoot_MapsToPartitionedTable()
    {
        TableSnapshot snapshot = Map(relationKind: "p", isPartition: false);

        Assert.Equal(RelationKind.PartitionedTable, snapshot.RelationKind);
        Assert.True(snapshot.IsPartitionedRoot);
        Assert.False(snapshot.IsPartition);
    }

    [Fact]
    public void LeafPartition_MapsToPartition()
    {
        TableSnapshot snapshot = Map(relationKind: "r", isPartition: true);

        Assert.Equal(RelationKind.Partition, snapshot.RelationKind);
        Assert.False(snapshot.IsPartitionedRoot);
        Assert.True(snapshot.IsPartition);
    }

    [Fact]
    public void SubpartitionedPartition_MapsToPartition_NotToAnIndependentRoot()
    {
        // relkind 'p' *and* relispartition true: a partitioned table that is itself a partition.
        // Partition state must win, or the middle of a partition tree would look like a root.
        TableSnapshot snapshot = Map(relationKind: "p", isPartition: true);

        Assert.Equal(RelationKind.Partition, snapshot.RelationKind);
        Assert.False(snapshot.IsPartitionedRoot);
        Assert.True(snapshot.IsPartition);
    }

    [Theory]
    [InlineData("r")]
    [InlineData("p")]
    [InlineData("f")]
    public void PartitionStateTakesPrecedenceOverEveryRelationKindThatCanBeAPartition(string relationKind)
    {
        // Only these three can carry relispartition true. 'v' and 'm' are covered by
        // AnImpossibleRelationState_IsRejected: for them, partition state is not a precedence
        // question at all, because the state itself cannot exist.
        TableSnapshot snapshot = Map(relationKind: relationKind, isPartition: true);

        Assert.Equal(RelationKind.Partition, snapshot.RelationKind);
        Assert.False(snapshot.IsPartitionedRoot);
        Assert.True(snapshot.IsPartition);
    }

    [Fact]
    public void PartitionPrecedenceIsAppliedOnlyAfterTheWholeTupleIsAccepted()
    {
        // The regression R1-09 named: relispartition true must not be able to launder an
        // impossible relation into a plausible-looking Partition snapshot.
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(
            () => Map(relationKind: "v", isPartition: true));
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(
            () => Map(relationKind: "m", isPartition: true));
    }

    [Fact]
    public void View_MapsToView() =>
        Assert.Equal(RelationKind.View, Map(relationKind: "v", estimatedRowCount: null).RelationKind);

    [Fact]
    public void MaterializedView_MapsToMaterializedView() =>
        Assert.Equal(RelationKind.MaterializedView, Map(relationKind: "m").RelationKind);

    [Fact]
    public void ForeignTable_MapsToForeignTable() =>
        Assert.Equal(RelationKind.ForeignTable, Map(relationKind: "f").RelationKind);

    [Fact]
    public void NoAcceptedMappingEverProducesUnknown()
    {
        // RelationKind.Unknown exists in Core but this adapter never selects it: an unrecognised
        // value is a failure, not a value to pass through.
        foreach ((string relationKind, string persistence, bool isPartition, _) in AcceptedStates)
        {
            TableSnapshot snapshot = Map(
                relationKind: relationKind,
                persistence: persistence,
                isPartition: isPartition,
                estimatedRowCount: null);

            Assert.NotEqual(RelationKind.Unknown, snapshot.RelationKind);
            Assert.False(snapshot.IsPartitionedRoot && snapshot.IsPartition);
        }
    }

    // --- Joint relation/persistence/partition matrix (R1-09) ---------------------------------------

    /// <summary>The five <c>relkind</c> values D001's WHERE clause admits.</summary>
    private static readonly string[] AdmittedRelationKinds = ["r", "p", "v", "m", "f"];

    /// <summary>The three <c>relpersistence</c> values PostgreSQL defines.</summary>
    private static readonly string[] DefinedPersistences = ["p", "u", "t"];

    private static readonly bool[] PartitionFlags = [false, true];

    /// <summary>
    /// Every (relkind, relpersistence, relispartition) tuple a supported PostgreSQL 15–18 server
    /// can actually hold, with the mapping each must produce.
    /// </summary>
    /// <remarks>
    /// Reproduced against PostgreSQL 18.4 except the two unlogged-partitioned rows, which
    /// PostgreSQL 18 no longer creates but 15–17 do — see
    /// <see cref="AnUnloggedPartitionedTable_IsAcceptedForSupportedOlderMajors"/>.
    /// </remarks>
    public static readonly (string RelationKind, string Persistence, bool IsPartition, RelationKind Expected)[] AcceptedStates =
    [
        // Ordinary tables: every persistence, partition or not.
        ("r", "p", false, RelationKind.OrdinaryTable),
        ("r", "u", false, RelationKind.OrdinaryTable),
        ("r", "t", false, RelationKind.TemporaryTable),
        ("r", "p", true,  RelationKind.Partition),
        ("r", "u", true,  RelationKind.Partition),
        ("r", "t", true,  RelationKind.Partition),

        // Partitioned tables: every persistence, root or subpartition.
        ("p", "p", false, RelationKind.PartitionedTable),
        ("p", "u", false, RelationKind.PartitionedTable),
        ("p", "t", false, RelationKind.PartitionedTable),
        ("p", "p", true,  RelationKind.Partition),
        ("p", "u", true,  RelationKind.Partition),
        ("p", "t", true,  RelationKind.Partition),

        // Views: permanent or temporary, never a partition.
        ("v", "p", false, RelationKind.View),
        ("v", "t", false, RelationKind.View),

        // Materialized views: permanent only, never a partition.
        ("m", "p", false, RelationKind.MaterializedView),

        // Foreign tables: permanent only, but legitimately attachable as a partition.
        ("f", "p", false, RelationKind.ForeignTable),
        ("f", "p", true,  RelationKind.Partition),
    ];

    /// <summary>
    /// Every remaining tuple over the five admitted kinds and three defined persistences. No
    /// supported PostgreSQL major can produce any of them.
    /// </summary>
    public static readonly (string RelationKind, string Persistence, bool IsPartition)[] RejectedStates =
    [
        // A view has no storage, so it cannot be unlogged, and it can never be a partition.
        ("v", "u", false),
        ("v", "u", true),
        ("v", "p", true),
        ("v", "t", true),

        // CREATE MATERIALIZED VIEW offers neither UNLOGGED nor TEMPORARY, and a materialized view
        // can never be a partition.
        ("m", "u", false),
        ("m", "u", true),
        ("m", "t", false),
        ("m", "t", true),
        ("m", "p", true),

        // A foreign table has no local storage: there is no UNLOGGED or TEMP foreign-table form.
        ("f", "u", false),
        ("f", "u", true),
        ("f", "t", false),
        ("f", "t", true),
    ];

    public static TheoryData<string, string, bool, RelationKind> AcceptedMatrix()
    {
        var data = new TheoryData<string, string, bool, RelationKind>();
        foreach ((string kind, string persistence, bool isPartition, RelationKind expected) in AcceptedStates)
        {
            data.Add(kind, persistence, isPartition, expected);
        }

        return data;
    }

    public static TheoryData<string, string, bool> RejectedMatrix()
    {
        var data = new TheoryData<string, string, bool>();
        foreach ((string kind, string persistence, bool isPartition) in RejectedStates)
        {
            data.Add(kind, persistence, isPartition);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(AcceptedMatrix))]
    public void ASupportedRelationState_MapsToItsDeclaredKind(
        string relationKind, string persistence, bool isPartition, RelationKind expected)
    {
        TableSnapshot snapshot = Map(
            relationKind: relationKind,
            persistence: persistence,
            isPartition: isPartition,
            estimatedRowCount: null);

        Assert.Equal(expected, snapshot.RelationKind);
        Assert.Equal(isPartition, snapshot.IsPartition);
        Assert.Equal(expected == RelationKind.PartitionedTable, snapshot.IsPartitionedRoot);
        Assert.False(snapshot.IsPartitionedRoot && snapshot.IsPartition);
    }

    [Theory]
    [MemberData(nameof(RejectedMatrix))]
    public void AnImpossibleRelationState_IsRejected(string relationKind, string persistence, bool isPartition)
    {
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(
            relationKind: relationKind,
            persistence: persistence,
            isPartition: isPartition,
            estimatedRowCount: null));
    }

    [Fact]
    public void TheMatrixIsJointExhaustiveAndDisjoint()
    {
        // The two tables must together cover every kind x persistence x partition combination
        // exactly once: a tuple silently absent from both would be an untested state.
        var accepted = AcceptedStates
            .Select(state => (state.RelationKind, state.Persistence, state.IsPartition))
            .ToHashSet();
        var rejected = RejectedStates.ToHashSet();

        Assert.Equal(17, accepted.Count);
        Assert.Equal(13, rejected.Count);
        Assert.Empty(accepted.Intersect(rejected));

        var all =
            from kind in AdmittedRelationKinds
            from persistence in DefinedPersistences
            from isPartition in PartitionFlags
            select (kind, persistence, isPartition);

        Assert.Equal(30, all.Count());
        Assert.Empty(all.Except(accepted.Union(rejected)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnUnloggedPartitionedTable_IsAcceptedForSupportedOlderMajors(bool isPartition)
    {
        // PostgreSQL 18 removed support for unlogged partitioned tables ("Migration to Version 18
        // - Incompatibilities", commit e2bab2d79), but 15-17 accepted CREATE UNLOGGED TABLE ...
        // PARTITION BY and recorded relpersistence 'u'. The adapter supports 15-18, so such a row
        // is a legitimate catalog state and must not be rejected just because 18 cannot create it.
        TableSnapshot snapshot = Map(relationKind: "p", persistence: "u", isPartition: isPartition);

        Assert.Equal(
            isPartition ? RelationKind.Partition : RelationKind.PartitionedTable,
            snapshot.RelationKind);
    }

    [Fact]
    public void AForeignTablePartition_IsNotRejected()
    {
        // Confirmed against PostgreSQL 18.4: a foreign table can be attached as a partition and
        // is recorded as relkind 'f' with relispartition true.
        TableSnapshot snapshot = Map(relationKind: "f", persistence: "p", isPartition: true);

        Assert.Equal(RelationKind.Partition, snapshot.RelationKind);
        Assert.True(snapshot.IsPartition);
    }

    [Fact]
    public void ATemporaryView_IsNotRejected()
    {
        // Confirmed against PostgreSQL 18.4: CREATE TEMP VIEW yields relkind 'v' with
        // relpersistence 't'.
        Assert.Equal(RelationKind.View, Map(relationKind: "v", persistence: "t").RelationKind);
    }

    // --- Fail-closed values ------------------------------------------------------------------------

    [Theory]
    [InlineData("i")]
    [InlineData("S")]
    [InlineData("t")]
    [InlineData("c")]
    [InlineData("x")]
    [InlineData("R")]
    public void AnUnknownRelationKind_IsRejected(string relationKind) =>
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(relationKind: relationKind));

    [Theory]
    [InlineData("x")]
    [InlineData("P")]
    [InlineData("U")]
    [InlineData("0")]
    public void AnUnknownPersistence_IsRejected(string persistence) =>
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(persistence: persistence));

    [Theory]
    [InlineData("")]
    [InlineData("rr")]
    [InlineData("relation")]
    public void ARelationKindThatIsNotExactlyOneCharacter_IsRejected(string relationKind) =>
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(relationKind: relationKind));

    [Theory]
    [InlineData("")]
    [InlineData("pp")]
    public void APersistenceThatIsNotExactlyOneCharacter_IsRejected(string persistence) =>
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(persistence: persistence));

    [Fact]
    public void ANullRequiredString_IsRejected()
    {
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(schemaName: null!));
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(tableName: null!));
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(relationKind: null!));
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(persistence: null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public void AnEmptyOrWhitespaceName_IsRejected(string name)
    {
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(schemaName: name));
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(tableName: name));
    }

    // --- Estimated rows ------------------------------------------------------------------------------

    [Fact]
    public void ANullEstimate_MeansUnknownAndIsPreserved() =>
        Assert.Null(Map(estimatedRowCount: null).EstimatedRowCount);

    [Fact]
    public void AZeroEstimate_IsValidAndIsNotConfusedWithUnknown()
    {
        TableSnapshot snapshot = Map(estimatedRowCount: 0);

        Assert.NotNull(snapshot.EstimatedRowCount);
        Assert.Equal(0, snapshot.EstimatedRowCount);
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(42L)]
    [InlineData(9_000_000_000L)]
    public void APositiveEstimate_IsPassedThroughUnchanged(long estimate) =>
        Assert.Equal(estimate, Map(estimatedRowCount: estimate).EstimatedRowCount);

    [Theory]
    [InlineData(-1L)]
    [InlineData(-42L)]
    public void ANegativeEstimate_IsRejected(long estimate)
    {
        // D001 already maps reltuples < 0 to NULL, so a negative value arriving here means the
        // shape contract was violated.
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(estimatedRowCount: estimate));
    }

    // --- Sizes -----------------------------------------------------------------------------------------

    [Fact]
    public void SizesArePassedThroughIndependently()
    {
        TableSnapshot snapshot = Map(tableSizeBytes: 8192, indexSizeBytes: 16384, totalSizeBytes: 40960);

        Assert.Equal(8192, snapshot.TableSizeBytes);
        Assert.Equal(16384, snapshot.IndexSizeBytes);
        Assert.Equal(40960, snapshot.TotalSizeBytes);
    }

    [Fact]
    public void TheTotalIsNeverRecomputedAndNoArithmeticIdentityIsRequired()
    {
        // The three sizes are independent server reads and the total legitimately includes
        // components the other two do not, so table + indexes == total is not required.
        TableSnapshot snapshot = Map(tableSizeBytes: 100, indexSizeBytes: 100, totalSizeBytes: 999);

        Assert.Equal(999, snapshot.TotalSizeBytes);
    }

    [Theory]
    [InlineData(-1L, 0L, 0L)]
    [InlineData(0L, -1L, 0L)]
    [InlineData(0L, 0L, -1L)]
    public void ANegativeSize_IsRejected(long table, long index, long total)
    {
        Assert.Throws<PostgreSqlTableSnapshotMappingException>(
            () => Map(tableSizeBytes: table, indexSizeBytes: index, totalSizeBytes: total));
    }

    [Fact]
    public void ZeroSizes_AreValid()
    {
        TableSnapshot snapshot = Map(relationKind: "v", estimatedRowCount: null);

        Assert.Equal(0, snapshot.TableSizeBytes);
        Assert.Equal(0, snapshot.IndexSizeBytes);
        Assert.Equal(0, snapshot.TotalSizeBytes);
    }

    // --- Primary key -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ThePrimaryKeyFlagIsTakenVerbatim(bool hasPrimaryKey) =>
        Assert.Equal(hasPrimaryKey, Map(hasPrimaryKey: hasPrimaryKey).HasPrimaryKey);

    // --- Failures leak nothing ------------------------------------------------------------------------------

    [Fact]
    public void AMappingRejection_NamesNothingItReceived()
    {
        const string schemaMarker = "marker-schema-04d";
        const string tableMarker = "marker-table-04d";
        const string kindMarker = "Z";

        PostgreSqlTableSnapshotMappingException exception =
            Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(
                schemaName: schemaMarker, tableName: tableMarker, relationKind: kindMarker));

        Assert.Equal("The PostgreSQL table metadata row is invalid.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        foreach (string surface in new[] { exception.Message, exception.ToString(), exception.StackTrace ?? string.Empty })
        {
            foreach (string marker in new[] { schemaMarker, tableMarker })
            {
                bool leaked = surface.Contains(marker, StringComparison.Ordinal);
                Assert.False(leaked, LeakMessage);
            }
        }
    }

    [Fact]
    public void EveryRejectionLooksTheSame()
    {
        string[] messages =
        [
            Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(relationKind: "i")).Message,
            Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(persistence: "x")).Message,
            Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(estimatedRowCount: -1)).Message,
            Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(tableSizeBytes: -1)).Message,
            Assert.Throws<PostgreSqlTableSnapshotMappingException>(() => Map(schemaName: " ")).Message,
        ];

        Assert.All(messages, message => Assert.Equal("The PostgreSQL table metadata row is invalid.", message));
    }

    [Fact]
    public void TheExceptionHasNoMessageOrInnerConstructor()
    {
        System.Reflection.ConstructorInfo[] constructors = typeof(PostgreSqlTableSnapshotMappingException)
            .GetConstructors(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

        System.Reflection.ConstructorInfo only = Assert.Single(constructors);
        Assert.Empty(only.GetParameters());
    }
}
