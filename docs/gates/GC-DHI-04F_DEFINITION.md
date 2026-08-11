# GC-DHI-04F — Snapshot Provider Composition and PostgreSQL Verification

**Definition date:** 2026-08-10  
**D1 correction date:** 2026-08-10  
**Gate state:** GC-DHI-04F DEFINITION CORRECTED — AWAITING HUMAN IMPLEMENTATION AUTHORIZATION  
**Implementation:** UNAUTHORIZED  
**Backlog:** final composition of PG-01 through PG-05; final verification of PG-06  
**Predecessor:** GC-DHI-04E approved and closed

## 1. Objective

GC-DHI-04F will close the PostgreSQL Metadata Adapter by composing the already
approved 04A–04E primitives into one PostgreSQL implementation of Core's
`IDatabaseSnapshotProvider`. One capture must yield one complete, valid,
engine-neutral `DatabaseSnapshot` or fail without returning a partial result.

This is a definition-only gate. It creates no provider, product code, test,
workflow, dependency, SQL statement, diagnostic, CLI behavior or publication.

## 2. Scope

The separately authorized implementation must:

1. add the smallest public PostgreSQL provider surface required by a later CLI;
2. own and compose the existing connection factory, session runner, capability
   probe, table query and composite index query;
3. capture through one connection, one verified session and one explicit
   `RepeatableRead`, read-only, non-deferrable, rollback-only transaction;
4. use one immutable schema filter for D001, E001 and E002;
5. construct metadata, capabilities, statistics, schemas, tables and indexes
   into one `DatabaseSnapshot`;
6. preserve atomicity, deterministic order, cancellation, sanitization,
   rollback and cleanup precedence;
7. preserve the ten-statement closed SQL inventory; and
8. establish permanent PostgreSQL 15/18 verification and complete PG-06 only
   after every real acceptance criterion passes.

## 3. Non-scope

GC-DHI-04F does not add or change DBH001–DBH005, findings, risk rules, Core
semantics, CLI commands/options/wiring, JSON or console reporting, exit codes,
connection-source precedence, business-row profiling, query plans,
`pg_stat_statements`, minimum-role deployment documentation, tags, releases or
NuGet publication. `DbHealthInspector.Cli`, Core and the frozen 04A–04E SQL and
mappers remain unchanged unless a later review proves a contradiction and the
gate is stopped as `BLOCKED` before any change.

## 4. Real Core contracts

The implementation must target these existing signatures, not substitutes:

```csharp
public interface IDatabaseSnapshotProvider
{
    Task<DatabaseSnapshot> CaptureAsync(CancellationToken cancellationToken);
}

public DatabaseSnapshot(
    DatabaseMetadata metadata,
    IReadOnlyCollection<SchemaSnapshot> schemas,
    IReadOnlyCollection<TableSnapshot> tables,
    IReadOnlyCollection<IndexSnapshot> indexes,
    CapabilitySnapshot capabilities,
    StatisticsSnapshot statistics);

public DatabaseMetadata(
    DatabaseEngine engine,
    string engineVersion,
    string databaseName,
    string? currentUser = null);

public CapabilitySnapshot(IReadOnlyCollection<CapabilityState> states);
public CapabilityState(
    CapabilityKind kind,
    CapabilityStatus status,
    string? reason = null);
public StatisticsSnapshot(DateTimeOffset? statisticsResetAtUtc);
public SchemaSnapshot(string schemaName);
```

`TableSnapshot` takes schema/table identity, `RelationKind`, root and partition
flags, nullable estimated rows, three non-negative size values and primary-key
state. `IndexSnapshot` takes schema/table/index identity, access method,
ordered key parts and INCLUDE columns, optional predicate, uniqueness and
nullable null-distinctness, constraint/validity flags, size and nullable scan
count. These signatures and properties are not extended by 04F.

## 5. Core invariants and collection semantics

- `DatabaseSnapshot` rejects null inputs/elements and ordinal duplicate schema,
  `(schema, table)` and `(schema, index)` identities. It defensively copies and
  preserves the supplied collection order.
- `CapabilitySnapshot` requires exactly one state for every defined
  `CapabilityKind`; 04C already supplies them in `CatalogMetadata`,
  `UsageStatistics`, `DataProfiling` order.
- `StatisticsResetAtUtc` is nullable and, when present, UTC.
- `TableSnapshot` rejects contradictory root/partition state and negative
  estimates or sizes.
- `IndexSnapshot` requires at least one key part, unique key positions and
  INCLUDE names, valid uniqueness relationships and non-negative values. Its
  equality is structural and order-sensitive for key parts and INCLUDE values.
- Core does not validate that every index refers to a table in the same
  `DatabaseSnapshot`; that is a PostgreSQL composition invariant frozen below.
- An empty `DatabaseSnapshot` is valid: all three object collections may be
  empty while metadata, capabilities and statistics remain mandatory.

No Core change is required. Discovery of a genuine incompatibility during
implementation is a `BLOCKED` result, not authority to modify Core.

## 6. Real PostgreSQL boundaries

The provider must reuse these actual internal contracts:

```csharp
internal sealed class PostgreSqlConnectionFactory : IAsyncDisposable
{
    internal static PostgreSqlConnectionFactory Create(string connectionString);
    internal ValueTask<NpgsqlConnection> OpenConnectionAsync(
        CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();
}

internal sealed class PostgreSqlInspectionSessionRunner
{
    internal ValueTask<TResult> RunAsync<TResult>(
        PostgreSqlInspectionSessionOptions options,
        Func<PostgreSqlInspectionOperationExecutor,
             CancellationToken,
             ValueTask<TResult>> operation,
        CancellationToken cancellationToken = default);
}

internal static ValueTask<PostgreSqlServerProbeResult> ProbeAsync(
    PostgreSqlInspectionOperationExecutor executor,
    CancellationToken cancellationToken);

internal ValueTask<PostgreSqlTableSnapshotQueryResult> ReadTableSnapshotsAsync(
    PostgreSqlSchemaFilter filter,
    CancellationToken cancellationToken);

internal ValueTask<PostgreSqlIndexSnapshotQueryResult> ReadIndexSnapshotsAsync(
    PostgreSqlSchemaFilter filter,
    bool usageStatisticsAvailable,
    CancellationToken cancellationToken);
```

The restricted operation executor exposes only typed C001–C004, D001 and the
E001/E002 composite operation. It exposes no connection, transaction, command,
statement ID, SQL text or generic parameter collection. The provider must not
introduce a parallel executor, mapper, query path or session abstraction.

## 7. Provider public API decision

Exactly one new exported PostgreSQL type is authorized:

```csharp
public sealed class PostgreSqlDatabaseSnapshotProvider
    : IDatabaseSnapshotProvider, IAsyncDisposable
{
    public static PostgreSqlDatabaseSnapshotProvider Create(
        string connectionString);

    public static PostgreSqlDatabaseSnapshotProvider Create(
        string connectionString,
        IReadOnlyCollection<string> includedSchemas,
        IReadOnlyCollection<string> excludedSchemas,
        TimeSpan statementTimeout);

    public Task<DatabaseSnapshot> CaptureAsync(
        CancellationToken cancellationToken);

    public ValueTask DisposeAsync();
}
```

Its constructors remain non-public. There is no new public factory type,
options type, exception type, interface, connection/session type or Npgsql
surface. The assembly's expected exported type count therefore grows from one
(`AssemblyMarker`) to exactly two.

The one-argument factory uses the existing default session options and
`PostgreSqlSchemaFilter.IncludeEverything`: 30 seconds statement timeout, 5
seconds lock timeout and 60 seconds idle-in-transaction timeout.

The four-argument factory validates and defensively copies both exact-name
collections once, creates one immutable filter, and maps the public statement
timeout into existing session options. Before creating
`PostgreSqlConnectionFactory` or any other resource, `statementTimeout` must:

- not equal `Timeout.InfiniteTimeSpan`;
- be positive;
- be an exact whole number of milliseconds;
- be at least 100 milliseconds; and
- be at most 5 minutes.

The caller's value is rejected if it has fractional milliseconds; it is never
rounded, truncated or clamped. After validation, the exact integer conversion
is named `statementTimeoutMilliseconds`. The lock timeout is then derived
exactly as:

```text
lockTimeoutMilliseconds =
    min(
        5000,
        statementTimeoutMilliseconds / 2
    )

lockTimeout = TimeSpan.FromMilliseconds(lockTimeoutMilliseconds)
```

The division is non-negative integer division. The idle-in-transaction timeout
remains exactly 60 seconds and is not derived from the statement timeout.

Frozen examples:

| Statement timeout | Derived lock timeout |
|---:|---:|
| 100 ms | 50 ms |
| 101 ms | 50 ms |
| 102 ms | 51 ms |
| 999 ms | 499 ms |
| 1000 ms | 500 ms |
| 9999 ms | 4999 ms |
| 10000 ms | 5000 ms |
| 30000 ms | 5000 ms |
| 300000 ms | 5000 ms |

For every accepted integer `S = statementTimeoutMilliseconds`,
`100 <= S <= 300000`. Therefore integer division gives `S / 2 >= 50`.
Taking `min(5000, S / 2)` preserves a lower bound of 50 and establishes an
upper bound of 5000. Because `S` is positive, integer `S / 2 < S`; taking the
minimum cannot increase it, so the derived lock timeout is strictly less than
the statement timeout. Both arguments to `min` are integers, so the result and
`TimeSpan.FromMilliseconds` value are always exact whole milliseconds. No
separate lower clamp is necessary.

This correction adds no public lock/idle argument, options type, factory type,
CLI parsing, environment variable or configuration file. It exposes only the
already approved future CLI inputs and does not implement that CLI.

## 8. Provider construction

Construction order is frozen:

1. check requested cancellation only in `CaptureAsync`, not in `Create`;
2. validate/copy schema lists and validate/derive session options before owning
   an external resource;
3. create the existing `PostgreSqlConnectionFactory`, which owns one
   `NpgsqlDataSource`;
4. create one immutable session runner over that factory and the canonical SQL
   inventory; and
5. publish the fully constructed provider only after every step succeeds.

The connection string is used only by the approved 04A boundary. It is never
retained by a second field, exposed, logged or placed in exception `Data`.

## 9. Ownership, lifetime and disposal

- The caller owns the public provider and must call `DisposeAsync`.
- The provider exclusively owns its `PostgreSqlConnectionFactory`; the factory
  exclusively owns its `NpgsqlDataSource`.
- Each capture's session scope owns its opened connection and transaction.
- The probe, mappers, executor, query results and snapshot buffers own no
  connection resource and do not outlive the callback.
- Success, primary failure and cancellation all rollback with
  `CancellationToken.None`, dispose transaction, then dispose connection.
- Provider disposal is asynchronous, idempotent and safe when invoked more
  than once. It prevents new captures, waits for already admitted captures to
  release their leases without canceling them, and then disposes the connection
  factory exactly once.
- A capture admitted before disposal completes normally. A capture attempted
  after disposal begins fails before opening a connection with
  `ObjectDisposedException(nameof(PostgreSqlDatabaseSnapshotProvider))`.
- No finalizer, synchronous dispose, sync-over-async, double dispose or resource
  transfer is authorized.

## 10. Concurrency contract

One provider instance is safe for concurrent `CaptureAsync` calls. The shared
state is immutable except for the lifecycle counter/state. Each admitted call
opens its own connection, transaction, executor and local composition buffers;
no session, query result or mutable collection is shared. The underlying
`NpgsqlDataSource` is the only shared I/O resource and is used through its
connection-per-call contract.

Disposal/capture coordination must be explicit, not a race delegated to 04A:
an atomic admission lease rejects calls after disposal starts, concurrent
disposers await the same completion, and final data-source disposal occurs only
after the in-flight count reaches zero.

## 11. Exact productive composition sequence

For a supported server, one `CaptureAsync` executes exactly:

```text
1  requested-cancellation check and provider lifecycle lease
2  runner creates one scope
3  open one connection through PostgreSqlConnectionFactory
4  begin one RepeatableRead transaction (read-only false initially,
   non-deferrable)
5  B001 SetTransactionReadOnly
6  B002 ApplyLocalTimeouts
7  B003 VerifySessionState
8  C001 ReadServerIdentity
9  normalize version; require supported major 15–18
10 C002 CheckCatalogMetadataAccess
11 C003 CheckUsageStatisticsAccess
12 C004 ReadStatisticsReset only when C003 is true
13 cancellation checkpoint
14 D001 ReadTableSnapshots with the provider's one filter
15 cancellation checkpoint
16 E001 ReadIndexMetadata with that same filter
17 E002 ReadIndexUsageStatistics exactly once only when UsageStatistics is
   Available; otherwise it is not prepared or executed
18 cancellation checkpoint
19 validate cross-object closure; derive and order schemas
20 construct one DatabaseSnapshot from the complete results
21 final in-transaction cancellation checkpoint
22 callback completes
23 rollback with CancellationToken.None
24 dispose transaction
25 dispose connection
26 post-cleanup cancellation checkpoint
27 release provider lifecycle lease
28 return the snapshot
```

Steps 3–7 and 23–25 remain owned by the existing runner. Steps 8–12 remain one
call to the existing capability probe. Steps 16–17 remain one call to the
existing composite index operation. 04F adds orchestration, not alternate
implementations.

## 12. Capability branching

### Catalog metadata

On a supported server, `CatalogMetadata` is required. C002 false throws the
existing fixed `PostgreSqlRequiredCatalogCapabilityException`; D001, E001 and
E002 do not execute and no snapshot is returned. An operational C002 failure is
handled by the existing sanitized session boundary.

### Usage statistics

C003 false produces `UsageStatistics = Unavailable`, skips C004 and E002,
preserves `StatisticsSnapshot(null)`, runs D001 and E001, and maps every index
`ScanCount` to null. C003 true runs C004; C004 SQLSTATE 42501 degrades through
the already approved 04C policy to the same unavailable state and skips E002.
Other C004 failures fail the capture. Available statistics preserve the
nullable UTC reset timestamp and allow E002.

### Data profiling

`DataProfiling` remains `Disabled` by policy for every version and never
causes a query.

## 13. Unsupported-server policy

The 04C contract is authoritative: a normalized major outside 15–18 is not a
probe exception. After C001 the probe returns explicit unsupported capability
states and does not execute C002–C004. The provider then executes no D001,
E001 or E002 and returns a valid `DatabaseSnapshot` containing:

- C001-derived PostgreSQL metadata;
- the 04C unsupported `CapabilitySnapshot` and null statistics reset; and
- empty schema, table and index collections.

This is a complete unsupported-server snapshot, not a partial supported-server
snapshot. Structurally invalid `server_version_num` remains the existing fixed
mapping failure. No version-specific productive SQL branch is added for majors
15, 16, 17 or 18.

## 14. Schema-filter policy

One `PostgreSqlSchemaFilter` is created per provider, immutable and defensively
copied. Both collections use exact ordinal, case-sensitive names; null, blank,
NUL-containing, duplicate or overlapping names fail through the existing fixed
filter error. Empty includes means include every otherwise eligible schema;
empty excludes means no caller exclusion.

The identical filter instance is passed to D001 and the E001/E002 composite
operation. The permanent SQL exclusions remain `pg_catalog`,
`information_schema`, `pg_toast%` and `pg_temp_%`; includes cannot re-enable
them. There is no wildcard, pattern, identifier, SQL fragment or separate table
and index filter.

## 15. Cross-object composition invariants

The existing row mappers and query-result constructors remain responsible for
row shape, value domains, duplicate identities and collection order. 04F adds
only the invariant that every `IndexSnapshot` must reference exactly one
`TableSnapshot` having the same ordinal `(SchemaName, TableName)` identity.

This closure is required because E001 obtains `pg_index.indrelid` from the
same catalog snapshot and applies the same schema predicate as D001. D001
includes every user relation kind that can own an eligible index: ordinary and
partitioned tables, materialized views and their physical/virtual members.
Partitioned table roots, table partitions, virtual index roots and physical
index partitions each remain distinct snapshots and close against their own
table identity. A missing table is therefore inconsistent composition, not an
allowed index kind and not a diagnostic finding.

Duplicate schema/table and schema/index identities continue to fail in the
existing result/Core guards. Any duplicate index identity associated with a
different table is also a failure. No merge, omission or last-write-wins policy
is permitted.

## 16. Schema derivation and empty semantics

There is no approved schema-list SQL and Core contains only a name-bearing
`SchemaSnapshot`. Schemas are therefore derived from the distinct schema names
of the validated table collection, then sorted ordinally. Referential closure
makes every index schema a member of that set. Empty user schemas that contain
no D001-eligible relation are intentionally not represented. If D001 and E001
return zero objects, all three object collections are empty and the snapshot is
valid. Unsupported servers use the same empty-object semantics.

## 17. Deterministic ordering

- schemas: `SchemaName`, ordinal ascending;
- tables: existing 04D `(SchemaName, TableName)`, ordinal ascending;
- indexes: existing 04E `(SchemaName, TableName, IndexName)`, ordinal ascending;
- index key parts and INCLUDE columns: existing server/mapper order;
- capabilities: `CatalogMetadata`, `UsageStatistics`, `DataProfiling`.

The provider must materialize these orders explicitly before Core's defensive
copy; dictionary/hash-set enumeration is never an output contract. With the
same database snapshot, capabilities and filter, captures are structurally
equivalent. Legitimately changing live usage counters or reset time are input
changes, not nondeterminism.

## 18. Atomicity

The only successful value is a fully constructed `DatabaseSnapshot` after all
required operations, cross-object checks and Core guards pass. Metadata-only,
tables-without-indexes due to failure, partial readers, partial merges and
partially constructed snapshots are never returned. Query result types publish
only after their readers finish, the provider constructs locally inside the
runner callback, and the caller receives nothing until rollback and all cleanup
complete.

## 19. Cancellation matrix

| Boundary | Required outcome |
|---|---|
| Before capture/admission | Same requested cancellation; no connection |
| Connection open | Token forwarded unchanged; existing 04A association/sanitization |
| Transaction begin or B001–B003 | Token forwarded; no callback; cleanup with `None` |
| C001, C002, C003 or C004 | Token forwarded; no later operation |
| After probe/before D001 | explicit checkpoint; no D001/E001/E002 |
| D001 or its reader | same requested cancellation; no result collection |
| After D001/before E001 | explicit checkpoint; no E001/E002 |
| E001 or before E002 | token forwarded; no partial index result |
| E002 | token forwarded; no merged result |
| After queries/before construction/return | explicit in-transaction checkpoint |
| During successful cleanup | cleanup ignores token; post-cleanup checkpoint cancels return |
| Cleanup-only failure racing cancellation | already captured cleanup failure remains authoritative |
| Provider lease release | non-cancelable and guaranteed in `finally` |

No token is replaced, linked unnecessarily or used for rollback. An unrelated
`OperationCanceledException` continues through the existing fixed execution
failure policy rather than impersonating requested cancellation.

## 20. Cleanup and EDI precedence

The existing `PostgreSqlAsyncCleanup`, reader/command cleanup and runner EDI
policy remain authoritative:

| Primary condition | Additional cleanup condition | Observable result |
|---|---|---|
| Query/shape failure | reader or command disposal fails | original query/shape failure |
| Mapping failure | reader disposal or rollback fails | original fixed mapping failure |
| Requested cancellation | reader, rollback or disposal fails | requested cancellation |
| Composition/Core guard failure | rollback/disposal fails | composition failure |
| Success | rollback fails | existing sanitized cleanup failure |
| Success | transaction disposal fails | first cleanup failure |
| Success | connection disposal fails | first cleanup failure |

All cleanup actions are attempted in order even after one fails. Rollback uses
`CancellationToken.None`; transaction disposal precedes connection disposal.
The provider lifecycle `finally` releases its lease even when the runner
throws. No cleanup exception may replace an already captured primary failure.

## 21. Error sanitization and construction failures

Existing connection, session, required-capability, schema-filter, table-mapping
and index-mapping failures are reused unchanged when semantically applicable.
04F needs one new **internal sealed** parameterless exception only:

```text
PostgreSqlSnapshotCompositionException
The PostgreSQL snapshot could not be composed safely.
```

It has no public/message/inner constructor, null `InnerException` and empty
`Data`. It is used for failed index-to-table closure and to wrap only
`ArgumentException`/`ArgumentOutOfRangeException` thrown by the final Core
snapshot construction from adapter-derived values. That narrow wrapping stops
Core duplicate messages from exposing object names. It must not catch
`OperationCanceledException`, Npgsql failures, out-of-memory/process failures
or arbitrary programming exceptions. Unexpected faults propagate and are not
misclassified as data errors.

No provider failure may expose connection string, password, non-allowlisted
host, sensitive role/database/object/filter value, SQL, parameter, SQLSTATE,
Npgsql/PostgreSQL server text, detail, hint, internal query, where/routine,
inner exception or populated `Data`.

## 22. SQL inventory and zero-new-SQL decision

Composition requires zero new productive SQL. The inventory remains exactly:

```text
B001 B002 B003 C001 C002 C003 C004 D001 E001 E002
```

The totals remain ten statements, eight command kinds, two parameter types,
ten definitions, ten frozen contracts and a validator matrix of 800
ID/kind/SQL combinations with exactly 10 accepted and 790 rejected. No F001,
backend-PID query, schema-list query or transaction-observation query enters
the product. If implementation discovers a true need for another statement,
the result is `BLOCKED` before expansion and requires a new reviewed definition.

## 23. Business-row prohibition

Product code continues to execute only the frozen transaction configuration,
catalog/capability and statistics statements. It accepts no raw or user SQL and
executes no business-table `SELECT`, `COUNT(*)`, `EXPLAIN`, `ANALYZE`, dynamic
SQL, identifier interpolation or `pg_stat_statements`. Test-owned synthetic DDL,
rows and observations remain confined to IntegrationTests and are excluded from
product, package and inventory scans.

## 24. PostgreSQL image matrix

| Major | Pinned image | Immutable manifest-list digest | Role |
|---|---|---|---|
| 18 | `postgres:18.4` | `sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a` | canonical Ubuntu server and packaging job |
| 15 | `postgres:15.18` | `sha256:6eb0add3b77c081df18aa518ce43df58fdcc40f2e6d868a6fd08038dc7acd425` | compatibility test-only job |

PostgreSQL's versioning policy listed 15.18 and 18.4 as the supported current
minors on 2026-08-10. The 15 digest was resolved with
`docker buildx imagetools inspect postgres:15.18`, confirmed with
`docker pull postgres:15.18@sha256:...`, and the running container reported
image ID/digest above, `15.18 (Debian 15.18-1.pgdg13+1)` and
`server_version_num = 150018`. Floating `postgres:15` and `postgres:18` are
forbidden.

## 25. Definition-time PostgreSQL 15 probe

A disposable 15.18 container executed the exact frozen B001–B003, C001–C004,
D001, E001 and E002 resources inside a `RepeatableRead`, read-only transaction.
The fixture included `NULLS NOT DISTINCT`, INCLUDE, expression, partial and
BRIN operator-class-option indexes, a partitioned index root/partition and the
deterministic invalid `CREATE INDEX ... ON ONLY` root. Every prepare/execute
succeeded. The container was stopped and removed; no repository file or
productive SQL changed.

This probe is design evidence, not the permanent matrix and not authority to
mark PG-06 complete.

## 26. PostgreSQL 15/18 compatibility findings

Official PostgreSQL 15 and 18 documentation confirms the productive surface:

- `pg_class` carries the used relation kinds, persistence and partition state;
- `pg_index` in 15 already includes `indnullsnotdistinct`, key/include counts,
  validity/readiness/liveness and key/collation/opclass vectors;
- `pg_attribute` in both versions includes index attribute rows and nullable
  ordered `attoptions`;
- `pg_index_column_has_property`, the used `pg_get_indexdef` and `pg_get_expr`
  overloads and relation-size functions exist in both;
- PostgreSQL 15 supports index operator-class parameters, partitioned indexes,
  INCLUDE, expression/partial indexes and `NULLS NOT DISTINCT`; and
- `pg_stat_all_indexes` exposes the E002 identity columns and `idx_scan` in
  both versions.

No 04E contract is incompatible with 15. The known version difference that
PostgreSQL 18 no longer creates unlogged partitioned tables remains handled by
the existing union mapper; common cross-version fixtures do not demand that DDL
on 18. PostgreSQL 18 may expose additional statistics columns, but E002 selects
only the stable four-column contract. Textual expression formatting, size,
estimates, scan counts, reset timestamps, database/user names and generated
object OIDs may legitimately differ and are not cross-version equality targets.

Normative sources:

- https://www.postgresql.org/support/versioning/
- https://www.postgresql.org/docs/15/sql-set-transaction.html
- https://www.postgresql.org/docs/18/transaction-iso.html
- https://www.postgresql.org/docs/15/catalog-pg-class.html
- https://www.postgresql.org/docs/15/catalog-pg-attribute.html
- https://www.postgresql.org/docs/18/catalog-pg-attribute.html
- https://www.postgresql.org/docs/15/catalog-pg-index.html
- https://www.postgresql.org/docs/18/catalog-pg-index.html
- https://www.postgresql.org/docs/15/functions-info.html
- https://www.postgresql.org/docs/15/brin-builtin-opclasses.html
- https://www.postgresql.org/docs/15/monitoring-stats.html
- https://www.postgresql.org/docs/18/monitoring-stats.html
- https://hub.docker.com/_/postgres

## 27. Cross-version semantic contract

Equivalent 15.18 and 18.4 fixtures must yield equal Core semantics for server
identity shape, required/optional capabilities, nullable reset time, ordinary
and partitioned tables, physical partitions, ordinary/virtual/physical index
members, INCLUDE order, expression/predicate presence, qualified collation and
operator-class structural identity, nullable/exact usage statistics, invalid
partitioned index flags and exact schema filtering.

Comparison normalizes only facts Core defines. Major/minor version text is
expected to differ. Object names created with a version suffix are normalized
by fixture design, not product code. Size, estimates, timestamps and scan
counts are checked for their contract (non-negative, nullable/exact against the
same server observation), not byte equality across servers. Expression text is
checked structurally against each server's own official deparser result.

## 28. CI architecture

The future workflow keeps the existing required jobs and adds one job:

| Job | Platform/image | Tests | Pack/upload |
|---|---|---|---|
| `Ubuntu` | `ubuntu-latest`, PostgreSQL 18.4 pinned | build; UnitTests; non-server IntegrationTests; PostgreSQLServer 18 | yes; sole `dbhealth-bootstrap-package` producer |
| `PostgreSQL 15` | `ubuntu-latest`, PostgreSQL 15.18 pinned | restore/build plus `dotnet test tests/DbHealthInspector.IntegrationTests --configuration Release --no-build --filter "Category=PostgreSql15"` | no |
| `Windows` | `windows-latest`, no server | build; UnitTests; non-server IntegrationTests; bootstrap-only CLI smoke | no |

The existing `Category=PostgreSqlServer` remains the PostgreSQL 18.4 exhaustive
suite. The new `Category=PostgreSql15` contains the complete provider and shared
15/18 compatibility contract but excludes documented 18-only empirical cases.
Shared assertion helpers freeze the same Core expectations in both categories.
The PostgreSQL 15 job is test-only and never runs UnitTests/non-server tests a
second time, packs, uploads, publishes or uses the canonical artifact name.
PostgreSQL 18 remains the authoritative exhaustive server suite and canonical
packaging job. Existing Windows smoke and package traceability remain unchanged.
The workflow is changed only by a separately authorized implementation and its
resulting job name must be added to required checks before closure.

## 29. Unit-test strategy

Future UnitTests must prove:

- the exact exported API and internal-only supporting types;
- default/custom option derivation and one normalized filter instance;
- exact supported and unsupported operation order;
- required catalog failure, optional statistics branches and C004 degradation;
- zero D/E calls for unsupported versions;
- complete snapshot construction, schema derivation, ordinal order and empty
  semantics;
- index-to-table closure and sanitized Core-construction failures;
- no partial result on every operation/mapper/composition failure;
- cancellation at every boundary in the matrix, including post-cleanup;
- first-primary EDI precedence for reader, command, rollback and disposal;
- provider/factory/connection/transaction ownership, idempotent disposal,
  post-disposal rejection and no double release;
- parallel captures use independent scopes/buffers and disposal waits for
  admitted calls; and
- exact ten-statement inventory, validator 10/790, no raw SQL/provider backdoor,
  no product business-row or test-owned SQL and no secret leakage.

Deterministic fakes, task gates and recording scope/gateway seams are required;
arbitrary sleeps are forbidden.

## 30. Real PostgreSQL strategy

The server suites on both pinned majors must cover a complete provider snapshot,
supported identity/capabilities/statistics, the common table/index zoo, exact
filtering, empty match, partitioned roots and members, invalid index, usage
available/unavailable, rollback on success/failure/cancellation, pool reuse,
cleanup and unchanged persistent controls. PostgreSQL 18 additionally remains
the authoritative permission-loss and exhaustive platform suite where an
existing fixture is version-specific.

`CatalogMetadata` unavailable is proven using the existing deterministic
required-function revocation topology; `UsageStatistics` unavailable uses the
existing statistics revocation topology. Fixture DDL and observations stay in
IntegrationTests. Every container has a bounded initialization/test/cleanup
deadline and is released on partial initialization failure.

## 31. Deterministic same-session proof

IntegrationTests may provide a test-owned `IPostgreSqlStatementGateway`
decorator through the existing internal scope/runner seam. It records B001–B003
without issuing another command; after B003 has verified the session, it runs
test-only `SELECT pg_backend_pid()` immediately before each executed C001–C004,
D001, E001 and E002 statement on the same live connection and transaction. It
must never query before B001, because `SET TRANSACTION READ ONLY` must precede
the first ordinary query. The record must show every executed C/D/E statement
on one PID and one scope; unsupported and degraded branches must show the exact
omissions. Direct connection/transaction reference identity links B001–B003 to
that same scope.

This proof runs in both pinned-major categories. The PID query is explicitly
test-only, absent from product source, inventory,
package and artifact. It neither creates F001 nor exposes PID publicly. Direct
connection and transaction reference identity is also recorded by the test
scope, closing the possibility that equal PIDs were asserted without proving
the provider's one-scope composition.

## 32. Deterministic same-transaction proof

A bounded two-session fixture uses task gates in the test gateway, never sleep:

1. seed one table before capture and start the provider transaction;
2. after C004 and before D001, pause the gateway; an administrative second
   session creates and commits a new table plus index; release D001;
3. after D001 and before E001, pause again; the second session creates and
   commits a new index on the pre-existing table; release E001; and
4. assert the provider omitted both post-snapshot changes while an out-of-band
   connection after completion sees them.

PostgreSQL Repeatable Read fixes visibility at the first non-transaction-control
statement, which occurs before both barriers. If D001 or E001 used a later
transaction, the committed object would appear. The recording gateway also
proves E002 uses the same `NpgsqlTransaction` reference. Fixture objects are
removed administratively after assertions. No timing-only claim, catalog
corruption or productive observation query is allowed.

## 33. Read-only and rollback proof

The final provider path must reuse and re-prove 04B's effective
`transaction_read_only = on`, `repeatable read`, non-deferrable state, bounded
timeouts and permitted-role write rejection with SQLSTATE 25006 inside tests.
Persistent control row/schema state remains unchanged. Success, operation
failure, mapping/composition failure and requested cancellation each show an
explicit rollback attempt, disposed transaction/connection, reusable pool and
no lingering transaction. No commit path exists.

## 34. Package and public-API audit

The future `.nupkg` audit must prove:

- exactly `AssemblyMarker` and `PostgreSqlDatabaseSnapshotProvider` are exported
  by `DbHealthInspector.PostgreSql`; no Npgsql/inventory/session/filter/mapping
  or exception type becomes public;
- the provider implements the existing Core interface and `IAsyncDisposable`
  with only the frozen public members;
- the productive provider is present and traceable to the exact repository
  commit, while CLI behavior remains bootstrap-only;
- no test assembly, Testcontainers/xUnit asset, fixture SQL/credential/result,
  DBH rule implementation, JSON reporter, test marker or connection string is
  packaged; and
- isolated tool installation still exposes only `dbhealth` and its bootstrap
  help/version behavior.

## 35. Security invariants

04F preserves 04A secret hygiene; static, exact and parameterized SQL; one
explicit read-only transaction; bounded timeouts; typed restricted execution;
fixed failure messages; primary-over-cleanup precedence; rollback-only
completion; no business-row query; and package/source/output leakage scans.
`SECURITY.md` needs no G0 change. Implementation documentation must reference
the composed safety mechanism without weakening any current invariant.

## 36. PG-06 completion boundary

04B completed the foundation: closed inventory model, two-layer fail-closed
validator, typed executor, static B001–B003, prohibited-class tests and no
raw-SQL API. 04C–04E expanded that same mechanism to the final ten statements
and parameterized schema arrays. PG-06 may be marked fully completed only after
04F proves all real backlog criteria together:

1. every production SQL resource is one of the exact ten inventoried contracts;
2. exhaustive tests reject prohibited classes and all 790 wrong combinations;
3. no public/internal product path accepts user SQL;
4. all external schema values are bound `text[]` parameters and the composed
   provider passes one validated filter; and
5. safety documentation references the inventory/validator/provider mechanism,
   with 15/18 and package scans green.

Definition, implementation or a green subset alone does not complete PG-06.

## 37. Implementation entry criteria

Implementation may start only after all of the following:

1. this definition and the three reconciled canonical documents are integrated;
2. definition CI passes Ubuntu and Windows;
3. the human owner reviews this integrated definition;
4. the human owner explicitly authorizes implementation; and
5. a separate, scoped Claude Code implementation prompt is issued.

Definition integration never implies implementation authorization.

## 38. Implementation exit criteria

Closure requires evidence of all of the following:

1. the exact public provider API and no unintended export;
2. one complete valid `DatabaseSnapshot` or atomic failure;
3. one connection/session and one Repeatable Read read-only transaction;
4. exact supported/unsupported capability sequencing;
5. D001 plus E001/conditional E002 with one filter;
6. cross-object closure, schema derivation and deterministic ordering;
7. every cancellation boundary and primary/cleanup precedence;
8. idempotent ownership/disposal and the concurrent-call contract;
9. unchanged ten-statement inventory, 10/790 validator and no product
   business-row or test SQL;
10. deterministic same-session and same-transaction evidence;
11. read-only, rollback, persistent-state, pool-reuse and cleanup evidence;
12. PostgreSQL 15.18 and 18.4 green with cross-version Core semantics;
13. UnitTests, non-server, both server jobs, Ubuntu/Windows build and CLI smoke
    green with zero failures/skips/warnings/errors;
14. package/reflection/secret-leakage audit green and exact commit traceability;
15. PG-06's five real acceptance criteria satisfied before it is completed;
16. no Core semantic change, diagnostic, CLI expansion, JSON/reporting,
    dependency, tag, release or publication; and
17. Codex review/integration plus final human approval and governance closure.

## 39. Authorization boundary and verdict

```text
GC-DHI-04F DEFINITION CORRECTED — AWAITING HUMAN IMPLEMENTATION AUTHORIZATION
PG-06 full completion — NOT YET COMPLETED
GC-DHI-04F implementation — UNAUTHORIZED
```

No provider implementation, test or CI matrix is authorized by this document.

## 40. Next action

```text
Await human review of the corrected GC-DHI-04F definition.
No GC-DHI-04F implementation is authorized.
```
