using DbHealthInspector.Core;
using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.UnitTests.TestSupport;

/// <summary>
/// Small, realistic sample snapshot values shared across Core snapshot tests.
/// </summary>
internal static class SampleSnapshots
{
    public static DatabaseMetadata Metadata(
        string engineVersion = "18.4", string databaseName = "demo_business", string? currentUser = "dbhealth") =>
        new(DatabaseEngine.PostgreSql, engineVersion, databaseName, currentUser);

    public static TableSnapshot OrdinaryTable(
        string schemaName = "operations",
        string tableName = "import_batch_rows",
        bool hasPrimaryKey = false,
        long? estimatedRowCount = 25_000,
        long tableSizeBytes = 4_194_304,
        long indexSizeBytes = 0,
        long totalSizeBytes = 4_194_304) =>
        new(
            schemaName,
            tableName,
            RelationKind.OrdinaryTable,
            isPartitionedRoot: false,
            isPartition: false,
            estimatedRowCount,
            tableSizeBytes,
            indexSizeBytes,
            totalSizeBytes,
            hasPrimaryKey);

    public static IndexKeyPartSnapshot KeyPartOnColumn(
        int position = 1,
        string columnName = "customer_id",
        IndexSortDirection sortDirection = IndexSortDirection.Ascending,
        IndexNullsOrdering nullsOrdering = IndexNullsOrdering.Last) =>
        new(position, columnName, expression: null, collation: null, operatorClass: null, sortDirection, nullsOrdering);

    public static IndexKeyPartSnapshot KeyPartOnExpression(
        int position = 1,
        string expression = "lower(email)",
        IndexSortDirection sortDirection = IndexSortDirection.Ascending,
        IndexNullsOrdering nullsOrdering = IndexNullsOrdering.Last) =>
        new(position, columnName: null, expression, collation: null, operatorClass: null, sortDirection, nullsOrdering);

    public static IndexSnapshot Index(
        string schemaName = "sales",
        string tableName = "orders",
        string indexName = "ix_orders_customer_id",
        IReadOnlyCollection<IndexKeyPartSnapshot>? keyParts = null,
        IReadOnlyCollection<string>? includedColumns = null,
        bool isUnique = false,
        bool isPrimaryKey = false,
        bool? backsConstraint = null,
        bool isValid = true,
        bool isReady = true,
        bool isLive = true,
        long sizeBytes = 65_536,
        long? scanCount = 0) =>
        new(
            schemaName,
            tableName,
            indexName,
            accessMethod: "btree",
            keyParts ?? [KeyPartOnColumn()],
            includedColumns ?? [],
            partialPredicate: null,
            isUnique,
            nullsNotDistinct: null,
            isPrimaryKey,
            backsConstraint: backsConstraint ?? (isPrimaryKey || isUnique),
            isValid,
            isReady,
            isLive,
            sizeBytes,
            scanCount);

    public static CapabilitySnapshot AllCapabilitiesAvailable() =>
        new(
        [
            new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available),
            new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available),
            new CapabilityState(CapabilityKind.DataProfiling, CapabilityStatus.Disabled, "Disabled by product design in v0.1.0."),
        ]);

    public static CapabilitySnapshot Capabilities(
        CapabilityStatus catalogMetadata = CapabilityStatus.Available,
        CapabilityStatus usageStatistics = CapabilityStatus.Available,
        CapabilityStatus dataProfiling = CapabilityStatus.Disabled) =>
        new(
        [
            new CapabilityState(CapabilityKind.CatalogMetadata, catalogMetadata, Reason(catalogMetadata)),
            new CapabilityState(CapabilityKind.UsageStatistics, usageStatistics, Reason(usageStatistics)),
            new CapabilityState(CapabilityKind.DataProfiling, dataProfiling, Reason(dataProfiling)),
        ]);

    private static string? Reason(CapabilityStatus status) =>
        status == CapabilityStatus.Available ? null : "Not available for this test.";

    public static StatisticsSnapshot StatisticsWithReset(DateTimeOffset? resetAtUtc = null) =>
        new(resetAtUtc ?? new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));

    public static DatabaseSnapshot Snapshot(
        DatabaseMetadata? metadata = null,
        IReadOnlyCollection<SchemaSnapshot>? schemas = null,
        IReadOnlyCollection<TableSnapshot>? tables = null,
        IReadOnlyCollection<IndexSnapshot>? indexes = null,
        CapabilitySnapshot? capabilities = null,
        StatisticsSnapshot? statistics = null) =>
        new(
            metadata ?? Metadata(),
            schemas ?? [new SchemaSnapshot("operations")],
            tables ?? [OrdinaryTable()],
            indexes ?? [Index()],
            capabilities ?? AllCapabilitiesAvailable(),
            statistics ?? StatisticsWithReset());
}
