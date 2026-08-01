# GC-DHI-04B — Integration Report

**Gate:** GC-DHI-04B — Read-Only Session and SQL Safety Kernel  
**Integration date:** 2026-08-01  
**Integrator:** Codex  
**Repository:** `https://github.com/rimch1985-ro/DbHealthInspector`  
**Verdict:** `READY FOR FINAL HUMAN CLOSURE`

This report records the authorized integration. It does not close GC-DHI-04B
and does not authorize or start GC-DHI-04C.

## 1. Objective

Integrate the reviewed GC-DHI-04B candidate without redesign or scope growth,
verify it locally and through multiplatform CI, audit the package produced by
the merge commit, and record the resulting canonical state.

The integrated boundary gives the PostgreSQL adapter one internal path for an
authorized operation to run only after a verified read-only session has been
established. The session cannot commit and the callback cannot submit raw SQL.

## 2. Backlog coverage

| Backlog item | Result |
|---|---|
| PG-02 — Explicit read-only inspection session | Completed |
| PG-06 — SQL safety foundation | Foundation completed |
| PG-06 — Full backlog item | Remains assigned to GC-DHI-04F |

GC-DHI-04C through GC-DHI-04F were not implemented or started.

## 3. Files integrated

The candidate contained exactly 44 files and no out-of-scope path.

| Category | Files | Lines |
|---|---:|---:|
| Production — PostgreSQL `Sessions/` and `Sql/` | 23 | +2490 |
| Unit tests — `Sessions/` and `Sql/` | 14 | +3784 |
| Integration tests — `PostgreSqlServer/` and `TestSupport/` | 5 | +774 |
| Design documentation | 1 | +662 |
| GitHub Actions | 1 | +27 / -4 |
| **Total** | **44** | **+7737 / -4** |

No file changed in Core, CLI, PostgreSQL `Connections/`, dependencies, build
configuration, ADRs, backlog or governance as part of the implementation
commit.

## 4. Architecture

The established dependency direction remains unchanged:

```text
DbHealthInspector.Cli       -> DbHealthInspector.Core
DbHealthInspector.Cli       -> DbHealthInspector.PostgreSql
DbHealthInspector.PostgreSql -> DbHealthInspector.Core
DbHealthInspector.Core      -> no infrastructure dependency
```

The implementation separates session lifecycle, SQL inventory, lexical safety
validation, typed parameter preparation, Npgsql execution, result-shape checks
and exception translation. Every type added by GC-DHI-04B is `internal`. No new
public PostgreSQL surface, package, DI container, host, logging abstraction,
retry policy or ORM was added. The stable public golden fingerprint remains:

```text
sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444
```

## 5. Transaction model

Each run uses exactly one transaction with the following frozen contract:

```text
Isolation:   RepeatableRead
Access mode: Read Only
Deferrable:  false
Completion:  rollback only
```

Options are validated before opening the connection. The exact execution order
is open, begin, B001, B002, B003, state verification, one authorized callback,
rollback, transaction disposal and connection disposal. B001 is the first SQL
statement in the transaction. No commit, savepoint, nested transaction,
ambient transaction or transaction reuse exists.

Default transaction-local timeouts are 30 seconds for statements, 5 seconds
for locks and 60 seconds for idle-in-transaction. Bounds, millisecond precision,
overflow and `LockTimeout < StatementTimeout` are validated without exposing
the rejected value.

## 6. SQL inventory

`PostgreSqlSqlInventory` is immutable after construction, addressable only by
`PostgreSqlSqlStatementId`, and contains exactly these three entries:

| ID | Purpose | Parameters |
|---|---|---|
| B001 — `SetTransactionReadOnly` | Establish `SET TRANSACTION READ ONLY` | None |
| B002 — `ApplyLocalTimeouts` | Apply the three transaction-local timeouts | Three positional Int32 millisecond values |
| B003 — `VerifySessionState` | Read back isolation, read-only state and timeout matches | Three positional Int32 millisecond values |

There is no lookup by SQL text, runtime registration, external SQL file,
assembly scan, arbitrary statement identifier or fourth statement. Values are
bound positionally; no value or identifier is interpolated into command text.

## 7. Validator

`PostgreSqlSqlSafetyValidator` is fail-closed defense in depth. It validates the
entire fixed inventory once during construction and accepts only the narrow
`SET TRANSACTION READ ONLY` and safe `SELECT` shapes needed by this gate.

It rejects blank input, NUL, comments, semicolons, multiple statements,
dollar-quoted blocks, backslash commands, unterminated literals or identifiers,
unaccounted characters, non-consecutive placeholders, all prohibited command
tokens, locking selects, `SELECT INTO`, `WITH`, unsafe `SET` variants and
invalid pipe grammar. Outside literals, `|` is valid only as one exact `||`
pair. Prohibited words are matched as whole tokens, so identifiers such as
`lock_timeout` remain valid without allowing the `LOCK` command.

The validator is intentionally not a general PostgreSQL parser. Static
inventory plus real-server execution provide the remaining boundary.

## 8. Operational callback boundary

The session runner owns the full `PostgreSqlSqlExecutor` for B001–B003. Only
after B003 verifies the effective state does it invoke the authorized operation
once, passing a restricted `PostgreSqlInspectionOperationExecutor` and the
caller's exact cancellation token.

The restricted executor rejects B001, B002, B003 and every other ID because
GC-DHI-04B contains no operational metadata statement. It exposes no raw SQL,
connection, transaction, Npgsql command or underlying executor. Operational
statements and their dispatch remain deferred to a separately authorized gate.

## 9. Cleanup and EDI contracts

Cleanup always attempts these steps in order:

```text
rollback with CancellationToken.None
dispose transaction asynchronously
dispose connection asynchronously
```

All steps are attempted even if an earlier step fails. Six transparent
`ExceptionDispatchInfo` capture sites protect the primary failure during
session cleanup, row-reader cleanup, non-query command cleanup, reader
acquisition cleanup, command-construction cleanup and multi-step cleanup.
These sites preserve type, instance, message, stack and precedence; they do
not classify or sanitize.

The precedence contract is:

```text
requested cancellation > expected infrastructure failure
primary failure > cleanup failure
cleanup failure surfaces only when no primary exists
```

Reader and command cleanup are independent and asynchronous. Commands are
disposed exactly once on construction failure, acquisition failure,
non-query failure and successful-reader paths. There is no synchronous
`Dispose()`, sync-over-async, finalizer or bare classifying catch.

## 10. Cancellation

Requested cancellation is checked before opening and again before an expected
stage failure can be translated. The GC-DHI-04A association rule is reused for
begin, B001, B002, B003 and callback stages.

The caller's exact token reaches open, begin, B001, B002, B003 and the callback.
Rollback intentionally accepts no caller token and uses
`CancellationToken.None`, so cleanup is still attempted after cancellation.
Associated cancellation propagates unchanged; unrelated cancellation becomes
the fixed sanitized failure kind for its stage. Cancellation racing an expected
failure wins, and cleanup cannot replace it.

## 11. PostgreSQL image

The server suite used one immutable PostgreSQL image:

| Item | Verified value |
|---|---|
| Repository | `docker.io/library/postgres` |
| Tag | `18.4` |
| Digest | `sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a` |
| Server version | PostgreSQL 18.4 (`server_version_num` 180004) |

The image was not referenced through `latest`. The permanent PostgreSQL 15/18
matrix remains assigned to GC-DHI-04F.

## 12. Inspection role

The fixture created a synthetic inspection role with `LOGIN`, `NOSUPERUSER`,
`NOCREATEDB`, `NOCREATEROLE` and `NOREPLICATION`. It received `CONNECT`, schema
`USAGE`, and both `SELECT` and `UPDATE` on the synthetic control table.

The deliberate `UPDATE` grant proves that write rejection comes from the
read-only transaction, not from a missing table privilege. The administrative
role was used only for fixture setup and out-of-band verification. Synthetic
credentials were scoped to the container and never printed.

## 13. Write rejection

After the exact production initialization sequence, a test-only update against
the synthetic control table failed with PostgreSQL SQLSTATE `25006`
(`read_only_sql_transaction`). An independent administrative connection then
verified that the original marker, row count, schema, table and table count
were unchanged.

The update exists only in the integration-test assembly. No raw-SQL escape
hatch, connection accessor, transaction accessor or command accessor was added
to product code.

## 14. Timeout evidence

| Contract | Configuration and trigger | Verified result |
|---|---|---|
| Statement timeout | 500 ms statement timeout; test-only `pg_sleep(2)` | SQLSTATE `57014`; server wait ended well below 2 seconds |
| Lock timeout | 300 ms lock timeout; 10 s statement timeout; independently held exclusive lock | SQLSTATE `55P03`; server wait ended well below the statement timeout |
| Idle-in-transaction timeout | 1 second idle timeout; approximately 4 seconds idle | Session became unusable; pool recovered; no lingering transaction |

Every timeout test had an external deadline. The lock test synchronized on a
confirmed held lock and released it in `finally`; it did not rely on a timing
guess.

## 15. Unit-test evidence

Unit-test coverage includes options, inventory, validator classes and
mutations, placeholder binding, result shapes, B002 null rejection by ordinal,
exact lifecycle ordering, callback gating, exception sanitization, cleanup
matrices, EDI stack preservation, command ownership, cancellation matrices and
reflection checks for absent commit/raw-SQL/public surfaces.

The test assembly lists 950 cases and executes 955 runtime cases because data
theories expand at runtime. The verified runtime result was:

```text
955 passed
0 failed
0 skipped
```

No server, DNS, Docker or sleep is required by the unit suite.

## 16. Integration-test evidence

The non-server integration suite completed:

```text
1 passed
0 failed
0 skipped
```

The PostgreSQL 18 server suite completed:

```text
12 passed
0 failed
0 skipped
```

Server tests cover lifecycle state, write rejection, statement timeout, lock
timeout and idle-in-transaction cleanup. The local and Ubuntu total is 968;
Windows intentionally excludes the 12 server tests and totals 956.

## 17. CI strategy

The workflow keeps server and non-server suites separate:

| Platform | Validation |
|---|---|
| Ubuntu | Restore, build, 955 unit tests, 1 non-server integration test, 12 PostgreSQL server tests, pack and artifact upload |
| Windows | Restore, build, 955 unit tests, 1 non-server integration test and CLI smoke |

Ubuntu runs the server trait exactly once. Windows excludes that trait rather
than discovering it as skipped. Both platforms therefore report zero skipped
tests. Pack occurs only after all Ubuntu tests pass. Actions remain pinned to
immutable commit SHAs.

## 18. PR evidence

| Item | Verified value |
|---|---|
| Pull request | `#4` |
| URL | `https://github.com/rimch1985-ro/DbHealthInspector/pull/4` |
| Base | `master` |
| Head | `feature/gc-dhi-04b-read-only-session` |
| Implementation commit | `fcefe276a78c0945defcfd4062998a441cf2f44c` |
| Files | 44 |
| PR CI run | `30717182433` |
| Ubuntu job | `91414655331` — success |
| Windows job | `91414655345` — success |
| Conflicts | None; mergeable state was clean |
| Review threads | 0 total; 0 open |
| Review comments | 0 |
| Auto-merge | Not enabled |

The PR diff matched the authorized candidate, contained one implementation
commit, no dependency change, no Core or CLI change, and no secret or generated
output.

PR CI results:

| Platform | Passed | Failed | Skipped | Build |
|---|---:|---:|---:|---|
| Ubuntu | 968 | 0 | 0 | 0 warnings / 0 errors |
| Windows | 956 | 0 | 0 | 0 warnings / 0 errors |

Ubuntu pack and upload succeeded; Windows CLI smoke succeeded.

## 19. Merge evidence

The PR was merged with an explicit merge commit, without squash, rebase,
amendment, force push or history rewrite.

| Item | Verified value |
|---|---|
| Method | Merge commit |
| Merge commit | `c67c62fbd262c4159cb8fe3a381e2ad299b8f9ce` |
| First parent | `a6cae28eeeb30c5ebec75604c586bf1699641139` |
| Second parent | `fcefe276a78c0945defcfd4062998a441cf2f44c` |
| Commit timestamp UTC | `2026-08-01T20:34:02Z` |
| GitHub merged-at timestamp | `2026-08-01T20:34:03Z` |

The feature branch was deleted locally and remotely after the merge. Local and
remote `master` then pointed to the merge commit.

## 20. Master CI

Master run `30717262246` validated the exact merge commit.

| Platform | Job ID | Passed | Failed | Skipped | Build |
|---|---:|---:|---:|---:|---|
| Ubuntu | `91414883472` | 968 | 0 | 0 | 0 warnings / 0 errors |
| Windows | `91414883480` | 956 | 0 | 0 | 0 warnings / 0 errors |

Ubuntu pack and artifact upload succeeded. Windows CLI smoke succeeded.

## 21. Artifact audit

Only the artifact produced by master run `30717262246` was used for the
canonical package audit.

| Item | Verified value |
|---|---|
| Artifact name | `dbhealth-bootstrap-package` |
| Artifact ID | `8823728204` |
| GitHub size | 919184 bytes |
| GitHub digest | `sha256:dfd55fc0a2cf01ca03e9377cc349a80c81c4a25eef1db0813e3b3f691977b721` |
| Downloaded ZIP size | 919184 bytes |
| Downloaded ZIP SHA-256 | `DFD55FC0A2CF01CA03E9377CC349A80C81C4A25EEF1DB0813E3B3F691977B721` |

The downloaded ZIP digest exactly matched the digest supplied by GitHub.

## 22. Package audit

| Item | Verified value |
|---|---|
| Filename | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg` |
| Size | 923753 bytes |
| SHA-256 | `5DFAD5257E08599F20B6F96623FB47F1509A669D3644587A2148C761AD2854C3` |
| Package ID | `DbHealthInspector.Tool` |
| NuGet identity version | `0.1.0-alpha.0` |
| Verified tool version | `0.1.0-alpha.0+c67c62fbd262c4159cb8fe3a381e2ad299b8f9ce` |
| Package type | `DotnetTool` |
| Command | `dbhealth` |
| License | `MIT` |
| Repository URL | `https://github.com/rimch1985-ro/DbHealthInspector` |
| Repository branch | `refs/heads/master` |
| Repository commit | `c67c62fbd262c4159cb8fe3a381e2ad299b8f9ce` |

NuGet normalizes the package identity and filename to the base prerelease
version. The installed tool's informational version carries the exact merge
SHA, and the `.nuspec` repository commit independently points to that same SHA.
`DotnetToolSettings.xml` declares `dbhealth` with entry point
`DbHealthInspector.Cli.dll` and the `dotnet` runner.

The package contains `DbHealthInspector.Core.dll`,
`DbHealthInspector.PostgreSql.dll` and `DbHealthInspector.Cli.dll`, plus
legitimate runtime dependencies including Npgsql. It contains no unit-test or
integration-test DLL, Testcontainers assembly, xUnit assembly, test fixture,
Docker configuration, credentials, connection string, container password,
control-marker value, `pg_sleep` test SQL, `LOCK TABLE` test SQL, synthetic
write-rejection SQL or test result file.

The `.nupkg` was installed into an isolated temporary tool path. `dbhealth
--help` succeeded and remained bootstrap-only, exposing only help and version.
`dbhealth --version` returned the exact informational version above. No
PostgreSQL inspection command, connection option or GC-DHI-04C behavior was
exposed. The ZIP, extraction, local source and isolated installation were
removed after hashes and evidence were recorded.

## 23. Security verification

The following controls were verified:

- No repository or package secret, real credential, connection string or
  container password was found.
- Sanitized session exceptions have fixed messages and expose no inner
  exception, SQLSTATE, SQL, parameter, server detail or connection metadata.
- No test assembly, Testcontainers, xUnit, test fixture or test-only SQL is in
  the package.
- No new public PostgreSQL type exists; the public golden fingerprint is
  unchanged.
- No raw SQL API, arbitrary statement text, connection, transaction or command
  crosses the callback boundary.
- Static inventoried SQL, typed positional values and the fail-closed validator
  remain the only product execution path.
- Read-only enforcement was proven with SQLSTATE `25006` while the role held
  the privilege needed to perform the rejected update.

## 24. Scope exclusions

GC-DHI-04B added none of the following:

- Capability, version, database-name or current-user probing.
- Table or index metadata queries.
- `pg_class`, `pg_namespace`, `pg_index` or `pg_stat_*` catalog behavior.
- Snapshot mapping or a PostgreSQL snapshot provider.
- DBH001–DBH005 executable diagnostic rules.
- CLI commands, connection options, JSON, console rendering or exit behavior.
- A fourth SQL statement, user SQL, dynamic SQL, commit path or retry policy.
- Core changes, CLI project changes, dependencies, tags, releases or NuGet
  publication.

GC-DHI-04C through GC-DHI-04F were not started.

## 25. Remaining deferred work

GC-DHI-04B still requires a separate final human closure record. GC-DHI-04C
remains unauthorized.

Future separately authorized gates retain capability probing, metadata
inventory and mapping, CLI/report behavior, diagnostic rules, final minimum
permissions, permanent PostgreSQL 15/18 verification and the remaining PG-06
backlog completion. Full PG-06 completion remains assigned to GC-DHI-04F.

No tag, release or NuGet publication is part of this gate.

## 26. Final integration verdict

```text
READY FOR FINAL HUMAN CLOSURE
```

The reviewed candidate was integrated without scope deviation through PR `#4`,
both PR and master CI passed, and the master artifact was independently audited
and installed in isolation. GC-DHI-04B is integrated but not finally closed.
