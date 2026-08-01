# GC-DHI-04A — Connection Boundary and Secret Hygiene Integration Report

## 1. Scope

GC-DHI-04A completes backlog item PG-01 by adding an internal PostgreSQL
connection boundary. The integrated scope is limited to connection-string
validation and normalization, sanitized metadata, asynchronous connection
opening, cancellation, exception translation, ownership and disposal.

## 2. Architecture

The dependency direction remains `DbHealthInspector.PostgreSql →
DbHealthInspector.Core`. Core has no PostgreSQL or infrastructure dependency.
All connection-boundary types are internal and no public PostgreSQL API was
added.

## 3. Files

The implementation commit contains exactly 20 files:

- 7 production files under `src/DbHealthInspector.PostgreSql/`.
- 12 test files including the UnitTests project reference.
- 1 design document.

No Core, CLI, IntegrationTests, dependency, CI, ADR, backlog, gate-definition or
security-policy file was changed by the implementation commit.

## 4. Review history R1/C1/R2

Claude Code implemented GC-DHI-04A. Codex performed R1 and returned findings on
the shared exception filter, network-dependent UnitTests, incomplete behavioral
matrices and an absolute documentation claim. Claude Code performed
GC-DHI-04A-C1. Codex completed R2, applied only Low-severity test/documentation
corrections and approved the candidate for human integration review. Human
integration authorization was recorded on `2026-07-31`.

## 5. Resolved findings

| Finding | Resolution |
|---|---|
| F-01 — shared exception filter | Replaced by stage-specific handling |
| F-02 — required loopback I/O | Replaced by a deterministic productive opener seam |
| F-03 — incomplete matrices | Cancellation, Options, normalization, TargetKind and propagation matrices completed |
| F-04 — absolute retention statement | Corrected to recognize Npgsql private configuration retention |
| R2 Low corrections | Fake retention removed, TimeoutException covered, documentation restored to 17 sections |

No review finding remains open.

## 6. Connection-string policy

`PostgreSqlConnectionStringPolicy` rejects null, blank and malformed input.
Expected parser failures are translated to a fixed, generic
`ArgumentException` with `ParamName=connectionString`, no inner exception, empty
`Data` and no original input.

## 7. Options rejection

Absent and empty `Options` values are accepted. Any non-empty value, including
quoted whitespace or PostgreSQL session parameters, is rejected. Npgsql's real
case-insensitive and last-wins parsing behavior is tested without adding a
manual alias parser.

## 8. Security normalization

| Setting | Effective value |
|---|---|
| PersistSecurityInfo | `false` |
| IncludeErrorDetail | `false` |
| LogParameters | `false` |
| IncludeFailedBatchedCommand | `false` |
| NoResetOnClose | `false` |
| Enlist | `false` |
| Multiplexing | `false` |
| ApplicationName | `DbHealthInspector` |

The normalized builder connection string is the exact value used to construct
the data source.

## 9. Metadata allowlist

`PostgreSqlConnectionMetadata` contains exactly five get-only properties:
`TargetKind`, `Port`, `SslMode`, `Pooling` and `ConnectionTimeoutSeconds`. It
does not retain host, database, username, password, passfile, certificate paths,
builder or data source references.

## 10. Data-source ownership

Each factory owns exactly one `NpgsqlDataSource`. The factory does not open a
connection during creation and does not store connections returned to callers.
Each returned `NpgsqlConnection` belongs to its caller.

## 11. Productive opener seam

The normal factory path always uses `NpgsqlDataSourceConnectionOpener.Default`,
which delegates directly to `NpgsqlDataSource.OpenConnectionAsync` with the
same cancellation token. The injectable overload remains internal. Tests use a
deterministic fake through the same production call path; there is no test-only
branch.

## 12. Cancellation

Pre-canceled requested tokens and associated cancellation exceptions propagate.
Unrelated cancellation exceptions are sanitized. Requested cancellation is
checked again before sanitization and therefore wins if it occurs during the
failed open. `CancellationToken.None == CancellationToken.None` does not create
an association.

## 13. Exception sanitization

Expected Npgsql open failures produce
`PostgreSqlConnectionException("The PostgreSQL connection could not be opened.")`.
The sanitized exception has no inner exception, empty `Data` and no retained
message, stack, SQLSTATE or other detail from the source exception.

## 14. Unexpected-exception propagation

`InvalidOperationException`, `ObjectDisposedException`, `ArgumentException`,
`NullReferenceException` and `TimeoutException` propagate unchanged. Data-source
construction is not surrounded by a catch. Parsing catches only
`ArgumentException` and `KeyNotFoundException`; opening catches only
`NpgsqlException` and unrelated `OperationCanceledException`.

## 15. Disposal

The factory implements asynchronous, idempotent disposal using
`Interlocked.Exchange`. Metadata remains readable after disposal. Opening after
disposal throws `ObjectDisposedException` without invoking the opener. There is
no finalizer or synchronous `IDisposable` surface.

## 16. Unit-test determinism

The `Connections` suite contains 114 tests. Three pre-commit integration runs
passed 114/114 in 194 ms, 218 ms and 289 ms of test execution respectively.
UnitTests contain no loopback connection, assumed closed port, DNS call, socket,
sleep, retry, environment condition or skipped network test.

## 17. Secret-leakage verification

Synthetic markers for connection-string secrets and a synthetic
`NpgsqlException` were driven through the production sanitization path. Markers
were absent from metadata, messages, `ToString()`, stack traces, `Data` and inner
exceptions. The audited package contained no high-confidence secret pattern.

## 18. Pull request

- Pull request: `#3`
- URL: `https://github.com/rimch1985-ro/DbHealthInspector/pull/3`
- Base: `master`
- Head: `feature/gc-dhi-04a-connection-boundary`
- Changed files: 20
- Open review conversations before merge: 0
- Merge method: merge commit

## 19. Commits

- Implementation commit:
  `8b838721c742b94e7ea0857019d49f5a8798ef79`
- Merge commit:
  `923ca38be1698f568665f7eacb3d760530e4a1ee`
- Integrator: `rimch1985-ro` through the Codex DevOps workflow
- Merge timestamp: `2026-08-01T02:24:40Z`
- Feature branch: deleted locally and remotely after merge confirmation

## 20. CI

| Context | Run | Ubuntu | Windows | Tests per job | Build |
|---|---:|---|---|---:|---|
| Pull request | `30679883155` | `91314638107` — SUCCESS | `91314638092` — SUCCESS | 479 | 0 warnings, 0 errors |
| Master merge | `30679948734` | `91314823473` — SUCCESS | `91314823529` — SUCCESS | 479 | 0 warnings, 0 errors |

The Ubuntu jobs restored, built, tested, packed and uploaded the package. The
Windows jobs restored, built, tested and passed the CLI smoke test.

## 21. Artifact audit

| Item | Verified value |
|---|---|
| Artifact | `dbhealth-bootstrap-package` |
| Artifact ID | `8811851264` |
| Artifact ZIP size | 881858 bytes |
| Artifact ZIP SHA-256 | `CC48F43D23BBBDFBEB69BA163819268E12FAAB1BE67BEE1328B222302E5BD037` |
| Package | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg` |
| Package size | 886611 bytes |
| Package SHA-256 | `F640EDAB051AE54A864ECF5A55BCDE45CB0D15316FCF2CA4FB5E08FF91FD4428` |
| Package type | `DotnetTool` |
| Command | `dbhealth` |
| License | `MIT` |
| Repository | `https://github.com/rimch1985-ro/DbHealthInspector` |
| Repository commit | `923ca38be1698f568665f7eacb3d760530e4a1ee` |
| Core DLL | Present |
| PostgreSQL DLL | Present |
| Help | Success; bootstrap options only |
| Version | `0.1.0-alpha.0+923ca38be1698f568665f7eacb3d760530e4a1ee` |
| Secret scan | No high-confidence matches |
| Installation | Isolated tool path; no global installation |
| Cleanup | ZIP, extraction, local source and temporary installation deleted |

The package was not published.

## 22. Risks and limitations

- A successful real PostgreSQL open remains deferred to GC-DHI-04B.
- `NpgsqlConnection.ConnectionString` is an intrinsic Npgsql API; the factory
  and metadata add no exposure path.
- Concurrent open/dispose on one factory remains unsupported and documented.
- Hostname, database and username reporting policies remain deferred.

## 23. Scope exclusions

GC-DHI-04A added no SQL, transaction, read-only session, SQL allowlist,
capability probe, catalog query, snapshot provider, snapshot mapping, CLI
inspection behavior, JSON reporting or executable DBH001–DBH005 rule. It added
no package dependency and made no CI or ADR change. GC-DHI-04B through
GC-DHI-04F were not started.

## 24. Verdict

```text
APPROVED AND CLOSED
```

GC-DHI-04A is integrated, validated and closed. GC-DHI-04B remains
unauthorized, unimplemented and not started.

## 25. Final human closure

- Closure date: `2026-07-31`
- Verdict: `APPROVED AND CLOSED`
- Backlog item: `PG-01 — Completed`
- Verified pull request: `#3`
- Verified implementation commit:
  `8b838721c742b94e7ea0857019d49f5a8798ef79`
- Verified merge commit:
  `923ca38be1698f568665f7eacb3d760530e4a1ee`
- Verified governance commit before closure:
  `6992896cc83a3e0b7fd06cfda46c920bf6d401c9`
- Verified CI runs:
  `30679883155`, `30679948734`, `30680235253`
- Tests per job:
  `479 passed, 0 failed, 0 skipped`
- Build per job:
  `0 warnings, 0 errors`
- Open findings: none

GC-DHI-04B remains unauthorized, unimplemented and not started.
