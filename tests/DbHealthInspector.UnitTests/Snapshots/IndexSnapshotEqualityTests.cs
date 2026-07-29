using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.UnitTests.TestSupport;

namespace DbHealthInspector.UnitTests.Snapshots;

/// <summary>
/// Covers the hand-written structural equality of <see cref="IndexSnapshot"/> (record-generated
/// equality would compare <see cref="IndexSnapshot.KeyParts"/> and
/// <see cref="IndexSnapshot.IncludedColumns"/> by reference; see
/// docs/design/core-domain-contracts.md §6.1).
/// </summary>
public sealed class IndexSnapshotEqualityTests
{
    private static IndexSnapshot BuildIndex(
        IReadOnlyCollection<IndexKeyPartSnapshot>? keyParts = null,
        IReadOnlyCollection<string>? includedColumns = null) =>
        SampleSnapshots.Index(
            keyParts: keyParts ??
            [
                SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "customer_id"),
                SampleSnapshots.KeyPartOnColumn(position: 2, columnName: "order_date"),
            ],
            includedColumns: includedColumns ?? ["region", "created_at"]);

    [Fact]
    public void Equals_IsTrueForIndependentlyConstructedEquivalentInstances()
    {
        IndexSnapshot first = BuildIndex();
        IndexSnapshot second = BuildIndex();

        Assert.Equal(first, second);
        Assert.True(first.Equals(second));
        Assert.True(first == second);
    }

    [Fact]
    public void GetHashCode_IsEqualForEquivalentInstances()
    {
        IndexSnapshot first = BuildIndex();
        IndexSnapshot second = BuildIndex();

        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_IsFalseWhenAKeyPartDiffers()
    {
        IndexSnapshot first = BuildIndex();
        IndexSnapshot second = BuildIndex(keyParts:
        [
            SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "customer_id"),
            SampleSnapshots.KeyPartOnColumn(position: 2, columnName: "different_column"),
        ]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_IsFalseWhenKeyPartOrderDiffers()
    {
        IndexKeyPartSnapshot a = SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "customer_id");
        IndexKeyPartSnapshot b = SampleSnapshots.KeyPartOnColumn(position: 2, columnName: "order_date");

        // Same two key parts, but the two snapshots below place them in different index-column
        // order relative to their own numbering scheme by swapping which one is "first".
        IndexSnapshot first = BuildIndex(keyParts: [a, b]);
        IndexSnapshot second = BuildIndex(keyParts:
        [
            SampleSnapshots.KeyPartOnColumn(position: 1, columnName: "order_date"),
            SampleSnapshots.KeyPartOnColumn(position: 2, columnName: "customer_id"),
        ]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_IsFalseWhenAnIncludedColumnDiffers()
    {
        IndexSnapshot first = BuildIndex(includedColumns: ["region", "created_at"]);
        IndexSnapshot second = BuildIndex(includedColumns: ["region", "different_column"]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_IsFalseWhenIncludedColumnOrderDiffers()
    {
        IndexSnapshot first = BuildIndex(includedColumns: ["region", "created_at"]);
        IndexSnapshot second = BuildIndex(includedColumns: ["created_at", "region"]);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Equals_IsFalseWhenComparedWithNull()
    {
        IndexSnapshot index = BuildIndex();

        Assert.False(index.Equals(null));
        Assert.False(index == null);
        Assert.True(index != null);
    }

    [Fact]
    public void Equals_IsTrueWhenComparedWithItself()
    {
        IndexSnapshot index = BuildIndex();

        Assert.True(index.Equals(index));
    }
}
