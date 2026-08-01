# GC-DHI-04B — Read-Only Session and SQL Safety Kernel

**Definition date:** 2026-07-31  
**Status:** Defined  
**Backlog:** PG-02 and PG-06 foundation  
**Predecessor:** GC-DHI-04A approved and closed  
**Verdict:** DEFINED — GC-DHI-04B IMPLEMENTATION AUTHORIZED NEXT

This document is an architecture and governance definition. It adds no product
code, executable SQL resource, test, dependency, project or CI change.

## 1. Objective

GC-DHI-04B defines the internal PostgreSQL infrastructure that will:

1. open a connection through `PostgreSqlConnectionFactory`;
2. begin one explicit transaction at `IsolationLevel.RepeatableRead`;
3. establish `READ ONLY` before the first query;
4. apply three transaction-local timeouts;
5. verify the effective session state;
6. execute only registered statements through closed IDs;
7. reject arbitrary or user-supplied SQL;
8. parameterize every variable value;
9. propagate requested cancellation;
10. sanitize expected PostgreSQL/Npgsql failures;
11. preserve unexpected failures;
12. finish through rollback in every outcome;
13. hide connections, transactions, commands and raw SQL outside the kernel;
14. prove against PostgreSQL 18 that persistent writes fail;
15. prove that success, failure and cancellation persist no changes; and
16. establish the SQL-safety foundation extended and finally verified in
    GC-DHI-04F.

GC-DHI-04B does not define functional metadata queries.

## 2. Backlog

| Item | Coverage in GC-DHI-04B | Completion status |
|---|---|---|
| `PG-02 — Implement read-only inspection session` | Full session lifecycle, verification, timeouts, cancellation and rollback contract | Authorized for implementation after this definition is integrated |
| `PG-06 — Enforce SQL safety allowlist` | Closed statement inventory, validator and executor foundation | Foundation authorized; backlog item completes only in GC-DHI-04F |

## 3. Scope

Included:

- immutable `PostgreSqlInspectionSessionOptions`;
- an internal session runner and bounded session state;
- connection and transaction lifecycle;
- `RepeatableRead`, read-only and rollback-only enforcement;
- transaction-local statement, lock and idle-in-transaction timeouts;
- verification of effective state before an authorized operation;
- closed statement IDs, definitions, inventory and parameter definitions;
- a fail-closed SQL safety validator;
- an executor that resolves only inventoried IDs;
- deterministic cancellation and error precedence;
- sanitized expected infrastructure failures;
- unit, safety-contract and focused PostgreSQL 18 integration tests;
- a focused CI adjustment during implementation when required; and
- an implementation design document.

Excluded:

- capability and server-version probing;
- database-name or current-user reporting;
- table or index metadata SQL;
- functional schema filters;
- `DatabaseSnapshot` mapping and `IDatabaseSnapshotProvider`;
- DBH001–DBH005;
- CLI behavior, connection-source resolution, JSON, console output and exit
  codes;
- permanent PostgreSQL 15/18 CI matrix;
- public Docker demo, tag, release or package publication.

## 4. Architecture

All new production types remain `internal` in
`DbHealthInspector.PostgreSql`. The dependency direction remains:

```text
DbHealthInspector.PostgreSql → DbHealthInspector.Core
DbHealthInspector.Core       → no infrastructure dependency
```

Recommended logical structure, without requiring these exact filenames:

```text
src/DbHealthInspector.PostgreSql/
├── Sessions/
│   ├── PostgreSqlInspectionSessionOptions.cs
│   ├── PostgreSqlInspectionSessionRunner.cs
│   ├── PostgreSqlInspectionSessionState.cs
│   ├── PostgreSqlInspectionSessionException.cs
│   └── PostgreSqlInspectionSessionFailureKind.cs
└── Sql/
    ├── PostgreSqlSqlStatementId.cs
    ├── PostgreSqlSqlCommandKind.cs
    ├── PostgreSqlSqlParameterDefinition.cs
    ├── PostgreSqlSqlStatementDefinition.cs
    ├── PostgreSqlSqlInventory.cs
    ├── PostgreSqlSqlSafetyValidator.cs
    └── PostgreSqlSqlExecutor.cs
```

Tests are expected under UnitTests `Sessions/` and `Sql/`, and IntegrationTests
`PostgreSqlServer/` and `TestSupport/`. The implementation design document is
expected at `docs/design/postgresql-read-only-session-and-sql-safety.md`.

No new interface is added to Core. No connection, transaction, command, raw
SQL, connection string, server exception or SQLSTATE crosses this boundary.

## 5. Transaction model

The transaction contract is frozen:

```text
Isolation level: RepeatableRead
Access mode: Read Only
Deferrable: false
Completion: rollback only
Autocommit inspection path: prohibited
```

Required sequence:

```text
PostgreSqlConnectionFactory.OpenConnectionAsync
    ↓
NpgsqlConnection.BeginTransactionAsync(
    IsolationLevel.RepeatableRead,
    cancellationToken)
    ↓
B001 — SET TRANSACTION READ ONLY
    ↓
B002 — Apply transaction-local timeouts
    ↓
B003 — Verify effective session state
    ↓
Execute authorized operation
    ↓
Rollback/dispose transaction
    ↓
Dispose connection
```

B001 is the first statement executed. No `SELECT`, `SHOW`, `SET LOCAL` or
`set_config` may precede it. The kernel has no `READ COMMITTED`, `SERIALIZABLE`,
`DEFERRABLE`, ambient `TransactionScope`, savepoint, nested-transaction,
transaction-reuse, `COMMIT` or autocommit inspection path.

`RepeatableRead` lets future 04C–04E operations observe one logical snapshot
without the waiting behavior and additional cost of `SERIALIZABLE READ ONLY
DEFERRABLE`.

## 6. Timeout policy

`PostgreSqlInspectionSessionOptions` is internal and immutable and provides a
`Default` instance.

| Option | Default | Minimum | Maximum | Relation |
|---|---:|---:|---:|---|
| `StatementTimeout` | 30 seconds | 100 milliseconds | 5 minutes | — |
| `LockTimeout` | 5 seconds | 50 milliseconds | 30 seconds | Strictly less than `StatementTimeout` |
| `IdleInTransactionTimeout` | 60 seconds | 250 milliseconds | 10 minutes | — |

Validation occurs before opening a connection. Zero, negative,
`Timeout.InfiniteTimeSpan`, sub-millisecond precision and checked millisecond
overflow are rejected with the correct `ParamName`. Sanitized failures do not
include option values.

The implementation must not add `transaction_timeout`, `idle_session_timeout`,
a command-timeout override or a retry count. These defaults belong to the
adapter and are not final CLI defaults. This section is the single normative
source for the values and ranges; summaries in project state, backlog and the
parent gate definition are non-normative references to it.

## 7. Initial SQL inventory

The initial production inventory contains exactly three statements.

### B001 — SetTransactionReadOnly

- ID: `SetTransactionReadOnly`
- Command kind: `SetTransactionReadOnly`
- Parameters: none
- Purpose: establish read-only transaction mode before any query

```sql
SET TRANSACTION READ ONLY
```

### B002 — ApplyLocalTimeouts

- ID: `ApplyLocalTimeouts`
- Command kind: `SelectConfiguration`
- Purpose: apply all timeouts transaction-locally

```sql
SELECT
    pg_catalog.set_config(
        'statement_timeout',
        $1::text || 'ms',
        true),
    pg_catalog.set_config(
        'lock_timeout',
        $2::text || 'ms',
        true),
    pg_catalog.set_config(
        'idle_in_transaction_session_timeout',
        $3::text || 'ms',
        true)
```

Ordered parameters:

| Position | Meaning | Declared type |
|---:|---|---|
| `$1` | statement-timeout milliseconds | integer |
| `$2` | lock-timeout milliseconds | integer |
| `$3` | idle-in-transaction-timeout milliseconds | integer |

### B003 — VerifySessionState

- ID: `VerifySessionState`
- Command kind: `SelectVerification`
- Purpose: block the operation unless every effective setting matches

```sql
SELECT
    pg_catalog.current_setting(
        'transaction_read_only')::boolean
        AS is_read_only,
    pg_catalog.current_setting(
        'transaction_isolation')
        AS isolation_level,
    pg_catalog.current_setting(
        'statement_timeout')::interval
        = ($1 * interval '1 millisecond')
        AS statement_timeout_matches,
    pg_catalog.current_setting(
        'lock_timeout')::interval
        = ($2 * interval '1 millisecond')
        AS lock_timeout_matches,
    pg_catalog.current_setting(
        'idle_in_transaction_session_timeout')::interval
        = ($3 * interval '1 millisecond')
        AS idle_timeout_matches
```

B003 uses the same three ordered integer millisecond parameters as B002. It
must produce exactly one row where:

```text
is_read_only = true
isolation_level = repeatable read
statement_timeout_matches = true
lock_timeout_matches = true
idle_timeout_matches = true
```

Any missing row, extra row, null, unexpected value or false comparison blocks
the authorized operation. No fourth production statement may be added in 04B.

## 8. SQL definition model

Internal types equivalent to the following responsibilities are authorized:

- `PostgreSqlSqlStatementId`: closed enum or value type containing only the
  three IDs in section 7;
- `PostgreSqlSqlCommandKind`: closed command-shape classification;
- `PostgreSqlSqlParameterDefinition`: ordered position and declared type;
- `PostgreSqlSqlStatementDefinition`: ID, kind, static SQL, ordered parameters
  and security purpose;
- `PostgreSqlSqlInventory`: one immutable canonical source in deterministic
  order.

The inventory must reject duplicate IDs, unknown IDs, lazy mutation and
mutable SQL. It must not scan assemblies by reflection, load SQL from external
directories or user files, accept runtime configuration, concatenate values or
identifiers, or retain logging metadata containing bound values.

## 9. SQL safety validator

`PostgreSqlSqlSafetyValidator` is a fail-closed defense in depth, not a complete
PostgreSQL parser. Every inventoried statement is validated before execution.

Lexical minimum:

- reject null, blank, byte NUL, `--`, `/* */`, dollar-quoted blocks, psql
  backslash commands, semicolons and multiple statements;
- tokenize outside string literals and quoted identifiers;
- reject any unknown form;
- accept in 04B only the exact `SET TRANSACTION READ ONLY` shape or a safely
  classified `SELECT`;
- leave `SHOW` and `SET LOCAL` un-inventoried for possible later gates.

Prohibited tokens outside strings and quoted identifiers:

```text
INSERT UPDATE DELETE MERGE CREATE ALTER DROP TRUNCATE VACUUM ANALYZE
REINDEX GRANT REVOKE COPY CALL DO EXECUTE PREPARE DEALLOCATE LOCK CLUSTER
CHECKPOINT REFRESH IMPORT REASSIGN SECURITY LISTEN NOTIFY UNLISTEN DISCARD
RESET LOAD COMMENT
```

For `SELECT`, reject `SELECT INTO`, `FOR UPDATE`, `FOR NO KEY UPDATE`, `FOR
SHARE`, `FOR KEY SHARE` and data-modifying CTEs. A `WITH` statement is accepted
only when the validator can prove the effective statement is `SELECT` and no
CTE contains DML.

Positional placeholders must start at `$1`, remain consecutive, have no gaps,
match the definition count and types exactly, and all be used. Identifiers
cannot be parameters. Undeclared, unused, duplicated-as-definition or extra
parameters are rejected. Any `SET` form other than exact B001 is rejected.

## 10. SQL executor

`PostgreSqlSqlExecutor` is bound to one existing connection and transaction and
accepts only `PostgreSqlSqlStatementId` plus values matching the resolved
definition. It:

- resolves SQL through the canonical inventory;
- validates parameter count, order and type;
- creates positional parameters without interpolation;
- uses asynchronous Npgsql APIs and the exact cancellation token;
- disposes commands and readers correctly; and
- returns only statement-specific internal results needed by the runner or the
  authorized callback.

It accepts no `string sql`, exposes no `NpgsqlCommand` or mutable
`CommandText`, and permits no batch, multi-statement command, explicit prepare
or identifier substitution.

## 11. Session runner

Authorized internal responsibilities may be expressed by types equivalent to:

```text
PostgreSqlInspectionSessionRunner
PostgreSqlInspectionSession
PostgreSqlInspectionSessionState
PostgreSqlInspectionSessionException
PostgreSqlInspectionSessionFailureKind
```

The runner validates options, opens through the 04A factory, begins the
transaction, runs B001 → B002 → B003, validates the verification row, invokes
one authorized operation, preserves its result or primary failure, rolls back,
and disposes transaction before connection.

The operation receives only:

```text
PostgreSqlSqlExecutor
CancellationToken
```

It never receives an Npgsql connection, transaction, connection string or raw
SQL. A session is internal, non-concurrent, confined to the runner callback,
allows one active operation, exposes no commit path and cannot survive the
runner.

Completion is invariant:

```text
success      → rollback + dispose
failure      → preserve primary failure + rollback/dispose
cancellation → preserve cancellation + rollback/dispose
```

## 12. Error and cancellation contract

Requested cancellation is checked before open and propagated through open,
begin, B001, B002, B003, operation and cleanup. It dominates an expected
infrastructure failure, is never converted into a session failure and never
returns a partial result.

Expected `NpgsqlException` and `PostgresException` failures during begin,
initialization, verification, authorized execution or cleanup are sanitized by
stage without retaining the source message, SQLSTATE, `Detail`, `Hint`, schema,
table, column, constraint, SQL text, parameters, connection metadata, inner
exception or `Data`. A verification mismatch uses the initialization fixed
message while retaining the distinct `VerificationFailed` kind.

Fixed messages:

```text
Initialization: "The PostgreSQL inspection session could not be initialized."
Execution:      "The PostgreSQL inspection operation failed."
Cleanup:        "The PostgreSQL inspection session could not be closed safely."
```

Allowed failure kinds:

```text
InitializationFailed
VerificationFailed
ExecutionFailed
CleanupFailed
```

Unexpected `InvalidOperationException`, `ObjectDisposedException`,
`ArgumentException`, `NullReferenceException`, non-Npgsql `TimeoutException`,
`OutOfMemoryException`, `StackOverflowException` and
`AccessViolationException` propagate unchanged. Broad `catch (Exception)`
filters are prohibited.

Precedence:

```text
Requested cancellation > expected infrastructure failure
Primary operation failure > cleanup failure
Cleanup failure surfaces only when no earlier failure exists
```

## 13. Cleanup and rollback

An uncommitted `NpgsqlTransaction.DisposeAsync()` performs rollback. The runner
may perform one explicit rollback when it is needed to verify completion, but
must not rollback twice. Transaction disposal precedes connection disposal.

The connection is always disposed. The runner does not dispose a connection
factory it does not own, retain a connection after the callback or promise
concurrent use of one session. There is no finalizer, sync-over-async,
`.Result`, `.Wait()`, `GetAwaiter().GetResult()` or `Task.Run`.

Cleanup cannot replace requested cancellation or an earlier primary error.

## 14. Unit testing

Required unit coverage includes:

- option defaults, exact bounds, below/above bounds, zero, negative, infinite,
  sub-millisecond, lock equal/greater than statement timeout and overflow;
- three exact inventory entries, unique IDs, stable order, correct kinds,
  immutable SQL, ordered parameters and unknown-ID rejection;
- every prohibited validator class, mixed case and whitespace, prohibited words
  inside strings, quoted identifiers, comments, semicolon, two statements,
  locking selects, `SELECT INTO`, DML CTE and all placeholder errors;
- deterministic runner sequence B001 → B002 → B003, no callback before
  verification, false verification blocking, exact token forwarding,
  cancellation and primary-failure precedence, cleanup-only failure,
  single callback invocation, disposal and absence of commit;
- synthetic secret markers in Npgsql exception message, `Data`, inner
  exception and constructible PostgreSQL metadata, absent from every exposed
  surface.

Deterministic seams must not require a network, Docker, DNS, sleeps or assumed
closed ports.

## 15. PostgreSQL integration testing

GC-DHI-04B uses a focused PostgreSQL 18 Testcontainers suite. The
implementation must pin an exact PostgreSQL 18 image tag and immutable digest,
record both in its design and handoff, and never use `latest`. The permanent
PostgreSQL 15/18 matrix remains reserved for GC-DHI-04F.

The fixture creates a synthetic persistent schema, persistent control table and
one control row. The inspected role is not a superuser and receives `CONNECT`,
schema `USAGE`, `SELECT` and `UPDATE` on the control table. Granting `UPDATE` is
mandatory so write rejection proves read-only enforcement rather than missing
permission.

Using the exact production initialization sequence, a test-only update against
the persistent control table must fail with SQLSTATE `25006`
(`read_only_sql_transaction`). The update appears only in IntegrationTests,
never in the production inventory or assembly. The original row, schema,
transaction and connection state are verified afterward.

Real-server paths cover:

- success: synthetic select, rollback/disposal and unchanged persistent state;
- failure: post-initialization synthetic failure, correct propagation or
  sanitization, cleanup and unchanged state;
- cancellation: cancelable synthetic operation, associated
  `OperationCanceledException`, cleanup and unchanged state;
- statement timeout: reduced valid options and test-only `pg_sleep`, sanitized
  server timeout and later pool reuse;
- lock timeout: a second connection holds an incompatible lock, lock timeout
  occurs before statement timeout, lock released in `finally`, no changes;
- idle-in-transaction timeout: strict temporal bounds, terminated or invalid
  session, pool recovery, no open transaction and no changes.

Test-only write, sleep and lock SQL never enters the production inventory.

## 16. Safety contracts

The implementation must prove:

- effective transaction state is read-only and repeatable read;
- B001 is the first statement and B001 → B002 → B003 ordering is exact;
- arbitrary SQL and every prohibited class are rejected;
- no production API accepts raw SQL;
- no product assembly contains test-only write, sleep or lock SQL;
- persistent control row and schema remain unchanged after success, failure,
  cancellation and timeout;
- no transaction or connection survives completion;
- no business-row query is added to production; and
- no secret appears in source, test output, package or audited artifact.

## 17. CI strategy

GC-DHI-04B may make only the focused CI change needed for the server suite.

Ubuntu runs restore, build, UnitTests, non-server IntegrationTests,
`PostgreSqlServer` IntegrationTests through Docker/Testcontainers, pack and
artifact upload. Windows runs restore, build, UnitTests, non-server
IntegrationTests and the CLI smoke test.

Server tests use:

```csharp
[Trait("Category", "PostgreSqlServer")]
```

Windows excludes that trait instead of reporting skipped tests. Both jobs must
report zero skipped tests. Counts are recorded separately and never presented
as equal when Ubuntu includes the server suite.

```text
Baseline before implementation: 479
Final exact counts: determined by implementation
Ubuntu > Windows because Ubuntu includes PostgreSqlServer tests
```

CI does not use a remote PostgreSQL server, a silent no-op environment flag or
a permanent 15/18 matrix in this gate.

## 18. Security and secret handling

Secrets must not appear in exceptions, output, container logs uploaded by CI,
snapshots, SQL with bound values, command diagnostics, assertion messages,
packages or artifacts. Tests use synthetic credentials only, never print a
connection string and do not persist passwords outside their fixture.

Sanitization scans source, captured test output, package and artifact. SQL text
is static and contains no value interpolation. Bound values and connection
metadata are never included in user-facing or cross-boundary exceptions.

## 19. Entry criteria

Implementation may begin only when:

1. GC-DHI-04A remains approved and closed;
2. this definition is integrated into `master`;
3. the documentation-only CI is green;
4. `PROJECT_STATE.md` authorizes only GC-DHI-04B;
5. GC-DHI-04C through GC-DHI-04F remain unauthorized;
6. the Claude Code prompt references this document;
7. GC-DHI-04A has no open finding;
8. the repository is clean and synchronized;
9. timeout defaults and ranges are frozen;
10. the inventory of three statements is frozen;
11. the focused PostgreSQL 18 strategy is recorded; and
12. the separate Ubuntu/Windows CI strategy is recorded.

## 20. Exit criteria

The candidate is ready for Codex review only when:

1. PG-02 is implemented;
2. the PG-06 foundation is implemented;
3. `RepeatableRead` read-only state is verified;
4. all three effective timeouts are verified;
5. no autocommit inspection path exists;
6. no commit path exists;
7. the inventory contains exactly B001, B002 and B003;
8. no raw-SQL API exists;
9. the validator fails closed;
10. prohibited-statement tests pass;
11. a permitted-role persistent write fails with SQLSTATE 25006;
12. the persistent control row remains unchanged;
13. the persistent schema remains unchanged;
14. success, failure and cancellation clean all resources;
15. timeout integration tests pass;
16. UnitTests pass;
17. real PostgreSQL 18 tests pass on Ubuntu;
18. Windows passes without skipped tests;
19. build has zero warnings and errors;
20. formatting passes;
21. dependencies have no known vulnerable or deprecated package;
22. secret scans pass;
23. Core and CLI remain unchanged;
24. GC-DHI-04C is not started; and
25. Claude Code performs no remote operation.

Integration and closure still require Codex review, human approval, pull
request, green CI, artifact audit, governance record and final human closure.

## 21. Prohibitions

GC-DHI-04B must not:

- implement GC-DHI-04C or query version, database name or current user as
  product functionality;
- query `pg_class`, `pg_index`, statistics or business rows;
- implement functional schema filters, snapshot mapping or a snapshot provider;
- use `COUNT(*)`, query plans or `pg_stat_statements`;
- accept user SQL, dynamic SQL, concatenated values or identifiers, batches,
  explicit prepared statements or multi-statement commands;
- add retries, ambient transactions, savepoints, nested transactions or commit;
- expose connection, transaction, command, SQLSTATE or raw SQL;
- change Core, CLI behavior, JSON, console or exit codes;
- implement DBH001–DBH005, public Docker demo, tag, release or publication.

This definition execution itself must not change product code, tests,
dependencies, projects, CI, ADRs, SECURITY.md or design documents.

## 22. Deferred decisions

The following remain deferred after GC-DHI-04B:

- CLI error format and connection-source precedence;
- console and JSON rendering;
- final minimum permissions for the 04C–04E metadata inventory;
- final hostname, database and username reporting policy;
- invalid-index fixture strategy; and
- permanent PostgreSQL 15/18 CI matrix.

The timeout defaults, isolation level, completion policy, initial SQL inventory
and focused PostgreSQL 18 strategy are resolved and no longer deferred.

## 23. Risks

| Risk | Required control |
|---|---|
| A write path survives | Read-only verification plus permitted-role SQLSTATE 25006 test |
| SQL validator is mistaken for a full parser | Fail closed, static inventory and exact-shape tests |
| Cleanup hides the primary failure | Explicit precedence matrix and deterministic seams |
| Timeout tests become flaky | Strict temporal bounds, focused Ubuntu execution and pool-recovery checks |
| Server errors leak sensitive detail | Stage-specific fixed exceptions and synthetic-marker scans |
| Test SQL enters product | Assembly/inventory scans and separated IntegrationTests fixtures |
| CI reports false parity | Separate Ubuntu/Windows counts and zero-skipped policy |
| PostgreSQL image drifts | Exact tag and digest recorded by implementation |
| Scope expands into metadata | Exactly three production statements and sequential gate authorization |

## 24. Authorization

GC-DHI-04A remains approved and closed. After this document is integrated into
`master` and its documentation-only CI is green, preparing a separate Claude
Code implementation prompt for GC-DHI-04B is authorized.

This document does not start implementation. GC-DHI-04C through GC-DHI-04F
remain unauthorized.

```text
DEFINED — GC-DHI-04B IMPLEMENTATION AUTHORIZED NEXT
```
