# PostgreSQL Database Snapshot Provider

**Gate:** GC-DHI-04F — Snapshot Provider Composition and PostgreSQL Verification
**Backlog:** final composition of PG-01 … PG-05; final verification evidence for PG-06

## 1. Objetivo

Compose the approved 04A–04E primitives into one PostgreSQL implementation of Core's
`IDatabaseSnapshotProvider`. One capture yields one complete, engine-neutral `DatabaseSnapshot`, or
fails without returning a partial result. 04F adds **orchestration only**: no new SQL, no new
mapper, no alternate session or query path.

## 2. Public API

Exactly one new exported type. The assembly's exported-type count grows from one to **two**:

```text
DbHealthInspector.PostgreSql.AssemblyMarker
DbHealthInspector.PostgreSql.Snapshots.PostgreSqlDatabaseSnapshotProvider
```

```csharp
public sealed class PostgreSqlDatabaseSnapshotProvider : IDatabaseSnapshotProvider, IAsyncDisposable
{
    public static PostgreSqlDatabaseSnapshotProvider Create(string connectionString);
    public static PostgreSqlDatabaseSnapshotProvider Create(
        string connectionString,
        IReadOnlyCollection<string> includedSchemas,
        IReadOnlyCollection<string> excludedSchemas,
        TimeSpan statementTimeout);
    public Task<DatabaseSnapshot> CaptureAsync(CancellationToken cancellationToken);
    public ValueTask DisposeAsync();
}
```

Constructors are non-public. There is no public options, factory, exception, interface or Npgsql
type. `CreateForTesting` is an **internal** seam over the existing scope-factory interface, used by
unit tests for deterministic fakes and by IntegrationTests for the same-session decorator.

## 3. Construction and validation

Frozen order — nothing external exists until every argument has been accepted:

1. null-check the two collections;
2. build one immutable `PostgreSqlSchemaFilter` from defensive copies;
3. validate the statement timeout and derive the session options;
4. resolve the validated SQL inventory singleton;
5. create `PostgreSqlConnectionFactory` (which owns the one `NpgsqlDataSource`);
6. create the session runner and publish the provider.

A rejected argument therefore leaks no data source, no connection and no server-side state.

Acquiring the factory is deliberately the **last fallible step**: everything after it is pure
construction that cannot fail in normal operation. That ordering is what removes the need for any
cleanup on the construction path, and with it the sync-over-async disposal an earlier revision used
(GC-DHI-04F-C1, R1-02). Neither the provider nor the lifecycle contains `GetAwaiter().GetResult()`,
`.Wait(` or `Task.Run(` — asserted by a source-level test.

The connection string is handed to the 04A boundary and **never** retained in a second field,
message, `Data` entry or `ToString`.

### 3.1 Timeout validation and D1 lock derivation

The statement timeout's range and precision rules are enforced by the existing
`PostgreSqlInspectionSessionOptions`, which already rejects infinite, non-positive,
fractional-millisecond, sub-100 ms and over-5-minute values with exactly the promised semantics.
Restating them in the provider would create a second copy that could drift, so the provider
constructs those options as a **pure validator** first, using the smallest accepted lock timeout
(50 ms) as a placeholder. That placeholder is valid for every acceptable statement timeout — the
shortest is 100 ms — so the only argument that can make the validation fail is the one the caller
actually supplied, and they see its own error.

The lock timeout is then derived over the exact integer millisecond value:

```text
lockTimeoutMilliseconds = min(5000, statementTimeoutMilliseconds / 2)
```

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

For every accepted `S` in `[100, 300000]`, integer division gives `S / 2 >= 50`, and `min(5000, …)`
preserves that lower bound while establishing 5000 as the upper one. Because `S` is positive,
`S / 2 < S`, so the derived value is always strictly shorter than the statement timeout and no
separate clamp is needed. Both operands are integers, so the result is always whole milliseconds.
The value is never rounded, truncated or clamped — an inexact input is refused.

The idle-in-transaction timeout is fixed at 60 seconds and is never derived.

## 4. Ownership, lifetime and concurrency

- The caller owns the provider and must `DisposeAsync` it.
- The provider exclusively owns its connection factory; the factory owns its data source.
- Each capture's scope owns its connection and transaction; nothing outlives the callback.

Disposal and capture are coordinated by an explicit admission lease
(`PostgreSqlSnapshotProviderLifecycle`), not by a race delegated to the data source. Disposal is
**one logical operation spanning the capture drain and the factory release**:

- `Admit` rejects with `ObjectDisposedException(nameof(PostgreSqlDatabaseSnapshotProvider))` once
  disposal has begun, **before** a connection is opened; the admission decision and the in-flight
  increment happen under one lock, so disposal can never observe a count that misses an admitted
  capture;
- a capture admitted before disposal completes normally and is never cancelled by disposal;
- the first disposer alone runs the release, and **every** disposer — including the first — awaits
  the same shared completion. A second caller therefore cannot return while the first is still
  releasing the factory, and if the release throws, all callers observe that same exception
  instance. The release is never retried by a later caller (GC-DHI-04F-C1, R1-01);
- the lease is released in a `finally`, so a failure or cancellation can never strand a disposer.

It is deliberately **not** a semaphore around captures: captures never wait for one another, only
disposal waits for captures. The lock guards a counter and a flag, is never held across an `await`,
and nothing spins or blocks on asynchronous work. There is no finalizer, no synchronous dispose and
no sync-over-async.

One instance is safe for concurrent captures: each gets its own connection, transaction, executor
and buffers, sharing only the immutable filter/options and the data source.

## 5. Exact composition sequence

```text
requested-cancellation check → admission lease
  runner scope: open connection → begin RepeatableRead transaction
  B001 → B002 → B003
  ProbeAsync: C001 → C002 → C003 → C004 (only when C003 is true)
  cancellation checkpoint
  D001 with the provider's one filter
  cancellation checkpoint
  E001 (+ E002 exactly once, only when UsageStatistics is Available)
  cancellation checkpoint
  closure validation → schema derivation → ordering → DatabaseSnapshot
  final in-transaction cancellation checkpoint
  rollback (CancellationToken.None) → dispose transaction → dispose connection
post-cleanup cancellation checkpoint → release lease → return
```

Steps for the session and its cleanup remain owned by the existing runner; the probe remains one
call; the index operation remains one call. The provider re-derives no version decision, re-reads no
row and re-implements no mapper.

## 6. Capability branching

| Branch | Behaviour |
|---|---|
| Unsupported major | C001 only. Complete snapshot: real metadata, 04C unsupported capabilities, null statistics reset, empty schemas/tables/indexes. Not a partial supported snapshot. |
| `CatalogMetadata` unavailable | Existing fixed `PostgreSqlRequiredCatalogCapabilityException` from the probe. D001, E001 and E002 execute **zero** times; no snapshot. |
| `UsageStatistics` unavailable (C003 false) | C004 and E002 skipped. D001 and E001 still run. Every `ScanCount` is null and `StatisticsResetAtUtc` is null. Absence is unknown, never zero. |
| `UsageStatistics` degraded (C003 true, **C004 → SQLSTATE 42501**) | C001–C004 all execute; the 04C policy degrades the capability to `Unavailable`. D001 and E001 still run, **E002 does not**, `StatisticsResetAtUtc` is null and every `ScanCount` is null. |
| `DataProfiling` | Always `Disabled`; never causes a query. |

The 42501 degradation is proven **through the whole provider composition**, not only at
`ProbeAsync` (GC-DHI-04F-C1, R1-03): the test observes the exact execution counts
(C001–C004 = 1 each, D001 = 1, E001 = 1, **E002 = 0**) and the resulting capability, statistics and
scan-count values. The classification authority stays entirely in the 04C probe — the provider
source contains no `PostgresException`, no `SqlState` and no `42501`, asserted by test.

## 7. Cross-object composition

**One filter instance.** Proven by two complementary tests (GC-DHI-04F-C1, R1-04A): the provider's
filter field is **reference-identical** to the instance it was constructed with — it stores the
caller's object, never a defensively rebuilt equivalent — and the provider source constructs a
`PostgreSqlSchemaFilter` exactly once, in the four-argument factory, never inside the capture path.
A binding-level `ReferenceEquals` cannot express this, because
`PostgreSqlSqlParameterValue.TextArray` copies its input defensively, so the bound arrays are
distinct objects even when one filter instance was used. A third test asserts all three filtered
statements bind identical contents.

**Closure.** Every `IndexSnapshot` must reference exactly one `TableSnapshot` with the same ordinal
`(SchemaName, TableName)`. E001 reads `pg_index.indrelid` from the same catalog snapshot under the
same schema predicate as D001, so a missing table means the two statements disagreed — inconsistent
composition, never an index to drop, merge or synthesise. Partition roots, table partitions, virtual
index roots and physical index partitions each remain distinct objects closing against their own
table identity.

**Schema derivation.** There is no schema-list SQL. Schemas are the distinct schema names of the
validated table collection, sorted ordinally. Closure guarantees every index schema is already a
member. Empty user schemas holding no D001-eligible relation are intentionally unrepresented.

**Ordering.** Materialized explicitly before Core's defensive copy — hash-set or dictionary
enumeration is never an output contract:

```text
schemas  : SchemaName                          (ordinal)
tables   : SchemaName, TableName               (ordinal)
indexes  : SchemaName, TableName, IndexName    (ordinal)
key parts / INCLUDE : existing 04E server order
capabilities        : CatalogMetadata, UsageStatistics, DataProfiling
```

**Atomicity.** The only successful value is a fully constructed snapshot. Query results publish only
after their readers finish, composition happens locally inside the runner callback, and the caller
receives nothing until rollback and cleanup complete. A failure anywhere — including after D001
succeeded — returns nothing at all.

## 8. Composition failures

One new **internal sealed** parameterless exception:

```text
PostgreSqlSnapshotCompositionException
The PostgreSQL snapshot could not be composed safely.
```

No public/message/inner constructor, `InnerException` always null, `Data` always empty. Used for
exactly two situations:

1. failed index-to-table closure; and
2. an `ArgumentException`/`ArgumentOutOfRangeException` from Core's final snapshot guards.

The second is wrapped deliberately narrowly because Core's duplicate messages embed the offending
schema, table or index name — for example `Duplicate table 'schema.table'.` — and those names must
not escape. The catch never absorbs `OperationCanceledException`, Npgsql failures, out-of-memory
conditions or arbitrary programming faults; those propagate unchanged rather than being disguised as
data errors.

## 9. Cancellation and cleanup

Deterministic coverage exists for every boundary: before admission, each of C001–C004, D001, E001
and E002, after the queries, after composition, and post-cleanup. The requested token is forwarded
unchanged and never replaced or unnecessarily linked. Rollback always uses `CancellationToken.None`,
transaction disposal precedes connection disposal, and every cleanup action is attempted even after
one fails. The lease release is non-cancelable and guaranteed.

Two boundaries needed evidence that no statement seam can express, and were closed by
GC-DHI-04F-C2 (R1-04C/D):

- **after the last query, before composition** — the token is cancelled from the final reader's
  disposal, i.e. once the operation has genuinely finished reading. Proven on both branches: with
  statistics available the last query is E002, without them it is E001. The test asserts every
  query ran and that composition was never entered;
- **after composition, before the callback returns** — the two points have no statement between
  them, so the provider carries one `internal` observation callback that is always `null` in
  production (neither public `Create` can set it) and reachable only through `CreateForTesting`.
  The test asserts composition *did* run, a snapshot *was* built, and it was still not returned.
  To distinguish this checkpoint from the post-cleanup one, a companion test pairs it with a
  rollback failure: with the in-transaction checkpoint present the cancellation becomes the
  callback's primary and outranks the cleanup failure, whereas without it the rollback failure
  would surface instead. Removing the checkpoint makes exactly that test fail.

EDI precedence is unchanged from the existing runner: a primary failure — query, mapping,
composition or requested cancellation — always outranks a cleanup failure; with no primary, the
first cleanup failure remains observable. In particular a **cleanup failure that became
authoritative first is not displaced by a cancellation requested afterwards**: the post-cleanup
checkpoint never overwrites an already-captured cleanup primary (R1-04E).

Two provider-level races are proven with task gates rather than inferred (R1-04A/B, extended by
GC-DHI-04F-C3 R3-01). A capture held genuinely in flight — lease still held — while `DisposeAsync`
starts keeps disposal pending; new captures are rejected from that moment and never reach a scope;
and only after the capture completes (with its own primary failure, or its own requested
cancellation and token identity intact) does the owned resource release run. Disposal never cancels
an admitted capture.

The races observe the **provider-owned resource release itself**, not merely the lease. The
provider holds one release delegate that `DisposeAsync` always hands to the lifecycle; a test
substitutes an observable, blockable resource through `CreateForTesting`, so the full ordering is
measured:

```text
dispose-start
  (release count still 0 while the capture is verifiably in flight)
resource-release-start        exactly once
resource-release-complete
DisposeAsync-complete
```

The three impossibilities are asserted explicitly: the release cannot begin before the drain (proven
by the zero count taken while the capture still holds its lease), `DisposeAsync` cannot complete
before the release did, and the release count can never exceed one. Reordering the lifecycle so the
release precedes the drain makes both race tests fail.

There is deliberately no test-recorded "lease released" marker: the lease is freed inside the
provider's own `finally`, which runs before the capture's exception reaches the test, so such a
marker would describe the recorder rather than the provider.

**Production binding.** Both public factories bind that delegate to the real
`PostgreSqlConnectionFactory.DisposeAsync` over the factory they created — asserted by tests that
check the delegate's target instance and method name. Only `CreateForTesting` can substitute a
double, and there is one disposal algorithm, not two.

## 10. Same-session proof (test-only)

IntegrationTests supply a `SameSessionProofScope` through the existing internal scope seam. It opens
a real connection and a real `RepeatableRead` transaction, then hands the executor a passive
decorator that runs a **test-only** `SELECT pg_backend_pid()` immediately before each executed
C001–C004, D001, E001 and E002 statement on that same connection and transaction.

B001–B003 are recorded **without** a probe: `SET TRANSACTION READ ONLY` must remain the first
statement of the transaction, and issuing an ordinary query ahead of it would change the very thing
under test.

The proof asserts one distinct backend PID across all seven executed C/D/E statements, plus
reference identity — the transaction belonged to this scope's own connection, recorded while both
were still alive — which closes the gap that equal PIDs alone would leave. It runs on **both** pinned
majors.

The PID query exists only in the IntegrationTests assembly: absent from product source, from the
frozen inventory and from the package. It creates no F001 and exposes no PID publicly.

## 11. Same-transaction proof (test-only)

Deterministic barriers, never timers. Implemented identically on **both** pinned majors
(GC-DHI-04F-C1, R1-05). During one capture:

- **Barrier 1** — after C004, before D001: an out-of-band admin session commits a new table **and**
  a new index on it;
- **Barrier 2** — after D001, before E001: it commits another index on a table that already existed
  when the capture's snapshot was established.

The capture's `RepeatableRead` snapshot predates both commits, so none of the three objects appears.
The second barrier is the one a single barrier cannot cover: its table *is* in the snapshot, so the
index was withheld by isolation rather than by its table being absent. Both suites additionally
assert that E001 and E002 ran in the same scope and on the same backend, closing the possibility
that the withheld index re-entered through statistics reconciliation.

A fresh out-of-band observation afterwards sees all three objects, proving the commits really
happened and the absence is isolation rather than a failed setup. No sleep, no timing assertion and
no catalog corruption is involved. All objects are dropped in a `finally`.

### 11.1 Read-only safety on both majors

PostgreSQL 15 carries its own read-only evidence rather than inheriting it by reusing the runner:
the verified B003 state is asserted (`transaction_read_only = on`, `repeatable read`, all three
timeouts matching), a test-owned write is rejected with SQLSTATE **25006** by a role that is
deliberately able to write, persistent row counts are unchanged, and the pool remains reusable
across repeated captures and a fresh provider.

**Non-deferrable is observed, not inferred** (GC-DHI-04F-C2, R1-05). B003 verifies isolation and
read-only but does not report the deferrable flag, so the same-session proof scope reads all three
settings directly from the capture's own live transaction, once, at the first statement after B003 —
the earliest point an ordinary query is permitted:

```text
transaction_isolation  -> repeatable read
transaction_read_only  -> on
transaction_deferrable -> off
```

The observation runs on **both** majors, on the capture's own connection and transaction (asserted
by reference identity), and its SQL exists solely in the IntegrationTests assembly — it is absent
from product source, from the frozen inventory and from the package.

## 12. SQL inventory preservation

Composition required **zero** new productive SQL. The inventory remains exactly:

```text
B001 B002 B003 C001 C002 C003 C004 D001 E001 E002
```

with ten statement IDs, eight command kinds, two parameter types, ten definitions, ten frozen
contracts and a validator matrix of 800 combinations — 10 accepted, 790 rejected. The frozen texts
are byte-identical:

```text
E001 6262 / d45b8ed1e0d842b1474839a3beadf6d1a0d4233cfa847c3887c41cfd4b1184d7
E002  737 / fe8f23a5dff2cdfb8d08acf4fb7f7a3f90aef4b7e9eee4b678cde8c260624919
C002 2027 / 777cb44afb178c299566f1a8c0251e3ab9ba47480bd578b6a339f4d1c24c5a90
D001 1816 / 13b4e88d7ac0053d87cf760b3e6a64ae879effa91de66a15bd693ba458680b87
```

The product accepts no raw or user SQL and executes no business-row `SELECT`, `COUNT(*)`, `EXPLAIN`,
`ANALYZE`, dynamic SQL or `pg_stat_statements`. Test-owned DDL, synthetic rows and the PID
observation stay confined to IntegrationTests.

## 13. PostgreSQL 15/18 matrix

| Major | Pinned image | Role |
|---|---|---|
| 18 | `postgres:18.4@sha256:3a82e1f5…744a` | canonical exhaustive server suite and sole packaging job |
| 15 | `postgres:15.18@sha256:6eb0add3…d425` | compatibility, test-only |

Floating `postgres:15` / `postgres:18` are forbidden. The PostgreSQL 15 fixture is completely
isolated: its own image, container, database, credentials and roles, with no mutable state shared
between majors, so either suite can run alone.

`Category=PostgreSqlServer` remains the PostgreSQL 18 exhaustive suite. The new
`Category=PostgreSql15` carries the provider and the shared compatibility contract; genuinely
18-only empirical cases — the relation-state discovery matrix, the FDW proofs and the permission-loss
topologies — stay in the 18 suite and are not duplicated.

Shared assertion helpers (`CrossVersionSnapshotAssertions`) freeze the same Core expectations in both
categories so they cannot drift. They live in one file, are never duplicated, and are **executed by
both major-specific suites** (GC-DHI-04F-C1/C2, R1-06):

| Shared helper | Freezes |
|---|---|
| `AssertSupportedSnapshotShape` | engine identity shape, capability meanings, `DataProfiling` Disabled, nullable UTC reset, order, closure, value domains |
| `AssertCommonIndexZoo` | keys, INCLUDE order, expression/predicate presence, uniqueness, constraint association, valid/ready/live, **exact** access method for all 21 common indexes, **exact** qualified collation identity, **exact** operator-class identity including the full ordered options encoding |
| `AssertCommonTableSemantics` | **exact** `SchemaName`/`TableName`/`RelationKind` for the ordinary table, the partitioned root and the physical member, each with **both** partition flags frozen — including `IsPartitionedRoot=false, IsPartition=false` on the ordinary table — and the three kinds proven distinct |
| `AssertCommonViewSemantics` | exact identity and `RelationKind` for the view and materialized view, root/partition flags, view zero-storage contract |
| `AssertCommonIndexMemberSemantics` | **exact** identity — schema, table and `IndexName` — plus access method for the virtual root *and* its physical member; root has no storage and no counter; the root's **and** the member's entire key structures are **each frozen independently** against the same fixture-derived contract (position, column, null expression, qualified collation, qualified operator class, sort direction, nulls ordering) before they are compared to one another; the member's table really is a `Partition` |
| `AssertInvalidIndexSemantics` | exact identity, access method, and the **complete** `IsValid`/`IsReady`/`IsLive` triple |
| `AssertUsageStatisticsAvailable` | capability Available; counter domain (non-negative or null), UTC reset contract |
| `AssertUsageStatisticsUnavailable` | capability Unavailable; reset null; every counter null; metadata still complete |
| `AssertFilteringSemantics` | include/exclude select the same identities ordinally and partition the unfiltered capture; permanent system-schema exclusion |

Expectations are derived **independently of the product** (GC-DHI-04F-C3, R3-02): from the fixture
DDL, the frozen 04D/04E contracts, or explicit constants. Nothing is compared against a value the
mapper produced, and the encoder is never called to build an expected string. Two concrete
examples:

```text
COLLATE "C"                     -> "pg_catalog"."C"
text_pattern_ops                -> "pg_catalog"."text_pattern_ops"
int4_minmax_multi_ops(32)       -> "pg_catalog"."int4_minmax_multi_ops"|options[1;19:values_per_range=32]
int4_bloom_ops(a=16, b=0.05)    -> …|options[2;23:n_distinct_per_range=1624:false_positive_rate=0.05]
int4_bloom_ops(b=0.05, a=16)    -> …|options[2;24:false_positive_rate=0.0523:n_distinct_per_range=16]
```

The last two are the inverse stored-order pair: same option set, opposite storage sequence,
therefore different canonical identities on both majors.

Independence has one further consequence worth stating explicitly (GC-DHI-04F-C4, R4-01;
GC-DHI-04F-C5, R5-01). Neither the partitioned index root nor its physical member is asserted by
comparing it to the other: both are produced by the same mapper in the same capture, so a defect that
corrupts them symmetrically would appear on both sides and the comparison would agree with it. The
fixture DDL declares exactly one key — `partitioned_orders (region text)` indexed by
`zoo_partitioned` — giving position `1`, column `region`, no expression, collation
`"pg_catalog"."default"`, operator class `"pg_catalog"."text_ops"`, ascending, nulls last. That
single frozen contract is applied **separately to the root and to the member**, so each side stands
on the fixture rather than on its counterpart. The member index name
`partitioned_orders_emea_region_idx` is likewise frozen exactly, not matched by prefix or naming
convention; it was observed identically on 15.18 and 18.4 before being frozen.

Only after both sides are independently anchored is root/member agreement asserted, and it then
compares the **complete** key structure — all seven fields, including `Expression`, `SortDirection`
and `NullsOrdering` — so a field that diverges between the two cannot pass unexamined. That
comparison is supplementary evidence; it is never the source of an expectation.

Both majors are also anchored to the raw catalog rather than to a constant alone (GC-DHI-04F-C4,
R4-02). Each suite reads `pg_attribute.attoptions` out of band for the inverse stored-order pair and
asserts the two arrays element by element, proves they hold the same set in a genuinely different
order, and bridges raw values to the independently spelled canonical identity to the identity the
provider actually mapped. The length prefixes (`23` for `n_distinct_per_range=16`, `24` for
`false_positive_rate=0.05`) are re-derived from the raw values rather than trusted as literals. The
raw arrays were observed to differ in order on 15.18 and 18.4 alike, so no version switch exists
here either.

The invalid partitioned index reports the identical triple on both majors —
`IsValid=false, IsReady=true, IsLive=true`, access method `btree` — observed directly on
PostgreSQL 15.18 and 18.4 before being frozen, so no version switch exists anywhere in the helper.

One documented fixture difference: the GC-DHI-04C statistics-revoked container is deliberately
minimal and holds no indexes, while PostgreSQL 15's degraded role sees the full zoo. The helper
takes an explicit `expectIndexes` flag for that reason — a **fixture** property, not a version
difference — and every other part of the degraded contract is asserted identically on both. The two
fixtures also place their views in different schemas, so `AssertCommonViewSemantics` takes the
schema as a parameter; the asserted contract is identical.

They assert **contracts**, not byte equality. What is deliberately *not* compared across majors, and
why:

| Not compared | Reason |
|---|---|
| version text | the major is asserted per suite; the rest legitimately differs |
| OIDs | server-generated and meaningless across instances |
| physical sizes, estimated rows | depend on page layout, fill and `ANALYZE` timing |
| live scan counts | depend on what each server actually executed |
| statistics reset timestamp | wall-clock, per container |
| deparser text | each server's own official `pg_get_indexdef`/`pg_get_expr` output |

Each of those is instead checked for its own contract — non-negative, nullable-or-exact, present or
absent — against that same server's own observation.

The whole common zoo — including `NULLS NOT DISTINCT`, INCLUDE, expression, mixed, partial,
collation, non-default opclass, all five non-B-tree access methods, BRIN operator-class options
(different values *and* inverse stored order), a partitioned index root with a physical partition,
and the deterministic `CREATE INDEX … ON ONLY` invalid root — was confirmed to be accepted by 15.18
before the fixture was written.

## 14. CI architecture

| Job | Platform | Tests | Pack/upload |
|---|---|---|---|
| `Ubuntu` | ubuntu-latest, PG 18.4 | build; UnitTests; non-server; PostgreSqlServer | yes — sole `dbhealth-bootstrap-package` producer |
| `PostgreSQL 15` | ubuntu-latest, PG 15.18 | restore/build; `Category=PostgreSql15` only | no |
| `Windows` | windows-latest, no server | build; UnitTests; non-server; CLI bootstrap smoke | no |

Both non-server filters now exclude **both** server categories, so no job reports skipped tests and
no server suite runs twice. The PostgreSQL 15 job never runs UnitTests or the non-server suite a
second time, never packs, never uploads and never uses the canonical artifact name.

> The new job's name must be added to the repository's required checks at integration; that is a
> platform setting, not a repository file, and was deliberately not changed here.

## 15. Scope exclusions

Not implemented and explicitly out of scope: DBH001–DBH005, diagnostic rules, findings, risk rules,
CLI `inspect` and its options, connection-source resolution, JSON and console reporting, exit-code
mapping, business-row profiling, query plans, `pg_stat_statements`, tags, releases and NuGet
publication. Core, the CLI and the PostgreSQL connection boundary are unchanged, no dependency was
added, and the CLI remains bootstrap-only.

## 16. Known limitations

- Sequencing C002/C003 before the object queries is enforced by the provider's own composition and
  by test composition; the restricted operation view still does not police call order by itself.
- `indisready` / `indislive` remain unit-only, as in 04E: producing a not-ready or not-live index
  needs a concurrent-build race the gate forbids fabricating.
- The PostgreSQL 15 suite verifies the provider and the shared contract, not the 18-only empirical
  matrices, which remain deliberately version-specific.
