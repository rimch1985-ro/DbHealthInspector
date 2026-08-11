namespace DbHealthInspector.PostgreSql.Indexes;

/// <summary>
/// One raw E001 row: exactly the thirty-one columns GC-DHI-04E §11 freezes, in that order, before
/// any of them is validated.
/// </summary>
/// <remarks>
/// <para>
/// A pure carrier. It performs no validation of its own — that belongs to
/// <see cref="PostgreSqlIndexSnapshotMapper"/>, which sees a whole index group at once and can
/// therefore reject contradictions a single row cannot detect.
/// </para>
/// <para>
/// The nullable members mirror the SQL exactly: ordinals 10–22 are nullable in the result shape,
/// and ordinals 17–21 are additionally required to be non-null for a key attribute and null for an
/// INCLUDE attribute. <see cref="OperatorClassOptions"/> distinguishes SQL NULL (no options) from
/// an empty array (options present but empty), which are different states.
/// </para>
/// </remarks>
internal sealed class PostgreSqlIndexMetadataRow
{
    /// <summary>The exact column count E001 promises.</summary>
    internal const int FieldCount = 31;

    internal PostgreSqlIndexMetadataRow(
        string schemaName,
        string tableName,
        string indexName,
        string accessMethod,
        string indexRelationKind,
        bool isIndexPartition,
        int attributeCount,
        int keyAttributeCount,
        int attributePosition,
        bool isKey,
        string? columnName,
        string? expression,
        string? collationSchema,
        string? collationName,
        string? operatorClassSchema,
        string? operatorClassName,
        string[]? operatorClassOptions,
        bool? isOrderable,
        bool? isAscending,
        bool? isDescending,
        bool? nullsFirst,
        bool? nullsLast,
        string? partialPredicate,
        bool isUnique,
        bool? nullsNotDistinct,
        bool isPrimaryKey,
        bool backsConstraint,
        bool isValid,
        bool isReady,
        bool isLive,
        long sizeBytes)
    {
        SchemaName = schemaName;
        TableName = tableName;
        IndexName = indexName;
        AccessMethod = accessMethod;
        IndexRelationKind = indexRelationKind;
        IsIndexPartition = isIndexPartition;
        AttributeCount = attributeCount;
        KeyAttributeCount = keyAttributeCount;
        AttributePosition = attributePosition;
        IsKey = isKey;
        ColumnName = columnName;
        Expression = expression;
        CollationSchema = collationSchema;
        CollationName = collationName;
        OperatorClassSchema = operatorClassSchema;
        OperatorClassName = operatorClassName;
        OperatorClassOptions = operatorClassOptions;
        IsOrderable = isOrderable;
        IsAscending = isAscending;
        IsDescending = isDescending;
        NullsFirst = nullsFirst;
        NullsLast = nullsLast;
        PartialPredicate = partialPredicate;
        IsUnique = isUnique;
        NullsNotDistinct = nullsNotDistinct;
        IsPrimaryKey = isPrimaryKey;
        BacksConstraint = backsConstraint;
        IsValid = isValid;
        IsReady = isReady;
        IsLive = isLive;
        SizeBytes = sizeBytes;
    }

    // Ordinals 0-9: the index header, repeated on every attribute row of the same index.
    internal string SchemaName { get; }

    internal string TableName { get; }

    internal string IndexName { get; }

    internal string AccessMethod { get; }

    internal string IndexRelationKind { get; }

    internal bool IsIndexPartition { get; }

    internal int AttributeCount { get; }

    internal int KeyAttributeCount { get; }

    // Ordinals 8-9: per-attribute position and role.
    internal int AttributePosition { get; }

    internal bool IsKey { get; }

    // Ordinals 10-22: per-attribute detail.
    internal string? ColumnName { get; }

    internal string? Expression { get; }

    internal string? CollationSchema { get; }

    internal string? CollationName { get; }

    internal string? OperatorClassSchema { get; }

    internal string? OperatorClassName { get; }

    /// <summary>
    /// <see langword="null"/> for SQL NULL — no options at all — and a possibly empty array when
    /// the server returned one. The two are deliberately different states.
    /// </summary>
    internal string[]? OperatorClassOptions { get; }

    internal bool? IsOrderable { get; }

    internal bool? IsAscending { get; }

    internal bool? IsDescending { get; }

    internal bool? NullsFirst { get; }

    internal bool? NullsLast { get; }

    internal string? PartialPredicate { get; }

    // Ordinals 23-30: index-wide facts, repeated on every attribute row.
    internal bool IsUnique { get; }

    internal bool? NullsNotDistinct { get; }

    internal bool IsPrimaryKey { get; }

    internal bool BacksConstraint { get; }

    internal bool IsValid { get; }

    internal bool IsReady { get; }

    internal bool IsLive { get; }

    internal long SizeBytes { get; }
}
