using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.PostgreSql.Tables;

/// <summary>
/// Maps one raw D001 row to one <see cref="TableSnapshot"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every value is validated <b>before</b> a <see cref="TableSnapshot"/> is constructed. That
/// ordering is deliberate: Core's own guards are correct but they name the offending parameter and
/// sometimes the offending value, and those exceptions would escape through the session boundary.
/// Pre-validating means a bad row always surfaces as the fixed, valueless
/// <see cref="PostgreSqlTableSnapshotMappingException"/> instead.
/// </para>
/// <para>
/// It reads no rows itself, holds no state, and never falls back to
/// <see cref="RelationKind.Unknown"/> — an unrecognised <c>relkind</c> or persistence is a mapping
/// failure, not a value to pass through.
/// </para>
/// </remarks>
internal static class PostgreSqlTableSnapshotMapper
{
    private const char OrdinaryRelation = 'r';
    private const char PartitionedRelation = 'p';
    private const char ViewRelation = 'v';
    private const char MaterializedViewRelation = 'm';
    private const char ForeignRelation = 'f';

    private const char PermanentPersistence = 'p';
    private const char UnloggedPersistence = 'u';
    private const char TemporaryPersistence = 't';

    /// <summary>
    /// Maps one already-read D001 row.
    /// </summary>
    /// <exception cref="PostgreSqlTableSnapshotMappingException">
    /// Any value is missing, out of range, or a combination this adapter does not recognise.
    /// </exception>
    internal static TableSnapshot Map(
        string schemaName,
        string tableName,
        string relationKind,
        string relationPersistence,
        bool isPartition,
        long? estimatedRowCount,
        long tableSizeBytes,
        long indexSizeBytes,
        long totalSizeBytes,
        bool hasPrimaryKey)
    {
        if (string.IsNullOrWhiteSpace(schemaName) || string.IsNullOrWhiteSpace(tableName))
        {
            throw new PostgreSqlTableSnapshotMappingException();
        }

        char kind = SingleCharacterOf(relationKind);
        char persistence = SingleCharacterOf(relationPersistence);

        // The three catalog fields are validated as one tuple, never independently: every value
        // here is individually legal in some relation, so checking them separately would admit
        // combinations PostgreSQL cannot produce — an unlogged materialized view, a view attached
        // as a partition — and silently map them to a plausible-looking snapshot.
        if (!IsSupportedRelationState(kind, persistence, isPartition))
        {
            throw new PostgreSqlTableSnapshotMappingException();
        }

        if (estimatedRowCount is < 0)
        {
            throw new PostgreSqlTableSnapshotMappingException();
        }

        if (tableSizeBytes < 0 || indexSizeBytes < 0 || totalSizeBytes < 0)
        {
            throw new PostgreSqlTableSnapshotMappingException();
        }

        (RelationKind mappedKind, bool isPartitionedRoot, bool mappedIsPartition) =
            Classify(kind, persistence, isPartition);

        return new TableSnapshot(
            schemaName,
            tableName,
            mappedKind,
            mappedIsPartition ? false : isPartitionedRoot,
            mappedIsPartition,
            estimatedRowCount,
            tableSizeBytes,
            indexSizeBytes,
            totalSizeBytes,
            hasPrimaryKey);
    }

    /// <summary>
    /// Whether <paramref name="relationKind"/>, <paramref name="persistence"/> and
    /// <paramref name="isPartition"/> together describe a relation state a supported PostgreSQL
    /// server can actually hold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The allowlist is the union of the states observable across the supported major range
    /// (PostgreSQL 15–18), not only those PostgreSQL 18 can still create. Every accepted tuple was
    /// either reproduced against PostgreSQL 18.4 or is documented as producible by an earlier
    /// supported major; every rejected tuple is one the server refuses in all of them.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> a pair of independent allowlists. <c>relkind</c> 'm' and
    /// persistence 'u' are each legal on their own, but an unlogged materialized view has never
    /// existed, and treating the fields separately is exactly what let that state through before.
    /// </para>
    /// </remarks>
    private static bool IsSupportedRelationState(char relationKind, char persistence, bool isPartition) =>
        relationKind switch
        {
            // Ordinary and partitioned tables own storage, so all three persistences are real, and
            // either kind may itself be a partition.
            //
            // 'p' with unlogged persistence is accepted on purpose. PostgreSQL 18 removed support
            // for unlogged partitioned tables (Release 18, "Migration to Version 18 —
            // Incompatibilities", commit e2bab2d79), but 15–17 accepted CREATE UNLOGGED TABLE ...
            // PARTITION BY and recorded relpersistence 'u'. Such a table still exists on a
            // supported server, so rejecting it would fail a legitimate catalog row.
            OrdinaryRelation or PartitionedRelation =>
                persistence is PermanentPersistence or UnloggedPersistence or TemporaryPersistence,

            // A view has no storage: UNLOGGED is refused by the grammar in every supported major,
            // and a view can never be attached as a partition. Temporary views are ordinary.
            ViewRelation =>
                (persistence is PermanentPersistence or TemporaryPersistence) && !isPartition,

            // CREATE MATERIALIZED VIEW has offered neither UNLOGGED nor TEMPORARY since 15, and a
            // materialized view can never be a partition.
            MaterializedViewRelation =>
                persistence == PermanentPersistence && !isPartition,

            // A foreign table has no local storage — there is no UNLOGGED or TEMP form — but it is
            // legitimately attachable as a partition, so partition state stays open here.
            ForeignRelation =>
                persistence == PermanentPersistence,

            _ => false,
        };

    /// <summary>
    /// Applies the frozen precedence: partition membership decides first, and only then does
    /// <c>relkind</c> choose among the remaining kinds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A partitioned relation that is itself a partition — a subpartitioned partition — is
    /// therefore <see cref="RelationKind.Partition"/>, never an independent root. That is the
    /// whole reason partition state is tested before <c>relkind</c>.
    /// </para>
    /// <para>
    /// Reached only after <see cref="IsSupportedRelationState"/> has accepted the tuple, so
    /// <c>relispartition</c> can no longer promote an impossible state — a view or a materialized
    /// view marked as a partition — into <see cref="RelationKind.Partition"/>. The trailing throw
    /// is unreachable defence, kept so the switch stays total.
    /// </para>
    /// </remarks>
    private static (RelationKind Kind, bool IsRoot, bool IsPartition) Classify(
        char relationKind,
        char persistence,
        bool isPartition)
    {
        if (isPartition)
        {
            return (RelationKind.Partition, false, true);
        }

        return relationKind switch
        {
            PartitionedRelation => (RelationKind.PartitionedTable, true, false),

            // A temporary ordinary table cannot appear in a normal D001 result, because the query
            // excludes pg_temp_*. The branch exists so the mapping is complete and provable.
            OrdinaryRelation when persistence == TemporaryPersistence =>
                (RelationKind.TemporaryTable, false, false),
            OrdinaryRelation => (RelationKind.OrdinaryTable, false, false),

            ViewRelation => (RelationKind.View, false, false),
            MaterializedViewRelation => (RelationKind.MaterializedView, false, false),
            ForeignRelation => (RelationKind.ForeignTable, false, false),

            _ => throw new PostgreSqlTableSnapshotMappingException(),
        };
    }

    private static char SingleCharacterOf(string value)
    {
        if (value is null || value.Length != 1)
        {
            throw new PostgreSqlTableSnapshotMappingException();
        }

        return value[0];
    }
}
