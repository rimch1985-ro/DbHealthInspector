using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.UnitTests.Capabilities.TestSupport;
using Npgsql;

namespace DbHealthInspector.UnitTests.Capabilities;

/// <summary>
/// Cancellation across C001–C004 (GC-DHI-04C §17), including the rule that a requested
/// cancellation always wins over the one authorized <c>42501</c> degradation.
/// </summary>
public sealed class PostgreSqlServerCapabilityProbeCancellationTests
{
    private static ValueTask<PostgreSqlServerProbeResult> ProbeAsync(ProbeScript script, CancellationToken cancellationToken) =>
        PostgreSqlServerCapabilityProbe.ProbeAsync(script.View(), cancellationToken);

    private static PostgresException InsufficientPrivilege() =>
        new("permission denied", "ERROR", "ERROR", "42501");

    public static TheoryData<string> AllStatements() =>
    [
        nameof(PostgreSqlSqlStatementId.ReadServerIdentity),
        nameof(PostgreSqlSqlStatementId.CheckCatalogMetadataAccess),
        nameof(PostgreSqlSqlStatementId.CheckUsageStatisticsAccess),
        nameof(PostgreSqlSqlStatementId.ReadStatisticsReset),
    ];

    [Fact]
    public async Task PreCanceledToken_PreventsC001()
    {
        ProbeScript script = ProbeScript.Healthy();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => ProbeAsync(script, cts.Token).AsTask());

        Assert.Empty(script.ExecutedIds);
    }

    [Fact]
    public async Task TheExactTokenReachesEveryCapabilityStatement()
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        Assert.True(cts.Token.CanBeCanceled);
        ProbeScript script = ProbeScript.Healthy();

        await ProbeAsync(script, cts.Token);

        Assert.Equal(4, script.Tokens.Count);
        Assert.All(script.Tokens, token => Assert.Equal(cts.Token, token));
    }

    [Theory]
    [MemberData(nameof(AllStatements))]
    public async Task CancellationDuringAnyStatement_Propagates(string idName)
    {
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var associated = new OperationCanceledException(cts.Token);

        ProbeScript script = ProbeScript.Healthy().FailingAt(id, associated);

        Exception? thrown = await Record.ExceptionAsync(() => ProbeAsync(script, cts.Token).AsTask());

        Assert.Same(associated, thrown);
    }

    [Theory]
    [MemberData(nameof(AllStatements))]
    public async Task CancellationBetweenStatements_PreventsTheNextOne(string idName)
    {
        // Cancel from inside the seam of the named statement; the probe must not continue past it.
        var id = Enum.Parse<PostgreSqlSqlStatementId>(idName);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        ProbeScript script = ProbeScript.Healthy()
            .BeforeStatement(id, cts.Cancel)
            .FailingAt(id, new OperationCanceledException(cts.Token));

        await Assert.ThrowsAsync<OperationCanceledException>(() => ProbeAsync(script, cts.Token).AsTask());

        // The cancelled statement is the last one attempted.
        Assert.Equal(id, script.ExecutedIds[^1]);
    }

    [Fact]
    public async Task CancellationRacingC004InsufficientPrivilege_LetsCancellationWin()
    {
        // C003 said statistics were readable; the privilege is withdrawn *and* the caller cancels.
        // Cancellation must dominate, so the caller is never told "statistics unavailable" when
        // what actually happened is that they asked to stop.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

        ProbeScript script = ProbeScript.Healthy()
            .BeforeStatement(PostgreSqlSqlStatementId.ReadStatisticsReset, cts.Cancel)
            .FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, InsufficientPrivilege());

        await Assert.ThrowsAsync<OperationCanceledException>(() => ProbeAsync(script, cts.Token).AsTask());
    }

    [Fact]
    public async Task WithoutCancellation_TheSameRaceStillDegrades()
    {
        // The control for the test above: identical failure, no cancellation, degradation happens.
        ProbeScript script = ProbeScript.Healthy()
            .FailingAt(PostgreSqlSqlStatementId.ReadStatisticsReset, InsufficientPrivilege());

        PostgreSqlServerProbeResult result = await ProbeAsync(script, TestContext.Current.CancellationToken);

        Assert.Equal(CapabilityStatus.Unavailable, result.Capabilities.GetState(CapabilityKind.UsageStatistics).Status);
    }

    [Fact]
    public void NoneVersusNone_IsNotAssociation()
    {
        // The frozen GC-DHI-04A rule, reused unchanged: two default tokens are structurally equal
        // but neither is cancelable, so they never count as association.
        var unrelated = new OperationCanceledException(CancellationToken.None);

        Assert.False(PostgreSqlConnectionFactoryAssociationProbe.IsRequestedCancellation(unrelated, CancellationToken.None));
    }
}

/// <summary>
/// A thin accessor that lets this suite assert against the GC-DHI-04A association rule without
/// duplicating it.
/// </summary>
internal static class PostgreSqlConnectionFactoryAssociationProbe
{
    internal static bool IsRequestedCancellation(OperationCanceledException exception, CancellationToken requestedToken) =>
        DbHealthInspector.PostgreSql.Connections.PostgreSqlConnectionFactory.IsRequestedCancellation(exception, requestedToken);
}
