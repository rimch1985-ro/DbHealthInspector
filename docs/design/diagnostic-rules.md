# Diagnostic rules — DBH001 to DBH005

The interpretation layer: it turns an already-captured `DatabaseSnapshot` into database-health
findings. Implemented by GC-DHI-05A; the approved contract is
`docs/gates/GC-DHI-05A_DEFINITION.md`.

Every finding produced by these rules carries this document as its
`DocumentationReference`.

## 1. Shape

All five rules implement the existing `IInspectionRule`. Each is pure: it reads the snapshot,
performs no I/O, holds no state between calls, reads no clock, and returns the same findings
for the same snapshot every time. That is what makes them unit-testable without a database,
and it is why none of them takes a cancellation token — the I/O already happened when the
snapshot was built.

Nothing about orchestration, ordering, summary counts or overall risk lives here. Those were
delivered by CORE-04 and are reused unchanged: `InspectionOrchestrator` runs the rules in
ordinal code order, gates them on capabilities, isolates their failures and assembles an
immutable `InspectionResult`; `OverallRiskCalculator` derives the risk.

`ApprovedDiagnostics.CreateRegistrations()` composes the five. It is a plain factory that
names each rule explicitly — no reflection, no container, no plugin mechanism.

## 2. The rules

| Code | Name | Category | Severity | Confidence | Requires |
|---|---|---|---|---|---|
| DBH001 | `TABLE_WITHOUT_PRIMARY_KEY` | Structure | Warning | High | — |
| DBH002 | `LARGE_TABLE` | Capacity | Info | Medium | — |
| DBH003 | `EXACT_DUPLICATE_INDEX` | Indexing | Warning | High | — |
| DBH004 | `UNUSED_INDEX_CANDIDATE` | Statistics | Info | Low or Medium | `UsageStatistics` |
| DBH005 | `INVALID_INDEX` | Indexing | Critical | High | — |

Severities and confidences are frozen by `PROJECT_RULES.md` §5 and are not the rules'
choice.

## 3. Decisions frozen by this gate

Seven decisions were absent from canonical material and were frozen in the definition (§14).

### D-1 — DBH001 relation scope

Ordinary tables and partitioned roots are evaluated. A physical partition is not: its key is
defined at the root, so reporting each partition would multiply one root defect across every
partition. Views, materialized views and foreign tables cannot carry a primary key; temporary
tables are session-scoped; unclassified relations are never reported.

### D-2 — DBH002 relation scope

Ordinary tables and physical partitions are evaluated. The partitioned **root** is excluded,
and this is the subtle one: the snapshot reports *physical* relation sizes without descendant
aggregation, so a root's own `TotalSizeBytes` is its own nearly-empty storage rather than the
logical total of its partitions. Reporting it would be misleading in both directions — it
would never fire for a genuinely huge partitioned table, and if it did fire the number would
not mean what a reader assumes. The partitions carry the real storage and are in scope, so
nothing is lost.

DBH001 and DBH002 therefore diverge on the root deliberately: DBH001 asks a *structural*
question that belongs to the root, DBH002 a *physical-storage* question that belongs to the
partitions.

### D-3 — Thresholds

| Threshold | Default |
|---|---:|
| `LargeTableRowThreshold` | `1_000_000` rows |
| `LargeTableSizeThresholdBytes` | `1_073_741_824` (1 GiB) |
| `UnusedIndexSizeThresholdBytes` | `10_485_760` (10 MiB) |

**Product defaults, not database facts.** They describe no property of PostgreSQL; they
encode the tool's opinion about when size is worth a human's attention. All comparisons are
inclusive (`value >= threshold`).

They live in `DiagnosticThresholds`, one immutable value object whose only invariant is that
each value is positive — no range, ordering, preset or profile policy. It is deliberately not
a configuration subsystem: no provider, no binding, no file or environment access. GC-DHI-05B
maps the approved CLI options onto it.

### D-4 — DBH004 is conservative

Beyond the backlog's primary-key and unique exclusions, DBH004 also requires
`BacksConstraint == false` and `IsReady == true`.

`BacksConstraint` matters because a PostgreSQL index can enforce a constraint without being
unique or a primary key — an exclusion-constraint index is exactly that shape, and the
existing fixture `indexed_orders_span_excl` is a live example. Such an index cannot be
removed without destroying the constraint it enforces, so presenting it as an unused
candidate would be misleading.

`IsReady` matters because an index in a transitional readiness state has no scan history
comparable to a fully ready one, so a zero count there means something different.

### D-5 — DBH004 confidence

`Medium` when `StatisticsResetAtUtc` is present, `Low` when it is null. Presence only: the
rule never computes elapsed time and never reads a clock, because that would make it impure
and make the same snapshot yield different findings on different runs. Never `High` — a zero
scan count is never direct evidence that an index is unnecessary.

### D-6 — DBH003 emits one finding per group

A structural equivalence group of *n* indexes produces exactly one finding, not
*n*(*n*−1)/2 pairs. The finding is anchored on the ordinally-first index name, so two disjoint
groups on one table produce two findings with distinct fingerprints.

### D-7 — Categories

Structure, Capacity, Indexing, Statistics, Indexing — as tabulated in §2.

## 4. DBH003 structural equivalence

`IndexSnapshot.Equals` is **not** used, and is not modified. That override also compares
`IndexName`, `SizeBytes` and `ScanCount`, which is correct for identity but returns `false`
for exactly the duplicates this rule must find — two duplicates necessarily have different
names and usually different sizes. `ExactDuplicateIndexRuleTests` asserts this directly: two
snapshots that are `NotEqual` under the record's own equality are still reported as a
duplicate group.

The rule therefore builds its own structural key from schema, table, access method, the
ordered key parts with every structural property of each (column, expression, collation,
operator class including its encoded options, sort direction, nulls ordering), the ordered
INCLUDE columns, the partial predicate, uniqueness and null-distinctness.

Excluded from the key, so that indexes differing only in these remain duplicates:
`IndexName`, `SizeBytes`, `ScanCount`, `IsValid`, `IsReady`, `IsLive`, `IsPrimaryKey`,
`BacksConstraint`.

The key is built with length prefixes and explicit presence markers for optional values, so
no combination of field values can encode to the same string as a different combination —
the same technique the finding fingerprint uses. A null is marked distinctly from any present
value, including the empty string.

## 5. Missing statistics are never findings

The governing principle: **a missing optional statistic must never become a false positive.**

| Condition | Behavior |
|---|---|
| `UsageStatistics` unavailable | DBH004 is registered as requiring it, so the orchestrator skips the rule entirely and records `SkippedUnavailableCapability`. The rule cannot misread absence because it never runs. |
| `ScanCount` null | No DBH004 finding — a second, independent guard inside the rule. Unknown is not zero. |
| `EstimatedRowCount` null | DBH002's row criterion is skipped; the size criterion still applies and can fire alone. Never coerced to `0`. The evidence key is omitted. |
| `StatisticsResetAtUtc` null | DBH004 may still fire, at `Low` confidence, with the timestamp evidence key omitted. |

## 6. Evidence and fingerprint stability

`core-domain-contracts.md` §9.1 requires a finding's fingerprint to survive changes to its
current size or row estimate between two inspections of the same database. So:

- Evidence identifying **what the object is** participates in the fingerprint (`Include`).
- Evidence carrying a **fluctuating measurement** — sizes, row estimates, scan counts,
  statistics timestamps — does not (`Exclude`).

Without this, a table's DBH002 finding would become a *new* finding on every growth tick.
`TableWithoutPrimaryKeyRuleTests` and `LargeTableRuleTests` each assert the fingerprint is
stable across simulated growth.

One case reads the other way: DBH005's `is_valid`, `is_ready` and `is_live` **do**
participate, because they describe *which kind* of invalidity this is — a stable property of
the condition, not a measurement.

## 7. Recommendations are never instructions

No rule emits DDL, and none instructs an automatic change. DBH004 is explicit that it reports
a candidate for human review and that the index must not be dropped automatically; DBH003
warns that an index backing a constraint must be removed through its constraint rather than
dropped directly; DBH005 asks for a supervised rebuild and states that the tool performs no
DDL. These are asserted by tests, not left to reviewer discipline.
