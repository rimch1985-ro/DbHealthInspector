using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.UnitTests.Rules.TestSupport;

/// <summary>
/// Builds the minimal <see cref="DatabaseSnapshot"/> shapes the DBH001–DBH005 tests need.
/// Every factory takes explicit arguments for the field under test and supplies neutral,
/// clearly non-triggering values for everything else, so a test's intent is visible at the
/// call site.
/// </summary>
internal static class DiagnosticSnapshotBuilder
{
    internal const string Schema = "app";

    internal static TableSnapshot Table(
        string name,
        RelationKind relationKind = RelationKind.OrdinaryTable,
        bool hasPrimaryKey = true,
        long? estimatedRowCount = 0,
        long totalSizeBytes = 0,
        bool isPartitionedRoot = false,
        bool isPartition = false) =>
        new(
            Schema,
            name,
            relationKind,
            isPartitionedRoot,
            isPartition,
            estimatedRowCount,
            tableSizeBytes: totalSizeBytes,
            indexSizeBytes: 0,
            totalSizeBytes: totalSizeBytes,
            hasPrimaryKey);

    internal static IndexKeyPartSnapshot KeyPart(
        int position = 1,
        string? columnName = "id",
        string? expression = null,
        string? collation = null,
        string? operatorClass = null,
        IndexSortDirection sortDirection = IndexSortDirection.Ascending,
        IndexNullsOrdering nullsOrdering = IndexNullsOrdering.Last) =>
        new(position, columnName, expression, collation, operatorClass, sortDirection, nullsOrdering);

    internal static IndexSnapshot Index(
        string name,
        string tableName = "orders",
        string accessMethod = "btree",
        IReadOnlyCollection<IndexKeyPartSnapshot>? keyParts = null,
        IReadOnlyCollection<string>? includedColumns = null,
        string? partialPredicate = null,
        bool isUnique = false,
        bool? nullsNotDistinct = null,
        bool isPrimaryKey = false,
        bool backsConstraint = false,
        bool isValid = true,
        bool isReady = true,
        bool isLive = true,
        long sizeBytes = 0,
        long? scanCount = 1) =>
        new(
            Schema,
            tableName,
            name,
            accessMethod,
            keyParts ?? [KeyPart()],
            includedColumns ?? [],
            partialPredicate,
            isUnique,
            nullsNotDistinct,
            isPrimaryKey,
            backsConstraint,
            isValid,
            isReady,
            isLive,
            sizeBytes,
            scanCount);

    internal static DatabaseSnapshot Snapshot(
        IReadOnlyCollection<TableSnapshot>? tables = null,
        IReadOnlyCollection<IndexSnapshot>? indexes = null,
        DateTimeOffset? statisticsResetAtUtc = null,
        CapabilityStatus usageStatistics = CapabilityStatus.Available,
        CapabilityStatus catalogMetadata = CapabilityStatus.Available) =>
        new(
            new DatabaseMetadata(DatabaseEngine.PostgreSql, "18.4", "inspector_test"),
            [new SchemaSnapshot(Schema)],
            tables ?? [],
            indexes ?? [],
            Capabilities(usageStatistics, catalogMetadata),
            new StatisticsSnapshot(statisticsResetAtUtc));

    /// <summary>
    /// <paramref name="catalogMetadata"/> defaults to <see cref="CapabilityStatus.Available"/>, so
    /// every existing caller is unaffected. It becomes settable for the unsupported-server case,
    /// where the provider composes a snapshot with the required capability unavailable and no
    /// relations at all.
    /// </summary>
    private static CapabilitySnapshot Capabilities(
        CapabilityStatus usageStatistics, CapabilityStatus catalogMetadata) =>
        new(
        [
            catalogMetadata == CapabilityStatus.Available
                ? new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available)
                : new CapabilityState(
                    CapabilityKind.CatalogMetadata,
                    catalogMetadata,
                    "The server version is not supported."),
            usageStatistics == CapabilityStatus.Available
                ? new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available)
                : new CapabilityState(
                    CapabilityKind.UsageStatistics, usageStatistics, "Statistics are not available."),
            new CapabilityState(
                CapabilityKind.DataProfiling, CapabilityStatus.Disabled, "Disabled by product policy."),
        ]);
}
