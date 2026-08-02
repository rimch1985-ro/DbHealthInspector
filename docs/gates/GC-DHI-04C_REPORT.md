# GC-DHI-04C — Integration Report

**Gate:** GC-DHI-04C — Server Metadata and Capability Probe  
**Backlog item:** PG-03  
**Integration authorization date:** 2026-08-01  
**Repository:** `rimch1985-ro/DbHealthInspector`  
**Verdict:** `READY FOR FINAL HUMAN CLOSURE`

---

## 1. Objective

Integrate the approved GC-DHI-04C candidate through a reviewed pull request,
multiplatform CI, an explicit merge commit, audit of the merge artifact and an
isolated tool installation, without starting GC-DHI-04D.

## 2. Backlog coverage

`PG-03 — Implement server capability probe` is implemented and integrated.
GC-DHI-04C is not yet finally closed.

## 3. Definition and authorization

The normative definition is `docs/gates/GC-DHI-04C_DEFINITION.md`. Human
authorization covered only PG-03, C001–C004 and the integration workflow. It
did not authorize GC-DHI-04D–04F, releases, tags or NuGet publication.

## 4. Implementation commit

`55bb7b93b2a21c5bd24ded21f9df7b5e881c10c5`

The commit message is `feat(postgresql): add server capability probe`. Its
parent is `6d1b044aace4567335defbeff17c97a27b97c315`.

## 5. Files integrated

The candidate contains exactly 36 files, 5262 insertions and 217 deletions:

| Category | Files | Added | Deleted |
|---|---:|---:|---:|
| Production | 14 | 1034 | 92 |
| Unit tests | 13 | 2328 | 104 |
| Integration tests | 8 | 1408 | 21 |
| Documentation | 1 | 492 | 0 |

No project, package, workflow, Core, CLI or connection-boundary file changed.

## 6. Architecture

The probe is internal to `DbHealthInspector.PostgreSql`, maps into existing Core
capability and metadata contracts, and preserves dependency direction
`PostgreSql → Core`. The CLI remains bootstrap-only.

## 7. Internal types

The implementation adds internal probe, identity, result, version normalizer,
support-status and required-catalog exception types. Package reflection found
only the preexisting exported `DbHealthInspector.PostgreSql.AssemblyMarker`;
no new exported PostgreSQL type was introduced.

## 8. Version normalization

The probe reads numeric `server_version_num`. The single normalizer handles
pre-10 and 10-or-newer encodings and derives the normalized version, major and
support state without parsing display text.

## 9. Supported-version policy

PostgreSQL majors 15 through 18 are `Supported`. Other structurally valid
majors are represented as `Unsupported`. Frozen vectors include 18.4, 19.0 and
9.6.24.

## 10. Capability policy

- `CatalogMetadata` is required; absence stops the probe with a sanitized
  exception.
- `UsageStatistics` is optional and degrades to unavailable.
- `DataProfiling` is disabled by policy.
- Exactly one state is emitted for each capability.

## 11. SQL inventory

The productive inventory contains exactly:

| ID | Statement |
|---|---|
| B001 | SetTransactionReadOnly |
| B002 | ApplyLocalTimeouts |
| B003 | VerifySessionState |
| C001 | ReadServerIdentity |
| C002 | CheckCatalogMetadataAccess |
| C003 | CheckUsageStatisticsAccess |
| C004 | ReadStatisticsReset |

Package reflection confirmed seven IDs, six command kinds and seven inventory
definitions. There is no eighth statement.

## 12. Frozen validator contract

Seven frozen contracts jointly require the exact statement ID, command kind,
ordinal SQL, parameter count, positions and types. The exhaustive matrix has
294 combinations: exactly seven accepted and 287 rejected. No productive caller
uses `ValidateText` as an execution authorization path.

## 13. Typed operation boundary

C001–C004 execute only through typed operation methods. There is no public or
internal caller-facing raw-SQL method, no arbitrary statement registration and
no operational callback dispatch by statement ID.

## 14. Result invariants

`PostgreSqlServerProbeResult` revalidates the PostgreSQL engine, normalized
engine version, derived major and support state through the single version
normalizer. It owns no resource, cache, mutable static state or exception.

## 15. Row-shape contracts

C001 requires exactly version number, database name and current user. C002 and
C003 require one Boolean. C004 accepts one nullable UTC timestamp. Missing,
extra, null-for-required and type-mismatched values fail closed.

## 16. C004 `42501`

C004 runs only after C003 reports true. A server-side `42501` race during C004
degrades usage statistics to unavailable using a fixed neutral reason; other
exceptions retain their normal propagation semantics. The race is unit-tested.

## 17. Cancellation

The exact cancellation token crosses the session, probe, executor, gateway and
reader layers. Requested cancellation propagates and the rollback-only session
still performs cleanup.

## 18. Sanitization and leakage

Failure messages are fixed and neutral. Tests cover message, `ToString`, stack,
`Data`, inner exception, capability reasons, result rendering, fields,
delegates and closures without rendering marker values in assertion failures.

## 19. Normal PostgreSQL evidence

Using `postgres:18.4` at
`sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a`,
the real sequence was:

```text
B001 B002 B003 C001 C002 C003 C004
```

C003 was observed as `true`, C004 was observed, and usage statistics were
reported available.

## 20. Permission-loss evidence

The dedicated role is `NOSUPERUSER`, has no `pg_monitor` or
`pg_read_all_stats` membership, and has both PUBLIC and direct statistics
privileges revoked while required catalog access remains available. The real
sequence was:

```text
B001 B002 B003 C001 C002 C003
```

C003 was observed as `false`, C004 was absent, and usage statistics were
reported unavailable.

## 21. Fixture topology and deadlines

Normal and revoked scenarios use separate one-fixture collections with
parallelization disabled. Empirical execution showed at most one concurrent
container and zero leftovers. Initialization has a 120-second linked deadline;
failed-init cleanup and revoked test bodies each have independent 30-second
deadlines.

## 22. Failed-init cleanup

Primary failures are captured with `ExceptionDispatchInfo`, cleanup is
attempted immediately, and the same primary is rethrown. Cleanup failures never
replace it. Timed-out cleanup tasks have their eventual exceptions observed;
ownership is cleared before disposal, preventing a second owner or double
disposal.

## 23. Unit-test evidence

Local and CI execution completed 1243 unit tests with zero failures and zero
skipped. Test listing reports 1238 entries; one `MemberData` entry expands into
six runtime cases, explaining the net difference of five.

## 24. Integration-test evidence

Ubuntu completed 13 non-server integration tests and 30 PostgreSQLServer tests.
The non-server group contains one bootstrap and twelve lifecycle tests. Windows
completed the 13 non-server tests; server tests are Ubuntu-only by workflow
design.

## 25. GC-DHI-04B regression

B001-first ordering, `RepeatableRead`, explicit read-only state, all timeouts,
rollback-only cleanup, SQLSTATE `25006`, cancellation, exact `||`, placeholder
rules and zero public operational API remain covered and passed.

## 26. Pull request evidence

- Pull request: `#5`
- URL: `https://github.com/rimch1985-ro/DbHealthInspector/pull/5`
- Base: `master`
- Head: `feature/gc-dhi-04c-capability-probe`
- Files: 36
- Conflicts: none
- Review threads/comments: none
- CI run: `30731960075`
- Ubuntu job: `91453798149`
- Windows job: `91453798196`

Ubuntu passed 1286 tests, pack and artifact upload. Windows passed 1256 tests
and the CLI smoke test. Both builds had zero warnings and zero errors.

## 27. Merge evidence

- Method: merge commit
- Merge commit: `6d6772cf59c711ca522902b6135223e0ca00c6a1`
- First parent: `6d1b044aace4567335defbeff17c97a27b97c315`
- Second parent: `55bb7b93b2a21c5bd24ded21f9df7b5e881c10c5`
- Timestamp: `2026-08-02T04:14:10Z`

The feature branch was deleted locally and remotely.

## 28. Master CI

Run `30732019651` validated the exact merge commit:

| Platform | Job | Passed | Failed | Skipped | Build |
|---|---:|---:|---:|---:|---|
| Ubuntu | 91453966554 | 1286 | 0 | 0 | 0 warnings, 0 errors |
| Windows | 91453966613 | 1256 | 0 | 0 | 0 warnings, 0 errors |

Ubuntu packed and uploaded the artifact; Windows passed CLI smoke.

## 29. Artifact audit

- Name: `dbhealth-bootstrap-package`
- Artifact ID: `8828280555`
- Size: 931378 bytes
- GitHub digest:
  `sha256:dbc0cd2bb489868807245aeeb17583429add5866f936ef0eda16d4a3ea6acd46`
- Downloaded ZIP SHA-256:
  `DBC0CD2BB489868807245AEEB17583429ADD5866F936EF0EDA16D4A3EA6ACD46`

The digest and downloaded ZIP hash match.

## 30. Package audit

- Filename: `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg`
- Size: 936051 bytes
- SHA-256:
  `F5A15EE3608E6D86C13351A60F4CEC6B621F534645DA86B88DAC976881FF51F1`
- ID/identity: `DbHealthInspector.Tool` / `0.1.0-alpha.0`
- Verified version:
  `0.1.0-alpha.0+6d6772cf59c711ca522902b6135223e0ca00c6a1`
- Type/command: `DotnetTool` / `dbhealth`
- License: MIT
- Repository: `rimch1985-ro/DbHealthInspector`
- Repository commit: exact merge commit

The package contains Core, PostgreSql, CLI and `DotnetToolSettings.xml` and was
installed only into an isolated temporary tool path. `--help` remained
bootstrap-only and exposed neither PostgreSQL inspection nor connection
options. All temporary audit files were removed.

## 31. Security verification

The artifact contains no unit/integration test assemblies, Testcontainers,
xUnit, test fixtures, recording gateway, lifecycle helper, fixture SQL, Docker
configuration, synthetic fixture credentials, markers, connection strings,
TRX or test results. The packaged inventory contains only B001–B003 and
C001–C004. No public PostgreSQL API was added.

The golden fingerprint remains:

```text
sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444
```

## 32. Scope exclusions

No table query, index query, snapshot provider, diagnostic rule, CLI behavior,
JSON reporting, dependency, project or workflow change was included. No tag,
release or NuGet publication was performed.

## 33. Deferred work

GC-DHI-04D through GC-DHI-04F remain unauthorized, undefined for
implementation, unimplemented and not started. GC-DHI-04D requires a separate
human authorization after final closure of GC-DHI-04C.

## 34. Integration verdict

PG-03 implemented and integrated.  
GC-DHI-04C not yet finally closed.  
GC-DHI-04D remains unauthorized.

```text
READY FOR FINAL HUMAN CLOSURE
```
