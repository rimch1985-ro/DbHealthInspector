# GC-DHI-04E — Index Snapshot Query and Mapping

**Gate:** GC-DHI-04E — Index Snapshot Query and Mapping  
**Backlog:** PG-05 — Implement index snapshot query  
**Definition date:** 2026-08-10  
**D1 correction date:** 2026-08-10  
**Status:** Defined  
**Predecessor:** GC-DHI-04D approved and closed  
**Implementation:** not authorized  
**Verdict:** GC-DHI-04E DEFINITION CORRECTED — AWAITING HUMAN IMPLEMENTATION AUTHORIZATION  

---

## 1. Objective and authorization boundary

GC-DHI-04E defines and freezes the future PostgreSQL index-snapshot query and
mapping contract. It resolves the catalog shape, optional-statistics boundary,
Core mapping, safety rules and verification strategy required by PG-05.

This document is governance only. It adds no product code, executable SQL
resource, test, dependency, workflow, snapshot provider, diagnostic rule, CLI
behavior or reporting behavior. PG-05 is defined but not implemented. Human
review, explicit human implementation authorization and a separate Claude Code
prompt remain mandatory.

Human review identified D1-01: the original frozen E001 preserved the
operator-class namespace and name but discarded per-key operator-class
options. This correction resolves D1-01 without changing Core or implementing
PG-05. The corrected contract preserves the exact ordered option array inside
the existing `IndexKeyPartSnapshot.OperatorClass` string identity.

## 2. Preserved Core contract

The future adapter maps only to the existing `IndexSnapshot` and
`IndexKeyPartSnapshot` types. Core remains unchanged and receives no Npgsql
type, PostgreSQL OID, SQL, command, reader, connection or exception.

`IndexSnapshot` retains:

```text
SchemaName
TableName
IndexName
AccessMethod
KeyParts
IncludedColumns
PartialPredicate
IsUnique
NullsNotDistinct
IsPrimaryKey
BacksConstraint
IsValid
IsReady
IsLive
SizeBytes
ScanCount
```

`IndexKeyPartSnapshot` retains:

```text
Position
ColumnName XOR Expression
Collation
OperatorClass
SortDirection
NullsOrdering
```

Defensive copies, read-only collections, order-sensitive equality, unique key
positions, unique INCLUDE names, primary-key implications, non-negative size
and nullable non-negative scan counts remain mandatory.

## 3. PostgreSQL normative basis

Only official PostgreSQL 15 and 18 documentation is normative:

| Contract | PostgreSQL 15 | PostgreSQL 18 |
|---|---|---|
| `pg_index` | [15](https://www.postgresql.org/docs/15/catalog-pg-index.html) | [18](https://www.postgresql.org/docs/18/catalog-pg-index.html) |
| `pg_class` | [15](https://www.postgresql.org/docs/15/catalog-pg-class.html) | [18](https://www.postgresql.org/docs/18/catalog-pg-class.html) |
| `pg_attribute` | [15](https://www.postgresql.org/docs/15/catalog-pg-attribute.html) | [18](https://www.postgresql.org/docs/18/catalog-pg-attribute.html) |
| `pg_am` | [15](https://www.postgresql.org/docs/15/catalog-pg-am.html) | [18](https://www.postgresql.org/docs/18/catalog-pg-am.html) |
| `pg_constraint` | [15](https://www.postgresql.org/docs/15/catalog-pg-constraint.html) | [18](https://www.postgresql.org/docs/18/catalog-pg-constraint.html) |
| `pg_collation` | [15](https://www.postgresql.org/docs/15/catalog-pg-collation.html) | [18](https://www.postgresql.org/docs/18/catalog-pg-collation.html) |
| `pg_opclass` | [15](https://www.postgresql.org/docs/15/catalog-pg-opclass.html) | [18](https://www.postgresql.org/docs/18/catalog-pg-opclass.html) |
| `pg_stat_all_indexes` | [15](https://www.postgresql.org/docs/15/monitoring-stats.html#MONITORING-PG-STAT-ALL-INDEXES-VIEW) | [18](https://www.postgresql.org/docs/18/monitoring-stats.html#MONITORING-PG-STAT-ALL-INDEXES-VIEW) |
| catalog/property functions | [15](https://www.postgresql.org/docs/15/functions-info.html) | [18](https://www.postgresql.org/docs/18/functions-info.html) |
| `CREATE INDEX` operator-class parameters | [15](https://www.postgresql.org/docs/15/sql-createindex.html) | [18](https://www.postgresql.org/docs/18/sql-createindex.html) |
| array equality and ordering | [15](https://www.postgresql.org/docs/15/functions-array.html) | [18](https://www.postgresql.org/docs/18/functions-array.html) |
| relation size | [15](https://www.postgresql.org/docs/15/functions-admin.html) | [18](https://www.postgresql.org/docs/18/functions-admin.html) |
| deterministic invalid partitioned index | [15](https://www.postgresql.org/docs/15/ddl-partitioning.html) | [18](https://www.postgresql.org/docs/18/ddl-partitioning.html) |
| `CompareOpclassOptions` source | [REL_15_STABLE](https://github.com/postgres/postgres/blob/REL_15_STABLE/src/backend/commands/indexcmds.c#L349-L384) | [REL_18_STABLE](https://github.com/postgres/postgres/blob/REL_18_STABLE/src/backend/commands/indexcmds.c#L361-L396) |

The reviewed functions are exactly `pg_get_indexdef`, `pg_get_expr`,
`pg_index_column_has_property` and `pg_relation_size`. PostgreSQL documents
non-pretty deparsing as the more stable form and documents `pg_class.relkind`
`i` and `I`, key-before-INCLUDE ordering, expression zeroes in `indkey`, the
per-key collation and operator-class vectors, and independent validity flags.

`CREATE INDEX` permits `opclass (opclass_parameter = value, ...)` per key.
`pg_attribute` documents `attoptions` as a nullable `text[]` of
`keyword=value` strings and includes rows for indexes. `pg_options_to_table`
can split that array into name/value rows, but E001 deliberately does not use
it: direct typed-array retrieval preserves nullability and storage order
without adding a function privilege, a second result shape or client parsing
of PostgreSQL array syntax.

The official `CompareOpclassOptions` source obtains each index attribute's
options and compares non-null `text[]` values through `array_eq` under C
collation. PostgreSQL array comparison is element-by-element in storage order.
Consequently, null versus non-null, element bytes and element order are all
structural; alphabetical sorting is forbidden.

## 4. Definition-time PostgreSQL 18.4 probes

A disposable container used only for design verification ran:

```text
postgres:18.4
sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a
```

Observed results:

- `pg_get_indexdef(index_oid, key_position, false)` returned `lower(b)` for an
  expression key carrying a separate operator class, `DESC`, `NULLS FIRST`,
  INCLUDE and predicate; it did not repeat those decorations.
- B-tree key properties returned one exact direction and one exact null order.
- Hash, GIN, GiST, SP-GiST and BRIN key properties returned `orderable=false`
  and all four direction/null flags false.
- INCLUDE positions returned null for properties that do not apply.
- physical indexes and physical index partitions were `relkind=i` and had
  direct non-negative `pg_relation_size` values and `pg_stat_all_indexes` rows.
- partitioned roots and partitioned index partitions were `relkind=I`, had
  direct size zero and had no `pg_stat_all_indexes` row.
- `CREATE INDEX ... ON ONLY` a partitioned table produced a deterministic
  `relkind=I`, `indisvalid=false`, `indisready=true`, `indislive=true` row.
- the exact future C002 privilege expression evaluated to true.
- the exact E001 and E002 statements prepared and executed successfully.

A second disposable D1 probe used built-in BRIN operator classes and proved:

- two `int4_minmax_multi_ops` indexes differing only by
  `values_per_range=32` versus `values_per_range=64` had different
  `attoptions` arrays;
- two `int4_bloom_ops` indexes with identical option name/value pairs in
  opposite orders retained those opposite array orders and compared unequal;
- each produced array was one-dimensional with lower bound one;
- `pg_options_to_table` emitted the elements in stored order; and
- `pg_get_indexdef(index_oid, 1, false)` returned only the key `id`, while the
  full definition contained the options. The per-key overload therefore
  cannot replace direct `attoptions` retrieval;
- applying the frozen encoding yielded distinct values for
  `values_per_range=32` and `values_per_range=64`, and for the two opposite
  option orders; and
- the corrected future C002, E001 and byte-identical E002 statements prepared
  and executed successfully, with E001 returning the typed options array.

Both disposable container runs and all probe objects were removed. No fixture
or generated file remains.

## 5. E001/E002 architecture

The future architecture is frozen as two independent statements:

```text
E001 — ReadIndexMetadata          — required structural metadata
E002 — ReadIndexUsageStatistics   — optional usage statistics
```

E001 always runs after the caller has established `CatalogMetadata =
Available`. It contains no reference to `pg_stat_all_indexes` and produces all
`IndexSnapshot` fields except `ScanCount`.

E002 runs only when `UsageStatistics = Available`. If usage statistics are
unavailable, E002 executes zero times and every structural snapshot receives
`ScanCount = null`. Zero is never substituted for missing information.

## 6. Frozen future inventory and validator

After PG-05 implementation the inventory shall be exactly:

```text
B001 — SetTransactionReadOnly
B002 — ApplyLocalTimeouts
B003 — VerifySessionState
C001 — ReadServerIdentity
C002 — CheckCatalogMetadataAccess
C003 — CheckUsageStatisticsAccess
C004 — ReadStatisticsReset
D001 — ReadTableSnapshots
E001 — ReadIndexMetadata
E002 — ReadIndexUsageStatistics
```

E001 adds `SelectIndexMetadata`. E002 reuses `SelectStatistics`. Parameter
types remain exactly `Int32` and `TextArray`.

Frozen totals:

```text
Statement IDs:        10
Command kinds:         8
Parameter types:       2
Inventory definitions: 10
Frozen contracts:      10
```

The exhaustive independent validator matrix is `10 × 8 × 10 = 800`:
exactly ten canonical combinations accepted and 790 rejected. Expected
combinations remain independently transcribed in tests rather than projected
from the productive inventory.

## 7. Schema filter

E001 and E002 reuse the existing immutable `PostgreSqlSchemaFilter` without a
second filter type:

```text
$1 — included schemas — TextArray
$2 — excluded schemas — TextArray
```

Both parameters are always present and non-null. Empty arrays retain the
GC-DHI-04D semantics. Names are exact, ordinal and case-sensitive and are never
concatenated as identifiers. Both statements always exclude:

```text
pg_catalog
information_schema
pg_toast*
pg_temp_*
```

## 8. Exact future C002

C002 keeps the same statement ID, `SelectCapabilityCheck` kind, zero
parameters and one-row/one-non-null-Boolean result. It is the current closed
C002 plus only the four functions called by E001. D1 reads
`pg_attribute.attoptions` directly and therefore adds no function call or
privilege check. Existing GC-DHI-04D checks remain unchanged.

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
AND pg_catalog.has_function_privilege(
    current_user,
    'pg_catalog.pg_relation_size(regclass)',
    'EXECUTE')
AND pg_catalog.has_function_privilege(
    current_user,
    'pg_catalog.pg_get_indexdef(oid,integer,boolean)',
    'EXECUTE')
AND pg_catalog.has_function_privilege(
    current_user,
    'pg_catalog.pg_get_expr(pg_node_tree,oid,boolean)',
    'EXECUTE')
AND pg_catalog.has_function_privilege(
    current_user,
    'pg_catalog.pg_index_column_has_property(regclass,integer,text)',
    'EXECUTE')
        AS catalog_metadata_available
```

C001, C003, C004, D001 and B001–B003 remain byte-identical. C003 remains the
sole optional access check for `pg_stat_all_indexes`.

## 9. Exact E001 SQL

The following text is frozen exactly for future E001:

```sql
SELECT
    table_namespace.nspname::text
        AS schema_name,
    table_relation.relname::text
        AS table_name,
    index_relation.relname::text
        AS index_name,
    access_method.amname::text
        AS access_method,
    index_relation.relkind::text
        AS index_relation_kind,
    index_relation.relispartition
        AS is_index_partition,
    index_record.indnatts::integer
        AS attribute_count,
    index_record.indnkeyatts::integer
        AS key_attribute_count,
    index_attribute.attnum::integer
        AS attribute_position,
    (index_attribute.attnum <= index_record.indnkeyatts)
        AS is_key,
    CASE
        WHEN index_record.indkey[index_attribute.attnum - 1] <> 0
            THEN table_attribute.attname::text
        ELSE NULL::text
    END
        AS column_name,
    CASE
        WHEN index_attribute.attnum <= index_record.indnkeyatts
             AND index_record.indkey[index_attribute.attnum - 1] = 0
            THEN pg_catalog.pg_get_indexdef(
                index_relation.oid,
                index_attribute.attnum,
                false)
        ELSE NULL::text
    END
        AS expression,
    collation_namespace.nspname::text
        AS collation_schema,
    collation_record.collname::text
        AS collation_name,
    operator_class_namespace.nspname::text
        AS operator_class_schema,
    operator_class.opcname::text
        AS operator_class_name,
    CASE
        WHEN index_attribute.attnum <= index_record.indnkeyatts
            THEN index_attribute.attoptions
        ELSE NULL::text[]
    END
        AS operator_class_options,
    CASE
        WHEN index_attribute.attnum <= index_record.indnkeyatts
            THEN pg_catalog.pg_index_column_has_property(
                index_relation.oid,
                index_attribute.attnum,
                'orderable')
        ELSE NULL::boolean
    END
        AS is_orderable,
    CASE
        WHEN index_attribute.attnum <= index_record.indnkeyatts
            THEN pg_catalog.pg_index_column_has_property(
                index_relation.oid,
                index_attribute.attnum,
                'asc')
        ELSE NULL::boolean
    END
        AS is_ascending,
    CASE
        WHEN index_attribute.attnum <= index_record.indnkeyatts
            THEN pg_catalog.pg_index_column_has_property(
                index_relation.oid,
                index_attribute.attnum,
                'desc')
        ELSE NULL::boolean
    END
        AS is_descending,
    CASE
        WHEN index_attribute.attnum <= index_record.indnkeyatts
            THEN pg_catalog.pg_index_column_has_property(
                index_relation.oid,
                index_attribute.attnum,
                'nulls_first')
        ELSE NULL::boolean
    END
        AS nulls_first,
    CASE
        WHEN index_attribute.attnum <= index_record.indnkeyatts
            THEN pg_catalog.pg_index_column_has_property(
                index_relation.oid,
                index_attribute.attnum,
                'nulls_last')
        ELSE NULL::boolean
    END
        AS nulls_last,
    CASE
        WHEN index_record.indpred IS NULL
            THEN NULL::text
        ELSE pg_catalog.pg_get_expr(
            index_record.indpred,
            index_record.indrelid,
            false)
    END
        AS partial_predicate,
    index_record.indisunique
        AS is_unique,
    CASE
        WHEN index_record.indisunique
            THEN index_record.indnullsnotdistinct
        ELSE NULL::boolean
    END
        AS nulls_not_distinct,
    index_record.indisprimary
        AS is_primary_key,
    EXISTS (
        SELECT 1
        FROM pg_catalog.pg_constraint AS constraint_record
        WHERE constraint_record.conindid = index_relation.oid
          AND constraint_record.contype IN ('p', 'u', 'x')
    )
        AS backs_constraint,
    index_record.indisvalid
        AS is_valid,
    index_record.indisready
        AS is_ready,
    index_record.indislive
        AS is_live,
    CASE index_relation.relkind
        WHEN 'i' THEN pg_catalog.pg_relation_size(index_relation.oid)
        WHEN 'I' THEN 0::bigint
    END
        AS size_bytes
FROM pg_catalog.pg_index AS index_record
INNER JOIN pg_catalog.pg_class AS index_relation
    ON index_relation.oid = index_record.indexrelid
INNER JOIN pg_catalog.pg_class AS table_relation
    ON table_relation.oid = index_record.indrelid
INNER JOIN pg_catalog.pg_namespace AS table_namespace
    ON table_namespace.oid = table_relation.relnamespace
INNER JOIN pg_catalog.pg_am AS access_method
    ON access_method.oid = index_relation.relam
INNER JOIN pg_catalog.pg_attribute AS index_attribute
    ON index_attribute.attrelid = index_relation.oid
   AND index_attribute.attnum > 0
   AND index_attribute.attnum <= index_record.indnatts
   AND NOT index_attribute.attisdropped
LEFT JOIN pg_catalog.pg_attribute AS table_attribute
    ON table_attribute.attrelid = table_relation.oid
   AND table_attribute.attnum =
       index_record.indkey[index_attribute.attnum - 1]
   AND NOT table_attribute.attisdropped
LEFT JOIN pg_catalog.pg_collation AS collation_record
    ON index_attribute.attnum <= index_record.indnkeyatts
   AND collation_record.oid =
       index_record.indcollation[index_attribute.attnum - 1]
LEFT JOIN pg_catalog.pg_namespace AS collation_namespace
    ON collation_namespace.oid = collation_record.collnamespace
LEFT JOIN pg_catalog.pg_opclass AS operator_class
    ON index_attribute.attnum <= index_record.indnkeyatts
   AND operator_class.oid =
       index_record.indclass[index_attribute.attnum - 1]
LEFT JOIN pg_catalog.pg_namespace AS operator_class_namespace
    ON operator_class_namespace.oid = operator_class.opcnamespace
WHERE index_relation.relkind IN ('i', 'I')
  AND table_namespace.nspname <> 'pg_catalog'
  AND table_namespace.nspname <> 'information_schema'
  AND table_namespace.nspname NOT LIKE 'pg_toast%'
  AND table_namespace.nspname NOT LIKE 'pg_temp_%'
  AND (
      pg_catalog.cardinality($1::text[]) = 0
      OR table_namespace.nspname::text = ANY($1::text[])
  )
  AND NOT (
      table_namespace.nspname::text = ANY($2::text[])
  )
ORDER BY
    table_namespace.nspname,
    table_relation.relname,
    index_relation.relname,
    index_attribute.attnum
```

Deterministic E001 text identity uses UTF-8 without BOM, LF separators and no
terminal newline inside the fence:

```text
Exact length: 6262 bytes
SHA-256: d45b8ed1e0d842b1474839a3beadf6d1a0d4233cfa847c3887c41cfd4b1184d7
```

E001 has exactly two `TextArray` parameters in positions 1 and 2. Its new
output column reads the already joined `pg_attribute.attoptions` directly; no
new SQL function or parameter type is introduced. E001 contains no statistics
view, business-row read, aggregate, dynamic SQL, identifier interpolation or
descendant traversal.

## 10. Exact E002 SQL

The following text is frozen exactly for future E002:

```sql
SELECT
    statistics.schemaname::text
        AS schema_name,
    statistics.relname::text
        AS table_name,
    statistics.indexrelname::text
        AS index_name,
    statistics.idx_scan::bigint
        AS scan_count
FROM pg_catalog.pg_stat_all_indexes AS statistics
WHERE statistics.schemaname <> 'pg_catalog'
  AND statistics.schemaname <> 'information_schema'
  AND statistics.schemaname NOT LIKE 'pg_toast%'
  AND statistics.schemaname NOT LIKE 'pg_temp_%'
  AND (
      pg_catalog.cardinality($1::text[]) = 0
      OR statistics.schemaname::text = ANY($1::text[])
  )
  AND NOT (
      statistics.schemaname::text = ANY($2::text[])
  )
ORDER BY
    statistics.schemaname,
    statistics.relname,
    statistics.indexrelname
```

Deterministic E002 text identity uses the same UTF-8/LF/no-terminal-newline
measurement and remains byte-identical to G0:

```text
Exact length: 737 bytes
SHA-256: fe8f23a5dff2cdfb8d08acf4fb7f7a3f90aef4b7e9eee4b678cde8c260624919
```

E002 has the same two `TextArray` parameters. It returns only `idx_scan`; it
does not return `idx_tup_read`, `idx_tup_fetch` or `last_idx_scan`.

## 11. E001 exact multirecord shape

E001 returns one row per index attribute, including keys and INCLUDE
attributes. Each row has exactly 31 columns:

| Ordinal | Value | CLR type | Nullable |
|---:|---|---|---|
| 0 | Schema name | String | No |
| 1 | Table name | String | No |
| 2 | Index name | String | No |
| 3 | Access method | String | No |
| 4 | Index `relkind` | one-character String | No |
| 5 | Is index partition | Boolean | No |
| 6 | Attribute count | Int32 | No |
| 7 | Key attribute count | Int32 | No |
| 8 | Attribute position | Int32 | No |
| 9 | Is key | Boolean | No |
| 10 | Column name | String | Yes |
| 11 | Expression | String | Yes |
| 12 | Collation schema | String | Yes |
| 13 | Collation name | String | Yes |
| 14 | Operator-class schema | String | Yes |
| 15 | Operator-class name | String | Yes |
| 16 | Operator-class options | String[] | Yes |
| 17 | Orderable | Boolean | No for key; Yes for INCLUDE |
| 18 | Ascending | Boolean | No for key; Yes for INCLUDE |
| 19 | Descending | Boolean | No for key; Yes for INCLUDE |
| 20 | Nulls first | Boolean | No for key; Yes for INCLUDE |
| 21 | Nulls last | Boolean | No for key; Yes for INCLUDE |
| 22 | Partial predicate | String | Yes |
| 23 | Unique | Boolean | No |
| 24 | Nulls not distinct | Boolean | Yes for non-unique |
| 25 | Primary key | Boolean | No |
| 26 | Backs constraint | Boolean | No |
| 27 | Valid | Boolean | No |
| 28 | Ready | Boolean | No |
| 29 | Live | Boolean | No |
| 30 | Size bytes | Int64 | No |

For ordinals 17–21, every key row must contain a non-null Boolean and every
INCLUDE row must contain SQL null. Ordinal 16 is SQL null for INCLUDE and for a
key without explicit operator-class options; when non-null on a key it is a
typed ordered array. `NullsNotDistinct` must be non-null exactly when
`IsUnique=true`.

Zero indexes is valid. A structural row failure abandons the entire read.

## 12. Grouping and EOF semantics

The raw group identity is the ordinal, case-sensitive triple
`(SchemaName, TableName, IndexName)`. Every row in a group repeats identical
header values. A group must contain exactly `AttributeCount` rows with
positions `1..AttributeCount` and no gap or duplicate. `KeyAttributeCount`
must be at least one and no greater than `AttributeCount`.

Rows `1..KeyAttributeCount` must be keys. Remaining rows must be INCLUDE
attributes. A group is finalized only when the next identity or EOF is read.
EOF with no rows returns an empty collection; EOF finalizes one valid pending
group. EOF after a malformed group fails without returning a partial result.

## 13. Key and INCLUDE mapping

`indnkeyatts` and `indnatts` are authoritative:

```text
positions 1..indnkeyatts          -> ordered KeyParts
positions indnkeyatts+1..indnatts -> ordered IncludedColumns
```

A simple key has non-blank `ColumnName`, null `Expression`, key metadata and
one exact ordering state. An expression key has null `ColumnName`, non-blank
`Expression` and the same key metadata. Exactly one is present.

An INCLUDE row must resolve to one non-blank table column, must have null
expression, collation, operator-class identity, operator-class options and
ordering-property fields, and never creates an `IndexKeyPartSnapshot`. INCLUDE
order is preserved. Contradictory rows fail closed.

## 14. Expression and predicate mapping

Expression keys use only:

```text
pg_get_indexdef(index_oid, key_position, false)
```

The official per-column overload and the PostgreSQL 18.4 probe establish that
the returned value is the key column/expression without the separately mapped
collation, operator class, direction or null ordering. No `CREATE INDEX` text
is parsed in C#.

Partial predicates use only:

```text
pg_get_expr(indpred, table_oid, false)
```

Non-partial indexes map null. Partial indexes require a non-blank deparsed
predicate. Raw `pg_node_tree` values never cross the SQL boundary.

## 15. Access method, collation and operator-class structural identity

There is no productive access-method name allowlist. `pg_am.amname` is
preserved exactly, including a valid name unknown to the product. This does not
relax shape safety: every key must still provide the generic structural state
required by Core. A null or unknown required ordering property that cannot be
represented faithfully fails with the fixed generic index-metadata error.

Collation and the base operator-class identity are constructed in C# from the
separate catalog namespace and object-name columns as an always-qualified,
always-quoted value:

```text
"<schema with embedded quotes doubled>"."<name with embedded quotes doubled>"
```

This base representation is ordinal, stable, search-path-independent and
unambiguous and is never reused as a SQL identifier. Collation is null when
`indcollation` is zero. Operator class is required for every key. Both are
null for INCLUDE rows. A half-present schema/name pair fails.

`OperatorClass` additionally preserves the exact nullable ordered
`attoptions` array. The encoding is frozen as:

```text
SQL NULL options:
<qualified-operator-class-identity>

non-NULL options:
<qualified-operator-class-identity>|options[<count>;<length>:<value>...]
```

`count` and each `length` use invariant unsigned decimal without leading
zeroes. `length` is the exact .NET `String.Length` in UTF-16 code units. Values
are appended verbatim in stored array order, without trimming, Unicode
normalization, semantic parsing or sorting. The element count plus each length
prefix makes the representation injective even when an option contains `:`,
`]` or the literal marker. Embedded double quotes in namespace/name remain
doubled by the qualified-identity rule.

Examples:

```text
"pg_catalog"."int4_minmax_multi_ops"
"pg_catalog"."int4_minmax_multi_ops"|options[1;19:values_per_range=32]
```

A non-null empty array encodes as `|options[0;]` and therefore remains distinct
from SQL null, matching PostgreSQL's compatibility distinction. Null array
elements fail closed. The array and the temporary encoding inputs never enter
Core separately; only the resulting canonical string is supplied to the
existing `IndexKeyPartSnapshot.OperatorClass` property. Thus different option
values or stored option orders cannot collapse into structural equality, and
Core remains unchanged.

## 16. Sort direction and null ordering

For an orderable key:

```text
orderable = true
exactly one of ascending/descending = true
exactly one of nulls_first/nulls_last = true
```

All five property columns must first be non-null. The mapper transfers the
valid direction/null-order values directly to the two Core enums.

For a non-orderable key, PostgreSQL 18.4 returned all five properties false
for Hash, GIN, GiST, SP-GiST and BRIN. The mapper requires exactly that tuple
and applies the canonical Core normalization:

```text
SortDirection = Ascending
NullsOrdering = Last
```

For a non-orderable access method these two enum values are normalization
tokens, not claims that PostgreSQL can order a forward scan. This is safe
because PostgreSQL exposes no alternate ASC/DESC or NULLS syntax state for a
non-orderable key, the access method remains in structural identity, all
other structural fields remain preserved, and two distinct server states are
not collapsed. Any other non-orderable property tuple fails rather than being
invented silently. In particular, null/unknown in any required key property is
not converted to `Ascending`, `Descending`, `First` or `Last`; it fails with
the fixed generic index-metadata error.

## 17. Uniqueness and constraint association

`IsUnique` maps `indisunique`. `NullsNotDistinct` maps:

```text
IsUnique=false -> null
IsUnique=true  -> exact indnullsnotdistinct Boolean
```

`IsPrimaryKey` maps `indisprimary`. `BacksConstraint` is true only when
`pg_constraint.conindid` equals the index OID and `contype` is exactly `p`,
`u` or `x`: primary key, unique or exclusion. Foreign-key association does not
qualify. The mapper rejects primary without unique or constraint backing.

## 18. Validity, readiness and liveness

`indisvalid`, `indisready` and `indislive` map independently with no inferred
relationship. The mapper never computes validity from readiness/liveness and
never suppresses invalid indexes.

## 19. Physical, partitioned-root and partition policy

E001 admits exactly `pg_class.relkind` `i` and `I`.

| State | Meaning | Size | Usage statistics |
|---|---|---:|---|
| `i`, `relispartition=false` | ordinary physical index | direct `pg_relation_size` | E002 row when available |
| `i`, `relispartition=true` | physical index partition | direct `pg_relation_size` | E002 row when available |
| `I`, either partition state | virtual partitioned index/root | `0` | `null` |

Partitioned index roots and partitioned index partitions are retained because
they carry structural and validity state. They never aggregate descendants.
No `SUM`, `pg_partition_tree`, `pg_inherits`, recursive CTE or descendant
storage/usage calculation is permitted. Every physical child remains its own
snapshot.

## 20. E002 shape and statistics merge

E002 returns exactly four non-null columns:

| Ordinal | Value | CLR type |
|---:|---|---|
| 0 | Schema name | String |
| 1 | Table name | String |
| 2 | Index name | String |
| 3 | Scan count | Int64 |

The merge identity is the ordinal, case-sensitive triple
`(SchemaName, TableName, IndexName)`; OIDs never enter the internal result or
Core.

```text
statistics available + matching physical row -> exact non-negative idx_scan
statistics unavailable                       -> null
virtual index                                 -> null
physical index with no E002 row               -> null, never zero
```

Duplicate E002 identities, negative counters and E002 rows with no structural
E001 identity fail the whole operation. A hypothetical E002 row matching a
virtual `I` row is contradictory and also fails. Structural snapshots are
still returned when E002 is skipped.

## 21. Internal result, ordering and duplicates

The future result is equivalent to:

```text
internal sealed class PostgreSqlIndexSnapshotQueryResult

Properties:
ReadOnlyCollection<IndexSnapshot> Indexes
```

It defensively copies, can be empty and exposes no Npgsql, OID, SQL,
connection, transaction, command, reader or exception. It is not a record and
inherits a non-sensitive `ToString()`.

Final order is ordinal by `SchemaName`, `TableName`, `IndexName`. Key parts are
position-ascending and INCLUDE columns preserve index-attribute order. The
mapper rejects duplicate `(SchemaName, IndexName)`, key position, INCLUDE
column or E002 identity. There is no last-write-wins behavior.

The mapping preserves every field used by the existing order-sensitive
`IndexSnapshot` equality and therefore the future DBH003 structural comparison.
DBH003 itself is not implemented here.

## 22. Typed operation boundary and sequencing

The future restricted operation surface adds one method semantically
equivalent to:

```text
ReadIndexSnapshotsAsync(
    PostgreSqlSchemaFilter filter,
    bool usageStatisticsAvailable,
    CancellationToken cancellationToken)
```

It accepts no SQL, statement ID, dictionary, gateway or resource. It executes
E001 exactly once, then E002 exactly once only when the Boolean is true, and
returns complete snapshots. The callback remains explicitly typed.

The future row seam adds exactly one provider-neutral typed accessor to both
`IPostgreSqlRowSource` and `IPostgreSqlRowReader`:

```text
string[] GetStringArray(int ordinal)
```

The accessor is called only after `IsNull(ordinal)` is false and the provider
implementation uses its typed `string[]` read. The mapper immediately makes a
defensive copy before advancing the reader, rejects null elements and builds
the canonical `OperatorClass` string. No array, Npgsql type or mutable
collection crosses into Core or the final result. Logically ordinal 16 is
`string[]?`; SQL null and a non-null empty array remain distinguishable. This
output accessor does not add a SQL parameter type: inventory parameter types
remain exactly `Int32` and `TextArray`.

GC-DHI-04E does not claim to enforce C001–C004 sequencing. Real tests compose:

```text
verified session
-> capability probe
-> require CatalogMetadata
-> E001
-> if UsageStatistics available: E002
-> map IndexSnapshot
```

With C003 unavailable, E001 runs, E002 execution count is zero and every scan
count is null. Mandatory productive provider composition remains GC-DHI-04F.

## 23. Permission-loss strategies

### C002 required function

A dedicated disposable fixture revokes `EXECUTE` on exactly one new function,
preferably `pg_get_indexdef(oid,integer,boolean)`, from `PUBLIC` and directly
from a non-superuser inspection role with no inherited role. It proves:

```text
previous required checks true
selected new function check false
C002 false
E001 execution count 0
generic non-sensitive failure
```

### C003 optional statistics

The existing GC-DHI-04C permission-loss semantics are reused. The fixture
proves C002 true, C003 false, E001 executes, E002 does not and every resulting
`ScanCount` is null. Usage statistics never become required.

## 24. Invalid, readiness and liveness strategy

The deterministic real invalid-index fixture is:

```text
partitioned table with at least one partition
CREATE INDEX ... ON ONLY <partitioned table> (...)
```

Official PostgreSQL partitioning documentation specifies that this produces a
partitioned index marked invalid until matching partition indexes are attached.
It requires no race, sleep, concurrent-build failure, catalog update or
corruption and supplies the mandatory real `IsValid=false` evidence.

`IsReady=false` and `IsLive=false` are transient internal lifecycle states for
which no safe persistent DDL fixture is defined. They receive exhaustive mapper
unit coverage only. Integration tests must not race CREATE/DROP, sleep for a
window or update `pg_catalog`. This limitation is explicit and accepted by
this definition.

## 25. PostgreSQLServer scenarios

Future PostgreSQL 18.4 verification covers at least:

- simple and multicolumn B-tree;
- unique, primary-key-backed and unique-constraint-backed indexes;
- reproducible exclusion-constraint backing using built-in types;
- INCLUDE and preserved INCLUDE order;
- expression and mixed column/expression keys;
- partial predicate;
- explicit collation and non-default operator class;
- built-in BRIN indexes with the same operator-class identity and different
  option values;
- built-in BRIN indexes with identical option pairs in opposite stored order,
  proving distinct canonical `OperatorClass` values;
- ASC/DESC and NULLS FIRST/LAST;
- Hash, GIN, GiST, SP-GiST and BRIN where constructible without an external
  extension, including at least one non-orderable method;
- physical partition index, partitioned root and partitioned index partition;
- deterministic invalid partitioned index;
- empty, include-filtered and exclude-filtered results;
- system-schema exclusion; and
- exact E001/E002 shapes and canonical ordering.

No PostgreSQL 15 container or permanent 15/18 matrix is added in this subgate.

## 26. Scan-count evidence

The statistics fixture proves three distinct outcomes:

```text
fresh observed physical index -> ScanCount = 0
forced real index scan        -> ScanCount > 0
C003 unavailable or virtual I -> ScanCount = null
```

Business-row access used only to force the physical scan remains in
IntegrationTests. The test uses a fresh index, a query shape that PostgreSQL
confirms used that index, `pg_stat_force_next_flush()`/backend-idle visibility
and a bounded new-transaction observation; it does not use `pg_stat_statements`
or production SQL.

## 27. Fail-closed mapping and errors

The future mapper rejects at least:

```text
wrong field count or CLR type
null required field
blank identifier
invalid relkind/partition tuple
missing, duplicate or gapped attribute position
header disagreement inside a group
attribute/key cardinality disagreement
invalid key/include discriminator
missing key part
unexpected INCLUDE expression or key metadata
invalid ColumnName/Expression XOR
half-present collation or operator-class identity
missing operator class
wrong operator-class-options CLR type or null array element
operator-class options on an INCLUDE row
impossible ordering-property tuple
null/unknown required ordering property on a key
negative size or scan count
contradictory primary/unique/constraint state
duplicate final index or E002 identity
unmatched E002 row
```

The exposed internal failures are fixed, generic and non-sensitive, equivalent
to:

```text
The PostgreSQL index metadata row is invalid.
The PostgreSQL index usage statistics row is invalid.
```

They store no received value, SQL, OID, SQLSTATE or server message and do not
expose configurable message or inner-exception constructors.

## 28. Cancellation and cleanup

Future tests cover:

```text
precancellation
E001 command execution
E001 first/middle/final/end-of-rows reads
E001 disposal
between E001 and E002
E002 command execution
E002 first/middle/final/end-of-rows reads
E002 disposal
primary failure plus cleanup failure
cleanup-only failure
```

Cancellation returns no partial result. The exact token reaches command and row
reads. Existing `PostgreSqlAsyncCleanup`/EDI precedence remains: the primary
failure wins, while cleanup-only failure propagates. Rollback uses
`CancellationToken.None`; there is no retry, commit or autocommit, and the pool
must remain reusable.

## 29. SQL safety

E001 and E002 are static, inventoried, frozen, parameterized SELECT statements
over catalog/statistics metadata. They contain no business-row SELECT,
`COUNT(*)`, `EXPLAIN`, `pg_stat_statements`, dynamic SQL, identifier
interpolation, DDL, DML, permission command, maintenance command, COMMIT or
descendant aggregation.

Fixture DDL, GRANT/REVOKE and synthetic business-row access exist only in
future IntegrationTests and never in a productive assembly.

## 30. Implementation entry criteria

PG-05 implementation may start only when all are true:

1. GC-DHI-04D remains approved and closed.
2. This definition is integrated.
3. Definition CI is green.
4. E001 and E002 exact SQL remain frozen.
5. The exact C002 expansion remains frozen and C003 unchanged.
6. Result shape, grouping, key/INCLUDE and merge contracts remain frozen.
7. Ordering normalization, qualified identities and the ordered length-prefixed
   operator-class-options encoding remain accepted.
8. Partitioned-index and invalid-index policies remain accepted.
9. Readiness/liveness limitation and cancellation/cleanup contracts remain
   accepted.
10. The human owner reviews this definition.
11. The human owner explicitly authorizes implementation.
12. A separate Claude Code implementation prompt exists.

## 31. GC-DHI-04E exit criteria

Exit requires:

1. PG-05 implemented with exact E001, E002 and C002.
2. C003, B001–B003, C001, C004 and D001 byte-identical.
3. ten statements, eight kinds, two parameter types and ten frozen contracts.
4. validator matrix 800 with exactly ten accepted.
5. reused schema filter and permanent system exclusions.
6. strict 31-column E001 and four-column E002 shapes.
7. ordered keys and INCLUDE columns, expressions and predicates.
8. qualified collation/operator-class identities, exact ordered opclass options
   and access-method preservation.
9. exact ordering/null semantics and NullsNotDistinct policy.
10. PK/constraint and independent valid/ready/live mapping.
11. direct physical size, zero virtual size and no aggregation.
12. optional scan counts and correct unavailable-statistics degradation.
13. deterministic ordering and duplicate rejection.
14. complete cancellation, cleanup and fail-closed validation.
15. green PostgreSQL 18 normal, C002-loss and C003-loss fixtures.
16. deterministic invalid-index evidence and documented readiness/liveness
    limitation.
17. all GC-DHI-04A–04D regressions green.
18. Core, CLI, provider, diagnostics and reporting unchanged.
19. zero failures, skips, warnings and errors.
20. no GC-DHI-04F, tag, release or NuGet publication.

## 32. Deferred work and prohibitions

GC-DHI-04E does not implement `IDatabaseSnapshotProvider`, complete
`DatabaseSnapshot` composition, DBH001–DBH005, DBH003, DBH004, DBH005, CLI
inspection, connection-source resolution, console/JSON reporting, exit codes,
data profiling, query plans, `pg_stat_statements`, PostgreSQL 15 CI, automatic
remediation or GC-DHI-04F.

No Core change is permitted unless a separately authorized blocker decision is
made before implementation. This definition found no such blocker.

## 33. Definition validation and next action

The corrected exact E001, byte-identical E002 and unchanged future C002 were
reconciled with the existing inventory and typed boundary, reviewed against
PostgreSQL 15–18 documentation and official source, and syntax/probe verified
on the pinned PostgreSQL 18.4 image. Only the four canonical governance
documents may change in this correction commit.

```text
GC-DHI-04E DEFINITION CORRECTED — AWAITING HUMAN IMPLEMENTATION AUTHORIZATION
Human review of the corrected GC-DHI-04E definition.
No GC-DHI-04E implementation is authorized.
```
