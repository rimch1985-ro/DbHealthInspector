# PostgreSQL Index Snapshot Query and Mapping

Implementation notes for GC-DHI-04E (PG-05). Describes what was built, not what was proposed; the
gate definition remains the authority for every frozen contract.

---

## 1. Objetivo

Two new statements — E001 (index metadata) and E002 (index usage statistics) — plus one expanded
capability check (C002), mapped into the existing Core `IndexSnapshot` model through a single typed
operation. No provider, no snapshot composition, no diagnostic rule and no CLI surface: those
remain deferred.

## 2. Inventory

The productive inventory grows from eight statements to ten:

```text
B001 B002 B003  C001 C002 C003 C004  D001  E001 E002
```

| Total | Value |
|---|---:|
| Statement ids | 10 |
| Command kinds | 8 |
| SQL parameter types | 2 |
| Inventory definitions | 10 |
| Frozen contracts | 10 |

One new kind, `SelectIndexMetadata`, is added for E001. **E002 deliberately reuses
`SelectStatistics`**, the kind C004 already carries, because it reads a statistics view exactly as
C004 does. Sharing a kind grants nothing: layer 2 still binds each statement id to one exact SQL
text, so neither statement can carry the other's.

`GetStringArray` is a **row-seam output accessor**, not a parameter type. The bindable types remain
`Int32` and `TextArray` only.

## 3. Exact SQL

All three texts were extracted programmatically from the gate definition and verified from the
**compiled assembly**, never transcribed by hand:

| Statement | Length | SHA-256 |
|---|---:|---|
| E001 | 6262 | `d45b8ed1e0d842b1474839a3beadf6d1a0d4233cfa847c3887c41cfd4b1184d7` |
| E002 | 737 | `fe8f23a5dff2cdfb8d08acf4fb7f7a3f90aef4b7e9eee4b678cde8c260624919` |
| C002 | 2027 | `777cb44afb178c299566f1a8c0251e3ab9ba47480bd578b6a339f4d1c24c5a90` |

D001 (1816, `13b4e88d…80b87`) and B001–B003, C001, C003, C004 are byte-identical to GC-DHI-04D.

### 3.1 C002 expansion

C002 keeps its id, `SelectCapabilityCheck` kind, zero parameters and one-row/one-non-null-boolean
shape. It gains exactly the four functions E001 calls — `pg_relation_size`, `pg_get_indexdef`,
`pg_get_expr` and `pg_index_column_has_property` — for **seven** function checks in total. Nothing
was removed. `attoptions` is read straight from `pg_attribute`, so it adds no function call and
therefore no privilege check.

### 3.2 Lexical policy unchanged

E001 and E002 pass the existing layer-1 scanner with **no widening**: no new punctuation, no new
allowed token, no relaxed statement form. E001's `<>`, `<=`, `[`, `]` and `-` were already
admitted for D001. The frozen-contract layer remains the execution authority.

### 3.3 Catalogs read

E001 reads `pg_index`, `pg_class` (twice), `pg_namespace`, `pg_am`, `pg_attribute` (twice),
`pg_collation`, `pg_opclass` and `pg_constraint`. E002 reads `pg_stat_all_indexes`.
**`pg_inherits` is never queried** — it stays privilege-checked only, because descendant traversal
is prohibited.

## 4. E001 shape

Thirty-one columns, one row per index attribute. Ordinals 0–9 and 23, 25–30 are non-nullable;
10–22 describe a single attribute and are legitimately NULL for an INCLUDE column or an
inapplicable property; 24 is NULL for every non-unique index.

Nullability is validated **explicitly, before any typed read**, exactly as D001 does. Relying on
the driver to raise on a NULL would make the contract depend on provider behaviour rather than on
the shape E001 promises.

## 5. String-array seam

```csharp
string[] GetStringArray(int ordinal)
```

Added to both internal row contracts. The production implementation calls Npgsql's typed
`GetFieldValue<string?[]>`, so PostgreSQL's array **text** form — with its quoting, escaping and
`NULL` spelling — is never parsed. The driver's array is copied immediately, element order and an
empty array are preserved, an empty array stays distinct from SQL NULL, and a null element is
rejected rather than surfaced (a silent null would later be indistinguishable from an empty
option). No Npgsql type reaches Core.

## 6. Grouping and EOF

E001 orders by schema, table, index then attribute, so all rows of one index arrive consecutively.
The executor streams and closes a group as soon as the identity changes, and again at end of rows.

Each group must satisfy: row count equals `AttributeCount`; positions form exactly
`1..AttributeCount` with no gap and no duplicate; `1 ≤ KeyAttributeCount ≤ AttributeCount`; and
every repeated header column is identical across the group. Positions `1..KeyAttributeCount` are
keys; the rest are INCLUDE.

A group is **validated while its reader is still open**, so a malformed group is captured as the
primary failure. Deferring validation until after the read would let a reader-disposal failure
preempt it, and the primary must always win.

Two distinct duplicate rules apply as each group closes, both inside that same protected block:

| Rule | Compared | Rejects |
|---|---|---|
| Raw group identity | `(schema, table, index)` | the same index appearing in two non-contiguous runs — E001's ordering makes that a broken grouping assumption, not a duplicate to collapse |
| **Final index identity** | `(schema, index)` | two groups that differ only by table name, which would produce two result entries claiming the same index |

The final-identity rule uses a set spanning the **whole read**, never a comparison with the
previous group. An index name is unique within its schema but not within its table, so the two
colliding groups need not be adjacent — and after ordering by schema, *table*, then index, an
unrelated index whose table name sorts in between separates them. A neighbour-only scan therefore
misses the collision entirely (GC-DHI-04E-C1, R1-01). `PostgreSqlIndexSnapshotQueryResult` applies
the same global rule again as a second, independent layer.

EOF with a valid pending group finalises it; EOF with a malformed one fails the whole operation.
No partial collection is ever returned.

## 7. Keys, INCLUDE, expressions and predicates

A key part carries exactly one of column name or expression, never both and never neither. An
expression comes solely from `pg_get_indexdef(index_oid, key_position, false)`; no `CREATE INDEX`
text is parsed and no modifier is extracted from it. Collation, operator class, ordering and
options stay separate server-supplied fields.

An INCLUDE attribute is a plain stored column: expression, collation, operator class, options and
all five ordering properties must be null. It contributes its name to `IncludedColumns` in
attribute order and never becomes an `IndexKeyPartSnapshot`. An expression INCLUDE is invalid.

A partial predicate is the exact non-blank `pg_get_expr(..., false)` text; a non-partial index
reports null. No `pg_node_tree` is stored and no pretty-printing is applied.

### 7.1 SQL NULL is not a blank string

SQL NULL and a present-but-blank string are **different facts** and are never collapsed into one
category (GC-DHI-04E-C1, R1-03). NULL means the server said the field does not apply; `""`, `" "`,
`"\t"` or `"\r\n"` means it said the field applies and then supplied nothing usable. Only the first
is ever a valid absence; the second is always a broken row.

| Field state | Contract |
|---|---|
| Contractually **active** field | present **and** non-blank — schema, table, index, access method, an active column name or expression, both halves of an active collation or operator class, a non-null partial predicate |
| Contractually **inactive** field | strictly SQL NULL — a blank string is never accepted as a substitute |

Consequences worth stating explicitly, because each is a row that would otherwise map silently:

```text
simple key   + blank Expression      -> fail   (not "no expression")
expression   + blank ColumnName      -> fail   (not "no column")
INCLUDE      + any blank key-only    -> fail   (a blank is a populated field)
collation    + blank half            -> fail   (not "no collation")
opclass      + blank half            -> fail
predicate    + blank                 -> fail   (not "not partial")
```

Presence is therefore tested **before** presence decides what kind of key a row describes: a blank
expression on a simple key is rejected outright rather than being read as absence. Detection never
rewrites the value — whitespace is detected, not trimmed — so a legitimate value keeps exactly the
bytes the server sent, including surrounding whitespace.

Qualified pairs (collation, operator class) have exactly three admissible states: both halves SQL
NULL, both halves present and non-blank, or a failure. Optional collation may be absent; a key's
operator class may not.

## 8. Structural identity

Collations and operator classes render as `"schema"."name"` — always schema-qualified, always
double-quoted, embedded `"` doubled, compared ordinally, independent of `search_path`. This is a
structural identity, never SQL to execute. A half-present schema/name fails closed; `pg_catalog` is
never assumed. A key without an operator class is rejected; an INCLUDE with one is rejected.

### 8.1 Operator-class options

```text
no options (SQL NULL)  ->  "schema"."opclass"
options present        ->  "schema"."opclass"|options[<count>;<length>:<value>...]
empty array            ->  "schema"."opclass"|options[0;]
```

`count` and each `length` are invariant-culture decimals with no leading zero, and `length` is
.NET `String.Length` — UTF-16 code units, so a non-BMP character counts as 2.

**Length-prefixing is what makes the encoding injective.** Because each value's extent is known
before it is read, no option containing `:`, `;`, `]` or even the literal `|options[` can forge
structure. Values are copied verbatim: no trimming, no Unicode normalization, no case folding, no
semantic parsing and **no sorting** — stored order is itself part of the identity. A null element
is a mapping failure.

Adversarial pairs proven not to collide include `["a:b"]` vs `["a","b"]`, `["1:a"]` vs `["a"]`,
`["]"]` vs `["",""]`, `["|options[1;1:x]"]` vs `["x"]`, and NFC vs NFD spellings of `é`.

## 9. Access method

`pg_am.amname` is preserved verbatim. There is **no product allowlist**: built-ins are test cases,
not restrictions. An unknown method is kept by name as long as every generic property Core requires
is representable; an unknown or null required property fails closed.

## 10. Ordering normalization

All five ordering properties must be non-null for a key part.

- **Orderable** requires exactly one direction and exactly one nulls placement, and maps directly.
- **Non-orderable** is admitted in exactly one shape — all five false — and maps to
  `Ascending`/`Last` as **normalization tokens**, because Core's enums have no "not applicable"
  member.

Every other combination is a fixed mapping failure. A NULL or unknown property is **never** turned
into a token: the tokens are reached only when the server positively reported non-orderability.

## 11. Uniqueness, constraints, validity

`IsUnique` is `indisunique`. `NullsNotDistinct` is the exact server boolean for a unique index and
**null** for a non-unique one — false is never invented. `IsPrimaryKey` is `indisprimary`;
`BacksConstraint` is true only for `pg_constraint.contype` in `p`, `u`, `x` via `conindid`, never
through an indirect foreign-key association. A primary key that is not unique, or that backs no
constraint, is rejected.

`indisvalid`, `indisready` and `indislive` are preserved independently — none derived from another,
and an invalid index is never suppressed. All eight combinations have exhaustive unit coverage;
no catalog race is fabricated.

## 12. Physical and partitioned indexes

Only `relkind` `i` and `I` are admitted.

| Kind | SizeBytes | ScanCount |
|---|---|---|
| `i` physical (including a partition) | exact `pg_relation_size(index_oid)` | E002 value when available |
| `I` partitioned root (virtual) | 0 | null |

A virtual index claiming non-zero storage is rejected. There is **no descendant aggregation**: no
`SUM`, no `pg_partition_tree`, no `pg_inherits` traversal, no recursive CTE, no child size or usage
sum.

## 13. E002 and the merge

E002 is four columns — schema, table, index, scan count — and runs **exactly once, only when the
usage-statistics capability is available**. When it is not, E002 is not executed at all and every
scan count is null.

| Situation | ScanCount |
|---|---|
| Statistics available, matching physical row | exact `idx_scan` |
| Statistics unavailable | null |
| Physical index with no statistics row | null |
| Virtual `I` root | null |
| Duplicate E002 identity | **fail** |
| Negative E002 scan count | **fail** |
| E002 identity absent from E001 | **fail** |
| E002 row matching a virtual `I` | **fail** |

Identity is `(schema, table, index)` compared ordinally and case-sensitively. There is no
last-write-wins. **Absence is unknown, never zero**; zero is a real, known value.

Every one of the four failing rows above is detected **while the E002 reader is still open**. The
E001 identities are handed to the E002 read for exactly that reason: shape, negative counter,
duplicate identity, an identity E001 never reported, and an identity naming a virtual index are all
decided during the row loop, so nothing about the merge can fail once the reader has been released.
Reconciling afterwards would let a reader-disposal failure displace the semantic failure that must
stay primary (GC-DHI-04E-C1, R1-02). By the time the two statements are combined, every retained
statistic is already proven to name a physical index E001 reported, so the merge itself is a
lookup that cannot fail.

## 14. Result

`PostgreSqlIndexSnapshotQueryResult` exposes a `ReadOnlyCollection<IndexSnapshot>` built from a
defensive copy, sorted by schema, table then index — all ordinal. Key parts are ordered by position
and included columns keep their attribute order. A duplicate `(schema, index)` is rejected even
under a different table name, because an index name is unique per schema and the table must not be
able to disguise a duplicate. It carries no OID, no Npgsql type, no SQL, no reader and no stored
exception, and is deliberately **not** a record so its `ToString` renders no customer structure.

## 15. Typed operation boundary

```csharp
ReadIndexSnapshotsAsync(PostgreSqlSchemaFilter filter, bool usageStatisticsAvailable, CancellationToken ct)
```

One composite operation rather than two, because the merge is a contract the caller must not be
able to get wrong — or to skip. After GC-DHI-04E the restricted view exposes six typed operations
(C001, C002, C003, C004, D001, index snapshots) and still has no generic dispatch, no statement-id
argument, no raw SQL and no connection, command or reader exposure.

Deciding *whether* statistics may be read stays with the capability probe; sequencing the probe
before the operation remains the composing caller's responsibility until GC-DHI-04F owns it
productively. The view deliberately does not police that ordering.

## 16. Fail-closed errors

Two internal exceptions, each with a fixed message, no message/inner constructor, `InnerException`
always null and `Data` always empty:

```text
The PostgreSQL index metadata row is invalid.
The PostgreSQL index usage statistics row is invalid.
```

Neither carries SQL, an OID, a schema, table, index, expression, predicate, collation, operator
class, option value, received value, SQLSTATE or server message. Wrong CLR types are sanitized at
the row seam exactly as GC-DHI-04D does for D001: a narrow `catch (InvalidCastException)` around
the typed reads only — no general `catch (Exception)` converting arbitrary failures, and a
cancellation or driver fault passes through untouched.

## 17. Cancellation and cleanup

Deterministic coverage exists for: precancel; E001 command execution, before-first-row,
mid-row, final-row, EOF and reader disposal; between E001 and E002; and E002 command execution,
row boundaries, EOF and reader disposal. No sleeps and no races — a hook cancels the caller's token
at the exact moment.

No path returns a partial collection. Both readers are released through the existing EDI-safe
cleanup, so a disposal failure never replaces a shape, mapping or cancellation failure; a
cleanup-only failure still propagates. Rollback continues to use `CancellationToken.None` and the
pool stays reusable.

## 18. PostgreSQL 18.4 evidence

Against the canonical pinned image, the index zoo covers simple and multicolumn B-tree, unique,
primary-key-backed, unique-constraint-backed, exclusion-constraint-backed, INCLUDE with order,
expression, mixed column/expression, partial, explicit collation, non-default operator class,
ASC/DESC with NULLS FIRST/LAST, Hash, GIN, GiST, SP-GiST, BRIN, non-orderable keys, a partitioned
index root, its physical partition, an invalid partitioned index, include/exclude filters, system
schema exclusion and an empty result. **No external extension is installed** to reach any shape.

### 18.1 Operator-class options

Two BRIN indexes on the same column with the same opclass and different stored `attoptions`:

```text
int4_minmax_multi_ops(values_per_range=32) -> "pg_catalog"."int4_minmax_multi_ops"|options[1;19:values_per_range=32]
int4_minmax_multi_ops(values_per_range=64) -> "pg_catalog"."int4_minmax_multi_ops"|options[1;19:values_per_range=64]
```

The mapped identities differ, and the encoding is checked against the raw `attoptions` read out of
band. An index whose opclass stores no options gains no `|options[` marker at all.

**Inverse stored order.** Different *values* cannot show that stored order is preserved rather than
sorted — for that, two indexes must share the same option names *and* the same values and differ
only in order. Two BRIN indexes using the built-in `int4_bloom_ops` provide it (no extension is
involved); PostgreSQL 18.4 stores `attoptions` in the order the DDL supplied them:

```text
int4_bloom_ops(n_distinct_per_range=16, false_positive_rate=0.05)
  attoptions -> {n_distinct_per_range=16,false_positive_rate=0.05}
int4_bloom_ops(false_positive_rate=0.05, n_distinct_per_range=16)
  attoptions -> {false_positive_rate=0.05,n_distinct_per_range=16}
```

The integration test asserts, from the real catalog, that the two raw arrays hold the same element
set, are **not** sequence-equal, and map to different canonical identities whose option order equals
the stored order element for element — with the expected string rebuilt from the raw catalog values
independently of the mapper, so the assertion compares against PostgreSQL rather than against
itself. Had the encoding sorted, the two identities would have collapsed into one
(GC-DHI-04E-C1, R1-04).

### 18.2 Invalid index

`CREATE INDEX ... ON ONLY <partitioned table>` leaves the root invalid until a matching index is
attached for every partition. Fully deterministic — no `CONCURRENTLY` timing trick, no sleep, no
catalog write, no drop race. The index is reported with `IsValid = false`, size 0 and a null scan
count, and is never suppressed.

### 18.3 Scan counts

| Case | ScanCount | How |
|---|---|---|
| Fresh physical index | `0` | counters reset, index never queried |
| Genuinely used index | `> 0` | suite forces a scan and **verifies from the plan** that the index was chosen |
| Statistics unavailable | `null` | C003 false, E002 not executed |
| Virtual `I` root | `null` | no storage, no counter, no aggregation |

Visibility is forced with `pg_stat_force_next_flush()` and observed from a separate session. No
arbitrary sleep, no `pg_stat_statements`, and the only business rows read anywhere are read by the
test suite.

## 19. Capability degradation

On the statistics-revoked fixture the required catalog metadata is still reachable, so E001 runs
once and E002 is not executed at all — verified by recording the exact statements that reached the
server. Every scan count is null and the index metadata itself stays complete. C003 is unchanged,
and statistics never become a required capability.

### 19.1 Losing a new required function

A separate disposable container proves that one of the four functions the C002 expansion added
genuinely controls the capability. `pg_get_indexdef(oid, integer, boolean)` — the exact overload
E001 calls — has `EXECUTE` revoked from `PUBLIC` **and** from the inspection role;
`pg_get_indexdef(oid)`, which this adapter never calls, is untouched.

The fixture records the state **before** the revocation and enforces it, so the comparison is a
measured transition rather than an assumption that the privilege existed to begin with
(GC-DHI-04E-C1, R1-05):

| Observation | Before | After |
|---|---|---|
| `pg_table_size`, `pg_indexes_size`, `pg_total_relation_size` | true | true |
| `pg_relation_size`, `pg_get_expr`, `pg_index_column_has_property` | true | true |
| **`pg_get_indexdef(oid,integer,boolean)`** | **true** | **false** |
| **C002** (productive statement, real verified session) | **true** | **false** |
| `rolsuper` / role memberships | false / none | false / none |

C002 is observed through the productive statement rather than a privilege query reassembled in the
test, so the starting point is the one the product itself would have seen. With C002 false, the
composed path stops at the probe: the recorder shows the exact statement sequence ending at C002,
with `ReadIndexMetadata` and `ReadIndexUsageStatistics` executed **zero** times. The surfaced
failure is the existing sanitized required-capability exception — no fixture-specific exception was
introduced — and names neither the function, the overload, the role nor the database.

## 20. Scope exclusions

Not implemented, and explicitly out of scope: `IDatabaseSnapshotProvider`, `DatabaseSnapshot`
composition, diagnostic rules DBH003–DBH005, CLI `inspect`, connection-source resolution, JSON and
console reporting, exit-code mapping, a PostgreSQL 15 CI matrix, and GC-DHI-04F. Core, the CLI and
the PostgreSQL connection boundary were not modified, no dependency was added, and no new public
type was exported.

## 21. Known limitations

- `indisready` and `indislive` have exhaustive **unit** coverage but no live fixture: producing a
  not-ready or not-live index requires a concurrent-build race the gate forbids fabricating.
- The permanent PostgreSQL 15 comparison matrix remains deferred to GC-DHI-04F; 04E adds no
  PostgreSQL 15 container.
- Sequencing C002/C003 before the index operation is enforced by test composition, not by the
  operation view; productive sequencing belongs to GC-DHI-04F.
