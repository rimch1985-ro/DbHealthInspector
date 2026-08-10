using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The required-function permission path proven against a <b>real</b> PostgreSQL 18 server whose
/// inspection role genuinely cannot execute one of the three relation-size functions D001 needs
/// (GC-DHI-04D §23). A unit-only substitute is explicitly not accepted for this contract.
/// </summary>
/// <remarks>
/// The composition here is the same one the normal suite uses — verified session, probe, then
/// D001 only if the probe allows it. Losing the function privilege makes C002 false, which makes
/// the probe raise the fixed required-capability failure, which means D001 is never offered as a
/// safe operation. D001 is deliberately <b>not</b> executed directly to provoke a similar error:
/// the point is that the composition never gets that far.
/// </remarks>
[Collection(PostgreSqlRequiredFunctionRevokedSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class RequiredFunctionPermissionTests
{
    private const string LeakMessage = "Sensitive data was exposed.";

    private readonly PostgreSqlRequiredFunctionRevokedFixture _fixture;

    public RequiredFunctionPermissionTests(PostgreSqlRequiredFunctionRevokedFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    // --- Preconditions: the revocation is real and narrow --------------------------------------------

    [Fact]
    public async Task Precondition_OnlyTheSelectedFunctionLostItsExecutePrivilege()
    {
        using CancellationTokenSource deadline = TestDeadline();

        (bool tableSize, bool indexesSize, bool totalRelationSize) =
            await _fixture.ReadEffectiveFunctionPrivilegesAsync(deadline.Token);

        // has_function_privilege is PostgreSQL's own effective computation: direct grants, PUBLIC
        // and memberships all included.
        Assert.False(totalRelationSize, "The role must not be able to execute the revoked size function.");
        Assert.True(tableSize, "The other two size functions must be untouched.");
        Assert.True(indexesSize, "The other two size functions must be untouched.");
    }

    [Fact]
    public async Task Precondition_RoleIsNotASuperuserAndInheritsNothing()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // A superuser bypasses every privilege check, and an inherited role could quietly restore
        // the EXECUTE privilege the fixture removed.
        Assert.False(await _fixture.ReadIsSuperuserAsync(deadline.Token));
        Assert.Empty(await _fixture.ReadRoleMembershipsAsync(deadline.Token));
    }

    [Fact]
    public async Task Precondition_CatalogTableAccessSurvivedTheRevocation()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // Only one function privilege was removed; the catalog-table part of the allowlist must be
        // intact, or C002 would fail for the wrong reason.
        Assert.True(await _fixture.ReadEffectiveCatalogTablePrivilegeAsync(deadline.Token));
    }

    // --- The composed path stops before D001 -----------------------------------------------------------

    [Fact]
    public async Task TheProbeFails_AndD001IsNeverExecuted()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken,
            observe: true);

        RecordingPostgreSqlStatementGateway recorder = session.Recorder!;

        await Assert.ThrowsAsync<PostgreSqlRequiredCatalogCapabilityException>(async () =>
        {
            PostgreSqlServerProbeResult probe = await PostgreSqlServerCapabilityProbe.ProbeAsync(
                session.Operations, cancellationToken);

            // Never reached: the probe refuses first. Kept so the composition is the real one
            // rather than a probe call in isolation.
            if (probe.VersionSupport == PostgreSqlVersionSupportStatus.Supported
                && probe.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status == CapabilityStatus.Available)
            {
                _ = await session.Operations.ReadTableSnapshotsAsync(
                    PostgreSqlSchemaFilter.IncludeEverything, cancellationToken);
            }
        });

        IReadOnlyList<PostgreSqlSqlStatementId> executed = recorder.ExecutedStatements;

        // The session initialized, C001 ran, C002 ran and said no. Nothing after that.
        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
            ],
            executed);

        Assert.DoesNotContain(PostgreSqlSqlStatementId.ReadTableSnapshots, executed);
        Assert.Equal(0, executed.Count(id => id == PostgreSqlSqlStatementId.ReadTableSnapshots));
    }

    [Fact]
    public async Task TheFailureNamesNeitherTheFunctionNorTheServer()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken,
            observe: true);

        PostgreSqlRequiredCatalogCapabilityException exception =
            await Assert.ThrowsAsync<PostgreSqlRequiredCatalogCapabilityException>(
                () => PostgreSqlServerCapabilityProbe.ProbeAsync(session.Operations, cancellationToken).AsTask());

        Assert.Equal("Required PostgreSQL catalog metadata is unavailable.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        string[] forbidden =
        [
            "pg_total_relation_size",
            "pg_table_size",
            "pg_indexes_size",
            "has_function_privilege",
            "EXECUTE",
            "42501",
            "permission denied",
            PostgreSqlRequiredFunctionRevokedFixture.DatabaseName,
            PostgreSqlRequiredFunctionRevokedFixture.InspectionRoleName,
        ];

        foreach (string surface in new[] { exception.Message, exception.ToString(), exception.StackTrace ?? string.Empty })
        {
            foreach (string marker in forbidden)
            {
                // The marker is deliberately not part of the assertion, so a failure cannot print
                // the very value the test exists to keep out of CI output.
                bool leaked = surface.Contains(marker, StringComparison.OrdinalIgnoreCase);
                Assert.False(leaked, LeakMessage);
            }
        }
    }

    [Fact]
    public async Task C002ReportsFalse_ObservedOnTheRealServer()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken,
            observe: true);

        // Read C002 directly through the typed boundary, so the boolean under test is the one the
        // server actually returned rather than one inferred from the probe's verdict.
        bool catalogAvailable = await session.Operations.CheckCatalogMetadataAccessAsync(cancellationToken);

        Assert.False(catalogAvailable);
    }

    [Fact]
    public async Task TheSessionRemainsUsableAndTheFailureIsRepeatable()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
                _fixture.InspectionConnectionString,
                PostgreSqlInspectionSessionOptions.Default,
                cancellationToken);

            await Assert.ThrowsAsync<PostgreSqlRequiredCatalogCapabilityException>(
                () => PostgreSqlServerCapabilityProbe.ProbeAsync(session.Operations, cancellationToken).AsTask());
        }
    }
}
