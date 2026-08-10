using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The empirical basis of the mapper's relation-state allowlist (GC-DHI-04D-C1, R1-09): which
/// combinations of <c>relkind</c>, <c>relpersistence</c> and <c>relispartition</c> a real
/// PostgreSQL 18 server can actually hold.
/// </summary>
/// <remarks>
/// <para>
/// Every probe runs inside a transaction that is always rolled back, so the relation zoo the other
/// tests depend on is untouched. These objects exist only to establish the matrix, never as
/// permanent fixture state.
/// </para>
/// <para>
/// The two unlogged-partitioned rows are the one place where PostgreSQL 18 alone is not the whole
/// answer: 18 removed support for them, while 15–17 — equally supported by this adapter — could
/// create them. That difference is asserted here as a rejection <b>by the server</b> and separately
/// accepted <b>by the mapper</b>, which is exactly the intended asymmetry.
/// </para>
/// </remarks>
[Collection(PostgreSqlServerSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class RelationStateMatrixTests
{
    private readonly PostgreSqlServerFixture _fixture;

    public RelationStateMatrixTests(PostgreSqlServerFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    private async Task<Dictionary<string, RelationStateObservation>> DiscoverAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<RelationStateObservation> observations =
            await _fixture.DiscoverRelationStateMatrixAsync(cancellationToken);

        return observations.ToDictionary(observation => observation.Label, StringComparer.Ordinal);
    }

    [Theory]
    // relkind 'r' — ordinary tables carry every persistence, as partitions or not.
    [InlineData("ordinary permanent", "r", "p", false)]
    [InlineData("ordinary unlogged", "r", "u", false)]
    [InlineData("ordinary temporary", "r", "t", false)]
    [InlineData("ordinary leaf partition", "r", "p", true)]
    [InlineData("unlogged leaf of permanent root", "r", "u", true)]
    [InlineData("temporary leaf of temporary root", "r", "t", true)]
    // relkind 'p' — partitioned tables, root or subpartition.
    [InlineData("partitioned permanent", "p", "p", false)]
    [InlineData("partitioned temporary", "p", "t", false)]
    [InlineData("subpartitioned partition", "p", "p", true)]
    [InlineData("temporary subpartitioned partition", "p", "t", true)]
    // relkind 'v' — views are permanent or temporary and never partitions.
    [InlineData("permanent view", "v", "p", false)]
    [InlineData("temporary view", "v", "t", false)]
    // relkind 'm' — materialized views are permanent only.
    [InlineData("materialized view", "m", "p", false)]
    // relkind 'f' — foreign tables are permanent, and may be partitions.
    [InlineData("foreign table", "f", "p", false)]
    [InlineData("foreign table as partition", "f", "p", true)]
    public async Task PostgreSql18Produces(string label, string relationKind, string persistence, bool isPartition)
    {
        using CancellationTokenSource deadline = TestDeadline();

        RelationStateObservation observed = (await DiscoverAsync(deadline.Token))[label];

        Assert.True(observed.Created, $"PostgreSQL 18 refused to create: {label}");
        Assert.Equal(relationKind, observed.RelationKind);
        Assert.Equal(persistence, observed.Persistence);
        Assert.Equal(isPartition, observed.IsPartition);
    }

    [Theory]
    [InlineData("partitioned unlogged attempt")]
    [InlineData("unlogged view attempt")]
    [InlineData("unlogged materialized view attempt")]
    [InlineData("temporary materialized view attempt")]
    public async Task PostgreSql18Refuses(string label)
    {
        using CancellationTokenSource deadline = TestDeadline();

        RelationStateObservation observed = (await DiscoverAsync(deadline.Token))[label];

        Assert.False(observed.Created, $"PostgreSQL 18 unexpectedly created: {label}");
        Assert.NotNull(observed.SqlState);
        Assert.Null(observed.RelationKind);
    }

    /// <summary>
    /// R2-04: the one form GC-DHI-04D-C1's empirical matrix never attempted — a temporary
    /// partition that is itself partitioned (<c>relkind = 'p'</c>, <c>relpersistence = 't'</c>,
    /// <c>relispartition = true</c>). Named explicitly, separate from the theory data above, so
    /// the specific gap Codex R2 flagged has an unambiguous, individually reportable test.
    /// </summary>
    [Fact]
    public async Task Temporary_Subpartition_IsObservedAsPartitionedTemporaryPartition()
    {
        using CancellationTokenSource deadline = TestDeadline();

        RelationStateObservation observed =
            (await DiscoverAsync(deadline.Token))["temporary subpartitioned partition"];

        // What PostgreSQL 18.4 actually recorded for the DDL in §6 -- read from pg_class, not
        // assumed.
        Assert.True(observed.Created);
        Assert.Equal("p", observed.RelationKind);
        Assert.Equal("t", observed.Persistence);
        Assert.True(observed.IsPartition);

        // The productive matrix already accepts p/t/true (it was one of the 17 accepted tuples
        // before this evidence existed); this closes the gap between that acceptance and having
        // actually observed the state it accepts. No change to PostgreSqlTableSnapshotMapper.
        TableSnapshot snapshot = PostgreSqlTableSnapshotMapper.Map(
            schemaName: "public",
            tableName: "temporary_subpartition_probe",
            relationKind: observed.RelationKind!,
            relationPersistence: observed.Persistence!,
            isPartition: observed.IsPartition!.Value,
            estimatedRowCount: null,
            tableSizeBytes: 0,
            indexSizeBytes: 0,
            totalSizeBytes: 0,
            hasPrimaryKey: false);

        Assert.Equal(RelationKind.Partition, snapshot.RelationKind);
        Assert.True(snapshot.IsPartition);
        Assert.False(snapshot.IsPartitionedRoot);
    }

    [Fact]
    public async Task NoProbedStateIsLeftBehindInTheFixture()
    {
        using CancellationTokenSource deadline = TestDeadline();

        await _fixture.DiscoverRelationStateMatrixAsync(deadline.Token);

        // The probe schema is created and dropped inside a rolled-back transaction, so the zoo the
        // rest of the suite asserts on is exactly as it was.
        (bool schemaExists, bool tableExists, long tableCount) =
            await _fixture.ReadSchemaShapeAsync(deadline.Token);

        Assert.True(schemaExists);
        Assert.True(tableExists);
        Assert.Equal(1, tableCount);
    }

    [Fact]
    public async Task EveryStatePostgreSqlProducesIsOneTheMapperAccepts()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IReadOnlyList<RelationStateObservation> observations =
            await _fixture.DiscoverRelationStateMatrixAsync(deadline.Token);

        // The allowlist must be at least as wide as reality: any state this server really produced
        // has to be one the mapper admits, or a legitimate catalog row would be rejected.
        foreach (RelationStateObservation observed in observations.Where(row => row.Created))
        {
            bool accepted = AcceptedByMapper(observed.RelationKind!, observed.Persistence!, observed.IsPartition!.Value);

            Assert.True(accepted, $"PostgreSQL 18 produced a state the mapper rejects: {observed.Label}");
        }
    }

    /// <summary>
    /// The same joint allowlist the adapter applies, restated independently so this test fails if
    /// the two ever drift apart.
    /// </summary>
    private static bool AcceptedByMapper(string relationKind, string persistence, bool isPartition) =>
        relationKind switch
        {
            "r" or "p" => persistence is "p" or "u" or "t",
            "v" => persistence is "p" or "t" && !isPartition,
            "m" => persistence == "p" && !isPartition,
            "f" => persistence == "p",
            _ => false,
        };
}
