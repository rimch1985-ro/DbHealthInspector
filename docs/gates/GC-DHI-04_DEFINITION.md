# GC-DHI-04 — PostgreSQL Metadata Adapter

**Definition date:** 2026-07-30  
**Status:** Defined  
**Verdict:** DEFINED — GC-DHI-04A AUTHORIZED NEXT

## 1. Objective

GC-DHI-04 defines the controlled implementation path for a PostgreSQL metadata
adapter that produces the engine-neutral `DatabaseSnapshot` required by
`IDatabaseSnapshotProvider`.

The completed adapter will:

- use `NpgsqlDataSource`;
- support PostgreSQL 15 through 18;
- operate asynchronously and support cancellation;
- execute metadata-only inspection inside an explicit read-only transaction;
- expose no secrets;
- use only static, inventoried and reviewed SQL;
- parameterize every external value;
- map PostgreSQL metadata to Core without leaking server-specific types.

This definition is governance and architecture only. It adds no product code,
SQL, tests, dependencies, project changes or running PostgreSQL environment.

## 2. Scope

GC-DHI-04 covers:

- a safe PostgreSQL connection boundary;
- read-only session and transaction enforcement;
- the SQL safety kernel and inventory;
- server metadata and capability probing;
- table metadata querying and mapping;
- index metadata querying and mapping;
- composition of a PostgreSQL `IDatabaseSnapshotProvider`;
- unit, real PostgreSQL integration and safety-contract verification;
- compatibility verification on PostgreSQL 15 and 18.

GC-DHI-04 excludes:

- DBH001–DBH005 executable rules;
- CLI commands, options or connection-source resolution;
- JSON or other report serialization;
- console rendering;
- exit-code mapping;
- public Docker demo work;
- tags, releases or package publication.

## 3. Architecture boundary

All productive PostgreSQL behavior belongs in:

```text
src/DbHealthInspector.PostgreSql
```

The dependency direction remains:

```text
DbHealthInspector.PostgreSql → DbHealthInspector.Core
DbHealthInspector.Core       → no infrastructure dependency
```

Core must not receive:

- Npgsql types;
- SQL text or SQL resources;
- connection strings;
- PostgreSQL exceptions;
- server-specific enums;
- connection or transaction ownership concerns.

The adapter maps PostgreSQL results into existing Core models. CLI composition,
argument resolution and presentation remain outside this gate.

## 4. Subgates

### GC-DHI-04A — Connection Boundary and Secret Hygiene

**Backlog:** `PG-01 — Implement connection factory`  
**Authorization:** authorized next after this definition is integrated

Scope:

- create the connection boundary in `DbHealthInspector.PostgreSql`;
- construct and own `NpgsqlDataSource`;
- accept a connection string already resolved by a future composition layer;
- open connections asynchronously with cancellation;
- expose sanitized connection metadata through an explicit allowlist;
- redact connection-related exceptions and messages;
- define ownership and `DisposeAsync`;
- add unit tests for sanitization, disposal and secret absence;
- add only a minimal open/cancel integration test if existing CI can support it
  without expansion.

Exclusions:

- no `--connection` resolution;
- no environment-variable access;
- no catalog SQL;
- no inspection transaction;
- no complete snapshot provider;
- no CLI changes.

Exit result: a safe, tested connection boundary without inspection behavior.

### GC-DHI-04B — Read-Only Session and SQL Safety Kernel

**Backlog:** `PG-02`, `PG-06` foundation  
**Authorization:** unauthorized until GC-DHI-04A is approved and closed

Scope:

- open a connection through the approved boundary;
- start an explicit transaction and set it read-only;
- apply statement, lock and idle-in-transaction timeouts transaction-locally
  where PostgreSQL permits;
- verify read-only state before metadata queries;
- implement safe rollback and cleanup;
- define the authorized production-SQL inventory;
- classify and reject prohibited statements;
- reject user-supplied SQL;
- verify that external values are parameters;
- prove against a persistent control table that writes fail;
- prove no change persists after success, failure or cancellation.

Exact default timeout values remain deferred. This subgate defines validated
options, not CLI defaults. The write-rejection fixture must not rely on a
temporary table.

Exclusions: no table or index metadata queries, snapshot mapping, capability
implementation or diagnostic rules.

### GC-DHI-04C — Server Metadata and Capability Probe

**Backlog:** `PG-03 — Implement server capability probe`  
**Authorization:** unauthorized until GC-DHI-04B is approved and closed

Scope:

- obtain PostgreSQL version and normalized major version;
- report supported or unsupported status for the 15–18 range;
- obtain database name and current user;
- map catalog-metadata and usage-statistics capabilities;
- compose `DataProfiling` status according to the approved composition policy;
- capture the statistics-reset timestamp when available;
- use generic, non-sensitive reasons for unavailable capabilities;
- map to `DatabaseMetadata`, `CapabilitySnapshot` and `StatisticsSnapshot`.

Optional capability loss must be explicit. Insufficient permission degrades an
optional capability when safe continuation is possible. Required failures
propagate. Raw PostgreSQL messages must not be stored.

Exclusions: no table queries, index queries or DBH rules.

### GC-DHI-04D — Table Snapshot Query and Mapping

**Backlog:** `PG-04 — Implement table snapshot query`  
**Authorization:** unauthorized until GC-DHI-04C is approved and closed

Scope:

- map schema, table name and relation kind;
- map partition-root and partition state;
- map estimated rows and table, index and total sizes;
- map primary-key state;
- use only catalog and statistics metadata;
- use static SQL and parameterized schema filters;
- deterministically exclude system schemas and order results;
- map explicitly to Core types;
- document null and unknown-value handling.

The query must not read business rows or use `COUNT(*)`.

Tests cover ordinary and partitioned tables, partitions, views, materialized
views where applicable, foreign tables where applicable, mapping behavior and
schema exclusions.

### GC-DHI-04E — Index Snapshot Query and Mapping

**Backlog:** `PG-05 — Implement index snapshot query`  
**Authorization:** unauthorized until GC-DHI-04D is approved and closed

Scope:

- map validity, readiness, liveness, uniqueness and primary-key support;
- map constraint association and access method;
- preserve ordered key columns, included columns and expression parts;
- map predicates, collation and operator classes;
- map scan count and size;
- use only static catalog/statistics SQL and parameterized filters;
- produce stable ordering of indexes and index parts;
- preserve the structural-equality contract of `IndexSnapshot`;
- represent missing statistics through capabilities or optional values rather
  than invented data.

No query plans or `pg_stat_statements` dependency are permitted.

Tests cover simple, multicolumn, unique, primary-key-backed, INCLUDE,
expression, partial, collation and operator-class indexes; `idx_scan`;
constraint association; and invalid/not-ready/not-live states through an
approved fixture or documented strategy.

### GC-DHI-04F — Snapshot Provider Composition and PostgreSQL Verification

**Authorization:** unauthorized until GC-DHI-04E is approved and closed

Scope:

- implement PostgreSQL `IDatabaseSnapshotProvider`;
- compose the connection boundary, read-only session, capability probe, table
  query and index query;
- produce one complete valid `DatabaseSnapshot`;
- capture the snapshot in one session and one explicit read-only transaction;
- guarantee no business-row queries;
- guarantee deterministic ordering;
- propagate requested cancellation;
- sanitize exposed failures;
- verify rollback, cleanup and ownership;
- complete the SQL inventory and safety allowlist;
- run mandatory PostgreSQL 15 and 18 verification.

Exit result: an adapter ready for later composition, still without CLI behavior
or executable diagnostic rules.

## 5. Dependencies

The mandatory sequence is:

```text
GC-DHI-04A
    ↓
GC-DHI-04B
    ↓
GC-DHI-04C
    ↓
GC-DHI-04D
    ↓
GC-DHI-04E
    ↓
GC-DHI-04F
```

Subgates cannot be skipped, combined or started early. Every subgate requires:

1. implementation by Claude Code;
2. review by Codex;
3. corrections when required;
4. human approval;
5. integration through a pull request;
6. green CI;
7. governance registration;
8. closure before the next subgate starts.

## 6. SQL safety

Production SQL must satisfy all of these rules:

- SQL is static, inventoried and reviewed.
- User-supplied SQL is rejected.
- Every external value is a parameter.
- Schema filters are never concatenated.
- Variable identifiers are removed by design or selected from an explicit
  allowlist.
- Dynamic SQL is prohibited.
- Stored procedures created by the tool are prohibited.
- Multiple statements are prohibited except explicitly reviewed
  transaction-initialization resources.
- SQL resources are classified and verified by safety tests.

Allowed command classes:

```text
SELECT
SHOW
SET LOCAL
BEGIN / transaction API
COMMIT
ROLLBACK
```

Prohibited command classes:

```text
INSERT
UPDATE
DELETE
MERGE
CREATE
ALTER
DROP
TRUNCATE
VACUUM
ANALYZE
REINDEX
GRANT
REVOKE
COPY FROM
CALL
DO
```

Writes used to prove enforcement belong only to the safety test suite and never
to a production assembly.

## 7. Secret handling

The adapter must never expose:

- a complete connection string;
- passwords or certificate passwords;
- passfile paths or contents;
- tokens;
- potentially sensitive exception detail;
- SQL containing bound values;
- credentials in snapshots, logs, test output or user-facing messages.

Sanitized connection metadata uses an explicit allowlist. Redaction operates at
the connection boundary before a message or failure crosses into another
layer. Tests use synthetic secrets and assert their absence from every exposed
surface.

## 8. Transaction model

Every productive snapshot capture:

1. opens one connection through the approved boundary;
2. starts one explicit transaction;
3. sets transaction read-only;
4. applies transaction-local timeout settings where supported;
5. verifies read-only state;
6. runs capability, table and index metadata operations in that transaction;
7. rolls back or closes safely on success, failure and cancellation;
8. disposes transaction, connection and owned data-source resources according
   to their documented ownership.

There is no authorized inspection path in autocommit. Cancellation and failure
must not leave an open transaction or persistent database change.

## 9. Capability degradation

Capability results must contain exactly the Core-defined capability set.

- Required catalog-metadata failure propagates.
- Optional usage-statistics permission loss becomes `Unavailable` when safe
  continuation is possible.
- `DataProfiling` follows composition policy and remains disabled for the
  metadata-only product unless a future authorized decision changes it.
- An unavailable or disabled capability never disappears silently.
- Reasons are generic and non-sensitive; raw server messages are not retained.
- Missing statistics are represented by capability state or nullable contract
  values, never fabricated.

## 10. Mapping strategy

PostgreSQL result shapes are translated explicitly into immutable Core models:

- server identity to `DatabaseMetadata`;
- capability outcomes to `CapabilitySnapshot`;
- statistics reset information to `StatisticsSnapshot`;
- relations to stable `TableSnapshot` values;
- index metadata and ordered parts to `IndexSnapshot`.

Mapping must:

- preserve Core invariants and structural equality;
- use documented conversions and enum mappings;
- reject or explicitly handle unknown values;
- distinguish absent information from zero;
- preserve ordered index key and INCLUDE parts;
- produce stable schema, table and index ordering;
- avoid Npgsql or PostgreSQL-specific types in Core;
- never query or map business-row values.

## 11. Testing strategy

### Unit tests

- connection-metadata sanitization and error redaction;
- option validation and ownership behavior;
- row-to-Core mapping;
- SQL inventory completeness;
- statement classification;
- parameter enforcement;
- deterministic ordering;
- capability degradation.

### Integration tests

- real PostgreSQL through Testcontainers;
- connection and requested cancellation;
- explicit read-only transaction state;
- statement, lock and idle transaction timeouts;
- capability degradation under restricted permissions;
- table and index metadata;
- failure/cancellation cleanup and disposal.

### Safety contracts

- persistent control row remains unchanged;
- persistent schema remains unchanged;
- a write attempt is rejected;
- no business-row query exists or executes;
- no prohibited SQL resource exists;
- no secret appears in snapshots, output, exceptions or test diagnostics.

## 12. PostgreSQL 15/18 matrix

PostgreSQL 15 is the oldest supported version and PostgreSQL 18 is the newest.
GC-DHI-04F must execute the complete adapter integration and safety suite
against both versions.

Earlier subgates may use a focused real-server test where required, without
expanding permanent CI prematurely. The permanent 15/18 CI shape remains a
deferred decision until real query cost and reliability are known.

Any version-specific query or mapping branch must have explicit coverage.

## 13. Gate entry criteria

Every subgate may start only when:

- its predecessor is approved, integrated, registered and closed;
- the human owner has authorized that subgate;
- the repository is clean and synchronized;
- the implementation prompt names exact backlog items and exclusions;
- unresolved predecessor findings are absent;
- no deferred decision required by that subgate remains ambiguous.

GC-DHI-04A additionally requires this definition to be integrated. That
integration is the authorization event for preparing its Claude Code
implementation prompt.

## 14. Gate exit criteria

Every subgate exits only when:

- its exact scope and acceptance criteria are implemented;
- unit, integration and safety tests appropriate to the subgate pass;
- build completes with zero warnings and errors;
- no secret leakage or prohibited SQL is detected;
- architecture and dependency direction remain compliant;
- Codex review findings are resolved;
- human approval is recorded;
- the implementation is integrated by PR with green CI;
- governance state and gate report are updated;
- the subgate is explicitly closed.

GC-DHI-04 exits only after GC-DHI-04F verifies the composed provider on
PostgreSQL 15 and 18 and all six subgates are closed.

## 15. Prohibitions

Across GC-DHI-04:

- no DBH001–DBH005 implementation;
- no CLI argument or connection-source resolution;
- no JSON, console rendering or exit-code mapping;
- no business-row reads, `COUNT(*)`, profiling or sampling;
- no query plans or `pg_stat_statements`;
- no automatic DDL, DML, maintenance or permission changes;
- no user-supplied or dynamic SQL;
- no PostgreSQL types in Core;
- no extra production project;
- no tag, release or package publication.

For this definition gate specifically:

- no product-code or project modification;
- no Npgsql usage change;
- no SQL creation;
- no tests or CI change;
- no PostgreSQL or Docker startup;
- no GC-DHI-04A implementation.

## 16. Risks

| Risk | Control |
|---|---|
| Secret leakage through connection or server errors | Boundary redaction, allowlisted metadata and leakage tests |
| Accidental write capability | Explicit read-only transaction, verified state and persistent control-row test |
| Unsafe or hidden SQL | Static inventory, statement classifier and prohibited-resource tests |
| Business-row access | Catalog-only inventory and no-business-row safety contract |
| Permission differences | Explicit capability degradation with generic reasons |
| PostgreSQL version drift | 15/18 matrix and coverage for version branches |
| Incorrect mapping | Explicit mapping, Core validation and representative fixtures |
| Transaction leakage after cancellation | Cancellation tests plus rollback, cleanup and ownership assertions |
| Scope growth | Sequential subgates and explicit exclusions |
| Invalid-index fixture instability | Deferred strategy resolved before GC-DHI-04E integration |

## 17. Deferred decisions

The following remain pending and must be resolved in the relevant subgate
before its integration:

- exact default values for statement, lock and idle transaction timeouts;
- final CLI error format;
- precedence of connection sources;
- console rendering;
- JSON mapping;
- definitive invalid-index reproducibility strategy;
- permanent PostgreSQL 15/18 CI matrix;
- exact minimum role permissions after real queries are validated;
- final hostname policy for reports.

These decisions do not block this definition or authorization of GC-DHI-04A.

## 18. Authorization status

| Subgate | Status |
|---|---|
| GC-DHI-04A — Connection Boundary and Secret Hygiene | Authorized next after this definition is integrated |
| GC-DHI-04B — Read-Only Session and SQL Safety Kernel | Unauthorized |
| GC-DHI-04C — Server Metadata and Capability Probe | Unauthorized |
| GC-DHI-04D — Table Snapshot Query and Mapping | Unauthorized |
| GC-DHI-04E — Index Snapshot Query and Mapping | Unauthorized |
| GC-DHI-04F — Snapshot Provider Composition and PostgreSQL Verification | Unauthorized |

No adapter implementation is started by this document. The next authorized
action is to prepare the Claude Code implementation prompt for GC-DHI-04A.

```text
DEFINED — GC-DHI-04A AUTHORIZED NEXT
```
