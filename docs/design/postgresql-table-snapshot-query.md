# PostgreSQL Table Snapshot Query and Mapping

**Gate:** GC-DHI-04D — Table Snapshot Query and Mapping
**Backlog:** PG-04
**Predecessors:** GC-DHI-04A, GC-DHI-04B and GC-DHI-04C approved and closed
**Scope:** `DbHealthInspector.PostgreSql.Tables`, plus the D001 and C002 changes to `Sql/` and
`Sessions/`
**Status:** Implemented; pending Codex review.

## 1. Objetivo

Answer one question, once per inspection: *which table-like relations exist, and how big are
they*. The query runs only inside a verified GC-DHI-04B session, executes only the inventoried
D001 statement, reads only `pg_catalog`, and returns one immutable collection of existing Core
`TableSnapshot` values. No business row is ever read.

## 2. Dependencias A–C

GC-DHI-04A supplies the connection boundary and the cancellation-association rule; GC-DHI-04B the
`RepeatableRead`, read-only, rollback-only session, the frozen inventory, the two-layer validator,
the typed row seams and the sanitized error boundary; GC-DHI-04C the capability probe whose C002
verdict decides whether reading catalog metadata is permitted at all. None of those contracts was
weakened. B001–B003 and C001/C003/C004 remain byte-for-byte unchanged.

## 3. Filtro de esquemas

```csharp
internal sealed class PostgreSqlSchemaFilter
{
    ReadOnlyCollection<string> IncludedSchemas { get; }
    ReadOnlyCollection<string> ExcludedSchemas { get; }
}
```

| Input | Meaning |
|---|---|
| Empty include list | Every eligible non-system schema |
| Non-empty include list | Only the exact names listed |
| Empty exclude list | No additional exclusion |
| Non-empty exclude list | Remove the exact names listed |
| Same name in both lists | Invalid filter |

Names are compared ordinally and case-sensitively — `Public` and `public` are genuinely different
schemas in PostgreSQL, so treating them as the same would be a bug, not a convenience. There is no
pattern, wildcard, regular expression, dynamic identifier or SQL fragment: a name only ever travels
as an element of a bound `text[]`.

Rejected: a null collection, a null name, an empty or whitespace-only name, a name containing NUL,
a duplicate within either list, and the same name in both lists. Every rejection raises
`PostgreSqlSchemaFilterException` with exactly:

```text
The PostgreSQL schema filter is invalid.
```

The message never names the offending schema, and every rejection is indistinguishable from every
other — a caller cannot learn *why* a filter was refused.

Both lists are copied on construction and sorted with `StringComparer.Ordinal`, so a caller that
mutates its own array afterwards cannot change an existing filter, and two filters built from the
same names in any order bind identical arrays.

## 4. TextArray

The inventory gains exactly one parameter type:

```text
PostgreSqlSqlParameterType.TextArray  ->  NpgsqlDbType.Array | NpgsqlDbType.Text
```

The payload is a `ReadOnlyCollection<string>` over a copy the caller never held; the gateway builds
a fresh array from it at bind time. Element order is preserved exactly. An empty array is valid and
means "no filter of that kind". A null sequence or a null element is rejected at construction.

No `object`, `dynamic`, generic conversion, arbitrary `NpgsqlDbType`, dictionary or
caller-supplied parameter name exists. Reading the text payload of an `Int32` value is refused
rather than silently returning something — the payload is closed by construction.

Parameter types are now exactly two, and no more.

## 5. C002 ampliado

C002 keeps its statement id, its `SelectCapabilityCheck` kind, its one-row/one-Boolean shape and
its zero parameters. It gains exactly the three checks GC-DHI-04D §8 specifies:

```text
has_function_privilege(current_user, 'pg_catalog.pg_table_size(regclass)',           'EXECUTE')
has_function_privilege(current_user, 'pg_catalog.pg_indexes_size(regclass)',         'EXECUTE')
has_function_privilege(current_user, 'pg_catalog.pg_total_relation_size(regclass)',  'EXECUTE')
```

Missing any one of them makes C002 false, which makes the probe raise the existing fixed
`PostgreSqlRequiredCatalogCapabilityException`. The failure never identifies which function was
missing, never adds a sensitive reason and never grants anything.

The added text is reproduced **verbatim from the definition, including its indentation**, so the
addition can be diffed against §8 character for character. SQL is whitespace-insensitive, so this
affects only how the literal looks.

C001, C003 and C004 are byte-identical to GC-DHI-04C.

## 6. D001

D001 is copied character-for-character from GC-DHI-04D §9 — 1816 characters, extracted from the
definition file rather than retyped — and a unit test compares the productive constant against an
independent transcription of the same text.

```text
ID          ReadTableSnapshots
Kind        SelectTableMetadata
Parameters  $1 TextArray (included schemas), $2 TextArray (excluded schemas)
```

It reads `pg_catalog.pg_class`, inner-joined to `pg_catalog.pg_namespace`, with a correlated
`EXISTS` over `pg_catalog.pg_constraint` — the three relations this gate authorizes and no others.
It contains no `COUNT(*)`, no aggregate, no `pg_size_pretty`, no `pg_partition_tree`, no
`pg_inherits` aggregation, no recursive CTE, no dynamic identifier and no concatenated schema name.
The index relkind `'i'` never appears.

Four system-schema exclusions are part of the frozen text rather than of the filter:

```text
nspname <> 'pg_catalog'
nspname <> 'information_schema'
nspname NOT LIKE 'pg_toast%'
nspname NOT LIKE 'pg_temp_%'
```

An include list therefore cannot re-enable a system schema — naming one simply matches nothing.

The inventory is now exactly eight statements: B001, B002, B003, C001, C002, C003, C004, D001 —
eight ids, seven command kinds, two parameter types, eight definitions, eight frozen contracts.

## 7. Shape

| Ordinal | Value | Type | Nullable |
|---:|---|---|---|
| 0 | Schema name | String | No |
| 1 | Relation name | String | No |
| 2 | `relkind` | one-character String | No |
| 3 | `relpersistence` | one-character String | No |
| 4 | Is partition | Boolean | No |
| 5 | Estimated rows | Int64 | **Yes** |
| 6 | Table size | Int64 | No |
| 7 | Index size | Int64 | No |
| 8 | Total size | Int64 | No |
| 9 | Primary-key state | Boolean | No |

Zero rows is valid. Every row must have exactly ten columns, and ordinal 5 is the only column that
may be NULL. The row seam gained only `GetInt64(int)` — no `GetValue`, `object`, `dynamic`,
exposed `GetFieldValue<T>`, generic mapper, column-name lookup or exposed `NpgsqlDataReader`.

Rows are mapped as they are read. A shape or mapping failure at any row abandons the whole read:
no partial collection is ever returned.

## 8. Mapping

`relkind`, `relpersistence` and `relispartition` are validated **as one tuple**, never as three
independent allowlists. Each value is individually legal in *some* relation, so checking them
separately admits states PostgreSQL cannot produce — an unlogged materialized view, a view attached
as a partition — and maps them to a plausible-looking snapshot.

### 8.1 Accepted states

The union of what the supported major range (PostgreSQL 15–18) can hold. 17 tuples:

| `relkind` | Persistence | `relispartition` | Core kind | Source |
|---|---|---:|---|---|
| `r` | `p` | false | `OrdinaryTable` | PG 18.4 observed |
| `r` | `u` | false | `OrdinaryTable` | PG 18.4 observed |
| `r` | `t` | false | `TemporaryTable` | PG 18.4 observed |
| `r` | `p` | true | `Partition` | PG 18.4 observed |
| `r` | `u` | true | `Partition` | PG 18.4 observed |
| `r` | `t` | true | `Partition` | PG 18.4 observed |
| `p` | `p` | false | `PartitionedTable` | PG 18.4 observed |
| `p` | `t` | false | `PartitionedTable` | PG 18.4 observed |
| `p` | `u` | false | `PartitionedTable` | PG 15–17 only — see §8.3 |
| `p` | `p` | true | `Partition` | PG 18.4 observed |
| `p` | `t` | true | `Partition` | PG 18.4 observed |
| `p` | `u` | true | `Partition` | PG 15–17 only — see §8.3 |
| `v` | `p` | false | `View` | PG 18.4 observed |
| `v` | `t` | false | `View` | PG 18.4 observed |
| `m` | `p` | false | `MaterializedView` | PG 18.4 observed |
| `f` | `p` | false | `ForeignTable` | PG 18.4 observed |
| `f` | `p` | true | `Partition` | PG 18.4 observed |

### 8.2 Rejected states

The remaining 13 of the 30 combinations over five kinds and three persistences. No supported major
can produce any of them:

| `relkind` | Persistence | `relispartition` | Why impossible |
|---|---|---:|---|
| `v` | `u` | false / true | `CREATE UNLOGGED VIEW` — views have no storage (`42601`) |
| `v` | `p` / `t` | true | a view can never be attached as a partition (`42809`) |
| `m` | `u` | false / true | `materialized views cannot be unlogged` (`0A000`) |
| `m` | `t` | false / true | no `TEMP MATERIALIZED VIEW` form exists (`42601`) |
| `m` | `p` | true | a materialized view can never be a partition (`42809`) |
| `f` | `u` / `t` | false / true | no `UNLOGGED`/`TEMP FOREIGN TABLE` form exists (`42601`) |

### 8.3 Supported-version caution

PostgreSQL 18 **removed** support for unlogged partitioned tables — Release 18, *Migration to
Version 18 → Incompatibilities*, commit `e2bab2d79`. PostgreSQL 15, 16 and 17 accepted
`CREATE UNLOGGED TABLE … PARTITION BY` and recorded `relpersistence = 'u'` on the partitioned table.
Such a relation still exists on a supported server, so the adapter accepts `p` + `u` even though the
PostgreSQL 18 fixture cannot create one. Rejecting it would fail a legitimate catalog row on 15–17.

Materialized views are the opposite case and need no such allowance: `CREATE MATERIALIZED VIEW` has
offered neither `UNLOGGED` nor `TEMPORARY` in any supported version (confirmed against the
PostgreSQL 15 synopsis), so `m` is permanent-only across the whole range.

The permanent PostgreSQL 15/18 comparison matrix remains deferred to GC-DHI-04F; C1 adds no
PostgreSQL 15 container.

### 8.4 Empirical basis

`RelationStateMatrixTests` reproduces the matrix against the pinned PostgreSQL 18.4 image: every DDL
form is attempted inside a transaction that is always rolled back, with one savepoint per probe, and
the resulting `pg_class` state recorded. Nothing it creates outlives the call, so the relation zoo
the rest of the suite asserts on is untouched. A further test asserts that every state PostgreSQL
actually produced is one the mapper accepts, so the allowlist can never become narrower than
reality. All 19 forms Codex R2 required are covered — including the temporary-partition topology
below, closed by GC-DHI-04D-C2 (R2-04).

**Temporary partition topology.** A temporary partitioned root has three distinct empirically
observed shapes, and it matters which one a given relation is:

| Relation | `relkind` | `relpersistence` | `relispartition` | Core kind |
|---|---|---:|---:|---|
| Temporary partitioned root (e.g. `m_part_temp`) | `p` | `t` | `false` | `PartitionedTable` |
| Temporary subpartition — itself partitioned (e.g. `m_sub_temp`) | `p` | `t` | `true` | `Partition` |
| Temporary leaf partition (e.g. `m_leaf_temp`) | `r` | `t` | `true` | `Partition` |

The middle row — `p`/`t`/`true` — is a temporary table that is simultaneously a partition of its
temporary root *and* itself partitioned by range. PostgreSQL requires every relation in a partition
hierarchy to share the root's persistence, so a temporary root can only be partitioned by further
temporary relations; this is the temporary analogue of the permanent subpartitioned-partition case
in the table above. Reproduced with:

```sql
CREATE TEMP TABLE m_part_temp(id int) PARTITION BY RANGE(id);
CREATE TEMP TABLE m_sub_temp PARTITION OF m_part_temp
    FOR VALUES FROM (10) TO (20) PARTITION BY RANGE(id);
```

Both statements succeed on PostgreSQL 18.4 (no `SQLSTATE`), and `pg_class` shows `m_sub_temp` as
`relkind = 'p'`, `relpersistence = 't'`, `relispartition = true`. The mapper accepts this tuple —
`PostgreSqlTableSnapshotMapper.Map` returns `RelationKind.Partition` with `IsPartition = true` and
`IsPartitionedRoot = false` — and no change to the mapper was required: the tuple was already one of
the 17 accepted rows in §8.1, and this closes the gap between that acceptance and having actually
observed the state it accepts.

### 8.5 Ordering

Every value is validated **before** a `TableSnapshot` is constructed. That ordering matters: Core's
own guards are correct but they name the offending parameter and sometimes the offending value, and
those exceptions would escape through the session boundary. Pre-validating means a bad row always
surfaces as the fixed, valueless `PostgreSqlTableSnapshotMappingException`.

An unrecognised `relkind` or persistence is a failure. `RelationKind.Unknown` is never selected —
a test walks every *accepted* combination and asserts it never appears.

The `TemporaryTable` branch is unreachable from a normal query, because D001 excludes `pg_temp_*`.
It has unit coverage anyway so the mapping is complete and provable.

### 8.6 Wrong CLR types

A column whose runtime type is not the one D001 promises makes the reader raise
`InvalidCastException`. That is a bad row like any other, so it is translated — at the exact seam
where the ten typed reads happen, and nowhere else — into the same fixed
`PostgreSqlTableSnapshotMappingException`, with `InnerException` null and `Data` empty. The original
exception never escapes, so no driver-authored message naming CLR types or values reaches a surface
that crosses the session boundary.

The catch is narrow in both dimensions: one concrete exception type, and only the ten reads. A
cancellation, an Npgsql failure or a disposal failure passes through untouched — none of them means
"wrong type", and absorbing them would hide a real fault behind a mapping error. All ten ordinals
have unit coverage, at the first, middle and last row, each asserting that no partial result is
returned and that the reader and the command are both released.

## 9. Particiones

Partition membership is tested **first**, before `relkind` — but only **after** the whole tuple has
been accepted by §8. That ordering is what makes a *subpartitioned partition* — `relkind = 'p'` and
`relispartition = true` — a `Partition` rather than an independent root, without letting
`relispartition` launder an impossible state into a plausible-looking `Partition`. A view or a
materialized view marked as a partition is rejected, never reclassified.

Verified against real PostgreSQL 18.4 with a three-level tree: a root, one subpartitioned
partition, and two leaves.

## 10. Tamaños de raíz no agregados

**Partition-root sizes are the direct PostgreSQL size-function results for the root's own OID;
DbHealthInspector does not aggregate descendants.** D001 calls `pg_table_size`, `pg_indexes_size`
and `pg_total_relation_size` on each relation's own OID and nothing else — no `SUM`, no
`pg_partition_tree`, no recursive CTE, no `pg_inherits` walk, no child traversal.

The contract is therefore an *equality against PostgreSQL itself*, not a fixed number. The
integration tests read the three functions out of band for the root's OID and assert that D001
reports exactly those values, for the root, the intermediate partition and both leaves alike.

A root's size is **not** asserted to be zero. PostgreSQL 18.4 happens to return zero for a
partitioned root — recorded below as an observation, not a requirement — but that is a property of
the server version, not of this adapter, and freezing it would turn one version's incidental answer
into a contract:

```text
observed on PostgreSQL 18.4 (informative, not contractual)
events            (root, relkind p)          table 0       index 0   total 0
events_emea       (subpartitioned partition) table 0       index 0   total 0
events_amer       (leaf)                     table 122880  index 0   total 122880
events_emea_2026  (leaf)                     table 122880  index 0   total 122880
```

What *is* asserted, on any version: each of the four relations appears exactly once and stands on
its own; every size is non-negative; each snapshot equals the direct function result for its own
OID; and the root's total is strictly less than the sum of its leaves, so no aggregation happened.

## 11. Estimates

Views and any `reltuples < 0` become NULL in SQL; otherwise the returned `bigint` is used
unchanged. Null means *unknown*, not zero; zero is a legitimate value. A negative value arriving at
the mapper is a failure. Nothing is re-estimated, rounded again in C#, or derived from `COUNT(*)`,
and no business row is read to produce it.

Verified: an analyzed table reports its real estimate; a never-analyzed table reports NULL, because
PostgreSQL stores `reltuples = -1` until the first `ANALYZE`.

## 12. Primary key

`HasPrimaryKey` comes exclusively from the correlated `EXISTS` over `pg_constraint` with
`contype = 'p'` and `conrelid = relation.oid`. It is never inferred from a unique index,
`relhasindex`, `indisprimary` or column nullability — tests assert those identifiers appear nowhere
in the inventory. DBH001 is not implemented here.

## 13. Orden

D001 orders by schema then relation name, and the mapper sorts **again** in memory with
`StringComparer.Ordinal` on `SchemaName` then `TableName`. The repetition is deliberate: database
collation and process culture are not things this adapter is willing to depend on for a
deterministic result.

## 14. Duplicados

A duplicate `(SchemaName, TableName)` pair fails the whole result. Names differing only by case are
not duplicates, and the same table name in two schemas is not a duplicate. An empty result is
valid, and no OID enters the result.

## 15. Boundary

`PostgreSqlInspectionOperationExecutor` now exposes exactly five typed operations:

```text
ReadServerIdentityAsync            C001
CheckCatalogMetadataAccessAsync    C002
CheckUsageStatisticsAccessAsync    C003
ReadStatisticsResetAsync           C004
ReadTableSnapshotsAsync            D001
```

`ReadTableSnapshotsAsync` takes an already-validated `PostgreSqlSchemaFilter` and a token, and
nothing else. No overload accepts a statement id, SQL text, a pattern or a generic parameter
collection; nothing returns the executor, gateway, connection, transaction, command or reader; and
B001–B003 remain unnameable through this surface.

## 16. Límite de secuenciación C002/D001

GC-DHI-04D implements the **query and the mapper**, not the provider. The operation view therefore
does **not** enforce that C002 runs before D001, and this design does not claim it does.

Real PostgreSQL tests compose the ordering themselves:

```text
verified session -> PostgreSqlServerCapabilityProbe.ProbeAsync
                 -> require Supported
                 -> require CatalogMetadata Available
                 -> ReadTableSnapshotsAsync
```

and on the revoked-function fixture:

```text
verified session -> capability probe -> C002 false
                 -> PostgreSqlRequiredCatalogCapabilityException
                 -> D001 execution count 0
```

The mandatory productive sequencing is GC-DHI-04F's work. No provider or state machine was added
here.

## 17. Error model

```text
PostgreSqlSchemaFilterException          "The PostgreSQL schema filter is invalid."
PostgreSqlTableSnapshotMappingException  "The PostgreSQL table metadata row is invalid."
```

Both are internal, sealed, and have exactly one parameterless constructor — no code path in the
assembly can attach a message, an inner exception or `Data`. `InnerException` is always null and
`Data` is always empty. Neither carries a schema, table, `relkind`, persistence, filter, SQL,
SQLSTATE or PostgreSQL message. Expected Npgsql errors continue through the existing sanitized
boundary; unexpected exceptions follow the existing propagation contract. No catch-all classifier
was added.

## 18. Leakage

Leak assertions use `Assert.False(leaked, "Sensitive data was exposed.")` with a fixed message, so
a failure never prints the marker or the surrounding surface, and no marker is used as theory data.
Coverage spans `Message`, `ToString()`, `StackTrace`, `Data`, `InnerException`, the result's
`ToString()`, and the result's fields. `PostgreSqlTableSnapshotQueryResult` is deliberately not a
`record`: a generated `ToString()` would render every schema and table name structurally.

## 19. Cancellation

A precancelled token prevents D001 entirely — stated explicitly in the executor rather than
inherited from driver behaviour. The caller's exact token reaches the gateway and every read.
Cancellation before the first row, between rows or during the final read stops the loop, returns no
partial collection, and still releases the reader.

Every stage at which D001 can be interrupted has explicit coverage:

| Stage | Outcome |
|---|---|
| Precancelled token | No statement executed at all |
| **Command execution** | Requested token surfaces; **no reader acquired**; nothing to release |
| Before the first row | No partial collection; reader released |
| Between rows | No partial collection; reader released |
| Final row | No partial collection; reader released |
| End-of-rows read | No partial collection; reader released |
| **Reader disposal, no primary** | Cleanup-only: the cancellation itself surfaces |
| **Reader disposal, primary exists** | Primary stays authoritative; disposal never displaces it |
| Cleanup-only failure | Propagates |
| Real-server callback cancellation | Session rolls back; connection released; pool reusable |

The two stages added by GC-DHI-04D-C1 are marked in bold. Both are deterministic — a hook cancels
the caller's token at the exact moment, with no sleep and no race. A requested cancellation is never
rewritten into a mapping error, and a disposal cancellation never replaces an existing primary,
including when that primary is a wrong-type rejection.

## 20. Cleanup

The reader is released through the existing `PostgreSqlAsyncCleanup` rather than `await using`, so
a disposal failure can never replace a shape failure, a mapping failure or a cancellation. A
cleanup-only failure still propagates. Rollback continues to use `CancellationToken.None`, the pool
stays reusable, and no retry, logging, commit or autocommit was added.

## 21. Normal fixture

Reuses only:

```text
docker.io/library/postgres:18.4
sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a
```

The fixture builds one relation of every kind D001 admits, so a single real query can be checked
against the whole mapping table rather than a hand-picked subset: an ordinary table with a primary
key (analyzed), one without (never analyzed), an unlogged table, a three-level partition tree, a
view, a materialized view, a foreign table, and a second populated schema so include and exclude
filters have something to choose between. All DDL and synthetic rows belong exclusively to
IntegrationTests.

## 22. Foreign-table fixture

A real `postgres_fdw` extension, a loopback server pointed at the test database itself, a synthetic
user mapping, and a foreign table over a synthetic fixture relation. Setup fails loudly if the
extension is unavailable; nothing is skipped.

### 22.1 Same-session detector

The evidence that D001 opens no remote connection comes from `postgres_fdw_get_connections()`,
observed in **the same local session that executes D001**.

That function reports the remote connections cached by the current local backend and nothing else —
a second session always sees an empty set. Two consequences follow, and both are why the earlier
`pg_stat_activity` evidence was insufficient:

- Sampling from a *separate* admin connection can never observe the inspected backend's cache, so a
  connection opened and closed between two samples would be invisible.
- `COUNT(*)` over `pg_stat_activity` is a *global* measure, not a statement about the target server,
  and depends on the remote backend still being alive at sample time.

`postgres_fdw` keeps its per-server entry for the life of the backend rather than forgetting it, so
a connection that had been opened and released would still appear in the second observation. That
closes the transient-connection gap.

### 22.2 Proof sequence

All five steps share one backend:

| Stage | `postgres_fdw_get_connections()` rows for the target server |
|---|---|
| Before D001 | 0 |
| After D001 (which really read the relation zoo) | 0 |
| After the suite reads the foreign table itself | 1 — `valid`, with a `remote_backend_pid` |

The recorded columns are `server_name`, `user_name`, `valid`, `used_in_xact`, `closed` and
`remote_backend_pid`, and assertions filter on the **target server** rather than on a global count.

The third stage is the positive control: without it, the two zeros would be equally consistent with
a detector that can never see anything at all.

### 22.3 No dependence on timing or pooling

The proof connection is deliberately **unpooled**. `postgres_fdw` caches remote connections per
backend, and a pooled `NpgsqlConnection` can hand back a backend on which an earlier test already
opened one — which would make the "before" observation report a connection this session never made.
This was observed in practice while building the proof. An unpooled connection removes the
dependency entirely. Nothing in the proof uses a sleep, a timing window, connection lifetime, or
global activity sampling.

The full production inspection path keeps a separate, explicitly secondary cross-session check: it
runs on a backend the test cannot observe from the inside, so it is supporting evidence, never the
primary proof.

The loopback server, its user mapping and the role's grants are all genuinely usable, so "D001
opened no remote connection" is a statement about D001 rather than about a broken setup. D001 itself
is unchanged: the foreign table is still reported as `ForeignTable` with all three sizes exactly 0,
no estimate and no primary key.

## 23. Permission fixture

A dedicated, disposable container revokes `EXECUTE` on exactly one required size function —
`pg_total_relation_size(regclass)` — from `PUBLIC` **and** directly from the inspection role.
Revoking only the direct grant would prove nothing, because `PUBLIC` holds `EXECUTE` by default.

Verified during initialization, before any test runs:

```text
selected function EXECUTE   false
other two functions         true
catalog-table allowlist     true
rolsuper                    false
role memberships            none
```

The suite then proves C002 is false, the probe raises the fixed required-capability failure, D001
is never executed in the composed callback, and no function name reaches any exposed surface. D001
is deliberately **not** run directly to provoke a similar error: the point is that the composition
never gets that far. The fixture has its own collection, its own container, its own deadlines and
immediate cleanup on failed initialization.

## 24. Validator

Both layers are retained. Layer 1 gained exactly four punctuation characters — `<`, `>`, `[`, `]` —
all required by D001's `<>` comparisons, its `reltuples < 0` test and its `text[]` casts, and by
nothing else. No prohibited token, statement form or placeholder rule was relaxed, and layer 2 still
pins the exact text of every authorized statement, so widening the character set cannot widen what
may execute.

The exhaustive matrix is now `8 × 7 × 8 = 448` combinations, of which exactly **eight** are
accepted and 440 rejected. The expectation is an independent table in the test, not a projection of
the productive one.

D001 mutation coverage rejects: a changed catalog table, join target, join predicate or join type; a
changed constraint table; each removed system filter; an added index or sequence relkind; a removed
relkind; changed sized-relkinds; swapped, missing, extra or retyped parameters; a changed `text[]`
cast; a concatenated schema literal; a changed primary-key predicate or correlation; a changed or
arithmetically adjusted size function; a changed estimate guard; a changed or removed `ORDER BY`; a
`COUNT(*)`; a business table; a second statement; a comment; a semicolon; `FOR UPDATE`;
`SELECT INTO`; and a GC-DHI-04E index statement. `ValidateText` still authorizes nothing.

## 25. CI

`.github/workflows/ci.yml` is unchanged. New suites carry
`[Trait("Category", "PostgreSqlServer")]`, so Ubuntu runs them and Windows excludes them. Zero
skipped tests.

```text
Unit-test list entries:              1441
Unit-test runtime executions:        1446
Non-server IntegrationTests:           13
PostgreSQLServer IntegrationTests:     75
Local total:                         1534
Expected Ubuntu total:               1534
Expected Windows total:              1459
```

## 26. Limitaciones

- C002 is a *privilege* check, not a guarantee a later call succeeds. A privilege can still be
  withdrawn between the probe and D001; that race is out of scope here.
- The `TemporaryTable` branch cannot be reached by a normal query, so its only coverage is unit
  coverage.
- Sizes are three independent server reads. No arithmetic identity among them is required or
  asserted, because the total legitimately includes components the other two do not.
- D001 pins SQL by exact text, so any future reformatting of the statement is a deliberate contract
  change, not a cosmetic one.
- The filter does not police system schemas; D001's frozen `WHERE` clause does. A caller may name
  `pg_catalog` in an include list, and it simply matches nothing.
- Partition parentage, bounds and the partition tree itself are not modelled. Only per-relation
  root/partition state is reported.

## 27. Trabajo diferido a 04E/04F

`IndexSnapshot` and index SQL; column snapshots; detailed constraints; partition bounds and parent
identity; `SchemaSnapshot` and `DatabaseSnapshot` composition; `IDatabaseSnapshotProvider`; the
mandatory productive C002-before-D001 sequencing; DBH001–DBH005; CLI, JSON, console and exit codes;
the PostgreSQL 15 matrix; and the final minimum-role deployment recipe.

## 28. Declaración

```text
No snapshot provider was added.
No diagnostic rule was implemented.
No CLI behavior, JSON reporting, console output or exit code was added.
No index query exists.
No business row is read by the product.
The productive SQL inventory contains exactly B001, B002, B003, C001, C002, C003, C004 and D001.
GC-DHI-04E through GC-DHI-04F were not started.
```
