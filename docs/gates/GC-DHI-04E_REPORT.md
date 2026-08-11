# GC-DHI-04E — Index Snapshot Query and Mapping Integration Report

**Status:** READY FOR FINAL HUMAN CLOSURE  
**Integration date:** 2026-08-10  
**Backlog item:** PG-05 — Implement index snapshot query  
**Repository:** `rimch1985-ro/DbHealthInspector`

This report records the authorized implementation and integration of
GC-DHI-04E. It does not close the gate and does not authorize or start
GC-DHI-04F.

## 1. Definition and authorization history

The gate was defined in `docs/gates/GC-DHI-04E_DEFINITION.md` by governance
commit `f23ffabbfc6d2a0812b56de452311a3916e433fa`. CI run `31437692456` passed on
Ubuntu and Windows.

Human review identified D1-01: operator-class parameters from ordered
`pg_attribute.attoptions` could not be discarded without weakening structural
index identity. Governance commit
`fa7f2eebecebb6230669c715b3f9b4e4ae9552ec` corrected the definition by
freezing a typed-array seam and an injective, ordered encoding in the existing
Core `OperatorClass` string. CI run `31439303451` passed on Ubuntu and Windows.

The corrected definition then received human approval and PG-05 implementation
authorization. Integration remained separately gated until Codex review and
human integration authorization.

## 2. Implementation history

Claude Code completed the authorized work in three local delivery phases:

1. `IMPLEMENT-1` established the E001/E002 inventory, typed execution boundary,
   C002 capability expansion and provider-neutral row-reader support.
2. `IMPLEMENT-2` completed index-row mapping, immutable query results,
   structural identity, ordering, validation, error surfaces and focused unit
   coverage.
3. `IMPLEMENT-3` completed real PostgreSQL 18.4 scenarios, optional statistics,
   permission-loss evidence, regression coverage and synchronized design
   documentation.

The three phases remained an uncommitted local candidate until review was
complete.

## 3. Codex R1, Claude C1 and Codex R2

Codex R1 found five functional issues:

| Finding | R1 evidence | Final disposition |
|---|---|---|
| R1-01 | Final duplicate identity was not global across the whole E001 result | RESOLVED |
| R1-02 | E002 semantic reconciliation could fail after reader disposal | RESOLVED |
| R1-03 | SQL `NULL` and non-null blank text were not always distinct | RESOLVED |
| R1-04 | The real inverse stored `attoptions` order lacked complete proof | RESOLVED |
| R1-05 | The permission fixture did not yet prove the pre/post causal transition | RESOLVED |

Claude C1 corrected all five without expanding scope. Codex R2 independently
confirmed the global `(SchemaName, IndexName)` duplicate check, pre-disposal
E002 reconciliation, NULL-versus-blank semantics, inverse ordered `attoptions`
evidence and the C002 true-to-false permission transition. R2 found no new
functional issue.

Codex made comment-only clarifications in six candidate files:

- `PostgreSqlInspectionOperationExecutor.cs`
- `PostgreSqlSqlInventory.cs`
- `PostgreSqlSqlParameterType.cs`
- `PostgreSqlSqlSafetyValidator.cs`
- `PostgreSqlSchemaFilter.cs`
- `PostgreSqlSqlFrozenStatementContractTests.cs`

The reviewed diffs confirmed that these six corrections changed no executable
behavior.

## 4. Candidate inventory

The authorized candidate contained exactly 31 files:

| Classification | Files |
|---|---:|
| Production | 15 |
| Unit tests | 8 |
| Integration tests and support | 7 |
| Design documentation | 1 |
| Total | 31 |

The integrated diff contained 6278 insertions and 121 deletions. Core, CLI,
the PostgreSQL connection boundary, workflows, dependencies, build
configuration and pre-existing governance documents were outside the
functional delta.

## 5. Scope and architecture

PG-05 adds internal PostgreSQL adapter behavior for reading and mapping index
metadata. It does not add a snapshot-provider implementation or compose a
`DatabaseSnapshot`. No diagnostic rule, DBH003–DBH005 behavior, CLI inspection
command, JSON reporting, PostgreSQL 15 CI matrix or GC-DHI-04F implementation
was introduced.

All new PostgreSQL implementation types remain internal. The sole exported
PostgreSQL type remains the pre-existing assembly marker, so new exported
PostgreSQL types equal zero. Core contains no PostgreSQL dependency.

## 6. Productive SQL inventory

The productive SQL inventory is exactly:

```text
B001
B002
B003
C001
C002
C003
C004
D001
E001
E002
```

The packaged assembly contains exactly ten statement IDs, eight command kinds,
two SQL parameter types, ten inventory definitions and ten frozen contracts.
D002 and E003 are absent.

## 7. Frozen SQL contracts

The normative definition text, Release assembly and packaged assembly agree:

| Statement | Length | SHA-256 |
|---|---:|---|
| C002 | 2027 | `777cb44afb178c299566f1a8c0251e3ab9ba47480bd578b6a339f4d1c24c5a90` |
| D001 | 1816 | `13b4e88d7ac0053d87cf760b3e6a64ae879effa91de66a15bd693ba458680b87` |
| E001 | 6262 | `d45b8ed1e0d842b1474839a3beadf6d1a0d4233cfa847c3887c41cfd4b1184d7` |
| E002 | 737 | `fe8f23a5dff2cdfb8d08acf4fb7f7a3f90aef4b7e9eee4b678cde8c260624919` |

B001–B003, C001, C003 and C004 remained byte-identical.

## 8. Validator contract

An external reflection harness executed every packaged ID/kind/SQL tuple:

```text
10 statement IDs × 8 command kinds × 10 canonical SQL texts = 800
accepted: 10
rejected: 790
```

The ten accepted tuples are exactly the ten canonical definitions. Every other
combination failed closed.

## 9. E001 and E002 shapes

E001 is the required structural index query and has exactly 31 typed columns,
one row per index attribute. E002 is the optional usage-statistics query and
has exactly four scalar columns, one row per observed physical index. E001 can
produce complete structural `IndexSnapshot` values when usage statistics are
unavailable; in that state `ScanCount` is `null`.

C002 retains its statement identity and expands only for the four functions
required by E001. C003 remains byte-identical and continues to protect optional
usage-statistics access.

## 10. Typed provider-neutral seam

`GetStringArray` was added to the provider-neutral row-reader seam. Npgsql owns
the provider-specific conversion; the mapper consumes a typed ordered array.
Wrong provider type, SQL `NULL` where prohibited and invalid content are
translated to fixed non-sensitive failures.

This seam preserves the exact order of `pg_attribute.attoptions` without
exposing Npgsql types to mapping contracts or Core.

## 11. Mapping and structural identity

The mapper preserves:

- ordered key parts and ordered INCLUDE columns;
- simple column keys versus expression keys;
- canonical partial predicates;
- exact access-method names;
- schema-qualified, search-path-independent collations and operator classes;
- injective ordered operator-class option encoding;
- uniqueness, `NULLS NOT DISTINCT`, primary-key and constraint state;
- independent valid, ready and live flags;
- ordinal case-sensitive identities and final ordering.

SQL `NULL` denotes absence. Non-null blank text is invalid and is never
collapsed into absence. Valid strings are preserved without trimming.

## 12. Grouping, duplicates and ordering

Raw E001 groups use `(SchemaName, TableName, IndexName)`. Completed final
indexes are globally unique by `(SchemaName, IndexName)` across the entire
read, not merely adjacent groups. The negative control
`(a,t1,shared)`, `(a,t2,other)`, `(a,t3,shared)` fails with the fixed mapping
message before reader disposal. Same index names in different schemas and
ordinal case differences remain distinct.

The final collection is ordered ordinally by schema, table and index; key parts
are ordered by position and INCLUDE columns preserve catalog attribute order.

## 13. Optional statistics reconciliation

When C003 is available, E002 maps the exact non-negative `idx_scan` value.
Missing physical statistics never invent zero. Virtual partitioned index roots
retain `ScanCount = null`.

Negative counts, duplicate statistics, unmatched identities and statistics for
virtual indexes are detected inside the reader-protected region. The
post-disposal merge is mechanical. Primary mapping failures retain precedence
over cleanup failures; cleanup-only failures remain observable.

## 14. PostgreSQL evidence

Real integration testing used only:

```text
postgres:18.4
sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a
```

Evidence covered ordinary and partitioned indexes, index partitions,
multicolumn keys, INCLUDE, expressions, predicates, collations, access methods,
operator classes and ordered options, unique/constraint state, invalid roots,
scan counts `0`, `>0` and `null`, C003 degradation and exact required-function
permission loss. Physical `relkind i` uses direct `pg_relation_size`; virtual
`relkind I` uses size zero and null scan count.

## 15. Pre-integration validation

| Validation | Result |
|---|---|
| Restore | Passed |
| Release build | 0 warnings, 0 errors |
| Unit tests | 1831 passed, 0 failed, 0 skipped |
| Non-server integration tests | 13 passed, 0 failed, 0 skipped |
| PostgreSQLServer integration tests | 152 passed, 0 failed, 0 skipped |
| Local total | 1996 passed |
| Format verification | Passed |
| Vulnerable packages | None |
| Deprecated packages | None |
| `git diff --check` | Passed |

R1-01 through R1-05 were `RESOLVED`; no functional finding remained open.

## 16. Branch and implementation commit

Branch `feature/gc-dhi-04e-index-snapshot` was created from
`fa7f2eebecebb6230669c715b3f9b4e4ae9552ec`. Exactly the approved 31 files were
staged; no unstaged or unrelated untracked file remained.

The implementation commit is:

```text
e50442baea7130a094584e5c5024fb92894f95ab
feat(postgresql): add index snapshot query
```

Its only parent is the authorized baseline
`fa7f2eebecebb6230669c715b3f9b4e4ae9552ec`.

## 17. Pull request and PR CI

Pull request `#7`, `feat(postgresql): add GC-DHI-04E index snapshot query`, used
base `master` and head `feature/gc-dhi-04e-index-snapshot`. It was not a draft,
had no auto-merge request, contained exactly 31 files and was mergeable cleanly.

PR CI run `31454410407` completed successfully:

| Platform | Job | Tests | Build | Additional result |
|---|---:|---:|---|---|
| Ubuntu | `93665147247` | 1831 + 13 + 152 = 1996 | 0 warnings, 0 errors | Pack and artifact upload passed |
| Windows | `93665147207` | 1831 + 13 = 1844 | 0 warnings, 0 errors | CLI smoke passed |

All test partitions reported zero failures and zero skipped tests.

## 18. Merge and branch cleanup

PR `#7` was merged with an explicit merge commit:

```text
f78720891766f831e1fd7d46a68c2aef9dbb83f2
```

Its parents are exactly:

```text
first:  fa7f2eebecebb6230669c715b3f9b4e4ae9552ec
second: e50442baea7130a094584e5c5024fb92894f95ab
```

No squash or rebase was used. The local and remote feature branches were
deleted after verifying the merge and no residual feature ref remained.

## 19. Master CI

Master CI run `31454525066` is tied to the merge SHA and completed successfully:

| Platform | Job | Tests | Build | Additional result |
|---|---:|---:|---|---|
| Ubuntu | `93665473589` | 1831 + 13 + 152 = 1996 | 0 warnings, 0 errors | Pack and artifact upload passed |
| Windows | `93665473508` | 1831 + 13 = 1844 | 0 warnings, 0 errors | CLI smoke passed |

All test partitions reported zero failures and zero skipped tests.

## 20. Canonical artifact

Only the artifact produced by master CI for the merge SHA is canonical:

| Field | Value |
|---|---|
| Name | `dbhealth-bootstrap-package` |
| Artifact ID | `9087542583` |
| Size | 964597 bytes |
| GitHub digest | `sha256:89d76672dee68b32ee54ad4c2ef7c5747bd61d199f9b17775d5ffa048a552193` |
| Downloaded ZIP SHA-256 | `89d76672dee68b32ee54ad4c2ef7c5747bd61d199f9b17775d5ffa048a552193` |

The independently downloaded ZIP digest matches GitHub exactly. Neither the PR
artifact nor a later governance artifact is canonical for GC-DHI-04E.

## 21. Canonical NuGet package

The canonical artifact contains exactly one package:

| Field | Value |
|---|---|
| Filename | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg` |
| Size | 969244 bytes |
| SHA-256 | `D6CCDD2D2AF3EFCD750BAA2BB95F7FB698720F4203C0A2A9C84D8EBE892D7257` |
| ID | `DbHealthInspector.Tool` |
| Identity version | `0.1.0-alpha.0` |
| Package type | `DotnetTool` |
| Command | `dbhealth` |
| License | MIT |
| Repository | `https://github.com/rimch1985-ro/DbHealthInspector` |
| Repository commit | `f78720891766f831e1fd7d46a68c2aef9dbb83f2` |
| Verified tool version | `0.1.0-alpha.0+f78720891766f831e1fd7d46a68c2aef9dbb83f2` |

The package was not published.

## 22. Package, assembly and leakage audit

The package contains `DbHealthInspector.Core.dll`,
`DbHealthInspector.PostgreSql.dll`, `DbHealthInspector.Cli.dll` and
`DotnetToolSettings.xml`. It contains no test assembly, xUnit, Testcontainers,
fixture file, TRX, Docker configuration, synthetic credential, permission role,
GRANT/REVOKE fixture SQL, index-zoo DDL, business-row fixture data or
negative-control probe.

An external harness verified the packaged assemblies directly:

```text
new exported PostgreSQL types: 0
statement IDs: 10
command kinds: 8
parameter types: 2
inventory definitions: 10
frozen contracts: 10
snapshot-provider implementations: 0
inspection-rule implementations: 0
```

UTF-8 and UTF-16 scans found zero hits for every required test-only marker,
including the permission fixture, inverse-option zoo, synthetic marker,
`pg_stat_force_next_flush`, Testcontainers and xUnit. Legitimate productive SQL
contains the expected PostgreSQL function names.

## 23. Golden fingerprint and isolated installation

The focused golden-vector test passed and retained:

```text
sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444
```

An isolated installation from the canonical package succeeded.
`dbhealth --version` returned the exact verified version for the merge SHA.
`dbhealth --help` retained bootstrap-only behavior and exposed exactly the
existing help and version options; it added no connection, inspect, tables,
indexes, diagnostics or JSON option.

## 24. Public-release audit

At integration time:

```text
local tags: 0
remote tag refs: 0
GitHub releases: 0
public NuGet package: absent (HTTP 404)
publication performed: none
```

No tag, release or NuGet publication was created.

## 25. Governance and final repository requirements

This report and `docs/agent-governance/PROJECT_STATE.md` are the only authorized
governance changes after the verified merge. They record integration, not final
closure. The governance commit is required to use:

```text
docs(governance): record GC-DHI-04E integration
```

Its CI must pass on Ubuntu and Windows. Any artifact from that documentation-only
commit does not replace the canonical merge artifact.

## 26. Backlog status

```text
PG-05: implemented and integrated
GC-DHI-04E: implemented and integrated; ready for final human closure
GC-DHI-04E: NOT YET CLOSED
PG-06 full completion: remains assigned to GC-DHI-04F
GC-DHI-04F: unauthorized, unimplemented and not started
```

## 27. Gate verdict and next action

```text
READY FOR FINAL HUMAN CLOSURE
PG-05 IMPLEMENTED AND INTEGRATED
GC-DHI-04E NOT YET FINALLY CLOSED
GC-DHI-04F UNAUTHORIZED
```

Await final human review and closure of GC-DHI-04E.  
GC-DHI-04F remains unauthorized.

## 28. Mandatory declaration

GC-DHI-04E was integrated through a reviewed pull request and verified by green CI.  
PG-05 was implemented and integrated within the authorized GC-DHI-04E scope.  
The productive SQL inventory contains exactly B001, B002, B003, C001, C002, C003, C004, D001, E001 and E002.  
The GC-DHI-04E validator contract remains exactly 10 accepted and 790 rejected combinations out of 800.  
GC-DHI-04E is not yet finally closed.  
GC-DHI-04F was not started.  
No snapshot provider, diagnostic rule, CLI behavior or JSON reporting was added.  
No tag, release or NuGet publication was performed.
