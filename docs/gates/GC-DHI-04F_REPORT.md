# GC-DHI-04F — Snapshot Provider Composition and PostgreSQL Verification Report

**Status:** APPROVED AND CLOSED  
**Integration date:** 2026-08-19  
**Closure date:** 2026-08-19  
**Final human closure:** APPROVED  
**Backlog coverage:** final composition of PG-01 through PG-05; PG-06 completed  
**Repository:** `rimch1985-ro/DbHealthInspector`

This report records the authorized implementation, integration, canonical
post-merge verification and final human closure of GC-DHI-04F. It closes the
PostgreSQL Metadata Adapter sequence GC-DHI-04A through GC-DHI-04F and marks
PG-06 — Enforce SQL safety allowlist — fully completed. It does not authorize
or implement the Phase 4 diagnostic rules, CLI inspection commands, JSON
reporting, a tag, a GitHub Release or NuGet publication.

## 1. Definition and authorization history

GC-DHI-04F was defined by governance commit
`2bfd5627dccf24d4cb81e5f8966b3b05130331f0` and D1-corrected by
`b952012ed3ced4ef72c9d97039500e0a8c53d0f9`. The corrected definition froze the
public provider surface, one-filter composition, rollback-only capture topology,
whole-millisecond timeout validation, deterministic lock-timeout derivation,
unchanged ten-statement SQL inventory, PostgreSQL 15.18/18.4 matrix and the
final PG-06 completion boundary.

The human project owner subsequently authorized implementation as a separate
phase. Implementation and its corrections remained independently reviewable
until integration authorization was granted.

## 2. Implemented provider surface

The PostgreSQL assembly exports exactly the intended provider in addition to the
pre-existing assembly marker:

```text
DbHealthInspector.PostgreSql.AssemblyMarker
DbHealthInspector.PostgreSql.Snapshots.PostgreSqlDatabaseSnapshotProvider
```

The provider implements Core's existing `IDatabaseSnapshotProvider` and
`IAsyncDisposable`. No public Npgsql type, public PostgreSQL exception, new Core
semantic, CLI inspection command or JSON/reporting contract was introduced.

The provider composes the already approved 04A–04E primitives. One successful
capture returns one complete engine-neutral `DatabaseSnapshot`; every failure or
requested cancellation returns no partial snapshot.

## 3. Capture topology and safety

Every supported capture uses one provider-owned data source, one admitted
capture scope, one connection and one explicit `RepeatableRead`, read-only,
non-deferrable transaction. The transaction has no commit path and always ends
through rollback and asynchronous cleanup.

The execution sequence is the frozen composition:

```text
B001 -> B002 -> B003
C001 -> C002 -> C003 -> conditional C004
D001
E001 -> conditional E002
closure validation -> schema derivation -> DatabaseSnapshot
rollback -> transaction disposal -> connection disposal
```

Unsupported servers retain the definition's complete metadata/capability
snapshot semantics and execute no D001/E001/E002. Optional usage-statistics
loss preserves metadata while returning null reset/counter values rather than
invented zeros.

## 4. Schema filter and composition invariants

One immutable validated `PostgreSqlSchemaFilter` is constructed per provider and
reused for D001 and the E001/E002 composite operation. Include/exclude values
remain exact ordinal names and are bound as `text[]` parameters. Permanent
system-schema exclusions cannot be re-enabled by the caller.

Every `IndexSnapshot` must close against exactly one `TableSnapshot` with the
same ordinal `(SchemaName, TableName)` identity. Schemas are derived from the
validated table collection and the provider materializes deterministic ordinal
ordering before Core performs its defensive copy.

## 5. Lifecycle, cancellation and cleanup

The provider lifecycle admits concurrent captures independently while disposal
waits for all already admitted captures and prevents new admission. Resource
release occurs once and is shared by all concurrent disposers.

Deterministic tests cover cancellation before admission, across the C/D/E
operation boundaries, after the last query, after composition, during cleanup
and after successful cleanup. Requested cancellation retains token identity.
Rollback uses `CancellationToken.None`, primary failures retain EDI precedence,
and cleanup-only failures remain observable without replacing an existing
primary failure.

## 6. Same-session and same-transaction proof

IntegrationTests use test-owned observation seams only. Both pinned PostgreSQL
majors prove that all executed C/D/E operations share one backend/session and
one live transaction. B001 remains the first transaction statement; the test
PID/settings observations do not enter product SQL or the package.

A deterministic two-session barrier fixture proves Repeatable Read visibility:
objects committed after the capture snapshot but before D001/E001 remain absent
from that capture while an out-of-band post-capture observation sees them.
Timing sleeps and productive observation SQL are not used.

## 7. Read-only proof

Both PostgreSQL 15.18 and 18.4 verify effective `repeatable read`,
`transaction_read_only = on`, non-deferrable state and the configured timeout
policy on the capture's own transaction. A role that is otherwise able to write
is rejected with SQLSTATE `25006`; persistent controls remain unchanged and the
pool remains reusable.

## 8. Productive SQL inventory

GC-DHI-04F adds zero productive SQL. The inventory remains exactly:

```text
B001 B002 B003 C001 C002 C003 C004 D001 E001 E002
```

The integrated totals remain:

```text
statement IDs:       10
command kinds:        8
parameter types:      2
inventory definitions: 10
frozen contracts:    10
validator tuples:   800
accepted:             10
rejected:            790
```

The product accepts no raw or caller-provided SQL and executes no business-row
`SELECT`, `COUNT(*)`, `EXPLAIN`, `ANALYZE`, dynamic SQL or
`pg_stat_statements` query.

## 9. PG-06 final acceptance

PG-06's five real acceptance criteria are now satisfied together:

| Criterion | Closure evidence | Result |
|---|---|---|
| Every production SQL resource is inventoried | Exact ten-statement inventory and ten frozen contracts | PASS |
| Prohibited classes and wrong tuples fail closed | Safety tests plus validator matrix: 10 accepted / 790 rejected | PASS |
| No product path accepts user SQL | Restricted typed operation boundary; no raw/user-SQL product API | PASS |
| External schema values are parameterized | One validated schema filter; non-null `text[]` bindings | PASS |
| Safety documentation plus permanent verification is green | Provider/safety design docs, PostgreSQL 15/18 CI and package/leakage audit | PASS |

Therefore:

```text
PG-06: COMPLETED
```

## 10. PostgreSQL compatibility matrix

The permanent matrix uses exactly the pinned images frozen by the definition:

```text
PostgreSQL 18.4
sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a

PostgreSQL 15.18
sha256:6eb0add3b77c081df18aa518ce43df58fdcc40f2e6d868a6fd08038dc7acd425
```

The shared cross-version assertions independently anchor provider semantics to
fixture DDL, frozen 04D/04E contracts and raw catalog observations rather than
comparing one mapper result to another. Expected cross-version differences such
as server-generated OIDs, version text, physical sizes, live counters and
statistics reset timestamps are checked for their own contracts rather than
byte equality.

## 11. Implementation and integration commits

The reviewed feature implementation commit is:

```text
657e1596c1bbc34d592136933abd823df4e89f58
feat(postgresql): compose database snapshot provider
```

Two separately reviewed integration corrections followed without amending,
rebasing or squashing the implementation commit:

```text
0624652fd0d117e03ac72d1bf53e30c21cda852a
fix(ci): correct GC-DHI-04F integration traceability

57b4d3f76a6fb1cf6d84b05c051895f3f4468b77
fix(deps): resolve SSH.NET security advisory
```

The resulting feature history was exactly three commits above baseline
`b952012ed3ced4ef72c9d97039500e0a8c53d0f9`.

## 12. Integration traceability correction

Pull-request checkout intentionally continues to test GitHub's synthetic
`refs/pull/N/merge` result. C1 introduced `CI_SOURCE_REVISION` so PR artifacts
use the canonical feature head for assembly and NuGet provenance instead of the
synthetic merge SHA. On `push`, `CI_SOURCE_REVISION` resolves directly to
`github.sha`.

The Ubuntu packaging job now fails closed unless both conditions are exact:

1. CLI informational version = `0.1.0-alpha.0+CI_SOURCE_REVISION`; and
2. the single `.nuspec` `<repository commit="...">` value =
   `CI_SOURCE_REVISION`.

Checkout semantics, trigger security and the three protected job names were not
weakened.

## 13. SSH.NET security correction

Testcontainers.PostgreSql 4.13.0 resolved a vulnerable SSH.NET dependency. The
reviewed correction centrally pins:

```text
SSH.NET 2026.0.0
```

and references it only from `DbHealthInspector.IntegrationTests` with
`PrivateAssets="all"`. No production project references SSH.NET and no
NuGet-audit or warning suppression was introduced. Normal restore/build/test
validation remained green and the canonical package contains no SSH.NET,
Renci.SshNet or Testcontainers runtime asset.

## 14. Pull request and PR CI

Pull request `#8`, `feat(postgresql): compose GC-DHI-04F snapshot provider`,
used base `master` and head `feature/gc-dhi-04f-snapshot-provider`.

Its final canonical feature head was:

```text
57b4d3f76a6fb1cf6d84b05c051895f3f4468b77
```

PR CI run `32296079038` (#45) completed successfully with all required jobs:

| Job | Evidence | Result |
|---|---|---|
| Ubuntu | build; 1935 UnitTests; 13 non-server; 174 PostgreSQL 18; pack; provenance; upload | PASS |
| PostgreSQL 15 | build; 24 PostgreSQL 15 tests | PASS |
| Windows | build; 1935 UnitTests; 13 non-server; CLI smoke | PASS |

Every test partition reported zero failed and zero skipped tests. Builds
reported zero warnings and zero errors.

The PR checkout tested synthetic merge SHA
`49eca81822a568790d0638d16f26cb8e7f80ec8c`, while package provenance correctly
used feature head `57b4d3f76a6fb1cf6d84b05c051895f3f4468b77`.

## 15. Merge

PR #8 was merged with an explicit merge commit:

```text
1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177
```

Its parents are exactly:

```text
first:  b952012ed3ced4ef72c9d97039500e0a8c53d0f9
second: 57b4d3f76a6fb1cf6d84b05c051895f3f4468b77
```

No squash, rebase or force push was used. GitHub verified the merge commit
signature. The merge introduced no additional file delta beyond the feature
head.

## 16. Canonical master CI

The `push` to `master` triggered canonical CI run:

```text
run number: 46
run ID:     32297138214
head SHA:   1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177
conclusion: success
```

Jobs:

| Job | Job ID | Canonical result |
|---|---:|---|
| Ubuntu | `96210964615` | SUCCESS |
| PostgreSQL 15 | `96210965000` | SUCCESS |
| Windows | `96210965118` | SUCCESS |

Canonical counts:

```text
Ubuntu build:             0 warnings / 0 errors
Ubuntu UnitTests:         1935 passed / 0 failed / 0 skipped
Ubuntu non-server:          13 passed / 0 failed / 0 skipped
PostgreSQL 18:             174 passed / 0 failed / 0 skipped
PostgreSQL 15:              24 passed / 0 failed / 0 skipped
Windows build:            0 warnings / 0 errors
Windows UnitTests:         1935 passed / 0 failed / 0 skipped
Windows non-server:          13 passed / 0 failed / 0 skipped
Windows CLI smoke:        PASS
```

## 17. Canonical provenance

The master runner checked out
`1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177`, and
`CI_SOURCE_REVISION` resolved to that same canonical SHA.

The provenance gate proved:

```text
CLI expected:
0.1.0-alpha.0+1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177

CLI actual:
0.1.0-alpha.0+1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177

nuspec repository commit expected:
1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177

nuspec repository commit actual:
1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177
```

## 18. Canonical artifact

Only the artifact produced by master CI run `32297138214` is canonical for this
gate:

| Field | Value |
|---|---|
| Name | `dbhealth-bootstrap-package` |
| Artifact ID | `9381656515` |
| Size | 973024 bytes |
| GitHub digest | `sha256:367da3484178f003432898f9a58e7d6a475efa2fc5da094cbcc0e60aeafcf890` |
| Downloaded ZIP SHA-256 | `367da3484178f003432898f9a58e7d6a475efa2fc5da094cbcc0e60aeafcf890` |

The independently downloaded ZIP digest matches GitHub exactly.

## 19. Canonical NuGet package

The artifact contains exactly one package:

| Field | Value |
|---|---|
| Filename | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg` |
| Size | 977570 bytes |
| SHA-256 | `36F56758865227B2C8C873E4D9BD1922D46D257A47DAE5CFF287C598A69D2197` |
| ID | `DbHealthInspector.Tool` |
| Identity version | `0.1.0-alpha.0` |
| Repository commit | `1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177` |
| Verified tool version | `0.1.0-alpha.0+1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177` |

## 20. Package and leakage audit

The canonical package contains the intended tool payload only. Production
runtime dependency inspection found no SSH.NET, Renci.SshNet, Testcontainers or
xUnit dependency. No test assembly, test result, fixture credential, connection
string, Docker/Testcontainers asset or test-owned observation SQL is packaged.

Public XML contract files for Core, PostgreSql and CLI remain byte-identical to
the already reviewed PR artifact. The merge changed provenance to the canonical
master SHA without introducing a new public-product contract.

## 21. Isolated canonical installation

The human project owner independently verified the exact canonical `.nupkg` on
Windows after downloading it from the audited master artifact.

SHA-256 verification returned exactly:

```text
36F56758865227B2C8C873E4D9BD1922D46D257A47DAE5CFF287C598A69D2197
```

Installation from a temporary local NuGet source succeeded:

```text
DbHealthInspector.Tool 0.1.0-alpha.0 installed successfully
command: dbhealth
```

`dbhealth --version` returned:

```text
0.1.0-alpha.0+1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177
```

`dbhealth --help` remained bootstrap-only and exposed only help/version options;
it did not expose `inspect`, connection, schema, diagnostic or reporting
commands. The isolated tool and temporary package source were removed after the
verification.

## 22. Branch protection

`master` remains protected. The required GitHub Actions contexts are exactly:

```text
Ubuntu
Windows
PostgreSQL 15
```

No branch-protection mutation was performed during closure verification.

## 23. Security and publication state

The integration introduced no security-warning suppression, audit disablement,
raw-SQL escape hatch, write path, release action or publication.

At closure:

```text
tag created:          NO
GitHub Release:       NO
NuGet publication:   NO
force push:           NO
rebase/squash:        NO
branch protection change: NO
```

Release engineering remains a separately authorized future activity.

## 24. Definition exit criteria

All seventeen implementation exit criteria from
`GC-DHI-04F_DEFINITION.md` are satisfied by the reviewed implementation,
PostgreSQL matrix, PR CI, master CI, package/provenance audit, isolated
installation, PG-06 final safety verification, Codex integration review and
human closure approval.

No Core semantic change, diagnostic-rule implementation, CLI expansion,
JSON/reporting implementation, tag, release or NuGet publication occurred.

## 25. Final gate verdict

```text
GC-DHI-04F: APPROVED AND CLOSED
PG-06: COMPLETED
PostgreSQL Metadata Adapter (GC-DHI-04A through GC-DHI-04F): COMPLETED
```

The next functional work belongs to Phase 4 — Diagnostic Rules. Its technical
gate and implementation require separate definition and human authorization.
This closure does not authorize implementation of DBH001–DBH005.

## 26. Governance integration boundary

This report and the corresponding `PROJECT_STATE.md` update are documentation
only. They change no `src/**`, test, workflow, dependency, SQL, package,
branch-protection, tag, release or publication state.

The closure becomes canonical when the governance pull request containing this
report and the state update is integrated into `master` after its required CI
checks pass.
