using DbHealthInspector.Core.Snapshots;

namespace DbHealthInspector.UnitTests.Snapshots;

public sealed class CapabilitySnapshotTests
{
    private static CapabilitySnapshot BuildSnapshot(
        CapabilityStatus catalogMetadata = CapabilityStatus.Available,
        CapabilityStatus usageStatistics = CapabilityStatus.Available,
        CapabilityStatus dataProfiling = CapabilityStatus.Disabled)
    {
        return new CapabilitySnapshot(
        [
            new CapabilityState(CapabilityKind.CatalogMetadata, catalogMetadata, Reason(catalogMetadata)),
            new CapabilityState(CapabilityKind.UsageStatistics, usageStatistics, Reason(usageStatistics)),
            new CapabilityState(CapabilityKind.DataProfiling, dataProfiling, Reason(dataProfiling)),
        ]);

        static string? Reason(CapabilityStatus status) =>
            status == CapabilityStatus.Available ? null : "Not available for this test.";
    }

    [Fact]
    public void Constructor_AllowsExactlyOneStatePerDefinedKind()
    {
        CapabilitySnapshot snapshot = BuildSnapshot();

        Assert.Equal(CapabilityStatus.Available, snapshot.GetState(CapabilityKind.CatalogMetadata).Status);
        Assert.Equal(CapabilityStatus.Available, snapshot.GetState(CapabilityKind.UsageStatistics).Status);
        Assert.Equal(CapabilityStatus.Disabled, snapshot.GetState(CapabilityKind.DataProfiling).Status);
    }

    [Fact]
    public void Constructor_RejectsAMissingKind()
    {
        Assert.Throws<ArgumentException>(() => new CapabilitySnapshot(
        [
            new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available),
            new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available),
        ]));
    }

    [Fact]
    public void Constructor_RejectsADuplicateKind()
    {
        Assert.Throws<ArgumentException>(() => new CapabilitySnapshot(
        [
            new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available),
            new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Unavailable, "Duplicate."),
            new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available),
            new CapabilityState(CapabilityKind.DataProfiling, CapabilityStatus.Disabled, "Disabled."),
        ]));
    }

    [Fact]
    public void Constructor_RejectsANullElement()
    {
        Assert.Throws<ArgumentException>(() => new CapabilitySnapshot(
        [
            new CapabilityState(CapabilityKind.CatalogMetadata, CapabilityStatus.Available),
            new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available),
            null!,
        ]));
    }

    // --- CapabilityState.Reason policy (DHI-R2-002) -------------------------------------

    [Fact]
    public void CapabilityState_AllowsAvailableWithoutReason()
    {
        var state = new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available);

        Assert.Null(state.Reason);
    }

    [Fact]
    public void CapabilityState_RejectsAvailableWithReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Available, "Explained anyway."));
    }

    [Fact]
    public void CapabilityState_AllowsUnavailableWithoutReason()
    {
        var state = new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Unavailable);

        Assert.Null(state.Reason);
    }

    [Fact]
    public void CapabilityState_AllowsUnavailableWithReason()
    {
        var state = new CapabilityState(
            CapabilityKind.UsageStatistics, CapabilityStatus.Unavailable, "Permission denied.");

        Assert.Equal("Permission denied.", state.Reason);
    }

    [Fact]
    public void CapabilityState_RejectsUnavailableWithEmptyReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Unavailable, ""));
    }

    [Fact]
    public void CapabilityState_RejectsUnavailableWithWhitespaceReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new CapabilityState(CapabilityKind.UsageStatistics, CapabilityStatus.Unavailable, "   "));
    }

    [Fact]
    public void CapabilityState_AllowsDisabledWithoutReason()
    {
        var state = new CapabilityState(CapabilityKind.DataProfiling, CapabilityStatus.Disabled);

        Assert.Null(state.Reason);
    }

    [Fact]
    public void CapabilityState_AllowsDisabledWithReason()
    {
        var state = new CapabilityState(
            CapabilityKind.DataProfiling, CapabilityStatus.Disabled, "Disabled by product design.");

        Assert.Equal("Disabled by product design.", state.Reason);
    }

    [Fact]
    public void CapabilityState_RejectsDisabledWithEmptyReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new CapabilityState(CapabilityKind.DataProfiling, CapabilityStatus.Disabled, ""));
    }

    [Fact]
    public void CapabilityState_RejectsDisabledWithWhitespaceReason()
    {
        Assert.Throws<ArgumentException>(() =>
            new CapabilityState(CapabilityKind.DataProfiling, CapabilityStatus.Disabled, "   "));
    }

    // --- DataProfiling neutrality --------------------------------------------------------

    [Fact]
    public void Constructor_AllowsDataProfilingAvailable()
    {
        // Core enforces no policy about which status DataProfiling should have; that is a
        // composition-time decision applied by the CLI in a later gate (ADR-0002).
        CapabilitySnapshot snapshot = BuildSnapshot(dataProfiling: CapabilityStatus.Available);

        Assert.Equal(CapabilityStatus.Available, snapshot.GetState(CapabilityKind.DataProfiling).Status);
    }

    [Fact]
    public void Constructor_AllowsDataProfilingUnavailable()
    {
        CapabilitySnapshot snapshot = BuildSnapshot(dataProfiling: CapabilityStatus.Unavailable);

        Assert.Equal(CapabilityStatus.Unavailable, snapshot.GetState(CapabilityKind.DataProfiling).Status);
    }

    [Fact]
    public void Constructor_AllowsDataProfilingDisabled()
    {
        CapabilitySnapshot snapshot = BuildSnapshot(dataProfiling: CapabilityStatus.Disabled);

        Assert.Equal(CapabilityStatus.Disabled, snapshot.GetState(CapabilityKind.DataProfiling).Status);
    }
}
