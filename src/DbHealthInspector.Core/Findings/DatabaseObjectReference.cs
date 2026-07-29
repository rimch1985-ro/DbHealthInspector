namespace DbHealthInspector.Core.Findings;

/// <summary>
/// Identifies the database object a finding is about, without any engine-specific concept such
/// as an OID or engine-specific quoting.
/// </summary>
/// <example>
/// A table reference: <c>ObjectType = Table</c>, <c>SchemaName = "operations"</c>,
/// <c>ObjectName = "import_batch_rows"</c>, <c>ParentObjectName = null</c>.
/// An index reference: <c>ObjectType = Index</c>, <c>SchemaName = "sales"</c>,
/// <c>ObjectName = "ix_orders_customer_id"</c>, <c>ParentObjectName = "orders"</c>.
/// </example>
public sealed record DatabaseObjectReference
{
    /// <summary>
    /// The kind of object referenced.
    /// </summary>
    public DatabaseObjectType ObjectType { get; }

    /// <summary>
    /// The schema the object belongs to, when applicable. <see langword="null"/> means no
    /// schema applies (for example a <see cref="DatabaseObjectType.Database"/> reference). When
    /// provided, cannot be empty or whitespace-only.
    /// </summary>
    public string? SchemaName { get; }

    /// <summary>
    /// The object's own name.
    /// </summary>
    public string ObjectName { get; }

    /// <summary>
    /// The name of the owning object, when the referenced object cannot be identified without
    /// it. Required when <see cref="ObjectType"/> is <see cref="DatabaseObjectType.Index"/> — a
    /// <see langword="null"/> value is rejected there specifically with
    /// <see cref="ArgumentNullException"/>, distinct from a blank one. For every other object
    /// type it is optional (<see langword="null"/> is allowed), but when provided, cannot be
    /// empty or whitespace-only: this field is either a genuine parent name or absent.
    /// </summary>
    public string? ParentObjectName { get; }

    /// <summary>
    /// Creates a database object reference.
    /// </summary>
    /// <param name="objectType">The kind of object referenced.</param>
    /// <param name="schemaName">
    /// The owning schema name, or <see langword="null"/> when not applicable. When provided,
    /// cannot be empty or whitespace-only.
    /// </param>
    /// <param name="objectName">The object's own name. Cannot be null, empty or whitespace.</param>
    /// <param name="parentObjectName">
    /// The owning object's name. Required for <see cref="DatabaseObjectType.Index"/>
    /// references: <see langword="null"/> throws <see cref="ArgumentNullException"/> there, and
    /// an empty or whitespace-only value throws <see cref="ArgumentException"/>. For every other
    /// object type it is optional (<see langword="null"/> is allowed), but when provided, cannot
    /// be empty or whitespace-only.
    /// </param>
    public DatabaseObjectReference(
        DatabaseObjectType objectType,
        string? schemaName,
        string objectName,
        string? parentObjectName = null)
    {
        Guard.AgainstUndefinedEnum(objectType, nameof(objectType));
        ObjectType = objectType;
        SchemaName = Guard.AgainstEmptyOrWhiteSpace(schemaName, nameof(schemaName));
        ObjectName = Guard.AgainstNullOrWhiteSpace(objectName, nameof(objectName));

        if (objectType == DatabaseObjectType.Index && parentObjectName is null)
        {
            throw new ArgumentNullException(nameof(parentObjectName));
        }

        ParentObjectName = Guard.AgainstEmptyOrWhiteSpace(parentObjectName, nameof(parentObjectName));
    }
}
