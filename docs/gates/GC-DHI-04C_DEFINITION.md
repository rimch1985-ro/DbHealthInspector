# GC-DHI-04C — Server Metadata and Capability Probe

**Definition date:** 2026-08-01  
**Status:** Defined  
**Backlog:** PG-03  
**Predecessor:** GC-DHI-04B approved and closed  
**Implementation:** not authorized  
**Verdict:** DEFINED — AWAITING HUMAN IMPLEMENTATION AUTHORIZATION

This document freezes the technical scope and authorization criteria for a
future GC-DHI-04C implementation. Integrating this definition does not
authorize or start product implementation. Implementation requires subsequent
human approval and a separate Claude Code prompt.

## 1. Objective

GC-DHI-04C defines a future internal PostgreSQL capability probe that executes
only inside the verified GC-DHI-04B session and:

1. reads a machine-readable PostgreSQL version and server identity;
2. normalizes the version and determines its major;
3. reports whether the major is in the supported 15–18 range;
4. checks required catalog-metadata access;
5. checks optional usage-statistics access separately;
6. reads `stats_reset` only when usage statistics are available;
7. composes all three existing Core capability states;
8. keeps data profiling disabled by product policy;
9. maps only to existing Core models; and
10. preserves the cancellation, rollback, cleanup and secret-hygiene contracts
    established by GC-DHI-04A and GC-DHI-04B.

Required failures propagate through a sanitized boundary. Only the explicitly
safe loss of optional statistics may degrade to an unavailable capability.

## 2. Authorization status

The authorized work for this gate is this definition only. No code, executable
SQL resource, test, dependency, project, workflow or running database is added.

Future implementation may begin only after:

1. this definition is integrated;
2. its documentation-only CI is green;
3. the definition is reviewed;
4. the human project owner explicitly authorizes implementation; and
5. a separate Claude Code implementation prompt is issued.

GC-DHI-04D through GC-DHI-04F remain unauthorized, unimplemented and not
started.

## 3. Canonical constraints

The future implementation must preserve:

```text
DbHealthInspector.PostgreSql -> DbHealthInspector.Core
DbHealthInspector.Core       -> no infrastructure dependency
```

All new PostgreSQL probe types are internal. No Npgsql type, SQL text,
SQLSTATE, connection, transaction, command or PostgreSQL-specific enum enters
Core. No new Core type or production project is authorized.

The probe consumes these existing Core contracts without modifying them:

- `DatabaseMetadata` requires an engine, normalized engine version, database
  name and optional current user;
- `CapabilitySnapshot` requires exactly one state for every existing
  `CapabilityKind`;
- the only capability kinds are `CatalogMetadata`, `UsageStatistics` and
  `DataProfiling`;
- an `Available` capability has a null reason;
- `StatisticsSnapshot` accepts a nullable UTC `DateTimeOffset` and rejects a
  non-zero offset; and
- `DatabaseEngine.PostgreSql` is the exact engine value.

The GC-DHI-04B session remains `RepeatableRead`, explicitly read-only,
non-deferrable, rollback-only and protected by transaction-local timeouts.

## 4. Scope

The future implementation is limited to:

- version and server-identity acquisition;
- version normalization and supported-range status;
- required catalog-metadata capability checking;
- optional usage-statistics capability checking;
- nullable statistics-reset acquisition;
- deterministic capability composition;
- an internal immutable probe result;
- typed operational methods for C001–C004;
- deterministic unit tests;
- focused real PostgreSQL 18 integration tests; and
- reuse of the current Ubuntu/Windows CI strategy.

It does not build table or index snapshots, a complete `DatabaseSnapshot`, a
snapshot provider, CLI/report behavior or executable diagnostics.

## 5. Result contract

The future implementation defines an internal result equivalent to:

```text
internal sealed class PostgreSqlServerProbeResult

Properties:
- DatabaseMetadata Metadata
- CapabilitySnapshot Capabilities
- StatisticsSnapshot Statistics
- int ServerVersionNumber
- int MajorVersion
- PostgreSqlVersionSupportStatus VersionSupport
```

`PostgreSqlVersionSupportStatus` is an internal closed enum with exactly:

```text
Supported
Unsupported
```

The result is immutable: constructor validation, get-only properties, no
setters and no mutable collection. It contains no Npgsql type, SQLSTATE,
connection, transaction, command, connection string or raw SQL. It must not
override `ToString()` in a way that renders database name or current user.

`Metadata.Engine` is exactly `DatabaseEngine.PostgreSql`. Metadata uses the
normalized version, database name and current user returned by C001.

## 6. Version normalization

The only version source is the integer `server_version_num` returned by C001.
The implementation must not parse `version()`, textual `server_version`, vendor
package suffixes, platform strings or build strings.

For PostgreSQL 10 or newer (`versionNumber >= 100000`):

```text
major = versionNumber / 10000
minor = versionNumber % 10000
normalized = "<major>.<minor>"
```

Frozen examples:

```text
150000 -> 15.0
150016 -> 15.16
180004 -> 18.4
190000 -> 19.0
```

For a PostgreSQL version before 10, solely to represent an unsupported server:

```text
major = versionNumber / 10000
minor = (versionNumber / 100) % 100
patch = versionNumber % 100
normalized = "<major>.<minor>.<patch>"
```

Frozen example:

```text
90624 -> 9.6.24
```

The implementation uses checked numeric conversion and rejects zero, negative,
overflowed or structurally impossible encoded values with a fixed internal
mapping failure. Version support is determined numerically, never textually.

## 7. Supported-version policy

The supported range is:

```text
15 <= MajorVersion <= 18
```

Majors below 15 or above 18 are represented explicitly as `Unsupported`.
Unsupported status is not itself an exception.

For an unsupported version:

1. execute C001 only;
2. do not execute C002, C003 or C004;
3. create `DatabaseMetadata` from the normalized version and recovered
   identity;
4. set `CatalogMetadata` to `Unavailable`;
5. set `UsageStatistics` to `Unavailable`;
6. set `DataProfiling` to `Disabled`;
7. set `StatisticsResetAtUtc` to null; and
8. set `VersionSupport` to `Unsupported`.

The exact reason for both unavailable capabilities is:

```text
The PostgreSQL server version is outside the supported range.
```

The reason never includes the real version, database name, current user or
server detail.

## 8. Required catalog capability

`CatalogMetadata` is mandatory for a supported server.

When C002 returns true:

```text
CatalogMetadata -> Available
Reason          -> null
```

When C002 returns false:

1. do not execute C003 or C004;
2. do not return a partial result; and
3. throw an internal sealed required-capability exception with the exact fixed
   message:

```text
Required PostgreSQL catalog metadata is unavailable.
```

The exception has no inner exception, has empty `Data`, and contains no object
name, SQL, current user, database name, SQLSTATE or PostgreSQL message.

An Npgsql error during C002 remains an expected operational failure handled by
the sanitized GC-DHI-04B session boundary. It never becomes optional
capability degradation.

## 9. Optional statistics and data-profiling policy

`UsageStatistics` is optional.

When C003 returns true:

```text
UsageStatistics -> Available
Reason          -> null
```

C004 then executes and supplies the nullable statistics-reset timestamp.

When C003 returns false:

```text
UsageStatistics -> Unavailable
Reason          -> Usage statistics are unavailable for this inspection.
```

C004 must not execute. `StatisticsResetAtUtc` is null.

If C003 returned true but C004 loses permission in a race and fails with exact
SQLSTATE `42501`, the implementation must re-check requested cancellation
before degrading. Only that case becomes `UsageStatistics.Unavailable` with
the same fixed generic reason. The PostgreSQL exception is discarded entirely:
it is not retained as an inner exception, in `Data`, a field, closure or log.
Every other C004 error propagates to the sanitized session boundary.

`DataProfiling` is always:

```text
DataProfiling -> Disabled
Reason        -> Data profiling is disabled by product policy.
```

No business-row access is enabled.

## 10. Capability composition order

States are constructed in this deterministic order:

```text
CatalogMetadata
UsageStatistics
DataProfiling
```

There is exactly one state for every existing `CapabilityKind`. The probe may
not omit or duplicate a kind, invent a fourth kind, attach a reason to an
`Available` state, or use a server value as a reason.

Capability outcomes are frozen as follows:

| Scenario | CatalogMetadata | UsageStatistics | DataProfiling |
|---|---|---|---|
| Supported; both checks true | Available / null | Available / null | Disabled / policy reason |
| Supported; C003 false | Available / null | Unavailable / statistics reason | Disabled / policy reason |
| Unsupported major | Unavailable / version reason | Unavailable / version reason | Disabled / policy reason |
| Supported; C002 false | Fixed required-capability exception; no result | Not evaluated | Not returned |

## 11. Frozen SQL inventory

After a future GC-DHI-04C implementation, the productive inventory contains
exactly seven statements in this order:

| ID | Command kind | Parameters | Purpose |
|---|---|---|---|
| B001 — `SetTransactionReadOnly` | `SetTransactionReadOnly` | None | Establish read-only mode |
| B002 — `ApplyLocalTimeouts` | `SelectConfiguration` | Three Int32 values | Apply transaction-local timeouts |
| B003 — `VerifySessionState` | `SelectVerification` | Three Int32 values | Verify the effective session state |
| C001 — `ReadServerIdentity` | `SelectServerIdentity` | None | Read numeric version and identity |
| C002 — `CheckCatalogMetadataAccess` | `SelectCapabilityCheck` | None | Check required catalog access |
| C003 — `CheckUsageStatisticsAccess` | `SelectCapabilityCheck` | None | Check optional statistics access |
| C004 — `ReadStatisticsReset` | `SelectStatistics` | None | Read nullable `stats_reset` |

The statement-ID enum contains exactly:

```text
SetTransactionReadOnly
ApplyLocalTimeouts
VerifySessionState
ReadServerIdentity
CheckCatalogMetadataAccess
CheckUsageStatisticsAccess
ReadStatisticsReset
```

The command-kind enum contains exactly:

```text
SetTransactionReadOnly
SelectConfiguration
SelectVerification
SelectServerIdentity
SelectCapabilityCheck
SelectStatistics
```

C001–C004 are authorized operations, take no parameters, use static SQL and
contain no dynamic identifier, external SQL, business-row access or 04D/04E
query. No eighth statement is authorized.

## 12. Exact SQL

### C001 — ReadServerIdentity

```sql
SELECT
    pg_catalog.current_setting(
        'server_version_num')::integer
        AS server_version_number,
    pg_catalog.current_database()::text
        AS database_name,
    current_user::text
        AS current_user
```

Shape: exactly one row, exactly three columns, all non-null.

### C002 — CheckCatalogMetadataAccess

```sql
SELECT
    pg_catalog.has_schema_privilege(
        current_user,
        'pg_catalog',
        'USAGE')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_namespace',
        'SELECT')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_class',
        'SELECT')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_inherits',
        'SELECT')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_index',
        'SELECT')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_attribute',
        'SELECT')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_am',
        'SELECT')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_constraint',
        'SELECT')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_collation',
        'SELECT')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_opclass',
        'SELECT')
        AS catalog_metadata_available
```

Shape: exactly one row, exactly one non-null boolean column. This is the 04C
catalog allowlist baseline. Any catalog or function needed later by GC-DHI-04D
or GC-DHI-04E must be added explicitly in its own gate before use.

### C003 — CheckUsageStatisticsAccess

```sql
SELECT
    pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_stat_database',
        'SELECT')
    AND pg_catalog.has_table_privilege(
        current_user,
        'pg_catalog.pg_stat_all_indexes',
        'SELECT')
        AS usage_statistics_available
```

Shape: exactly one row, exactly one non-null boolean column. The check accepts
any effective privilege path recognized by PostgreSQL and does not require
direct membership in a predefined role.

### C004 — ReadStatisticsReset

```sql
SELECT
    statistics.stats_reset
FROM pg_catalog.pg_stat_database AS statistics
WHERE statistics.datname = pg_catalog.current_database()
```

Shape: exactly one row and exactly one column. The column may be null. Null
means statistics access is available but no reset timestamp was reported; it
does not make the capability unavailable.

## 13. Typed operation boundary

`PostgreSqlInspectionOperationExecutor` remains the only object handed to the
verified-session callback. Its generic method that currently rejects every ID
is replaced by typed operations equivalent to:

```text
ReadServerIdentityAsync
CheckCatalogMetadataAccessAsync
CheckUsageStatisticsAccessAsync
ReadStatisticsResetAsync
```

The boundary accepts no SQL string and no arbitrary statement ID. It exposes
neither `PostgreSqlSqlExecutor` nor a connection, transaction or command.
B001–B003 remain reserved exclusively to the runner. Every typed operation
resolves its fixed C001–C004 statement through the canonical inventory.

The composition component is an internal sealed type equivalent to:

```text
PostgreSqlServerCapabilityProbe

ProbeAsync(
    PostgreSqlInspectionOperationExecutor executor,
    CancellationToken cancellationToken)
    -> PostgreSqlServerProbeResult
```

No table/index dispatch or generic mapper is authorized.

## 14. Typed row access and result shapes

The row seams may be extended only with:

```text
GetInt32(int ordinal)
GetDateTimeOffset(int ordinal)
```

The additions apply to the minimal source/reader seams needed to forward those
typed values. They do not add `GetValue`, `object`, `dynamic`, a generic mapper
or `NpgsqlDataReader` exposure.

Frozen shape contracts:

| Statement | Rows | Columns | Nullability | Typed projection |
|---|---:|---:|---|---|
| C001 | Exactly 1 | Exactly 3 | None null | Int32, string, string |
| C002 | Exactly 1 | Exactly 1 | Non-null | Boolean |
| C003 | Exactly 1 | Exactly 1 | Non-null | Boolean |
| C004 | Exactly 1 | Exactly 1 | Nullable | DateTimeOffset when non-null |

`GetDateTimeOffset` is called only after a non-null check. A non-null C004
value must have `Offset == TimeSpan.Zero`; any other offset is a shape/mapping
failure and is not normalized silently. Zero rows, extra rows, wrong column
count or unexpected null are rejected.

## 15. Exact execution sequences

Supported version and statistics available:

```text
Verified GC-DHI-04B session
-> C001
-> normalize version
-> C002 true
-> C003 true
-> C004
-> compose result
-> rollback through GC-DHI-04B
```

Supported version and statistics unavailable:

```text
Verified session
-> C001
-> C002 true
-> C003 false
-> do not execute C004
-> compose unavailable statistics result
-> rollback
```

Unsupported version:

```text
Verified session
-> C001
-> do not execute C002/C003/C004
-> compose explicit unsupported result
-> rollback
```

Required catalog unavailable:

```text
Verified session
-> C001
-> C002 false
-> do not execute C003/C004
-> throw fixed sanitized required-capability exception
-> rollback
```

No branch commits, retries or returns a partial result.

## 16. Error model

Expected C001–C004 Npgsql errors propagate into the existing GC-DHI-04B
operation boundary and become its fixed sanitized `ExecutionFailed` outcome,
except for the single optional C004 `42501` degradation defined in section 9.

The implementation uses only typed, stage-local catches. Exact
`PostgresException.SqlState == "42501"` may be inspected only around C004 and
only after C003 confirmed availability. The caught exception is then discarded
after cancellation is re-checked. No catch-all classifier is authorized.

The fixed required-catalog exception contains only its fixed message and no
source failure. Shape failures, C001/C002 failures, non-`42501` Npgsql errors,
connection failures, transaction failures and unexpected exceptions never
degrade to an optional capability.

## 17. Cancellation, rollback and cleanup

All GC-DHI-04B contracts remain normative:

- the caller's exact token reaches C001, C002, C003 and C004;
- a pre-canceled token prevents C001;
- cancellation between statements prevents the next statement;
- requested cancellation dominates a racing C004 `42501`;
- requested cancellation and primary failure dominate cleanup failures;
- rollback is still attempted with `CancellationToken.None`;
- transaction disposal precedes connection disposal;
- all cleanup steps are attempted;
- the session has no commit path; and
- there is no retry, logging, sync-over-async or new classifying catch-all.

The existing transparent EDI cleanup captures are preserved rather than
duplicated or reclassified.

## 18. Security and leakage policy

Database name and current user are authorized result metadata. They must never
appear in exception messages, capability reasons, logs, test diagnostics on
failure, SQL, `Data`, inner exceptions, parameterized test display names or CI
output.

The implementation and tests must not capture or expose:

- host, port, password or connection string;
- passfile or certificate secrets;
- client/server addresses;
- an additional session user;
- role memberships not needed by the checks;
- platform/build version text;
- raw PostgreSQL messages or SQLSTATE outside the localized internal decision;
- SQL text in failures; or
- business rows.

Leakage tests use synthetic markers and assert their absence from every exposed
surface. Capability reasons are exactly the generic strings frozen above.

## 19. Unit-test strategy

Future deterministic unit tests must cover:

### Version normalization

```text
90624  -> 9.6.24, major 9, Unsupported
150000 -> 15.0, major 15, Supported
150016 -> 15.16, major 15, Supported
180004 -> 18.4, major 18, Supported
190000 -> 19.0, major 19, Unsupported
```

They also cover zero, negative, overflow/impossible values, immutable result
properties and exact `DatabaseEngine.PostgreSql` mapping.

### Inventory and boundary

- exactly seven statements in B001–B003/C001–C004 order;
- unique IDs, exact SQL, exact kinds and exact parameters;
- C001–C004 have no parameters;
- no eighth statement or 04D/04E SQL;
- typed operation methods only;
- no generic ID dispatch or raw-SQL surface; and
- B001–B003 remain inaccessible to the callback.

### Shapes and capabilities

- C001 row, column and null contracts;
- exact C002/C003 booleans;
- C004 UTC value, valid null, non-zero-offset rejection, zero-row rejection,
  wrong-column rejection and second-row rejection;
- supported with statistics available/unavailable;
- unsupported below/above range;
- required catalog false;
- complete, deterministic three-kind snapshot;
- exact generic reasons and null reason for `Available`;
- C004 omitted after C003 false;
- C002–C004 omitted when unsupported; and
- C003/C004 omitted after C002 false.

### Cancellation and leakage

- cancellation before/during C001, between C001/C002, and during C002, C003 or
  C004;
- cancellation racing C004 `42501`;
- cleanup after cancellation; and
- absence of version, database name, current user, PostgreSQL message,
  SQLSTATE, object names and SQL text from prohibited surfaces.

Unit tests remain server-free, deterministic and free of Docker, DNS and
sleeps.

## 20. PostgreSQL 18 integration strategy

The future server suite reuses only:

```text
docker.io/library/postgres:18.4
sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a
```

No other image is resolved and the permanent PostgreSQL 15/18 matrix remains
deferred to GC-DHI-04F.

Required real-server cases:

1. identity: normalized `18.4`, major 18, `Supported`, expected database name
   and expected current user;
2. normal capabilities: catalog and usage statistics `Available`, data
   profiling `Disabled`;
3. statistics reset: UTC when non-null and accepted null when the server reports
   null;
4. real optional-statistics permission loss as defined in section 21; and
5. lifecycle: rollback, reusable pool, no lingering transaction, persistent
   state unchanged and real cancellation.

Administrative fixture SQL may grant or revoke permissions only inside
IntegrationTests. It is never added to the product inventory.

## 21. Permission-loss fixture

The optional-statistics unavailable path must be proven in a dedicated,
disposable PostgreSQL 18 container. The administrative fixture revokes the
effective access needed for `pg_stat_database` and `pg_stat_all_indexes`, then
proves:

1. C003 returns false;
2. the probe continues;
3. C004 is not executed;
4. `UsageStatistics` is `Unavailable` with the exact generic reason;
5. other capability states remain correct; and
6. no server detail or credential appears in output.

The fixture must account for effective privileges, including privileges
inherited through `PUBLIC`; checking or revoking only a direct role grant is not
sufficient. All permission changes belong to the administrative test fixture
and disposable container.

If PostgreSQL does not permit this fixture to be constructed deterministically,
future implementation is `BLOCKED`. A unit-only substitute is not accepted.

## 22. CI strategy

GC-DHI-04C reuses the existing workflow unchanged.

| Platform | Future implementation validation |
|---|---|
| Ubuntu | UnitTests, non-server IntegrationTests, PostgreSQLServer IntegrationTests, pack and artifact upload |
| Windows | UnitTests, non-server IntegrationTests and CLI smoke |

`Category=PostgreSqlServer` remains Ubuntu-only. No PostgreSQL 15/18 matrix,
remote server, external service, secret, new workflow or package-version change
is authorized. Exact future test counts are recorded only after implementation.

## 23. Entry criteria for future implementation

Implementation may begin only when all are true:

1. GC-DHI-04B remains approved and closed;
2. this definition is integrated into `master`;
3. its documentation-only CI is green;
4. Core, ADR-0002 and the parent GC-DHI-04 definition are consistent;
5. C001–C004 and the seven-statement inventory are frozen;
6. numeric version normalization and the 15–18 policy are frozen;
7. every generic capability reason is frozen;
8. the real permission-loss strategy is accepted as viable;
9. the human project owner explicitly authorizes implementation; and
10. a separate Claude Code implementation prompt references this definition.

Definition integration alone satisfies neither item 9 nor item 10.

## 24. Exit criteria

GC-DHI-04C can exit only when:

1. PG-03 is implemented;
2. C001–C004 match this definition exactly;
3. the total inventory contains exactly seven statements;
4. version normalization is correct;
5. supported range 15–18 is evaluated numerically;
6. unsupported versions produce an explicit non-throwing result;
7. `DatabaseMetadata` is correct;
8. required `CatalogMetadata` behavior is proven;
9. optional `UsageStatistics` degradation is proven;
10. `DataProfiling` is disabled with the fixed reason;
11. nullable UTC `StatisticsSnapshot` behavior is correct;
12. all reasons are fixed and non-sensitive;
13. the callback boundary exposes typed operations only;
14. no generic ID dispatch exists;
15. no raw-SQL path exists;
16. Core receives no Npgsql type;
17. cancellation is proven across all C statements;
18. exact `42501` degradation occurs only for the authorized optional C004
    race;
19. real PostgreSQL 18 identity and normal-capability tests pass;
20. the real permission-loss fixture passes;
21. rollback, pool recovery and unchanged persistent state are proven;
22. deterministic UnitTests pass;
23. Ubuntu and Windows CI are green;
24. build and tests have zero warnings, errors, failures and skips;
25. Core and CLI remain unchanged;
26. no GC-DHI-04D work exists; and
27. no tag, release or NuGet publication occurs.

Implementation integration and final closure remain separate human-controlled
steps after these candidate criteria are satisfied.

## 25. Exclusions and deferred decisions

GC-DHI-04C excludes:

- table and index snapshots or queries;
- functional schema filters;
- complete `DatabaseSnapshot` composition;
- `IDatabaseSnapshotProvider` implementation;
- DBH001–DBH005;
- CLI commands/options or connection-source resolution;
- console, JSON, report or exit-code behavior;
- data profiling and business-row access;
- `COUNT(*)`, `pg_stat_statements` and query plans;
- PostgreSQL 15 matrix or final role-grant recipe; and
- tags, releases and NuGet publication.

Still deferred to later gates:

- exact table SQL and index SQL;
- schema filters;
- final minimum-role deployment recipe;
- permanent PostgreSQL 15/18 matrix;
- invalid-index fixture;
- snapshot-provider composition;
- CLI/report behavior;
- executable diagnostics; and
- full PG-06 completion.

No longer deferred by this definition: C001–C004, version normalization,
supported range, capability policies, data-profiling policy, generic reasons,
`stats_reset`, permission-loss strategy and typed operation boundary.

## 26. Definition verdict and next action

```text
GC-DHI-04C DEFINED — AWAITING HUMAN IMPLEMENTATION AUTHORIZATION
```

The only next authorized action is:

```text
Await human review of the integrated GC-DHI-04C definition.
No GC-DHI-04C implementation is authorized.
```

GC-DHI-04D through GC-DHI-04F remain unauthorized, unimplemented and not
started.
