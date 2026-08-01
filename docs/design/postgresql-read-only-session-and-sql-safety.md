# PostgreSQL Read-Only Session and SQL Safety Kernel

**Gate:** GC-DHI-04B — Read-Only Session and SQL Safety Kernel
**Backlog:** PG-02 (complete) and PG-06 (foundation)
**Predecessor:** GC-DHI-04A approved and closed
**Scope:** `DbHealthInspector.PostgreSql.Sessions` and `DbHealthInspector.PostgreSql.Sql`
**Status:** Implemented; corrected per Codex reviews GC-DHI-04B-R1 (C1) and GC-DHI-04B-R2 (C2);
pending further Codex review.

## 0. Corrections applied in GC-DHI-04B-C1

Codex review GC-DHI-04B-R1 returned eight findings addressed here:

- **F-01** — Cleanup used an enumerated catch list, so an exception outside that set could replace
  a primary failure or a requested cancellation, and could stop later cleanup steps from running.
  Cleanup now captures through `ExceptionDispatchInfo`; see [§17](#17-precedencia-de-errores) and
  [§18](#18-rollback-y-disposal).
- **F-02** — The callback received the full `PostgreSqlSqlExecutor` and could therefore re-run
  B001/B002/B003. It now receives a restricted
  `PostgreSqlInspectionOperationExecutor`; see [§13](#13-session-runner).
- **F-03** — The validator accepted each `|` as independent punctuation, so `1 | 2` and `1 ||| 2`
  passed. It now recognises exactly the `||` pair; see [§10](#10-validator).
- **F-06** — B002 checked row and column counts but not NULLs. It now rejects a NULL in any of its
  three columns; see [§12](#12-executor).
- **F-07** — `VerificationFailed` used its own message. It now uses the canonical initialization
  message while keeping its distinct kind; see [§15](#15-error-model).
- **F-08** — Cancellation association was not applied per stage, and the B002 cancellation test
  cancelled before the runner was entered. Both are corrected; see [§16](#16-cancelación).
- **F-09** — A reader-disposal failure could prevent command disposal and could mask an execution
  or shape failure. C1 made reader and command disposal independent; **C1 did not fully close this
  finding** — the construction and acquisition paths were corrected later in C2 (R2-01). See
  [§12](#12-executor).
- **F-10** — `ThrowsAny` assertions were replaced with exact-instance or named-set assertions; see
  [§20](#20-unit-testing).

Frozen statements this document asserts:

```text
The session always ends through rollback.
There is no commit path.
B001 is the first statement in the transaction.
The productive inventory contains exactly B001, B002 and B003.
Raw SQL exists only in the PostgreSQLServer test fixture.
GC-DHI-04C through GC-DHI-04F were not started.
```

## 0-bis. Corrections applied in GC-DHI-04B-C2

Codex review GC-DHI-04B-R2 returned four findings:

- **R2-01 / F-09** — F-09 was only *partly* resolved by C1. The reader-acquisition path still used
  a bare `catch { await command.DisposeAsync(); throw; }`, and command construction still used a
  bare `catch { command.Dispose(); throw; }` — synchronous, and in both cases a disposal failure
  could replace the acquisition or construction failure. Both paths now capture their primary with
  `ExceptionDispatchInfo` before releasing anything, and there is no synchronous disposal left
  anywhere. See [§12](#12-executor) and [§19](#19-seams).
- **R2-02 / F-01** — the no-primary cleanup matrix executed every exception type but did not assert
  every outcome, and never exercised `PostgresException`. Both are now asserted per step and per
  type. See [§20](#20-unit-testing).
- **R2-03 / F-08** — the token-forwarding test observed B001–B003 and the callback but not `Open`
  or `Begin`. The fake now records both, and the assertion uses a genuinely cancelable token. See
  [§16](#16-cancelación).
- **R2-04** — documentation still described the C1-era design. Corrected throughout, including the
  catch-all inventory, which is **six**, not four.

## 1. Objetivo

Give the rest of the adapter exactly one way to run an authorized operation against PostgreSQL,
such that the operation provably cannot write, cannot run un-inventoried SQL, cannot outlive its
transaction, cannot leak server detail, and cannot commit.

## 2. Alcance

In scope: session options and validation; one `RepeatableRead` transaction; B001–B003; effective
state verification; the closed statement inventory; the fail-closed validator; the executor;
rollback-only completion; cancellation and error precedence; unit tests; PostgreSQL 18
integration tests; focused CI.

Out of scope, unchanged from the gate definition: capability/version probing, database-name or
current-user reporting, table/index metadata SQL, schema filters, `DatabaseSnapshot` and its
provider, DBH001–DBH005, CLI/JSON/console/exit codes, and the permanent 15/18 matrix.

## 3. Arquitectura

```text
DbHealthInspector.PostgreSql → DbHealthInspector.Core
DbHealthInspector.Core       → no infrastructure dependency
```

Every type added is `internal`. No new Core interface, no DI container, no generic host, no
logging, no retry policy, no ORM.

```text
src/DbHealthInspector.PostgreSql/
├── Sessions/
│   ├── PostgreSqlInspectionSessionOptions.cs
│   ├── PostgreSqlInspectionSessionState.cs
│   ├── PostgreSqlInspectionSessionFailureKind.cs
│   ├── PostgreSqlInspectionSessionException.cs
│   ├── IPostgreSqlInspectionSessionScope.cs
│   ├── PostgreSqlInspectionSessionScope.cs
│   ├── PostgreSqlInspectionOperationExecutor.cs
│   └── PostgreSqlInspectionSessionRunner.cs
└── Sql/
    ├── PostgreSqlSqlStatementId.cs
    ├── PostgreSqlSqlCommandKind.cs
    ├── PostgreSqlSqlParameterType.cs
    ├── PostgreSqlSqlParameterDefinition.cs
    ├── PostgreSqlSqlParameterValue.cs
    ├── PostgreSqlSqlStatementDefinition.cs
    ├── PostgreSqlPreparedStatement.cs
    ├── PostgreSqlSqlInventory.cs
    ├── PostgreSqlSqlSafetyValidator.cs
    ├── PostgreSqlSqlSafetyException.cs
    ├── PostgreSqlSqlExecutionExceptions.cs
    ├── IPostgreSqlStatementGateway.cs
    ├── NpgsqlStatementGateway.cs
    ├── PostgreSqlAsyncCleanup.cs
    └── PostgreSqlSqlExecutor.cs
```

Validation, inventory, execution, session lifecycle and error translation remain separate types.

## 4. Dependencia con GC-DHI-04A

The runner opens through the approved `PostgreSqlConnectionFactory` and never sees a connection
string. A `PostgreSqlConnectionException` raised while opening is already sanitized by 04A and is
re-thrown unchanged — wrapping it again would add a second layer without adding information. No
04A contract was modified.

## 5. Ownership

| Resource | Owner | Notes |
|---|---|---|
| `PostgreSqlConnectionFactory` | The caller | The runner never disposes it |
| `NpgsqlConnection` | The session scope | Always disposed, never retained after the run |
| `NpgsqlTransaction` | The session scope | One per run, always rolled back and disposed, never reused |
| Command / rows | `NpgsqlStatementGateway` | Released asynchronously and exactly once on every path: by the gateway when construction or acquisition fails, otherwise with the reader that owns them |
| `PostgreSqlSqlExecutor` | The scope that created it | Owns nothing, so implements no disposal; never outlives the callback |

The authorized operation receives only a restricted `PostgreSqlInspectionOperationExecutor` and a
`CancellationToken` (GC-DHI-04B-C1, F-02).

## 6. Opciones y validación

| Option | Default | Minimum | Maximum | Relation |
|---|---:|---:|---:|---|
| `StatementTimeout` | 30 s | 100 ms | 5 min | — |
| `LockTimeout` | 5 s | 50 ms | 30 s | Strictly `< StatementTimeout` |
| `IdleInTransactionTimeout` | 60 s | 250 ms | 10 min | — |

Rejected with the correct `ParamName`: zero, negative, `Timeout.InfiniteTimeSpan`,
sub-millisecond precision, below minimum, above maximum, millisecond overflow (checked
conversion), and `LockTimeout >= StatementTimeout`.

Because options validate themselves in their constructor, an invalid policy can never reach the
runner — validation therefore provably precedes opening a connection. Rejection messages never
include the offending value, and `ArgumentOutOfRangeException.ActualValue` is left null, so
operational configuration cannot escape through an exception.

No `transaction_timeout`, `idle_session_timeout`, command-timeout override or retry count is
added.

## 7. Transaction model

```text
Isolation level: RepeatableRead
Access mode:     Read Only (established by B001)
Deferrable:      false
Completion:      rollback only
```

`BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken)`. No `ReadCommitted`,
`Serializable`, `Snapshot`, ambient `TransactionScope`, savepoint, nested transaction or
transaction reuse. **There is no commit path**: `CommitAsync` is never called and no method or
property named `Commit`/`Complete`/`Save` exists on the runner, the scope, the scope interface or
the executor — verified by reflection tests.

## 8. Secuencia B001 → B002 → B003

```text
validate options (already done at construction)
check cancellation
open connection                       [GC-DHI-04A factory]
begin RepeatableRead
create executor
B001  SET TRANSACTION READ ONLY       ← first statement in the transaction
B002  apply the three local timeouts  ← exactly one row, three columns
B003  read back effective state       ← exactly one row, five non-null columns
verify state
invoke the authorized operation once
rollback → dispose transaction → dispose connection
```

## 9. Inventario

`PostgreSqlSqlInventory` is the single canonical source: built once, validated once, immutable
afterwards, addressable only by `PostgreSqlSqlStatementId`. There is no lookup by SQL text (a
reflection test asserts no method takes a `string`), no runtime registration, no external SQL
file and no assembly scan. It contains exactly three statements in the order B001, B002, B003.

| ID | Kind | Parameters |
|---|---|---|
| `SetTransactionReadOnly` | `SetTransactionReadOnly` | none |
| `ApplyLocalTimeouts` | `SelectConfiguration` | `$1`,`$2`,`$3` — Int32 milliseconds |
| `VerifySessionState` | `SelectVerification` | `$1`,`$2`,`$3` — Int32 milliseconds |

The `|| 'ms'` in B002 is part of the fixed static text: it appends a unit suffix to an
already-bound integer parameter and splices nothing.

## 10. Validator

`PostgreSqlSqlSafetyValidator` is fail-closed defence in depth, **not** a PostgreSQL parser. It
runs over every definition when the inventory is constructed, so an inventory that exists is one
whose statements are already proven — the executor never re-parses SQL at run time.

It scans character by character, tracking single-quoted literals (with the doubled-quote escape)
and double-quoted identifiers, and rejects: null, blank, byte NUL, `--`, `/*`, `*/`,
dollar-quoted blocks, backslash commands, semicolons (including trailing), multiple statements,
unterminated literals or identifiers, and any character it cannot account for.

Prohibited tokens, matched **whole-token** and case-insensitively outside literals and quoted
identifiers: `INSERT UPDATE DELETE MERGE CREATE ALTER DROP TRUNCATE VACUUM ANALYZE REINDEX GRANT
REVOKE COPY CALL DO EXECUTE PREPARE DEALLOCATE LOCK CLUSTER CHECKPOINT REFRESH IMPORT REASSIGN
SECURITY LISTEN NOTIFY UNLISTEN DISCARD RESET LOAD COMMENT`.

Whole-token matching is essential rather than incidental: B002 and B003 legitimately contain
`lock_timeout` and `lock_timeout_matches`, which merely *contain* `LOCK`. Because `_` is a word
character, these are single identifier tokens and are correctly accepted.

Pipe grammar (GC-DHI-04B-C1, F-03): outside literals and quoted identifiers a `|` must be
immediately followed by exactly one more `|`, and the pair is consumed as a single `||` token. A
lone `|`, a run of three or more, and a trailing `|` are all rejected. Pipes inside a string
literal or a quoted identifier remain ordinary content.

Shape rules: only two leading forms are accepted. `SET` is accepted **only** when the whole
normalised statement equals `SET TRANSACTION READ ONLY`; every other `SET`, including `SET LOCAL`
and `SET TRANSACTION READ ONLY DEFERRABLE`, is rejected. `SELECT` is accepted after the
prohibited-token scan and after rejecting `SELECT INTO`, `FOR UPDATE`, `FOR NO KEY UPDATE`,
`FOR SHARE` and `FOR KEY SHARE`.

**`WITH` is rejected as a whole class in GC-DHI-04B.** No inventoried statement uses a CTE, so
rejecting the class keeps the validator fail-closed instead of shipping a premature CTE parser
that would have to prove no CTE contains DML. A later gate that needs `WITH` must revisit this
decision explicitly.

Placeholders must start at `$1`, be consecutive with no gaps, contain no `$0`, match the
declaration count exactly, and every declaration must be used. A position may legitimately appear
more than once in the text as long as the set of distinct positions matches the declarations.
Identifiers can never be parameters.

## 11. Parameter binding

Values are carried by the immutable `PostgreSqlSqlParameterValue` struct (exact position, exact
declared type, payload) — never a mutable dictionary and never an arbitrary `object`.
`PostgreSqlSqlExecutor.Prepare` resolves the ID and checks count, ascending position and declared
type before anything reaches Npgsql. Binding uses `NpgsqlDbType.Integer` positionally; there is
no caller-controlled parameter name, no implicit conversion, no interpolation and no dynamic
`CommandText`.

## 12. Executor

`PostgreSqlSqlExecutor` accepts a `PostgreSqlSqlStatementId`, typed values and a
`CancellationToken`. It accepts no `string sql`, exposes no `NpgsqlCommand`, connection or
transaction, has no instance properties at all, and implements no disposal because it owns
nothing.

Execution goes through the narrow `IPostgreSqlStatementGateway` seam. Production is
`NpgsqlStatementGateway`, bound to one connection and one transaction: it creates a command per
operation, binds parameters, executes asynchronously with the exact token and disposes command
and reader. It never touches `CommandTimeout` (B002's server-side timeouts are the single timeout
authority), never prepares explicitly, never batches and performs no synchronous I/O.

Result-shape rules are enforced by the executor, identically for both row-returning statements
(GC-DHI-04B-C1, F-06): exactly one row, exactly the declared column count, **no NULL in any
column**, and no second row. B002 therefore rejects a NULL in ordinal 0, 1 or 2 — `set_config`
never returns NULL for a successful assignment — and B003 rejects a NULL in any of its five.
`set_config`'s echoed values are read and discarded, never exposed.

Readers and commands are released through `PostgreSqlAsyncCleanup` rather than `await using`
(GC-DHI-04B-C1, F-09). `await using` compiles to a `try/finally` in which a disposal failure
*replaces* the exception already propagating; capturing the primary first means an execution,
shape or cancellation failure always wins, while a disposal failure with no primary still
surfaces. The reader and the command are disposed independently, so the command is released even
when releasing the reader fails, and only the first disposal failure is surfaced.

**Gateway command lifecycle (GC-DHI-04B-C2, R2-01).** The same rule now holds for the two paths
C1 left uncovered:

| Path | Behaviour |
|---|---|
| Command construction (`CreateCommandAsync`) | Creating the handle and binding parameters run inside a transparent capture. On failure, a partially built command is released **asynchronously** and the construction failure is re-thrown with its original stack. A factory that threw leaves no command, and nothing is disposed. |
| Reader acquisition (`ExecuteReaderAsync`) | Acquisition runs inside a transparent capture. On failure the command — which nothing downstream will ever own — is released asynchronously exactly once, and its disposal failure is discarded rather than allowed to replace the acquisition failure, which on that path always exists. |
| Non-query execution | Primary captured, then the command released; primary wins, and a disposal failure alone still surfaces. |
| Successful acquisition | Ownership of rows and command transfers to the returned reader, which releases both independently. |

There is **no synchronous `Dispose()`** anywhere in the adapter, and no bare `catch`. The command
is disposed exactly once on every path, asserted by counters in the lifecycle tests.

## 13. Session runner

`PostgreSqlInspectionSessionRunner.RunAsync<TResult>(options, operation, cancellationToken)`
drives the sequence in §8 through `IPostgreSqlInspectionSessionScope`. The scope seam is
infrastructure, not a test-only path: production always uses
`PostgreSqlInspectionSessionScope`, and there is exactly one code path. The operation is invoked
exactly once and never before verification succeeds.

**Restricted operational view (GC-DHI-04B-C1, F-02).** The runner uses the full
`PostgreSqlSqlExecutor` only for B001, B002 and B003. After verification succeeds the callback
receives a `PostgreSqlInspectionOperationExecutor` instead, which:

- rejects `SetTransactionReadOnly`, `ApplyLocalTimeouts` and `VerifySessionState` — the callback
  cannot re-establish read-only mode, change the already-verified timeouts, or repeat the
  verification query;
- rejects every other id too, because GC-DHI-04B inventories no operational statement at all;
- exposes no connection, transaction, command, raw SQL, or the executor it wraps; and
- rejects with the fixed `PostgreSqlSqlSafetyException` message, never rendering the id or SQL.

GC-DHI-04C is where operational statements — and the dispatch that runs them through the bound
executor — will be introduced.

## 14. State verification

B003 must report all five conditions:

```text
is_read_only              = true
isolation_level           = "repeatable read"   (ordinal comparison)
statement_timeout_matches = true
lock_timeout_matches      = true
idle_timeout_matches      = true
```

The expected isolation string is exactly `repeatable read`, lowercase with a single space —
verified directly against PostgreSQL 18.4 rather than assumed. Any mismatch, missing row, extra
row or unexpected NULL blocks the operation with `VerificationFailed` and the fixed verification
message; no partial result is produced and the callback never runs.

## 15. Error model

| Stage | Caught | Failure kind | Fixed message |
|---|---|---|---|
| Open connection | — (04A already sanitized) | — | `PostgreSqlConnectionException` propagates unchanged |
| Begin transaction | `NpgsqlException`, unrelated OCE | `InitializationFailed` | The PostgreSQL inspection session could not be initialized. |
| B001, B002 | `NpgsqlException`, result-shape | `InitializationFailed` | The PostgreSQL inspection session could not be initialized. |
| B003 / verification mismatch | `NpgsqlException`, result-shape, unrelated OCE | `VerificationFailed` | The PostgreSQL inspection session could not be initialized. |
| Authorized operation | `NpgsqlException`, unrelated OCE | `ExecutionFailed` | The PostgreSQL inspection operation failed. |
| Rollback / disposal (no primary) | `NpgsqlException` only | `CleanupFailed` | The PostgreSQL inspection session could not be closed safely. |

**`VerificationFailed` deliberately reuses the initialization message** (GC-DHI-04B-C1, F-07):
the caller-visible text must not reveal that the session got as far as reading back its own
state, which is a detail about the server interaction rather than about the caller. The distinct
`FailureKind` remains available to internal callers.

Every stage classifies with **typed** catches — `OperationCanceledException` (filtered to the
unrelated case), `NpgsqlException` and, where a malformed result is possible,
`PostgreSqlSqlResultShapeException`. No classification path uses `catch (Exception)`.

`PostgreSqlInspectionSessionException` has a single constructor taking only a failure kind, so no
code path — anywhere in the assembly — can attach a message, an inner exception or `Data`. It
does not override `ToString()`. No SQLSTATE, `Detail`, `Hint`, schema, table, column, constraint,
SQL text, bound parameter, connection metadata or original stack trace survives. `PostgresException`
derives from `NpgsqlException`, so a single catch per stage covers both without unreachable
duplicates.

Unexpected `InvalidOperationException`, `ObjectDisposedException`, `ArgumentException`,
`NullReferenceException`, non-Npgsql `TimeoutException`, `OutOfMemoryException` and
`AccessViolationException` propagate unchanged, same instance.

## 16. Cancelación

Requested cancellation is checked before opening and is re-checked immediately before any
expected failure is converted into a session exception, so cancellation always outranks a stage
failure.

Association reuses the frozen 04A rule directly —
`PostgreSqlConnectionFactory.IsRequestedCancellation` — rather than re-implementing it, and is
applied at **every** stage (GC-DHI-04B-C1, F-08): begin, B001, B002, B003 and the callback. An
`OperationCanceledException` associated with the requested token propagates unchanged; an
unrelated one is sanitized to that stage's kind:

| Stage | Unrelated OCE becomes |
|---|---|
| Begin | `InitializationFailed` |
| B001 | `InitializationFailed` |
| B002 | `InitializationFailed` |
| B003 | `VerificationFailed` |
| Callback | `ExecutionFailed` |

Open keeps the GC-DHI-04A contract unchanged. `CancellationToken.None` compared against another
`CancellationToken.None` is never treated as association.

**Token forwarding (GC-DHI-04B-C2, R2-03).** The caller's exact token is asserted to reach every
stage that takes one — `Open`, `Begin`, B001, B002, B003 and the callback — using a genuinely
cancelable token rather than `CancellationToken.None`, so equality is meaningful. `Rollback`
deliberately does **not** take a token: it is the one operation that must still be attempted when
the caller's token is already canceled, so it is always issued with `CancellationToken.None`. The
scope interface encodes this by giving `RollbackAsync` no parameter at all, which a test asserts
by reflection alongside a behavioural check that rollback still runs after a cancellation.

| Scenario | Result |
|---|---|
| Pre-canceled token | `OperationCanceledException`; nothing opened |
| Cancellation during open/begin/B001/B002/B003/operation | Propagates |
| Cancellation racing an expected failure | Cancellation wins |
| Cleanup fails after cancellation | Cancellation wins |

## 17. Precedencia de errores

```text
Requested cancellation   > expected infrastructure failure
Primary failure          > cleanup failure
Cleanup failure surfaces only when no earlier failure exists
```

**No classification path uses `catch (Exception)`.** Every stage that sanitizes uses typed
catches (§15).

Precedence is enforced by *transparent capture* (GC-DHI-04B-C1, F-01). The session body runs
inside one `catch (Exception)` whose sole action is
`ExceptionDispatchInfo.Capture(exception)` — it inspects nothing, classifies nothing, sanitizes
nothing and changes no type, message, stack trace or instance identity. Cleanup then runs to
completion, and only afterwards is the captured primary re-thrown with
`ExceptionDispatchInfo.Throw()`, which preserves the original throw site.

There are exactly **six** such transparent capture sites — the final inventory after C2 — all
documented in code. None classifies, none sanitizes, none uses a filter for classification, and
every one preserves the original type, message, instance and stack:

| Location | Primary protected | Cleanup performed | EDI | Classifies | Sanitizes |
|---|---|---|---|---|---|
| `PostgreSqlInspectionSessionRunner.RunAsync` | whole session body | rollback, tx dispose, conn dispose | yes | no | no |
| `PostgreSqlSqlExecutor.ReadSingleRowAsync` | row reading and shape checks | reader dispose | yes | no | no |
| `NpgsqlStatementGateway.ExecuteNonQueryAsync` | non-query execution | command dispose | yes | no | no |
| `NpgsqlStatementGateway.ExecuteReaderAsync` | reader acquisition | command dispose | yes | no | no |
| `NpgsqlStatementGateway.CreateCommandAsync` | command construction and binding | partial command dispose | yes | no | no |
| `PostgreSqlAsyncCleanup.RunAllAsync` | each cleanup step | n/a (is the mechanism) | yes | no | no |

Every other catch in the adapter is **typed**: the runner's five stages use
`OperationCanceledException` (filtered to the unrelated case), `NpgsqlException` and
`PostgreSqlSqlResultShapeException`, and `PostgreSqlInspectionSessionOptions` uses
`OverflowException`. There is no bare `catch` anywhere.

When no primary exists, the **first** cleanup failure is the one surfaced, and only an
`NpgsqlException` becomes `CleanupFailed`; anything else is a defect and is re-thrown exactly as
captured. Later cleanup failures are dropped rather than aggregated — they are consequences of
the first, and attaching them to `Data` or an inner exception would widen the sanitized surface.

## 18. Rollback y disposal

Policy, chosen once and applied everywhere: **explicit rollback, then dispose transaction, then
dispose connection**. Explicit rollback is preferred because it lets a cleanup failure be
classified and verified. After a successful explicit rollback the transaction is already
complete, so `DisposeAsync` releases resources without a second logical rollback.

Rollback always uses `CancellationToken.None`: it is the one operation that must still be
attempted when the caller's token is already canceled.

All three cleanup steps are always attempted, in order, regardless of which failed — including
when an earlier step throws (GC-DHI-04B-C1, F-01). `PostgreSqlAsyncCleanup.RunAllAsync` runs each
step, captures the first failure transparently, and keeps going, so a rollback failure can never
prevent the transaction or the connection from being released.

There is no finalizer, no sync-over-async, no `.Result`, `.Wait()`, `GetAwaiter().GetResult()` or
`Task.Run`.

## 19. Seams

| Seam | Production implementation | Why it exists |
|---|---|---|
| `IPostgreSqlStatementGateway` | `NpgsqlStatementGateway` | Lets resolution, binding and result-shape rules be proven without a server |
| `IPostgreSqlRowReader` | `NpgsqlStatementGateway.CommandBoundRowReader` | Minimal read surface; enables row/column/NULL tests |
| `IPostgreSqlCommandHandle` | `NpgsqlStatementGateway.NpgsqlCommandHandle` | Lets command construction, acquisition and asynchronous disposal failures be proven without a server (C2, R2-01) |
| `IPostgreSqlRowSource` | `NpgsqlStatementGateway.NpgsqlRowSource` | Separates the rows from the command so the two can be released independently |
| `IPostgreSqlInspectionSessionScope(Factory)` | `PostgreSqlInspectionSessionScope(Factory)` | Lets any single lifecycle stage fail deterministically |

The command-handle factory receives an already-resolved `PostgreSqlPreparedStatement`, so neither
a caller nor an authorized operation can influence the command text through the seam: the
production handle reads it from the inventory-resolved statement and nowhere else.

No `if (isTest)`, no conditional compilation, no service locator, no mocking library and no
duplicated production logic. The normal path always uses the real factory, real
`BeginTransactionAsync`, a real `NpgsqlCommand` and the real executor.

## 20. Unit testing

Deterministic and server-free: no network, Docker, DNS, sleeps or assumed-closed ports. Coverage
includes option defaults/bounds/rejections; the three exact inventory entries with exact SQL,
order, kinds and parameters; every prohibited validator class plus casing, whitespace,
literals, quoted identifiers, comments, semicolons, two statements, locking selects,
`SELECT INTO`, `WITH`/DML-CTE and every placeholder error; executor resolution, binding, token
forwarding, result shape and reader disposal; the runner's exact sequence, callback gating,
stage classification, cancellation precedence, cleanup precedence, unexpected-exception
propagation and absence of any commit/savepoint API; and synthetic secret markers.

GC-DHI-04B-C1 adds: the cleanup matrix across steps, exception types and primary states
(including a bespoke exception subtype outside every enumerated set, plus all-steps-attempted,
disposal ordering, first-failure-wins and stack-trace preservation); executor disposal
precedence; the operational-view rejections and surface constraints; the pipe grammar and B002
mutations; B002 NULL rejection per ordinal; and the per-stage cancellation matrix.

GC-DHI-04B-C2 completes them. The no-primary cleanup matrix now asserts an **outcome** for every
step × type, not merely that the type was exercised: `NpgsqlException` **and**
`PostgresException` each become `CleanupFailed` (kind, fixed message, null inner, empty `Data`,
and no trace of the cleanup text), while `InvalidOperationException`,
`ObjectDisposedException`, `TimeoutException`, `ArgumentException` and a bespoke subtype each
surface as the very same instance. The with-primary matrix additionally asserts the primary's
kind and message and that the cleanup failure appears nowhere. New gateway lifecycle tests cover
command construction failure, reader acquisition failure, factory failure with no command to
dispose, disposal-count exactness on every path, reader/command disposal independence, and
original-throw-site preservation.

No `ThrowsAny` assertion remains in this gate's tests (F-10). Where a family of outcomes is
genuinely possible — only the idle-in-transaction case — `Record.ExceptionAsync` is used with an
explicitly named set (`NpgsqlException or InvalidOperationException`) rather than accepting any
`Exception`. Everywhere else the assertion is on the exact type or the exact instance.

## 21. Integration fixture

One PostgreSQL 18 container pinned by exact tag **and** immutable digest:

```text
Repository: docker.io/library/postgres
Tag:        18.4
Digest:     sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a
Resolved:   2026-08-01 via `docker pull postgres:18.4`
Verified:   PostgreSQL 18.4 (server_version_num 180004)
```

`latest` is never used. The fixture creates a synthetic schema, a persistent (not temporary, not
unlogged) control table and one control row, plus an inspection role that is `LOGIN NOSUPERUSER
NOCREATEDB NOCREATEROLE NOREPLICATION` and holds `CONNECT`, schema `USAGE`, and `SELECT` **and
`UPDATE`** on the control table. Granting `UPDATE` is mandatory so write rejection proves
read-only enforcement rather than a missing privilege. The administrative role is used only for
fixture setup and out-of-band verification, never to run an inspected session. Credentials are
synthetic and never printed.

All server tests carry `[Trait("Category", "PostgreSqlServer")]` and share one collection with
`DisableParallelization = true`, so lock and timeout tests never run concurrently.

## 22. Write-rejection contract

After the exact production initialization sequence, a test-only
`UPDATE <synthetic-control-table> SET marker = 'changed' WHERE id = 1` must fail with SQLSTATE
`25006` (`read_only_sql_transaction`). Inspecting SQLSTATE is legitimate here because the error
never crosses the production boundary — the command belongs to the test. Afterwards the original
marker, the row count, the schema, the table and the table count are all verified unchanged
through a separate administrative connection.

The update, `pg_sleep` and the lock statement exist **only** in the IntegrationTests assembly.
The product gained no `ExecuteRaw`, `ExecuteSql`, `CreateCommand`, `Connection` or `Transaction`
surface to support them: `TestOwnedInspectionSession` builds its own connection and transaction
and constructs the executor through its internal `(inventory, connection, transaction)`
constructor, rather than extracting resources out of a real runner session.

## 23. Timeout tests

| Test | Configured | Trigger | Expected |
|---|---|---|---|
| Statement timeout | 500 ms statement / 200 ms lock | test-only `pg_sleep(2)` | SQLSTATE `57014`, elapsed well under 2 s |
| Lock timeout | 10 s statement / 300 ms lock | second connection holds `ACCESS EXCLUSIVE`, inspection `SELECT` needs `ACCESS SHARE` | SQLSTATE `55P03`, elapsed well under the statement timeout |
| Idle-in-transaction | 1 s idle | stay idle ~4 s, then execute | session unusable; pool recovers; no lingering transaction |

The lock test synchronises deterministically — the lock is provably held once `LOCK TABLE`
returns — rather than sleeping and hoping, and releases the locker in `finally`. Every timeout
test carries a hard external deadline (15–20 s) so a hang fails fast instead of stalling CI.

**Timing vocabulary (GC-DHI-04B-C1).** Three different durations must not be conflated:

| Term | Meaning |
|---|---|
| Server wait | How long PostgreSQL actually waited before raising the timeout. For the lock test this is bounded by `lock_timeout` (300 ms) and asserted to be far below the 10 s statement timeout. |
| Test-body duration | The stopwatch inside the test, covering only the statement under test. |
| Whole-process wall-clock | What `dotnet test` reports for an isolated run, which is dominated by container pull/start and fixture setup, not by the timeout. |

An isolated single-test run therefore reports tens of seconds of wall-clock while the server wait
is well under one second. Wall-clock is never presented as the lock or statement wait. The
idle test deliberately asserts that the session is no longer usable rather than one specific
Npgsql exception subtype, because which subtype surfaces depends on whether the driver notices
the closed backend while writing or while reading; the cleanup contract is not weakened by this.

## 24. CI

| Job | Suites | Filters |
|---|---|---|
| Ubuntu | Restore, Build, UnitTests, IntegrationTests (non-server), IntegrationTests (server), Pack, Upload | `Category!=PostgreSqlServer` then `Category=PostgreSqlServer` |
| Windows | Restore, Build, UnitTests, IntegrationTests (non-server), CLI smoke | `Category!=PostgreSqlServer` |

Counts after GC-DHI-04B-C2:

```text
Unit-test list entries:              950
Unit-test runtime executions:        955
Non-server IntegrationTests:           1
PostgreSQLServer IntegrationTests:    12
Local total:                         968
Expected Ubuntu total:               968
Expected Windows total:              956
```

Discovery and runtime differ because `MemberData` theories expand at run time; no test is
adjusted merely to make the two numbers agree.

Suites are split so the server suite is never run twice and never reported as skipped. Windows
*excludes* the trait rather than skipping it, so both jobs report zero skipped tests. Ubuntu's
total is expected to exceed Windows' because only Ubuntu runs the server suite; the two are never
presented as equal. Pack still runs only after all Ubuntu tests. No remote PostgreSQL, no
silent no-op environment flag and no 15/18 matrix in this gate.

## 25. Seguridad y secretos

Sanitized exceptions carry a fixed message and nothing else (§15). SQL text is static with no
value interpolation. Bound values never appear in an exception, and option values never appear in
a validation message. Fixture credentials are synthetic, scoped to the container lifetime, and no
connection string or password is written to test output. Synthetic markers planted in an
`NpgsqlException`/`PostgresException` message, SQLSTATE, detail, hint, schema, table, column,
constraint, internal query, where, routine, `Data` and inner exception are asserted absent from
the session exception's message, `ToString()`, stack trace, `Data`, `InnerException` and from the
session state.

## 26. Limitaciones

- The validator is intentionally not a parser; it proves only the two narrow shapes this gate
  needs and rejects everything else, including forms that would be safe under a full parser. It
  is *lexical*, so a purely grammatical mutation whose tokens are each individually legal — for
  example deleting the `||` from B002, leaving `$1::text 'ms'` — is not something it can or claims
  to detect. Nothing depends on it doing so: the inventory's SQL is a compile-time constant no
  caller can mutate, and the PostgreSQL 18 suite executes each statement for real, where the
  server rejects a malformed statement outright. A test pins this boundary explicitly.
- `WITH` is rejected wholesale (§10); a later gate needing CTEs must revisit it.
- `NpgsqlStatementGateway`'s command construction — specifically that the transaction is assigned
  to the command — cannot be exercised without a live transaction object, so it is proven by the
  PostgreSQL 18 integration suite rather than by a unit test.
- The idle-in-transaction test asserts unusability rather than a specific exception subtype
  (§23).
- Cleanup captures every exception type transparently through `ExceptionDispatchInfo` (§18). An
  unexpected cleanup failure propagates unchanged only when no primary exists; it can never mask
  an existing primary or requested cancellation.
- `StackOverflowException` cannot be caught or thrown meaningfully in .NET; it is excluded from
  the propagation theory and the absence of a classifying catch-all is verified by inspection of
  the typed catches instead.
- The six transparent capture sites (§17) do use `catch (Exception)`. They are the mechanism that
  *preserves* exceptions rather than one that consumes them. Same-instance assertions cover the
  primary-preservation matrices, while named stack tests cover the session and command-construction
  paths required by this gate.

## 27. Trabajo diferido

CLI error format and connection-source precedence; console and JSON rendering; final minimum
permissions for the 04C–04E metadata inventory; final hostname/database/username reporting
policy; invalid-index fixture strategy; and the permanent PostgreSQL 15/18 CI matrix. PG-06
completes only in GC-DHI-04F.

## 28. Prohibiciones

No type in this gate is `public`. No capability probe, version/database/current-user query,
`pg_class`/`pg_namespace`/`pg_index`/`pg_stat_*` metadata query, business-row read, `COUNT(*)`
over user data, query plan or `pg_stat_statements`. No user SQL, dynamic SQL, concatenated values
or identifiers, batch, explicit prepare or multi-statement command. No retry, ambient
transaction, savepoint, nested transaction or commit. No connection, transaction, command,
SQLSTATE or raw SQL crosses the boundary. No change to Core, CLI, JSON, console or exit codes. No
logging and no environment access. No `xUnit1051` suppression and no skipped tests.
