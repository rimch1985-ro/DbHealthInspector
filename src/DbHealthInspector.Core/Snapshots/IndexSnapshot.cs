namespace DbHealthInspector.Core.Snapshots;

/// <summary>
/// A single index observed during an inspection, carrying enough structural and statistical
/// detail to support DBH003 (exact duplicate index), DBH004 (unused index candidate) and DBH005
/// (invalid index) in a future gate.
/// </summary>
/// <remarks>
/// Structural equality is hand-written rather than record-generated: the generated equality for
/// a record only compares <see cref="KeyParts"/> and <see cref="IncludedColumns"/> by reference
/// (their declared type, <see cref="IReadOnlyList{T}"/>, has no built-in element-wise equality),
/// so two independently constructed snapshots with identical values would otherwise never
/// compare equal. <see cref="Equals(IndexSnapshot?)"/> instead compares both lists element by
/// element, in order: the order of <see cref="KeyParts"/> is always semantically significant
/// (it is the index's column order), and the order of <see cref="IncludedColumns"/> is preserved
/// and likewise participates in equality, since this type does not reorder it.
/// </remarks>
public sealed record IndexSnapshot
{
    /// <summary>
    /// The owning schema name.
    /// </summary>
    public string SchemaName { get; }

    /// <summary>
    /// The owning table's name. Always required: an index cannot be represented without its
    /// owning table.
    /// </summary>
    public string TableName { get; }

    /// <summary>
    /// The index name.
    /// </summary>
    public string IndexName { get; }

    /// <summary>
    /// The index access method (for example <c>"btree"</c> or <c>"gin"</c>).
    /// </summary>
    public string AccessMethod { get; }

    /// <summary>
    /// The index's ordered key parts. At least one is required. Order is significant.
    /// </summary>
    public IReadOnlyList<IndexKeyPartSnapshot> KeyParts { get; }

    /// <summary>
    /// Columns included in the index without participating in its key (<c>INCLUDE</c> columns).
    /// Order is preserved and participates in equality; no column name may repeat.
    /// </summary>
    public IReadOnlyList<string> IncludedColumns { get; }

    /// <summary>
    /// The partial index predicate, when this is a partial index. When provided, cannot be
    /// empty or whitespace-only.
    /// </summary>
    public string? PartialPredicate { get; }

    /// <summary>
    /// Whether the index enforces uniqueness.
    /// </summary>
    public bool IsUnique { get; }

    /// <summary>
    /// Whether the index treats null values as not distinct for uniqueness purposes, when the
    /// server reports this (a PostgreSQL 15+ feature).
    /// </summary>
    public bool? NullsNotDistinct { get; }

    /// <summary>
    /// Whether the index backs the table's primary key.
    /// </summary>
    public bool IsPrimaryKey { get; }

    /// <summary>
    /// Whether the index backs a constraint (primary key, unique or exclusion).
    /// </summary>
    /// <remarks>
    /// <see cref="IsPrimaryKey"/> implies this is <see langword="true"/>, but not the other way
    /// around: an index can back a plain unique constraint without being a primary key.
    /// </remarks>
    public bool BacksConstraint { get; }

    /// <summary>
    /// Whether the server reports the index as valid (<c>indisvalid</c>).
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Whether the server reports the index as ready for inserts (<c>indisready</c>).
    /// </summary>
    public bool IsReady { get; }

    /// <summary>
    /// Whether the server reports the index as live (<c>indislive</c>).
    /// </summary>
    public bool IsLive { get; }

    /// <summary>
    /// The index's storage size, in bytes.
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// The server-reported scan counter, when statistics are available.
    /// </summary>
    public long? ScanCount { get; }

    /// <summary>
    /// Creates an index snapshot.
    /// </summary>
    public IndexSnapshot(
        string schemaName,
        string tableName,
        string indexName,
        string accessMethod,
        IReadOnlyCollection<IndexKeyPartSnapshot> keyParts,
        IReadOnlyCollection<string> includedColumns,
        string? partialPredicate,
        bool isUnique,
        bool? nullsNotDistinct,
        bool isPrimaryKey,
        bool backsConstraint,
        bool isValid,
        bool isReady,
        bool isLive,
        long sizeBytes,
        long? scanCount)
    {
        SchemaName = Guard.AgainstNullOrWhiteSpace(schemaName, nameof(schemaName));
        TableName = Guard.AgainstNullOrWhiteSpace(tableName, nameof(tableName));
        IndexName = Guard.AgainstNullOrWhiteSpace(indexName, nameof(indexName));
        AccessMethod = Guard.AgainstNullOrWhiteSpace(accessMethod, nameof(accessMethod));

        IReadOnlyList<IndexKeyPartSnapshot> keyPartsCopy =
            Guard.CopyDefensivelyRejectingNullElements(keyParts, nameof(keyParts));
        if (keyPartsCopy.Count == 0)
        {
            throw new ArgumentException("An index must declare at least one key part.", nameof(keyParts));
        }

        var seenPositions = new HashSet<int>();
        foreach (IndexKeyPartSnapshot part in keyPartsCopy)
        {
            if (!seenPositions.Add(part.Position))
            {
                throw new ArgumentException(
                    $"Duplicate key part position '{part.Position}'.", nameof(keyParts));
            }
        }

        KeyParts = keyPartsCopy;

        IReadOnlyList<string> includedColumnsCopy =
            Guard.CopyDefensivelyRejectingBlankElements(includedColumns, nameof(includedColumns));
        var seenIncludedColumns = new HashSet<string>(StringComparer.Ordinal);
        foreach (string column in includedColumnsCopy)
        {
            if (!seenIncludedColumns.Add(column))
            {
                throw new ArgumentException(
                    $"Duplicate included column '{column}'.", nameof(includedColumns));
            }
        }

        IncludedColumns = includedColumnsCopy;

        PartialPredicate = Guard.AgainstEmptyOrWhiteSpace(partialPredicate, nameof(partialPredicate));
        IsUnique = isUnique;
        NullsNotDistinct = nullsNotDistinct;
        IsPrimaryKey = isPrimaryKey;

        if (isPrimaryKey)
        {
            if (!isUnique)
            {
                throw new ArgumentException("A primary key index must be unique.", nameof(isUnique));
            }

            if (!backsConstraint)
            {
                throw new ArgumentException(
                    "A primary key index must back a constraint.", nameof(backsConstraint));
            }
        }

        BacksConstraint = backsConstraint;
        IsValid = isValid;
        IsReady = isReady;
        IsLive = isLive;
        SizeBytes = Guard.AgainstNegative(sizeBytes, nameof(sizeBytes));

        if (scanCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scanCount), scanCount, "Scan count cannot be negative.");
        }

        ScanCount = scanCount;
    }

    /// <summary>
    /// Compares two index snapshots by value, including element-wise, order-sensitive
    /// comparison of <see cref="KeyParts"/> and <see cref="IncludedColumns"/>.
    /// </summary>
    public bool Equals(IndexSnapshot? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return SchemaName == other.SchemaName
            && TableName == other.TableName
            && IndexName == other.IndexName
            && AccessMethod == other.AccessMethod
            && KeyParts.SequenceEqual(other.KeyParts)
            && IncludedColumns.SequenceEqual(other.IncludedColumns, StringComparer.Ordinal)
            && PartialPredicate == other.PartialPredicate
            && IsUnique == other.IsUnique
            && NullsNotDistinct == other.NullsNotDistinct
            && IsPrimaryKey == other.IsPrimaryKey
            && BacksConstraint == other.BacksConstraint
            && IsValid == other.IsValid
            && IsReady == other.IsReady
            && IsLive == other.IsLive
            && SizeBytes == other.SizeBytes
            && ScanCount == other.ScanCount;
    }

    /// <summary>
    /// Computes a hash code consistent with <see cref="Equals(IndexSnapshot?)"/>, including the
    /// element-wise contents of <see cref="KeyParts"/> and <see cref="IncludedColumns"/>.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(SchemaName, StringComparer.Ordinal);
        hash.Add(TableName, StringComparer.Ordinal);
        hash.Add(IndexName, StringComparer.Ordinal);
        hash.Add(AccessMethod, StringComparer.Ordinal);

        foreach (IndexKeyPartSnapshot part in KeyParts)
        {
            hash.Add(part);
        }

        foreach (string column in IncludedColumns)
        {
            hash.Add(column, StringComparer.Ordinal);
        }

        hash.Add(PartialPredicate);
        hash.Add(IsUnique);
        hash.Add(NullsNotDistinct);
        hash.Add(IsPrimaryKey);
        hash.Add(BacksConstraint);
        hash.Add(IsValid);
        hash.Add(IsReady);
        hash.Add(IsLive);
        hash.Add(SizeBytes);
        hash.Add(ScanCount);
        return hash.ToHashCode();
    }
}
