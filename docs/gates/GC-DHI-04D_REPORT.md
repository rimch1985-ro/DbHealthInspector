# GC-DHI-04D — Table Snapshot Query and Mapping

**Gate:** GC-DHI-04D — Table Snapshot Query and Mapping  
**Backlog:** PG-04  
**Status:** Implemented and integrated  
**Verdict:** READY FOR FINAL HUMAN CLOSURE  
**Integration date:** 2026-08-10  

---

## 1. Definition baseline

The accepted definition is `docs/gates/GC-DHI-04D_DEFINITION.md`. Integration
started from master commit `8fd101bcaf86bad70c2f6dc15ac0d2fc7087fa64` with
zero divergence from `origin/master`.

## 2. Human implementation authorization

The project owner authorized PG-04 and GC-DHI-04D implementation, review and
integration. The authorization excluded final gate closure, GC-DHI-04E,
GC-DHI-04F, D002, index inspection, a snapshot provider, diagnostic rules, CLI
inspection behavior, JSON reporting and release actions.

## 3. Claude initial implementation

Claude Code produced the initial D001 table-snapshot query, C002 permission
expansion, typed `TextArray` parameters, explicit table mapping, unit tests,
PostgreSQL integration tests and design documentation without remote GitHub
operations.

## 4. Codex R1 findings

Codex R1 required stronger fail-closed wrong-type mapping, a joint empirical
relation-state matrix, same-session foreign-connection evidence with a positive
control, and complete cancellation/cleanup evidence. The findings included the
focused R1-08/R1-20, R1-09, R1-17/R1-18 and R1-19 areas.

## 5. Claude C1

C1 implemented the R1 corrections. It added the exhaustive joint mapper
matrix, real PostgreSQL relation probes, same-backend
`postgres_fdw_get_connections()` evidence, explicit wrong-type sanitization,
and deterministic cancellation and cleanup seams.

## 6. Codex R2

R2 accepted the corrected production design and all regressions except
`R2-04`: the empirical fixture did not yet execute the temporary
subpartitioned-partition form `p/t/true`. The productive mapper already
accepted the state; only its real PostgreSQL 18 observation was missing.

## 7. Claude C2

C2 changed only the PostgreSQL test fixture, relation-state integration test
and design document. It added a real temporary root plus a temporary partition
that is itself partitioned, and did not modify production code or the
productive relation-state matrix.

## 8. Codex R3

R3 independently observed `p/t/true` from `pg_class` on PostgreSQL 18.4,
verified the complete 19-case matrix, repeated the focused matrix five times
and the whole PostgreSQLServer suite three times, and returned
`APPROVED FOR HUMAN INTEGRATION REVIEW`.

## 9. Final candidate inventory

The candidate contained exactly 33 files: 17 tracked modifications and 16 new
files. It changed only PostgreSQL adapter implementation, related unit and
integration tests, and `docs/design/postgresql-table-snapshot-query.md`. It
contained no Core, CLI, Connections, workflow, dependency, governance, backlog,
gate-definition or ADR changes.

## 10. D001 exactness

| Property | Verified value |
|---|---|
| ID | D001 — `ReadTableSnapshots` |
| Length | 1816 characters |
| SHA-256 | `13b4e88d7ac0053d87cf760b3e6a64ae879effa91de66a15bd693ba458680b87` |
| Normative ordinal equality | `true` |
| Business-row access | none |
| `COUNT(*)` | absent |

## 11. C002 expansion

C002 retains its identity and Boolean result. It adds exact `EXECUTE` checks
for `pg_table_size(regclass)`, `pg_indexes_size(regclass)` and
`pg_total_relation_size(regclass)`. A missing required permission makes C002
false without identifying the failed function in a caller-visible surface.

## 12. Statement inventory

The productive inventory contains exactly eight statements in this order:

```text
B001 — SetTransactionReadOnly
B002 — ApplyLocalTimeouts
B003 — VerifySessionState
C001 — ReadServerIdentity
C002 — CheckCatalogMetadataAccess
C003 — CheckUsageStatisticsAccess
C004 — ReadStatisticsReset
D001 — ReadTableSnapshots
```

There are seven command kinds, two parameter types and eight inventory
definitions. D002 and an index-statement kind do not exist.

## 13. Validator matrix

The independent frozen-contract test evaluates `8 × 7 × 8 = 448`
ID/kind/SQL combinations. Exactly eight canonical combinations are accepted and
440 are rejected. `ValidateText` is not an authorization path.

## 14. Schema filters

Include and exclude filters use exact ordinal schema names. Both are always
bound as non-null arrays. System exclusions always remove `pg_catalog`,
`information_schema`, `pg_toast*` and `pg_temp_*` before user filters apply.

## 15. TextArray

`TextArray` is the second and only newly authorized parameter type. It binds
ordered, defensive, non-null string arrays as
`NpgsqlDbType.Array | NpgsqlDbType.Text`. No generic value-conversion system was
introduced.

## 16. Ten-column shape

D001 maps exactly ten ordinal columns: schema, table, `relkind`,
`relpersistence`, partition membership, nullable estimated rows, table size,
index size, total size and primary-key state. Only estimated rows may be null.

## 17. Wrong-type sanitization

Every supported wrong CLR type at the ten ordinals is rejected through the
fixed, valueless table-snapshot mapping exception. A failure on any row abandons
the entire read; no partial collection or raw provider exception escapes.

## 18. Relation-state matrix

The mapper treats `relkind`, persistence and partition membership as one joint
state. It accepts exactly 17 states, rejects the remaining 13 of 30, and gives
partition membership precedence over partition-root classification.

## 19. PostgreSQL 15–18 compatibility

The matrix is the union of valid states over supported PostgreSQL 15–18.
PostgreSQL 18 no longer creates unlogged partitioned tables, while PostgreSQL
15–17 could; therefore `p/u/false` and `p/u/true` remain deliberately accepted.
The permanent 15/18 execution matrix remains assigned to GC-DHI-04F.

## 20. PostgreSQL 18 empirical 19-case matrix

The pinned image is `postgres:18.4` at
`sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a`.

| Case | Created | SQLSTATE | `relkind` | Persistence | Partition |
|---|---:|---|---|---|---:|
| Ordinary permanent | yes | `00000` | `r` | `p` | false |
| Ordinary unlogged | yes | `00000` | `r` | `u` | false |
| Ordinary temporary | yes | `00000` | `r` | `t` | false |
| Leaf permanent | yes | `00000` | `r` | `p` | true |
| Leaf unlogged | yes | `00000` | `r` | `u` | true |
| Leaf temporary | yes | `00000` | `r` | `t` | true |
| Partitioned permanent | yes | `00000` | `p` | `p` | false |
| Partitioned temporary | yes | `00000` | `p` | `t` | false |
| Partitioned unlogged attempt | no | `0A000` | — | — | — |
| Subpartition permanent | yes | `00000` | `p` | `p` | true |
| Subpartition temporary | yes | `00000` | `p` | `t` | true |
| Permanent view | yes | `00000` | `v` | `p` | false |
| Temporary view | yes | `00000` | `v` | `t` | false |
| Unlogged view attempt | no | `42601` | — | — | — |
| Materialized view | yes | `00000` | `m` | `p` | false |
| Unlogged materialized-view attempt | no | `0A000` | — | — | — |
| Temporary materialized-view attempt | no | `42601` | — | — | — |
| Foreign table | yes | `00000` | `f` | `p` | false |
| Foreign-table partition | yes | `00000` | `f` | `p` | true |

## 21. Temporary subpartition `p/t/true`

The real fixture creates a temporary partitioned root and a temporary partition
that is itself `PARTITION BY`. `pg_class` reports `p/t/true`; the unchanged
mapper returns `RelationKind.Partition`, `IsPartition = true` and
`IsPartitionedRoot = false`.

## 22. Foreign partition

A real `postgres_fdw` foreign table and foreign-table partition are created in
test-only fixture setup. D001 classifies the former as `ForeignTable` and the
latter as `Partition`, without reading remote business rows.

## 23. Partition sizes

Table, index and total sizes are the three direct PostgreSQL size-function
results for each relation OID. Partition-root sizes are not aggregated from
descendants and no arithmetic identity is imposed between the three values.

## 24. Primary-key mapping

Primary-key state comes only from a correlated `pg_constraint` test for primary
key constraints. Index flags, naming conventions and business rows are not used
as substitutes.

## 25. Ordinal ordering

All mapped rows are returned in deterministic schema/table order using ordinal,
case-sensitive comparison. The result collection and schema filters use
defensive copies.

## 26. Duplicate rejection

Duplicate schema/table identities are rejected fail-closed rather than merged,
overwritten or returned ambiguously.

## 27. Capability-before-D001 composition

The composed inspection path executes the required capability probe before
D001. If C002 reports unavailable catalog/size-function access, D001 is not
executed.

## 28. Permission fixture

A dedicated disposable PostgreSQL fixture revokes `EXECUTE` on
`pg_total_relation_size(regclass)` from both `PUBLIC` and the inspection role.
It proves C002 becomes false, the required-capability failure is generic and
D001 is never reached.

## 29. FDW same-session evidence

The primary negative proof observes `postgres_fdw_get_connections()` on the
same unpooled backend that executes D001. The target-server count remains zero
before and after D001; a real foreign-table read then changes it to one and
reports a valid remote backend PID as the positive control.

## 30. Cancellation and cleanup

Cancellation is covered during command execution and reader disposal. Requested
cancellation propagates without a partial result; cleanup never replaces the
primary failure, while cleanup-only failures still surface. The inspection
transaction remains rollback-only.

## 31. Test counts

Pre-integration validation passed 1504 UnitTests, 13 non-server integration
tests and 100 PostgreSQLServer tests: 1617 total, zero failed and zero skipped.
Release build had zero warnings and zero errors; format verification passed and
no vulnerable or deprecated package was reported.

## 32. Pull request

Pull request `#6`,
`https://github.com/rimch1985-ro/DbHealthInspector/pull/6`, used base `master`
and head `feature/gc-dhi-04d-table-snapshot`. It contained exactly 33 files,
5603 insertions and 73 deletions, with zero conflicts and zero out-of-scope
files.

## 33. Pull-request CI

| Platform | Run | Job | Tests | Build | Result |
|---|---:|---:|---:|---|---|
| Ubuntu | `31422387953` | `93565994145` | 1617 | 0 warnings, 0 errors | SUCCESS |
| Windows | `31422387953` | `93565993989` | 1517 | 0 warnings, 0 errors | SUCCESS |

Ubuntu packed and uploaded the package; Windows passed the bootstrap-only CLI
smoke test. There were no unresolved review threads or findings.

## 34. Merge

| Property | Value |
|---|---|
| Implementation commit | `f60057d9899dc541ea76c584b0af67225b147f5b` |
| Merge commit | `89a74c2e6a57c5ef732f5f46bac7e6a9ccc5e236` |
| First parent | `8fd101bcaf86bad70c2f6dc15ac0d2fc7087fa64` |
| Second parent | `f60057d9899dc541ea76c584b0af67225b147f5b` |
| Strategy | merge commit |

The feature branch was deleted locally and remotely after the merge.

## 35. Master CI

| Platform | Run | Job | Tests | Build | Result |
|---|---:|---:|---:|---|---|
| Ubuntu | `31422585918` | `93566643563` | 1617 | 0 warnings, 0 errors | SUCCESS |
| Windows | `31422585918` | `93566643623` | 1517 | 0 warnings, 0 errors | SUCCESS |

Ubuntu packed and uploaded the canonical merge artifact. Windows passed the
bootstrap-only CLI smoke test.

## 36. Canonical artifact

The only canonical GC-DHI-04D artifact is from master run `31422585918` on the
merge commit.

| Property | Value |
|---|---|
| Name | `dbhealth-bootstrap-package` |
| Artifact ID | `9075942338` |
| Size | 943676 bytes |
| GitHub digest | `sha256:6ff2d0e5eea3e1f458ed9995f73123dc54e28264a3d93a6fea8eb579e5fe5812` |
| Downloaded ZIP SHA-256 | `6FF2D0E5EEA3E1F458ED9995F73123DC54E28264A3D93A6FEA8EB579E5FE5812` |
| Digest equality | true |

The pull-request artifact and any later governance artifact are not canonical.

## 37. Package identity and hash

| Property | Value |
|---|---|
| Filename | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg` |
| Size | 948373 bytes |
| SHA-256 | `357156EB9BD9FC2140EB6F21D55DE29315CC2A7B521B9F788B437F03DCBC5492` |
| ID | `DbHealthInspector.Tool` |
| Identity version | `0.1.0-alpha.0` |
| Package type | `DotnetTool` |
| Command | `dbhealth` |
| License | MIT |
| Repository | `https://github.com/rimch1985-ro/DbHealthInspector` |
| Repository commit | `89a74c2e6a57c5ef732f5f46bac7e6a9ccc5e236` |
| Verified version | `0.1.0-alpha.0+89a74c2e6a57c5ef732f5f46bac7e6a9ccc5e236` |

## 38. Packaged assembly audit

The external reflection harness reported zero new exported PostgreSQL types,
eight statement IDs, seven command kinds, two parameter types, eight inventory
definitions and eight frozen contracts. Inventory order was exactly
B001–B003, C001–C004 and D001. D001 length/hash and the 17/13 mapper matrix
matched the source contract. No D002 or PostgreSQL snapshot-provider
implementation was present.

## 39. Isolated installation

The downloaded canonical package installed successfully into a temporary tool
directory outside the repository. `dbhealth --help` remained bootstrap-only;
`dbhealth --version` returned
`0.1.0-alpha.0+89a74c2e6a57c5ef732f5f46bac7e6a9ccc5e236`. No inspection command,
connection option, table/index CLI, diagnostic or JSON behavior was exposed.

## 40. Security and package-content audit

The package contains `DbHealthInspector.Core.dll`,
`DbHealthInspector.PostgreSql.dll`, `DbHealthInspector.Cli.dll` and
`DotnetToolSettings.xml`. It contains no unit/integration test assembly, xUnit,
Testcontainers, fixture class, relation-state DDL, `postgres_fdw` fixture SQL,
permission fixture SQL, synthetic credential, connection string, TRX, test
result or Docker configuration. Test-only type and marker searches returned
zero hits. The three PostgreSQL size-function names are legitimate productive
C002/D001 content.

## 41. Golden fingerprint

The focused golden-vector test passed and retained:

```text
sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444
```

## 42. Repository state

After merge, local and remote master pointed to
`89a74c2e6a57c5ef732f5f46bac7e6a9ccc5e236` with divergence `0/0`. Pull
request `#6` was merged, open PR count was zero and both feature branch refs
were absent. Governance is recorded by the subsequent documentation-only
commit.

## 43. Exclusions

GC-DHI-04D added no D002, index SQL, index mapping, snapshot provider,
diagnostic rule, DBH001–DBH005 execution, CLI inspection behavior, JSON
reporting, workflow, dependency, tag, release or NuGet publication. GC-DHI-04E
and GC-DHI-04F were not started. PG-06 full completion remains assigned to
GC-DHI-04F.

## 44. Pending final human closure

PG-04 is implemented and integrated. GC-DHI-04D is ready for final human review
but is not yet approved and closed. GC-DHI-04E remains unauthorized.

```text
READY FOR FINAL HUMAN CLOSURE
```
