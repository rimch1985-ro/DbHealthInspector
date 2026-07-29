using DbHealthInspector.Core.Findings;

namespace DbHealthInspector.UnitTests.Findings;

public sealed class DatabaseObjectReferenceTests
{
    [Fact]
    public void Constructor_AllowsTableWithoutParent()
    {
        var reference = new DatabaseObjectReference(
            DatabaseObjectType.Table, "operations", "import_batch_rows");

        Assert.Equal(DatabaseObjectType.Table, reference.ObjectType);
        Assert.Equal("operations", reference.SchemaName);
        Assert.Equal("import_batch_rows", reference.ObjectName);
        Assert.Null(reference.ParentObjectName);
    }

    [Fact]
    public void Constructor_AllowsTableWithAValidParent()
    {
        // A table's ParentObjectName is not required by any rule, but nothing forbids
        // providing one (for example a future "table belongs to a logical module" use).
        var reference = new DatabaseObjectReference(
            DatabaseObjectType.Table, "operations", "import_batch_rows", "logical_module");

        Assert.Equal("logical_module", reference.ParentObjectName);
    }

    [Fact]
    public void Constructor_RejectsBlankParentObjectNameForTable()
    {
        Assert.Throws<ArgumentException>(() =>
            new DatabaseObjectReference(DatabaseObjectType.Table, "operations", "t", parentObjectName: ""));
    }

    [Fact]
    public void Constructor_AllowsIndexWithAValidParent()
    {
        var reference = new DatabaseObjectReference(
            DatabaseObjectType.Index, "sales", "ix_orders_customer_id", "orders");

        Assert.Equal("orders", reference.ParentObjectName);
    }

    [Fact]
    public void Constructor_RejectsNullParentObjectNameForIndex()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseObjectReference(DatabaseObjectType.Index, "sales", "ix_x", parentObjectName: null));
    }

    [Fact]
    public void Constructor_RejectsEmptyParentObjectNameForIndex()
    {
        Assert.Throws<ArgumentException>(() =>
            new DatabaseObjectReference(DatabaseObjectType.Index, "sales", "ix_x", parentObjectName: ""));
    }

    [Fact]
    public void Constructor_RejectsWhitespaceParentObjectNameForIndex()
    {
        Assert.Throws<ArgumentException>(() =>
            new DatabaseObjectReference(DatabaseObjectType.Index, "sales", "ix_x", parentObjectName: "   "));
    }

    [Fact]
    public void Constructor_RejectsNullObjectName()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new DatabaseObjectReference(DatabaseObjectType.Table, "operations", null!));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankObjectName(string objectName)
    {
        Assert.Throws<ArgumentException>(() =>
            new DatabaseObjectReference(DatabaseObjectType.Table, "operations", objectName));
    }

    [Fact]
    public void Constructor_AllowsNullSchemaForNonIndexObjects()
    {
        var reference = new DatabaseObjectReference(DatabaseObjectType.Database, null, "demo_business");

        Assert.Null(reference.SchemaName);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_RejectsBlankSchemaName(string schemaName)
    {
        Assert.Throws<ArgumentException>(() =>
            new DatabaseObjectReference(DatabaseObjectType.Table, schemaName, "t"));
    }

    [Fact]
    public void Constructor_RejectsUndefinedObjectType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new DatabaseObjectReference((DatabaseObjectType)999, "s", "t"));
    }

    [Fact]
    public void Equality_IsValueBasedAndCaseSensitive()
    {
        var first = new DatabaseObjectReference(DatabaseObjectType.Table, "sales", "orders");
        var second = new DatabaseObjectReference(DatabaseObjectType.Table, "sales", "orders");
        var differentCase = new DatabaseObjectReference(DatabaseObjectType.Table, "Sales", "orders");

        Assert.Equal(first, second);
        Assert.NotEqual(first, differentCase);
    }
}
