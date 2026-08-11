using DbHealthInspector.Core.Snapshots;
using DbHealthInspector.IntegrationTests.TestSupport;
using DbHealthInspector.PostgreSql.Capabilities;
using DbHealthInspector.PostgreSql.Sessions;
using DbHealthInspector.PostgreSql.Sql;
using DbHealthInspector.PostgreSql.Tables;

namespace DbHealthInspector.IntegrationTests.PostgreSqlServer;

/// <summary>
/// The GC-DHI-04E new-required-function permission path proven against a <b>real</b> PostgreSQL 18
/// server whose inspection role genuinely cannot execute
/// <c>pg_get_indexdef(oid, integer, boolean)</c> — one of the four functions the C002 expansion
/// added, and the specific overload E001 calls (GC-DHI-04E §23, "C002 required function"). A
/// unit-only substitute is explicitly not accepted for this contract.
/// </summary>
/// <remarks>
/// <para>
/// This is distinct from <c>RequiredFunctionPermissionTests</c> (GC-DHI-04D), which revokes
/// <c>pg_total_relation_size(regclass)</c> — a function C002 already required before GC-DHI-04E.
/// That fixture proves the pre-existing path still works; it does not prove that any of the four
/// <b>new</b> functions actually controls the capability. This suite closes that specific gap.
/// </para>
/// <para>
/// The composition is the same one every other suite uses — verified session, probe, then the
/// index-snapshot operation only if the probe allows it. Losing the function privilege makes C002
/// false, which makes the probe raise the fixed required-capability failure, which means the
/// index-snapshot operation is never offered as a safe operation. E001/E002 are deliberately
/// <b>not</b> executed directly to provoke a similar error: the point is that the test-owned
/// composition never gets that far, which the recorder proves by execution count rather than by
/// the caught exception alone.
/// </para>
/// </remarks>
[Collection(PostgreSqlIndexRequiredFunctionRevokedSuite.Name)]
[Trait("Category", "PostgreSqlServer")]
public sealed class IndexRequiredFunctionPermissionTests
{
    private const string LeakMessage = "Sensitive data was exposed.";

    private readonly PostgreSqlIndexRequiredFunctionRevokedFixture _fixture;

    public IndexRequiredFunctionPermissionTests(PostgreSqlIndexRequiredFunctionRevokedFixture fixture) => _fixture = fixture;

    private static CancellationTokenSource TestDeadline()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestFixtureLifecycle.TestDeadline);
        return deadline;
    }

    // --- Pre-revocation positive control (R1-05) ------------------------------------------------

    [Fact]
    public void BeforeRevocation_AllSevenRequiredFunctionsWereAvailable()
    {
        // Captured by the fixture before any REVOKE ran. Without this, "the selected function is
        // now unavailable" would be equally consistent with a role that never had the privilege.
        IndexFunctionPrivileges before = _fixture.PrivilegesBeforeRevocation;

        Assert.True(before.TableSize);
        Assert.True(before.IndexesSize);
        Assert.True(before.TotalRelationSize);
        Assert.True(before.RelationSize);
        Assert.True(before.GetIndexDef, "The function under test must have been available first.");
        Assert.True(before.GetExpr);
        Assert.True(before.IndexColumnHasProperty);
    }

    [Fact]
    public void BeforeRevocation_C002WasTrue()
    {
        // Observed through the productive C002 statement over a real verified session, so the
        // starting point is the one the product itself would have seen.
        Assert.True(_fixture.CatalogMetadataAvailableBeforeRevocation);
    }

    [Fact]
    public void BeforeRevocation_TheRoleAlreadyHadNoPrivilegeBypass()
    {
        // The role was already NOSUPERUSER with no memberships before the revocation, so the
        // post-revocation reading cannot be explained by a privilege path appearing or vanishing.
        Assert.False(_fixture.IsSuperuserBeforeRevocation);
        Assert.Empty(_fixture.MembershipsBeforeRevocation);
    }

    [Fact]
    public async Task TheRevocationFlippedExactlyTheSelectedFunctionAndC002()
    {
        using CancellationTokenSource deadline = TestDeadline();
        CancellationToken cancellationToken = deadline.Token;

        IndexFunctionPrivileges before = _fixture.PrivilegesBeforeRevocation;
        IndexFunctionPrivileges after = await _fixture.ReadEffectiveFunctionPrivilegesAsync(cancellationToken);

        // The single differentiator: true -> false for pg_get_indexdef(oid,integer,boolean).
        Assert.True(before.GetIndexDef);
        Assert.False(after.GetIndexDef);

        // Everything else unchanged across the transition.
        Assert.Equal(before.TableSize, after.TableSize);
        Assert.Equal(before.IndexesSize, after.IndexesSize);
        Assert.Equal(before.TotalRelationSize, after.TotalRelationSize);
        Assert.Equal(before.RelationSize, after.RelationSize);
        Assert.Equal(before.GetExpr, after.GetExpr);
        Assert.Equal(before.IndexColumnHasProperty, after.IndexColumnHasProperty);

        await using TestOwnedInspectionSession session = await TestOwnedInspectionSession.StartAsync(
            _fixture.InspectionConnectionString,
            PostgreSqlInspectionSessionOptions.Default,
            cancellationToken);

        bool catalogAfter = await session.Operations.CheckCatalogMetadataAccessAsync(cancellationToken);

        // C002: true -> false, caused by that one function and nothing else.
        Assert.True(_fixture.CatalogMetadataAvailableBeforeRevocation);
        Assert.False(catalogAfter);
    }

    // --- Preconditions: the revocation is real, narrow, and the role can't recover it ------------

    [Fact]
    public async Task Precondition_OnlyTheNewIndexFunctionLostItsExecutePrivilege()
    {
        using CancellationTokenSource deadline = TestDeadline();

        IndexFunctionPrivileges privileges = await _fixture.ReadEffectiveFunctionPrivilegesAsync(deadline.Token);

        // has_function_privilege is PostgreSQL's own effective computation: direct grants, PUBLIC
        // and memberships all included.
        Assert.False(privileges.GetIndexDef, "The role must not be able to execute the revoked overload.");

        // Every other GC-DHI-04D/04E required function -- old and new -- must be untouched.
        Assert.True(privileges.TableSize);
        Assert.True(privileges.IndexesSize);
        Assert.True(privileges.TotalRelationSize);
        Assert.True(privileges.RelationSize);
        Assert.True(privileges.GetExpr);
        Assert.True(privileges.IndexColumnHasProperty);
    }

    [Fact]
    public async Task Precondition_RoleIsNotASuperuserAndInheritsNothing()
    {
        using CancellationTokenSource deadline = TestDeadline();

        // A superuser bypasses every privilege check, and an inherited role could quietly restore
        // the EXECUTE privilege the fixture removed. Effective memberships are checked directly
        // rather than assumed from NOINHERIT.
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

    // --- C002 result -------------------------------------------------------------------------------

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

    // --- The composed path stops before E001/E002 -------------------------------------------------

    [Fact]
    public async Task TheProbeFails_AndTheIndexOperationIsNeverOffered()
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

            // Never reached: the probe refuses first. Kept so the composition under test is the
            // real one -- probe, then decide, then call -- rather than a probe call in isolation.
            if (probe.VersionSupport == PostgreSqlVersionSupportStatus.Supported
                && probe.Capabilities.GetState(CapabilityKind.CatalogMetadata).Status == CapabilityStatus.Available)
            {
                bool statisticsAvailable =
                    probe.Capabilities.GetState(CapabilityKind.UsageStatistics).Status == CapabilityStatus.Available;

                _ = await session.Operations.ReadIndexSnapshotsAsync(
                    PostgreSqlSchemaFilter.IncludeEverything, statisticsAvailable, cancellationToken);
            }
        });

        IReadOnlyList<PostgreSqlSqlStatementId> executed = recorder.ExecutedStatements;

        // The session initialized, C001 ran, C002 ran and said no. Nothing after that -- the exact
        // sequence, not merely "no index statement present".
        Assert.Equal(
            [
                PostgreSqlSqlStatementId.SetTransactionReadOnly,
                PostgreSqlSqlStatementId.ApplyLocalTimeouts,
                PostgreSqlSqlStatementId.VerifySessionState,
                PostgreSqlSqlStatementId.ReadServerIdentity,
                PostgreSqlSqlStatementId.CheckCatalogMetadataAccess,
            ],
            executed);

        // Explicit execution counts for E001 and E002 individually -- not just their absence from
        // the sequence above, and not inferred from the caught exception alone.
        Assert.Equal(1, executed.Count(id => id == PostgreSqlSqlStatementId.CheckCatalogMetadataAccess));
        Assert.Equal(0, executed.Count(id => id == PostgreSqlSqlStatementId.ReadIndexMetadata));
        Assert.Equal(0, executed.Count(id => id == PostgreSqlSqlStatementId.ReadIndexUsageStatistics));
        Assert.DoesNotContain(PostgreSqlSqlStatementId.ReadIndexMetadata, executed);
        Assert.DoesNotContain(PostgreSqlSqlStatementId.ReadIndexUsageStatistics, executed);
    }

    // --- Failure surface -----------------------------------------------------------------------------

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

        // The exact same fixed, sanitized message the GC-DHI-04D fixture observes: no
        // fixture-specific exception was introduced for this scenario.
        Assert.Equal("Required PostgreSQL catalog metadata is unavailable.", exception.Message);
        Assert.Null(exception.InnerException);
        Assert.Empty(exception.Data);

        string[] forbidden =
        [
            "pg_get_indexdef",
            "pg_total_relation_size",
            "pg_table_size",
            "pg_indexes_size",
            "pg_relation_size",
            "pg_get_expr",
            "pg_index_column_has_property",
            "has_function_privilege",
            "EXECUTE",
            "42501",
            "permission denied",
            "oid, integer, boolean",
            PostgreSqlIndexRequiredFunctionRevokedFixture.DatabaseName,
            PostgreSqlIndexRequiredFunctionRevokedFixture.InspectionRoleName,
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
