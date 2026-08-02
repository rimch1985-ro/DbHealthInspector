# GC-DHI-04D — Table Snapshot Query and Mapping

**Definition date:** 2026-08-01  
**Status:** Defined  
**Backlog:** PG-04  
**Predecessor:** GC-DHI-04C approved and closed  
**Implementation:** not authorized  
**Verdict:** DEFINED — AWAITING HUMAN IMPLEMENTATION AUTHORIZATION

## 1. Purpose and authorization boundary

This document freezes the technical contract for the future PostgreSQL table
snapshot query and its explicit mapping to the existing Core `TableSnapshot`.
It is a governance definition only. It adds no product code, executable SQL
resource, test, dependency, workflow, running database or Docker environment.

Implementation may begin only after this definition is integrated, its
documentation CI is green, the human owner reviews it, explicitly authorizes
implementation and issues a separate Claude Code prompt. GC-DHI-04E and
GC-DHI-04F remain blocked.

## 2. Frozen objective

The future implementation shall:

1. execute only inside the verified GC-DHI-04B session;
2. extend the typed boundary established by GC-DHI-04C;
3. execute one multirecord D001 query;
4. query only PostgreSQL catalogs and catalog functions;
5. read no business rows and never use `COUNT(*)`;
6. bind schema filters as `text[]` parameters;
7. always exclude system schemas;
8. map explicitly to existing `TableSnapshot` values;
9. produce deterministic ordinal order;
10. reject unexpected shapes and values;
11. preserve rollback, cancellation and cleanup; and
12. leave Core, CLI, indexes, the provider, diagnostics and reporting unchanged.

## 3. Existing Core and PostgreSQL baseline

`TableSnapshot` has exactly ten required values: schema name, table name,
relation kind, partition-root state, partition state, estimated row count,
table size, index size, total size and primary-key state.
`EstimatedRowCount` is the only nullable numeric value. Sizes cannot be
negative, and one relation cannot be both a partitioned root and a partition.

The current productive SQL inventory is exactly B001–B003 and C001–C004:
seven IDs, six command kinds, seven definitions and seven frozen contracts.
`Int32` is the only current productive parameter type. The operation executor
does not accept SQL or dispatch by statement ID. No table query, mapper,
table-specific command kind or table-snapshot operation exists.

## 4. Internal result contract

The future result is equivalent to:

```text
internal sealed class PostgreSqlTableSnapshotQueryResult

Properties:
- ReadOnlyCollection<TableSnapshot> Tables
```

The constructor makes a defensive copy, sorts it canonically and exposes a
non-modifiable `ReadOnlyCollection<TableSnapshot>`. An empty collection is
valid. The result contains no Npgsql types, OIDs, SQL, connection, transaction,
command, reader or stored exception. Its `ToString()` must not expose schema
names. No new Core type is added, and `SchemaSnapshot` is not composed here;
GC-DHI-04F may derive schemas from integrated results.

## 5. Schema-filter contract

The future internal contract is equivalent to:

```text
internal sealed class PostgreSqlSchemaFilter

Properties:
- ReadOnlyCollection<string> IncludedSchemas
- ReadOnlyCollection<string> ExcludedSchemas
```

Semantics:

| Input | Meaning |
|---|---|
| Empty include list | Include every eligible non-system schema |
| Non-empty include list | Include only eligible schemas with exact names |
| Empty exclude list | No additional user-schema exclusion |
| Non-empty exclude list | Exclude exact matching schema names |
| Same name in both lists | Invalid filter |

Names use ordinal, case-sensitive comparison. Patterns, wildcards, regular
expressions and dynamic identifiers are forbidden. Null collections and null,
empty, whitespace-only or NUL-containing names are invalid. Duplicate names
within either list are rejected. The constructor defensively copies both lists
and sorts each copy with `StringComparer.Ordinal`; caller mutation cannot alter
the filter. A system schema cannot be re-enabled by the include list.

Both parameters are always present and bound as non-null arrays:

```text
$1 — included schemas
$2 — excluded schemas
```

The future inventory adds exactly one parameter type, `TextArray`, bound as:

```text
NpgsqlDbType.Array | NpgsqlDbType.Text
```

No `object`, `dynamic`, generic conversion or arbitrary `NpgsqlDbType` is
authorized.

## 6. Mandatory system-schema exclusions

D001 always excludes, before applying user filters:

```text
pg_catalog
information_schema
pg_toast*
pg_temp_*
```

`pg_toast_temp_*` is covered by `pg_toast*`. Temporary relations housed in
`pg_temp_*` are absent from a normal GC-DHI-04D snapshot. Core retains
`RelationKind.TemporaryTable`, but the normal query policy cannot produce it.

## 7. Future SQL inventory

After GC-DHI-04D implementation, and not before, the exact inventory shall be:

```text
B001 — SetTransactionReadOnly
B002 — ApplyLocalTimeouts
B003 — VerifySessionState
C001 — ReadServerIdentity
C002 — CheckCatalogMetadataAccess
C003 — CheckUsageStatisticsAccess
C004 — ReadStatisticsReset
D001 — ReadTableSnapshots
```

The new statement ID is `ReadTableSnapshots`; the new command kind is
`SelectTableMetadata`. Totals shall be eight statement IDs, seven command
kinds, eight inventory definitions and eight frozen contracts, in exact order
B001, B002, B003, C001, C002, C003, C004, D001. No D002 or index statement is
authorized.

## 8. Required C002 expansion

C002 retains every current check and adds exactly:

```sql
AND pg_catalog.has_function_privilege(
    current_user,
    'pg_catalog.pg_table_size(regclass)',
    'EXECUTE')
AND pg_catalog.has_function_privilege(
    current_user,
    'pg_catalog.pg_indexes_size(regclass)',
    'EXECUTE')
AND pg_catalog.has_function_privilege(
    current_user,
    'pg_catalog.pg_total_relation_size(regclass)',
    'EXECUTE')
```

C002 keeps its statement ID and command kind and still returns exactly one
non-null Boolean. Its exact SQL and frozen contract change explicitly. C001,
C003 and C004 remain byte-identical. Missing any required permission makes C002
false. The result never identifies the failed function, adds a sensitive
reason or grants a permission. Future integration must prove the real positive
path and a dedicated real-container path with `EXECUTE` revoked.

## 9. Exact D001 SQL

The following text is frozen exactly for the future D001 resource:

```sql
SELECT
    namespace.nspname::text
        AS schema_name,
    relation.relname::text
        AS table_name,
    relation.relkind::text
        AS relation_kind,
    relation.relpersistence::text
        AS relation_persistence,
    relation.relispartition
        AS is_partition,
    CASE
        WHEN relation.relkind = 'v'
            OR relation.reltuples < 0
            THEN NULL::bigint
        ELSE relation.reltuples::bigint
    END
        AS estimated_row_count,
    CASE
        WHEN relation.relkind IN ('r', 'm', 'p')
            THEN pg_catalog.pg_table_size(relation.oid)
        ELSE 0::bigint
    END
        AS table_size_bytes,
    CASE
        WHEN relation.relkind IN ('r', 'm', 'p')
            THEN pg_catalog.pg_indexes_size(relation.oid)
        ELSE 0::bigint
    END
        AS index_size_bytes,
    CASE
        WHEN relation.relkind IN ('r', 'm', 'p')
            THEN pg_catalog.pg_total_relation_size(relation.oid)
        ELSE 0::bigint
    END
        AS total_size_bytes,
    EXISTS (
        SELECT 1
        FROM pg_catalog.pg_constraint AS constraint_record
        WHERE constraint_record.conrelid = relation.oid
          AND constraint_record.contype = 'p'
    )
        AS has_primary_key
FROM pg_catalog.pg_class AS relation
INNER JOIN pg_catalog.pg_namespace AS namespace
    ON namespace.oid = relation.relnamespace
WHERE relation.relkind IN ('r', 'p', 'v', 'm', 'f')
  AND namespace.nspname <> 'pg_catalog'
  AND namespace.nspname <> 'information_schema'
  AND namespace.nspname NOT LIKE 'pg_toast%'
  AND namespace.nspname NOT LIKE 'pg_temp_%'
  AND (
      pg_catalog.cardinality($1::text[]) = 0
      OR namespace.nspname::text = ANY($1::text[])
  )
  AND NOT (
      namespace.nspname::text = ANY($2::text[])
  )
ORDER BY
    namespace.nspname,
    relation.relname
```

### PostgreSQL 15 and 18 normative verification

Official PostgreSQL 15 and 18 documentation confirms all syntax and catalog
contracts used above:

- `cardinality(anyarray)` returns the total element count and returns zero for
  an empty array ([15](https://www.postgresql.org/docs/15/functions-array.html),
  [18](https://www.postgresql.org/docs/18/functions-array.html));
- `expression = ANY(array)` returns false for an empty array
  ([15](https://www.postgresql.org/docs/15/functions-comparisons.html),
  [18](https://www.postgresql.org/docs/18/functions-comparisons.html));
- the three relation-size functions accept `regclass` and return `bigint`
  ([15](https://www.postgresql.org/docs/15/functions-admin.html),
  [18](https://www.postgresql.org/docs/18/functions-admin.html));
- `has_function_privilege` accepts a textual function signature and the
  `EXECUTE` privilege
  ([15](https://www.postgresql.org/docs/15/functions-info.html),
  [18](https://www.postgresql.org/docs/18/functions-info.html)); and
- `pg_class` documents `relkind`, `relpersistence`, `reltuples` and
  `relispartition`, while `pg_constraint` documents `contype = 'p'` and
  `conrelid`
  ([15 `pg_class`](https://www.postgresql.org/docs/15/catalog-pg-class.html),
  [18 `pg_class`](https://www.postgresql.org/docs/18/catalog-pg-class.html),
  [15 `pg_constraint`](https://www.postgresql.org/docs/15/catalog-pg-constraint.html),
  [18 `pg_constraint`](https://www.postgresql.org/docs/18/catalog-pg-constraint.html)).

Static syntactic analysis finds no PostgreSQL 15/18 incompatibility: casts,
qualified built-in calls, searched `CASE`, catalog join, correlated `EXISTS`,
array parameters, `ANY`, `LIKE` and `ORDER BY` are valid in both versions. No
Docker or server execution was used for this definition-time verification.

## 10. D001 parameters and frozen contract

| Position | Type | Meaning |
|---:|---|---|
| 1 | `TextArray` | Included schemas |
| 2 | `TextArray` | Excluded schemas |

Exactly two parameters are always present as non-null arrays. Empty arrays mean
no corresponding user filter. Concatenation, identifiers, extra parameters,
schema literals in SQL and values in logs or exceptions are forbidden. The
frozen contract validates statement ID, command kind, exact ordinal SQL,
parameter count, positions and types.

## 11. D001 multirecord shape

Each row has exactly ten columns:

| Ordinal | Value | Type | Nullable |
|---:|---|---|---|
| 0 | Schema name | String | No |
| 1 | Relation name | String | No |
| 2 | `relkind` | One-character String | No |
| 3 | `relpersistence` | One-character String | No |
| 4 | Is partition | Boolean | No |
| 5 | Estimated rows | Int64 | Yes |
| 6 | Table size | Int64 | No |
| 7 | Index size | Int64 | No |
| 8 | Total size | Int64 | No |
| 9 | Primary-key state | Boolean | No |

Zero or more rows are valid. The row seam gains only `GetInt64(int ordinal)`.
It does not gain `GetValue`, `object`, `dynamic`, an exposed
`GetFieldValue<T>`, a generic mapper, column-name lookup or an exposed
`NpgsqlDataReader`.

## 12. Relation and partition mapping

Partition state has mandatory precedence:

| `relkind` | Persistence | `relispartition` | Core kind | Root | Partition |
|---|---|---:|---|---:|---:|
| Any allowed | Any valid | true | `Partition` | false | true |
| `p` | Valid | false | `PartitionedTable` | true | false |
| `r` | `p` or `u` | false | `OrdinaryTable` | false | false |
| `r` | `t` | false | `TemporaryTable` | false | false |
| `v` | Valid | false | `View` | false | false |
| `m` | Valid | false | `MaterializedView` | false | false |
| `f` | Valid | false | `ForeignTable` | false | false |

The first row includes leaf partitions and subpartitioned partitions, so a
partition with children is never treated as an independent root. The temporary
ordinary-table branch has unit coverage even though normal D001 exclusions
remove `pg_temp_*`. Unknown `relkind` or persistence fails with a fixed,
sanitized internal error; it never silently becomes `RelationKind.Unknown`.

## 13. Estimated-row policy

Views and catalog values with `reltuples < 0` map to null; otherwise the mapper
uses the returned `bigint` unchanged. Zero is valid and null means unknown, not
zero. A negative final shape value is invalid. The adapter does not query
business rows, run `COUNT(*)`, fabricate or re-estimate counts, or round again
in C#.

## 14. Size policy

Ordinary tables, partitions, partitioned roots and materialized views use
`pg_table_size`, `pg_indexes_size` and `pg_total_relation_size`. Views and
foreign tables return zero. All three values are non-null and non-negative.
The mapper does not use `pg_size_pretty`, return formatted strings, calculate
the total in C# or require an exact arithmetic identity among independently
read sizes. Null or negative values are mapping failures.

## 15. Primary-key policy

`HasPrimaryKey` derives exclusively from a `pg_constraint` row whose
`contype = 'p'` and `conrelid` equals the relation OID. It is not inferred from
a unique index, `relhasindex` or columns. Views and objects without such a
constraint map false. Partitions reflect only what PostgreSQL exposes for their
own OID. DBH001 is not implemented here.

## 16. Deterministic ordering and duplicates

D001 orders by schema and relation name, but the mapper sorts again in memory
by `SchemaName` and then `TableName`, both with `StringComparer.Ordinal`. It
does not depend on database collation, process culture or case-insensitive
comparison. A duplicate `(SchemaName, TableName)` pair fails mapping. Empty
results are valid and OIDs do not enter the result.

## 17. Typed operation boundary

The future implementation adds one method equivalent to the following in both
`PostgreSqlInspectionOperationExecutor` and `PostgreSqlSqlExecutor`:

```text
ReadTableSnapshotsAsync(
    PostgreSqlSchemaFilter filter,
    CancellationToken cancellationToken)
```

It resolves only D001. It accepts no statement ID, SQL or generic dictionary,
cannot execute B001–C004, exposes no gateway or resource and creates no generic
multirecord executor or index behavior. The operation callback then contains
exactly five typed operations: C001–C004 and D001.

## 18. Two-layer validator

The future validator recognizes eight IDs, seven kinds and eight exact canonical
SQL texts. Its exhaustive matrix has `8 × 7 × 8 = 448` combinations: exactly
eight canonical combinations accepted and 440 rejected.

D001 mutation tests must reject changes to the catalog table, join, system
filters, relation-kind allowlist, index relkind, parameter presence/order/count,
`text[]` type, concatenated schema, business table, second statement, comment,
semicolon, `FOR UPDATE`, `COUNT(*)`, `ORDER BY`, primary-key predicate or size
function, and any GC-DHI-04E index SQL. Rejection may occur in the lexical layer
or frozen-contract layer.

## 19. Errors and leakage policy

The only new internal failures are sealed equivalents of:

```text
PostgreSqlTableSnapshotMappingException
PostgreSqlSchemaFilterException
```

Their fixed messages are:

```text
The PostgreSQL table metadata row is invalid.
The PostgreSQL schema filter is invalid.
```

They expose no public message/inner constructors, database/schema/table name,
received `relkind`, SQL, SQLSTATE or server message. `Data` is empty.
Mapper-created failures have null `InnerException`. Expected Npgsql errors
continue through the existing sanitized boundary; unexpected exceptions follow
the existing propagation contract.

## 20. Cancellation and cleanup

A precancelled token prevents D001. The exact token reaches the gateway;
cancellation during reading stops the loop and no partial collection is
returned. Reader and command are disposed. The primary failure dominates a
cleanup failure using the existing exception-dispatch policy; a cleanup-only
failure propagates according to that policy. Rollback continues with
`CancellationToken.None`, and the pool remains reusable. Retry, logging,
autocommit and commit are not added.

Required stages are: before D001, during command execution, before the first
row, between rows, during the final row, during reader disposal, cleanup failure
with a primary failure and cleanup failure without a primary failure.

## 21. Unit-test strategy

Future unit tests cover:

- filters: empty/empty, include only, exclude only, overlap, duplicates, null,
  empty, whitespace, NUL, case sensitivity, defensive copying and ordinal sort;
- relations: permanent, unlogged and temporary ordinary tables; partitioned
  root; leaf and subpartitioned partitions; view, materialized view, foreign
  table; unknown kind/persistence and invalid combinations;
- shape: zero, one and multiple rows; wrong field count; null in every required
  column; null/zero/negative estimates; negative sizes; duplicates; ordinal
  reorder; reader and command cleanup;
- inventory: exactly eight statements, seven kinds and two parameter types;
  exact D001 and expanded C002; unchanged B001–B003 and C001/C003/C004; no D002
  or GC-DHI-04E statement; and
- boundary: exactly five callback operations and no ID, SQL or infrastructure
  exposure.

## 22. PostgreSQLServer strategy

Future real tests reuse only:

```text
docker.io/library/postgres:18.4
sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a
```

No permanent 15/18 matrix is added. The normal fixture covers an ordinary table
with PK, one without PK, an unlogged table, never-analyzed null estimate,
analyzed non-negative estimate, partitioned root, leaf partition,
subpartitioned partition, view, materialized view, foreign table, non-negative
sizes, zero view/foreign sizes, include/exclude filters, absent system and
temporary schemas, ordinal order, empty match, rollback and pool recovery.

The reproducible foreign-table strategy uses `postgres_fdw` in the dedicated
test database, a loopback server and user mapping, and a foreign table over a
synthetic fixture relation. Setup DDL and synthetic rows belong exclusively to
IntegrationTests; productive inspection neither executes DDL nor reads them.
The fixture must fail setup rather than silently skip if `postgres_fdw` is not
available.

## 23. Required-function permission fixture

A separate container fixture revokes `EXECUTE` on one required size function
from PUBLIC and directly from a non-superuser inspection role. The role has no
inherited role that restores permission, while basic catalog access remains.
The fixture proves C002 is false, D001 is not offered as a safe operation and no
failed-function detail is exposed. It is isolated because it changes a built-in
function ACL. Both the normal positive path and this loss path are mandatory;
unit-only substitution or a skipped fixture blocks integration.

## 24. Future CI strategy

No workflow change is expected. Ubuntu runs UnitTests, non-server
IntegrationTests, PostgreSQLServer, pack and artifact upload. Windows runs
UnitTests, non-server IntegrationTests and CLI smoke. Implementation determines
new counts. PostgreSQL 15, remote servers, secrets, new workflows, package
versions and publication are not added.

## 25. Implementation entry criteria

Implementation may start only when GC-DHI-04C remains closed; this definition
is integrated with green documentation CI; D001, C002, schema filter, partition
precedence, sizes and estimates are frozen; the foreign-table and permission
fixtures remain viable; the human explicitly authorizes it; and a separate
Claude Code prompt exists.

## 26. GC-DHI-04D exit criteria

Exit requires all of the following:

1. PG-04 implemented with exact D001 and exact C002 expansion;
2. eight statements, seven command kinds, two parameter types and eight frozen
   contracts;
3. 448 validator combinations with exactly eight accepted;
4. safe include/exclude filters and permanent system-schema exclusion;
5. no business rows or `COUNT(*)`;
6. correct relation kind, partition precedence and fail-closed unknown values;
7. nullable estimates and three valid sizes;
8. primary-key state derived through `pg_constraint`;
9. ordinal ordering and duplicate rejection;
10. typed boundary, complete cancellation and exception-dispatch-safe cleanup;
11. green PostgreSQL 18 and permission-loss fixtures;
12. no GC-DHI-04A–04C regression, warning, error, failure or skipped test;
13. unchanged Core and CLI and no GC-DHI-04E work; and
14. no tag, release or publication.

## 27. Explicit exclusions

GC-DHI-04D excludes `IndexSnapshot`, index SQL, column snapshots, detailed
constraints, partition bounds or parent identity, `SchemaSnapshot` composition,
`DatabaseSnapshot`, `IDatabaseSnapshotProvider`, DBH001–DBH005, CLI and
connection resolution, console, JSON, exit codes, data profiling, business-row
access, `COUNT(*)`, query plans, `pg_stat_statements`, the PostgreSQL 15 matrix,
the final deployment-role recipe, tags, releases and NuGet publication.

## 28. Definition validation and next action

This definition was prepared only after inspection of the canonical governance,
ADR, backlog, design, Core and PostgreSQL SQL/session/capability contracts. Its
PostgreSQL 15/18 SQL compatibility was checked from official documentation and
static syntax analysis without starting Docker.

```text
GC-DHI-04D DEFINED — AWAITING HUMAN IMPLEMENTATION AUTHORIZATION
Await human review of the integrated GC-DHI-04D definition.
No GC-DHI-04D implementation is authorized.
```
