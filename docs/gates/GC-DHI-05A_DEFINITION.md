# GC-DHI-05A — Functional Diagnostic Rules and Inspection Orchestration

## 1. Gate identity

| Field | Value |
|---|---|
| Gate | `GC-DHI-05A` |
| Phase | Phase 4 — Diagnostic Rules |
| Backlog items | `RULE-01`, `RULE-02`, `RULE-03`, `RULE-04`, `RULE-05` |
| Baseline | `master` @ `4eb4f35113f751b6e10f697a3234de22dd364055` |
| Predecessor | `GC-DHI-04F` (`APPROVED AND CLOSED`) |
| Successor | `GC-DHI-05B` (CLI and reporting — not authorized here) |
| Document state | Definition only; no implementation authorized by this document |

This gate is the authorized next action recorded in `PROJECT_STATE.md` §24: *"Define the
next Phase 4 — Diagnostic Rules — gate and its authorization criteria."*

---

## 2. Functional objective

Transform an existing, already-captured `DatabaseSnapshot` into deterministic, immutable
database-health findings.

```text
DatabaseSnapshot
    ↓
DBH001 … DBH005 (pure rules)
    ↓
Findings
    ↓
deterministic order
    ↓
summary counts + overall risk
    ↓
InspectionResult
```

Phase 3 delivered metadata acquisition. This gate delivers **interpretation** — the layer
that makes the tool useful rather than merely safe.

---

## 3. User-visible contribution

After this gate the product can answer, for a real PostgreSQL database, without any new SQL
and without any CLI work:

- Which user tables have no primary key.
- Which tables have crossed a row or size threshold.
- Which indexes are exact structural duplicates of another index on the same table.
- Which sizeable non-unique indexes have recorded zero scans.
- Which indexes the engine has marked invalid.

…together with a summary count per severity and a single overall risk classification.

No console output, no JSON and no file writing are produced by this gate. The result is an
in-memory object consumed by `GC-DHI-05B`.

---

## 4. Existing contracts reused

Everything in this section **already exists** at the baseline commit and must be reused
unchanged. This gate introduces no second abstraction for any of it.

### 4.1 Reused as-is — no modification permitted

| Contract | File | Reuse |
|---|---|---|
| `IInspectionRule` | `src/DbHealthInspector.Core/Rules/IInspectionRule.cs` | The one and only rule abstraction. Five implementations. |
| `InspectionOrchestrator` | `src/…/Inspections/InspectionOrchestrator.cs` | Already implements capture, capability gating, isolation, cancellation, validation, ordering. |
| `InspectionResult` | `src/…/Inspections/InspectionResult.cs` | Immutable result; derives summary and risk in its constructor. |
| `InspectionSummary` | `src/…/Inspections/InspectionSummary.cs` | Counts derived by construction. |
| `OverallRisk` / `OverallRiskCalculator` | `src/…/Inspections/` | Risk matrix already frozen and implemented. |
| `InspectionRuleRegistration` | `src/…/Inspections/` | Carries `RequiredCapabilities` — the mechanism DBH004 uses. |
| `DiagnosticExecution*` | `src/…/Inspections/` | Per-rule status: `Completed` / `SkippedUnavailableCapability` / `Failed`. |
| `Finding`, `EvidenceItem`, `DatabaseObjectReference`, `DatabaseObjectType` | `src/…/Findings/` | Finding construction and evidence shape. |
| `FindingCodes` | `src/…/Findings/FindingCodes.cs` | `DBH001`–`DBH005` identities already declared. |
| `FindingSeverity`, `FindingConfidence`, `FindingCategory`, `RuleVersion` | `src/…/Findings/` | Classification vocabulary. |
| `FindingFingerprint*` | `src/…/Fingerprinting/` | Fingerprints are computed by `Finding` itself; rules never compute them. |
| Snapshot models | `src/…/Snapshots/` | `DatabaseSnapshot`, `TableSnapshot`, `IndexSnapshot`, `IndexKeyPartSnapshot`, `StatisticsSnapshot`, `CapabilitySnapshot`, `RelationKind`. |

### 4.2 What GC-DHI-05A actually adds

Only this:

1. Five `IInspectionRule` implementations in `src/DbHealthInspector.Core/Rules/`.
2. One immutable threshold-carrying options type for DBH002/DBH004 (§6.3, §8.3).
3. Their unit tests.
4. This document plus a rules design document.

**Nothing else.** No orchestration code, no risk code, no summary code, no ordering code —
all four already exist and already satisfy the requirements.

### 4.3 Rule contract obligations (already frozen by `IInspectionRule`)

Each rule must:

- Expose a stable `Code`, a `Version` (`RuleVersion.Initial` for a first implementation), a
  non-blank `Name` and a defined `Category`.
- Implement `Evaluate(DatabaseSnapshot) → IReadOnlyList<Finding>`, pure and deterministic.
- Perform **no** I/O — no file, console, network or database access.
- Hold no mutable state between calls.
- Take no cancellation token (evaluation is synchronous and in-memory by design).
- Have no knowledge of Npgsql, CLI formatting or JSON.

The orchestrator additionally **rejects** any rule output where a finding's `Code`,
`RuleVersion`, `Category` or `Engine` disagrees with its rule, or where fingerprints
collide. Every rule must therefore stamp each finding with its own identity and with
`snapshot.Metadata.Engine`.

### 4.4 Fingerprint stability obligation

`core-domain-contracts.md` §9.1 freezes: a finding's fingerprint must survive changes to
its *current size or row estimate* between two inspections of the same database.

Consequence, binding on every rule in this gate:

- Evidence identifying **what the object is** → `FingerprintParticipation.Include`.
- Evidence carrying a **fluctuating measurement** (size in bytes, estimated rows, scan
  count, statistics reset timestamp) → `FingerprintParticipation.Exclude`.

Violating this would make a table's DBH002 finding a *new* finding on every growth tick.

---

## 5. DBH001 — `TABLE_WITHOUT_PRIMARY_KEY`

| Property | Value | Source |
|---|---|---|
| Code | `FindingCodes.TableWithoutPrimaryKey` | existing |
| Category | `Structure` | this gate |
| Severity | `Warning` | **frozen** — `PROJECT_RULES.md` §5 |
| Confidence | `High` | **frozen** — `PROJECT_RULES.md` §5 |
| Required capabilities | none | catalog metadata is implied by a successful snapshot |
| Object reference | `DatabaseObjectType.Table`, schema, table | |

### 5.1 Trigger

For each `TableSnapshot` where `HasPrimaryKey == false` **and** the relation is in scope
per §5.2 → one finding.

`TableSnapshot.HasPrimaryKey` already exists and is populated by the frozen `D001`
statement. No PostgreSQL access occurs.

### 5.2 Relation-kind matrix (decision to freeze — §14 D-1)

| `RelationKind` | In scope | Rationale |
|---|:--:|---|
| `OrdinaryTable` | **yes** | The primary case. |
| `PartitionedTable` (root) | **yes** | PostgreSQL supports a primary key on a partitioned root, and it propagates to every partition. A root without one is a genuine finding. |
| `Partition` | no | Its key is defined at the root; reporting it would multiply one root defect across every partition. |
| `View` | no | Cannot have a primary key. |
| `MaterializedView` | no | Cannot have a primary key. |
| `ForeignTable` | no | Key enforcement is the remote server's concern. |
| `TemporaryTable` | no | Session-scoped; not a persistent structural risk. |
| `Unknown` | no | Never report on a relation the adapter could not classify. |

`IsPartition == true` is an additional independent exclusion, so a relation is excluded if
either its kind or its flag marks it a partition.

System schemas are already excluded by the frozen snapshot SQL; the rule adds no filtering
of its own.

### 5.3 Evidence

| Key | Participation | Notes |
|---|---|---|
| `schema` | Include | |
| `table` | Include | |
| `relation_kind` | Include | Distinguishes an ordinary table from a partitioned root. |
| `estimated_rows` | Exclude | Omitted entirely when `EstimatedRowCount` is null. |
| `total_size_bytes` | Exclude | Unit `bytes`. |

### 5.4 Recommendation

Non-destructive. States that a primary key should be defined, names no DDL to execute
automatically, and never proposes dropping or rewriting data.

---

## 6. DBH002 — `LARGE_TABLE`

| Property | Value | Source |
|---|---|---|
| Code | `FindingCodes.LargeTable` | existing |
| Category | `Capacity` | this gate |
| Severity | `Info` | **frozen** — `PROJECT_RULES.md` §5 |
| Confidence | `Medium` | **frozen** — `PROJECT_RULES.md` §5 |
| Required capabilities | none | |
| Object reference | `DatabaseObjectType.Table`, schema, table | |

### 6.1 Trigger

One finding per table that is in scope per §6.2 and where **either**:

- `EstimatedRowCount` is non-null **and** `EstimatedRowCount >= LargeTableRowThreshold`; **or**
- `TotalSizeBytes >= LargeTableSizeThresholdBytes`.

Both comparisons are **inclusive** (`>=`): a value exactly equal to its threshold fires.

`COUNT(*)` is never executed — the rule reads only snapshot fields.

A null `EstimatedRowCount` **never becomes zero**. It disables *only* the row criterion; the
size criterion still applies and can fire on its own.

### 6.2 Relation-kind matrix (decision to freeze — §14 D-2)

| `RelationKind` | In scope | Rationale |
|---|:--:|---|
| `OrdinaryTable` | **yes** | The primary case. |
| `Partition` | **yes** | A physical partition holds real storage and real rows; its own size is actionable on its own. |
| `PartitionedTable` (root) | no | See below. |
| `View` | no | Reports zero storage. |
| `MaterializedView` | no | Excluded from this gate. |
| `ForeignTable` | no | Storage lives on the remote server. |
| `TemporaryTable` | no | Session-scoped. |
| `Unknown` | no | Never report on a relation the adapter could not classify. |

**Why the partitioned root is excluded.** The existing PostgreSQL snapshot intentionally
reports *physical* relation sizes without descendant aggregation. A partitioned root's own
`TotalSizeBytes` is therefore its own (essentially empty) storage, **not** the logical
aggregate of its partitions. Presenting that figure as a "large table" signal would
misrepresent it in both directions: it would never fire for a genuinely huge partitioned
table, and if it did fire it would be reporting a number that does not mean what a reader
would assume. The partitions themselves carry the real storage and are individually in
scope, so no information is lost by excluding the root.

This is a deliberate divergence from the DBH001 matrix in §5.2: DBH001 asks a *structural*
question that belongs to the root, while DBH002 asks a *physical-storage* question that
belongs to the partitions.

### 6.3 Thresholds — decision to freeze (§14 D-3)

**No default threshold value exists anywhere in canonical repository material.** The CLI
flags `--large-table-row-threshold` and `--large-table-size-threshold-mb` are approved in
`PROJECT_RULES.md` §9, but their values are not recorded. This gate must therefore freeze
them.

| Threshold | Frozen default | Justification |
|---|---:|---|
| `LargeTableRowThreshold` | `1_000_000` rows | A round, widely-recognised operational inflection point. Below a million rows, "large" is rarely actionable on modern hardware. |
| `LargeTableSizeThresholdBytes` | `1_073_741_824` (1 GiB) | The point at which a single table stops fitting comfortably inside routine maintenance windows and buffer-cache assumptions. |

These are **product defaults, not database facts.** They do not describe any property of
PostgreSQL; they encode the tool's opinion about when size becomes worth a human's
attention. They are deliberately conservative — a false negative is cheap here, a noisy
`Info` finding on every small table is not.

**Carrier:** one immutable options record in Core, for example

```text
DiagnosticThresholds(
    long largeTableRowThreshold,
    long largeTableSizeThresholdBytes,
    long unusedIndexSizeThresholdBytes)
```

with a `Default` static instance. DBH002 and DBH004 receive it through their constructors; a
parameterless construction path uses `Default`.

**Validation invariant — frozen.** The constructor enforces positivity and nothing else:

```text
LargeTableRowThreshold        > 0
LargeTableSizeThresholdBytes  > 0
UnusedIndexSizeThresholdBytes > 0
```

Zero and negative values are rejected. There is **no** minimum, maximum, range, preset,
profile or relative-ordering policy beyond positivity. A caller may set any positive value,
however large or small.

**Frozen defaults:**

| Threshold | Default |
|---|---:|
| `LargeTableRowThreshold` | `1_000_000` |
| `LargeTableSizeThresholdBytes` | `1_073_741_824` |
| `UnusedIndexSizeThresholdBytes` | `10_485_760` |

All threshold comparisons throughout this gate are **inclusive**: `value >= threshold`.

This is **not** a configuration subsystem: no provider, no binding, no file reading, no
environment reading, no DI. It is one validated value object. `GC-DHI-05B` will map the
three approved CLI flags onto it; that mapping is **out of scope here**.

### 6.4 Evidence

| Key | Participation | Notes |
|---|---|---|
| `schema` | Include | |
| `table` | Include | |
| `exceeded_threshold` | Include | `rows`, `size`, or `rows_and_size` — states which threshold was exceeded, as `RULE-02` requires. |
| `estimated_rows` | Exclude | Omitted when null. |
| `row_threshold` | Exclude | Present when the row test participated. |
| `total_size_bytes` | Exclude | Unit `bytes`. |
| `size_threshold_bytes` | Exclude | Unit `bytes`. |

`exceeded_threshold` is `Include` because *which kind of largeness* is part of the
finding's identity; the measurements themselves are `Exclude` so the finding survives
growth.

---

## 7. DBH003 — `EXACT_DUPLICATE_INDEX`

| Property | Value | Source |
|---|---|---|
| Code | `FindingCodes.ExactDuplicateIndex` | existing |
| Category | `Indexing` | this gate |
| Severity | `Warning` | **frozen** — `PROJECT_RULES.md` §5 |
| Confidence | `High` | **frozen** — `PROJECT_RULES.md` §5 |
| Required capabilities | none | |
| Object reference | `DatabaseObjectType.Index`, schema, **group anchor** index, parent = table | |

### 7.1 Critical prerequisite — `IndexSnapshot.Equals` must NOT be used

`IndexSnapshot` overrides `Equals` to compare `IndexName`, `SizeBytes` **and** `ScanCount`
in addition to structure. That is *identity* equality and is correct for its own purpose —
but two exact duplicates necessarily have different names and almost always different
sizes, so `IndexSnapshot.Equals` returns `false` for precisely the case DBH003 must detect.

DBH003 must therefore build its **own structural key**, internal to the rule. It must not
modify, weaken or extend `IndexSnapshot.Equals`, and must not redesign `IndexSnapshot`.

### 7.2 Structural key — included dimensions

Two indexes are exact structural duplicates when they share the same `SchemaName` **and**
`TableName` and all of the following are equal:

| Dimension | Snapshot source | Available |
|---|---|:--:|
| Access method | `AccessMethod` | yes |
| Ordered key parts | `KeyParts`, compared **in order** | yes |
| — key column identity | `IndexKeyPartSnapshot.ColumnName` | yes |
| — key expression | `IndexKeyPartSnapshot.Expression` | yes |
| — collation | `IndexKeyPartSnapshot.Collation` | yes |
| — operator class **incl. encoded options** | `IndexKeyPartSnapshot.OperatorClass` | yes — the 04E encoding embeds `\|options[…]` |
| — sort direction | `IndexKeyPartSnapshot.SortDirection` | yes |
| — nulls ordering | `IndexKeyPartSnapshot.NullsOrdering` | yes |
| INCLUDE columns **and order** | `IncludedColumns`, compared in order | yes |
| Partial predicate | `PartialPredicate` | yes |
| Uniqueness | `IsUnique` | yes |
| Nulls-not-distinct | `NullsNotDistinct` | yes |

**No functional blocker exists.** Every dimension `RULE-03` requires is already present in
`IndexSnapshot`. This gate adds **no** PostgreSQL metadata and **no** SQL.

`IndexKeyPartSnapshot` is a plain record, so ordered element-wise value equality is
available directly.

### 7.3 Structural key — excluded dimensions

`IndexName`, `SizeBytes`, `ScanCount`, `IsValid`, `IsReady`, `IsLive`, `IsPrimaryKey` and
`BacksConstraint` are **excluded** from the structural comparison. Two indexes with
identical structure are duplicates regardless of which one happens to back a constraint;
that asymmetry belongs in the recommendation, not in the detection.

### 7.4 Must NOT be classified as duplicates

- **Prefix indexes.** `(a)` vs `(a, b)` — different key-part counts, never equal. Ordered
  comparison makes this structural, not a special case.
- **Reordered keys.** `(a, b)` vs `(b, a)` — order participates.
- **Different INCLUDE sets or INCLUDE order.**
- **Different predicates**, including one partial and one total.
- **Indexes on different tables**, even with identical structure — grouping is per
  `(SchemaName, TableName)`.

### 7.5 Finding shape (decision to freeze — §14 D-6)

One finding per **duplicate group** (two or more structurally equal indexes on one table),
not one per pair — a three-way duplicate produces one finding, not three.

- **Anchor:** the ordinally-first `IndexName` in the group becomes the `ObjectReference`
  object name; the table is the `ParentObjectName` (required for `DatabaseObjectType.Index`).
- Two disjoint groups on the same table therefore have different anchors and different
  fingerprints.

### 7.6 Evidence

| Key | Participation | Notes |
|---|---|---|
| `schema` | Include | |
| `table` | Include | |
| `duplicate_indexes` | Include | All member index names, ordinally sorted, comma-joined. Identifies **all** index identities, as `RULE-03` requires. |
| `duplicate_count` | Include | Group size. |
| `access_method` | Include | |
| `index_sizes_bytes` | Exclude | Per-index sizes in the same order as `duplicate_indexes`, satisfying `RULE-03`'s "reports both index identities and sizes". Excluded from the fingerprint because sizes fluctuate. |

### 7.7 Recommendation

Non-destructive. States that one of the duplicates is redundant and that a human should
confirm which to retain — explicitly noting that an index backing a constraint must not be
dropped directly. The rule never instructs automatic dropping.

---

## 8. DBH004 — `UNUSED_INDEX_CANDIDATE`

| Property | Value | Source |
|---|---|---|
| Code | `FindingCodes.UnusedIndexCandidate` | existing |
| Category | `Statistics` | this gate |
| Severity | `Info` | **frozen** — `PROJECT_RULES.md` §5 |
| Confidence | `Low` or `Medium` | **frozen** — `PROJECT_RULES.md` §5 (`Low/Medium`); selector in §8.4 |
| Required capabilities | **`CapabilityKind.UsageStatistics`** | declared on `InspectionRuleRegistration` |
| Object reference | `DatabaseObjectType.Index`, schema, index, parent = table | |

### 8.1 Statistics availability — handled by registration, not by the rule

DBH004 is the **only** rule registered with a required capability. When `UsageStatistics`
is not `Available`, the existing orchestrator records
`DiagnosticExecutionStatus.SkippedUnavailableCapability` and **never calls the rule**.

This is the structural guarantee that *absence of statistics is never read as zero*: the
rule cannot produce a false positive from missing data because it does not run at all.

### 8.2 Trigger

DBH004 is deliberately **conservative**. A finding is produced only when *every* condition
below holds:

```text
UsageStatistics capability is Available     (enforced by registration, §8.1)
AND ScanCount            == 0
AND SizeBytes            >= UnusedIndexSizeThresholdBytes
AND IsPrimaryKey         == false
AND IsUnique             == false
AND BacksConstraint      == false
AND IsValid              == true
AND IsReady              == true
AND IsLive               == true
```

Every exclusion, stated individually and frozen:

| Condition | Result |
|---|---|
| `ScanCount == null` | **no finding** |
| `ScanCount > 0` | **no finding** |
| `SizeBytes < UnusedIndexSizeThresholdBytes` | **no finding** |
| `IsPrimaryKey == true` | **no finding** |
| `IsUnique == true` | **no finding** |
| `BacksConstraint == true` | **no finding** |
| `IsValid == false` | **no finding** |
| `IsReady == false` | **no finding** |
| `IsLive == false` | **no finding** |
| `UsageStatistics` not `Available` | rule never runs; orchestrator records `SkippedUnavailableCapability` |

A null `ScanCount` is *unknown*, never zero — excluded even inside the capability-available
branch, as a second, independent line of defence beyond §8.1.

**Why `BacksConstraint == false` is required.** A PostgreSQL index may support an exclusion
constraint — or another constraint — without being either a primary-key index or a unique
index, so criteria 4 and 5 do not cover it. The existing fixture `indexed_orders_span_excl`
is exactly that shape: `IsUnique == false`, `IsPrimaryKey == false`, `BacksConstraint ==
true`. Presenting such an index as an unused-index candidate would be functionally
misleading, because the index cannot be removed without destroying the constraint it
enforces.

**Why `IsReady == true` is required.** An index in an abnormal or transitional readiness
state must not be presented as an ordinary unused-index candidate. Its scan history is not
comparable to that of a fully ready index, so a zero count means something different there.

Neither requirement needs additional metadata: `BacksConstraint`, `IsValid`, `IsReady` and
`IsLive` all already exist on `IndexSnapshot`.

### 8.3 Minimum-size threshold — decision to freeze (§14 D-3)

**Not found in canonical material.** `--unused-index-size-threshold-mb` is an approved CLI
flag; no value is recorded anywhere.

| Threshold | Frozen default | Justification |
|---|---:|---|
| `UnusedIndexSizeThresholdBytes` | `10_485_760` (10 MiB) | Below roughly ten mebibytes an unused index costs little enough that flagging it is noise rather than signal. A product default, not a database fact. |

Carried by the same `DiagnosticThresholds` value object as §6.3. No separate mechanism.

### 8.4 Confidence selection (decision to freeze — §14 D-5)

`PROJECT_RULES.md` records `Low/Medium`; the choice between them must reflect statistics
context, per `RULE-04`:

| Condition | Confidence |
|---|---|
| `StatisticsResetAtUtc` is **null** | `Low` — the observation window is of unknown length, so zero scans proves little. |
| `StatisticsResetAtUtc` is **non-null** | `Medium` — a known reset point makes zero scans meaningful, though still not proof the index is unneeded. |

Never `High`: a zero scan count is never direct evidence that an index is unnecessary.

**The selector is presence-only.** The rule tests whether `StatisticsResetAtUtc` is null and
nothing more. It must **not** compute elapsed time, must **not** read the system clock, and
must **not** call `DateTime.UtcNow`, `DateTimeOffset.UtcNow` or any equivalent. Doing so
would make the rule impure and non-deterministic, breaking the `IInspectionRule` contract in
§4.3 and making the same snapshot yield different findings on different runs.

### 8.5 Evidence

| Key | Participation | Notes |
|---|---|---|
| `schema` | Include | |
| `table` | Include | |
| `index` | Include | |
| `access_method` | Include | |
| `scan_count` | Exclude | Always `0` when the finding fires. |
| `index_size_bytes` | Exclude | Unit `bytes`. |
| `size_threshold_bytes` | Exclude | Unit `bytes`. |
| `statistics_reset_at_utc` | Exclude | **Omitted entirely when null.** Present as a round-trip UTC timestamp otherwise — `RULE-04`'s "includes statistics reset evidence when available". |

### 8.6 Recommendation

Must **explicitly** state that the index must not be dropped automatically, and that a human
should confirm the index is genuinely unused across a representative workload window before
acting. This is a mandatory acceptance criterion of `RULE-04`, not a stylistic preference.

---

## 9. DBH005 — `INVALID_INDEX`

| Property | Value | Source |
|---|---|---|
| Code | `FindingCodes.InvalidIndex` | existing |
| Category | `Indexing` | this gate |
| Severity | `Critical` | **frozen** — `PROJECT_RULES.md` §5 |
| Confidence | `High` | **frozen** — `PROJECT_RULES.md` §5 |
| Required capabilities | none | validity is catalog metadata, not statistics |
| Object reference | `DatabaseObjectType.Index`, schema, index, parent = table | |

### 9.1 Trigger

One finding per `IndexSnapshot` where `IsValid == false`.

`IsReady` and `IsLive` are **captured as evidence**, not used as filters — an index may be
invalid while still ready and live. The frozen, empirically verified triple for
`CREATE INDEX … ON ONLY <partitioned table>` on both pinned majors is
`IsValid=false, IsReady=true, IsLive=true`. Filtering on readiness would silently hide real
invalid indexes.

### 9.2 Evidence

| Key | Participation | Notes |
|---|---|---|
| `schema` | Include | |
| `table` | Include | |
| `index` | Include | |
| `is_valid` | Include | Always `false`. |
| `is_ready` | Include | Readiness state, per `RULE-05`. |
| `is_live` | Include | Liveness state, per `RULE-05`. |
| `access_method` | Include | |
| `index_size_bytes` | Exclude | Unit `bytes`. |

The three state flags are `Include` because they describe *which kind of invalidity* this
is — a stable property of the condition, not a fluctuating measurement.

### 9.3 Recommendation

Non-destructive. Describes that the index is not usable by the planner and that a rebuild
should be performed under human supervision. Emits no DDL and never proposes an automatic
drop.

---

## 10. Orchestration semantics

**Already implemented in full. This gate writes no orchestration code.**

`InspectionOrchestrator` at the baseline already provides, and must be reused unchanged:

| Requirement | Existing behavior |
|---|---|
| Capture exactly one snapshot | `_snapshotProvider.CaptureAsync` invoked once. |
| Reject a null snapshot | Throws `InvalidOperationException`. |
| Rule execution order | Registrations ordered by `Code.Value`, ordinal. |
| Capability gating | Unavailable required capability → `SkippedUnavailableCapability`; rule not invoked. |
| Failure isolation | Recoverable rule exception → `Failed` execution; other rules continue. |
| Process-fatal exceptions | `OutOfMemoryException`, `StackOverflowException`, `AccessViolationException` propagate. |
| Cancellation | Checked before capture, after capture, and before and after every rule; propagates with no partial result. |
| Output validation | Rejects null lists, mismatched `Code`/`RuleVersion`/`Category`/`Engine`, and duplicate fingerprints within and across rules. |
| Immutability | `InspectionResult` copies defensively. |
| Summary and risk | Derived in `InspectionResult`'s constructor. |

The only orchestration-adjacent work in this gate is **composing the five registrations**:

| Rule | Required capabilities |
|---|---|
| DBH001 | none |
| DBH002 | none |
| DBH003 | none |
| DBH004 | `CapabilityKind.UsageStatistics` |
| DBH005 | none |

Whether that composition lives in Core as a small helper or is assembled by the caller is an
implementation choice for the gate, provided it introduces no DI container, no plugin
mechanism and no configuration framework.

---

## 11. Overall-risk semantics

**Already frozen and already implemented. Not re-derived by this gate.**

`PROJECT_RULES.md` §11:

```text
High   = at least one Critical finding
Medium = at least one Warning and no Critical findings
Low    = only Info findings
None   = no findings
```

`OverallRiskCalculator.Calculate` implements exactly this, considering `FindingSeverity`
only — never confidence, weighting, counts or sizes. `InspectionResult` calls it.

No scoring engine, no weighting and no probabilistic model is permitted.

Given the frozen severities, the practical consequence is: **any DBH005 finding forces
`High`; any DBH001 or DBH003 finding forces at least `Medium`; DBH002 and DBH004 alone
yield `Low`.**

---

## 12. Deterministic ordering

**Already frozen and already implemented.**

`InspectionOrchestrator` produces:

- **Findings:** ordered by `Code.Value` (ordinal), then by `Fingerprint.Value` (ordinal).
- **Diagnostic executions:** ordered by `Code.Value` (ordinal).
- **Within one rule:** findings ordered by fingerprint before acceptance.

A rule therefore does **not** need to sort its own output for global determinism, and must
not implement any competing ordering. It must only guarantee that the *set* it returns is
deterministic for a given snapshot — which follows from purity plus a deterministic
traversal of the snapshot's already-ordered collections.

No sorting framework, no comparer registry and no configurable ordering is permitted.

---

## 13. Optional and missing-statistics semantics

The governing principle: **a missing optional statistic must never become a false
positive.**

| Condition | Required behavior |
|---|---|
| Healthy snapshot, zero findings | `InspectionResult` with empty `Findings`, `TotalFindings == 0`, `OverallRisk.None`, five `Completed` executions (or four plus one `Skipped`). Not an error. |
| Zero user tables | Every rule returns an empty list. Same as above. No rule may throw on an empty snapshot. |
| `UsageStatistics` unavailable | DBH004 is skipped by the orchestrator: one `SkippedDiagnostics`, no DBH004 findings. The other four rules run normally. |
| `EstimatedRowCount` null | DBH002's **row** test is skipped; the **size** test still applies. Null is never coerced to `0`. The `estimated_rows` evidence key is omitted. |
| `ScanCount` null | DBH004 never fires. Null means unknown, never zero. |
| `StatisticsResetAtUtc` null | DBH004 may still fire (if it ran at all), but with `Low` confidence, and the timestamp evidence key is omitted. |
| `PartialPredicate` null | Compared as null in DBH003 — a total index and a partial index are not duplicates. |
| `NullsNotDistinct` null | Compared as null; participates in the structural key. |

Under no circumstances may a rule substitute a default value for a null optional statistic
in order to make a comparison succeed.

---

## 14. Decisions this gate must freeze

Recorded explicitly because none of them exist in canonical repository material.

| ID | Decision | Value | Why it is needed |
|---|---|---|---|
| D-1 | DBH001 relation-kind matrix | §5.2 | `RULE-01` says "according to the approved rule"; no such rule is recorded anywhere. |
| D-2 | DBH002 relation-kind matrix (root excluded, partition included) | §6.2 | Not recorded. |
| D-3 | Three threshold defaults | 1,000,000 rows / 1 GiB / 10 MiB | CLI flags approved; values never recorded. |
| D-4 | DBH004 is conservative: additionally requires `BacksConstraint == false` and `IsReady == true` | §8.2 | `RULE-04` lists only the PK/unique exclusions, but a non-unique constraint-backing index (`indexed_orders_span_excl`) would otherwise be reported as removable, which is functionally misleading. |
| D-5 | DBH004 `Low` vs `Medium` selector | §8.4 | `PROJECT_RULES.md` records the pair, not the selector. |
| D-6 | DBH003 emits one finding per group, anchored ordinally-first | §7.5 | Not recorded. |
| D-7 | Rule → `FindingCategory` assignment | Structure / Capacity / Indexing / Statistics / Indexing | Not recorded. |

All seven are minimal, deterministic and free of new infrastructure. Each is open to the
reviewer's revision; none may be silently changed during implementation.

---

## 15. Minimum test matrix

Core unit tests only. The rules are pure, so **no PostgreSQL fixture may be added to test
them**, and no new SQL may be introduced for testing.

### DBH001
- Ordinary table without PK → one finding.
- Ordinary table with PK → none.
- Partitioned root without PK → one finding.
- Physical partition without PK → none.
- View / materialized view / foreign table / temporary / unknown → none.
- Evidence keys and participation correct; `estimated_rows` omitted when null.

### DBH002
- Below both thresholds → none.
- Row threshold exceeded → one finding, `exceeded_threshold = rows`.
- Size threshold exceeded → one finding, `exceeded_threshold = size`.
- Both exceeded → one finding, `exceeded_threshold = rows_and_size`.
- Null `EstimatedRowCount` below size threshold → none (null never treated as large).
- Null `EstimatedRowCount` above size threshold → one finding via size alone.
- Boundary: a value exactly equal to the threshold fires (`>=`), for both row and size.
- **Partitioned root above the size threshold → none** (root excluded, §6.2).
- **Physical partition above the size threshold → one finding** (partition in scope).
- View / materialized view / foreign table / temporary / unknown → none.

### DBH003
- Two structurally identical indexes → one finding naming both.
- Three identical → **one** finding, `duplicate_count = 3`.
- Prefix `(a)` vs `(a,b)` → none.
- Reordered `(a,b)` vs `(b,a)` → none.
- INCLUDE difference, in both set and order → none.
- Predicate difference, including partial vs total → none.
- Collation difference → none.
- Operator-class difference, **including differing encoded options** → none.
- Access-method difference → none.
- Uniqueness difference → none.
- Identical structure on **different tables** → none.
- Two disjoint duplicate groups on one table → two findings with distinct fingerprints.
- Explicit test that `IndexSnapshot.Equals` returning `false` does not suppress detection.

### DBH004

**Positive** — one case, every condition satisfied simultaneously: `ScanCount == 0`,
`SizeBytes` **exactly equal** to the threshold, not a primary key, not unique, not backing a
constraint, valid, ready and live → one finding. This doubles as the inclusive-boundary
test.

**Negative** — one case each, varying exactly one condition away from the positive case:

- `ScanCount` null → none *(the false-positive guard)*.
- `ScanCount > 0` → none.
- `SizeBytes` one byte below the threshold → none.
- `IsPrimaryKey == true` → none.
- `IsUnique == true` → none.
- `BacksConstraint == true` → none.
- `IsValid == false` → none.
- `IsReady == false` → none.
- `IsLive == false` → none.
- `UsageStatistics` unavailable → rule never invoked; the orchestrator records
  `SkippedUnavailableCapability` (asserted at composition level, §"Orchestration").

**Confidence:**

- `StatisticsResetAtUtc` present → `Medium`, timestamp evidence key present.
- `StatisticsResetAtUtc` null → `Low`, timestamp evidence key absent.

**Purity:** the rule reads no clock — asserted by the same snapshot producing identical
findings across repeated evaluations, and by the absence of any `UtcNow` call path.

### DiagnosticThresholds value object

- Defaults are exactly `1_000_000` / `1_073_741_824` / `10_485_760`.
- Zero row threshold rejected; negative row threshold rejected.
- Zero size threshold rejected; negative size threshold rejected.
- Zero unused-index threshold rejected; negative unused-index threshold rejected.

Positivity is the only invariant; no range, ordering or preset test is authorized.

### DBH005
- Invalid index → one finding.
- Valid index → none.
- Readiness and liveness captured as evidence for the `false/true/true` triple.
- Invalid **and** not ready **and** not live → still one finding (no filtering).

### Orchestration (composition level)
- All five rules registered and executed against one snapshot.
- Findings from every rule aggregate into one result.
- Deterministic order: `Code` then `Fingerprint`, ordinal — asserted, not assumed.
- Summary counts correct per severity, and `Total == Info + Warning + Critical`.
- Overall risk correct for: none / Info-only / Warning present / Critical present.
- Healthy snapshot → zero findings, `OverallRisk.None`, not an error.
- `UsageStatistics` unavailable → DBH004 skipped, `SkippedDiagnostics == 1`, other four complete.
- Cancellation propagates, as the existing orchestrator contract requires.
- **The §17 success-example scenario asserted end to end.**

Combinatorial matrices beyond these are not authorized unless they expose a genuine
functional ambiguity.

---

## 16. Hard out-of-scope

The following are **excluded** from GC-DHI-05A. Each is `DEFERRED — NOT REQUIRED FOR
FUNCTIONAL MVP`.

CLI command implementation · System.CommandLine changes · connection-string resolution ·
console formatting · JSON output · JSON Schema · file writing · report persistence · HTML ·
dashboard or UI · API · additional database engines · **new PostgreSQL SQL** · modification
of any frozen statement (`B001`–`B003`, `C001`–`C004`, `D001`, `E001`–`E002`) · PostgreSQL
session, transaction or schema-filtering changes · a second snapshot path · reopening
`GC-DHI-04A`–`04F` · new DBH rules (`DBH006`+) · query-performance analysis · `EXPLAIN` ·
`pg_stat_statements` · automatic repair · DDL · DML · index dropping · schema migrations ·
telemetry · logging-framework changes · DI framework · plugin framework · caching ·
benchmarking · unrelated performance optimization · provenance changes · CI redesign ·
unrelated dependency upgrades · documentation beautification · refactoring for elegance.

Additionally forbidden by this gate specifically:

- Introducing a second rule abstraction alongside `IInspectionRule`.
- Reimplementing orchestration, summary, risk or ordering — all four already exist.
- Modifying `IndexSnapshot.Equals` or `GetHashCode` to serve DBH003.
- Building a configuration subsystem for the three thresholds.

---

## 17. Functional acceptance criteria

GC-DHI-05A is complete when code can take an existing valid `DatabaseSnapshot` and produce
an immutable, deterministic `InspectionResult` containing correct DBH001–DBH005 findings,
summary counts and overall risk — with **no CLI output required**.

### Success example (must be expressible as a test)

Given one `DatabaseSnapshot` containing:

- one ordinary table without a primary key;
- one table exceeding a large-table threshold;
- two exact duplicate indexes on one table;
- one sufficiently large, zero-scan, non-unique, valid index;
- one invalid index;

with `UsageStatistics` available, the diagnostic layer produces:

| Expectation | Value |
|---|---|
| DBH001 findings | 1 |
| DBH002 findings | 1 |
| DBH003 findings | 1 (one group naming both indexes) |
| DBH004 findings | 1 |
| DBH005 findings | 1 |
| `TotalFindings` | 5 |
| `InfoFindings` | 2 (DBH002, DBH004) |
| `WarningFindings` | 2 (DBH001, DBH003) |
| `CriticalFindings` | 1 (DBH005) |
| `TotalDiagnostics` | 5, all `Completed` |
| `OverallRisk` | `High` (a Critical finding is present) |
| Finding order | DBH001, DBH002, DBH003, DBH004, DBH005 |
| `HasErrors` | `false` |

### Additional criteria

1. Zero new PostgreSQL SQL; the inventory remains exactly ten statements.
2. `IInspectionRule` is the only rule abstraction.
3. Every rule is unit-testable with no database connection.
4. No rule references Npgsql, CLI or JSON types.
5. Release build: 0 warnings, 0 errors; `dotnet format` passes.
6. All existing tests continue to pass, unchanged.
7. Both frozen `PROJECT_RULES.md` tables — severity/confidence (§5) and risk (§11) — are
   honored exactly.

---

## 18. Completion definition

GC-DHI-05A is `COMPLETE` when **all** of the following hold:

1. Five `IInspectionRule` implementations exist for DBH001–DBH005 and satisfy §5–§9.
2. The `DiagnosticThresholds` value object exists with the §6.3 and §8.3 defaults and
   constructor validation.
3. The five registrations compose, with `UsageStatistics` required only by DBH004.
4. The §15 test matrix passes in full, including the §17 success example.
5. No production file outside `src/DbHealthInspector.Core/Rules/` and the single thresholds
   type is modified — in particular, nothing under `src/DbHealthInspector.PostgreSql/`.
6. The frozen SQL inventory is byte-identical: `B001`–`B003`, `C001`–`C004`, `D001`,
   `E001`–`E002`.
7. Exported PostgreSQL types remain exactly two (`AssemblyMarker`,
   `PostgreSqlDatabaseSnapshotProvider`).
8. A rules design document is added under `docs/design/` recording the seven §14 decisions.
9. Quality gate passes: 0 warnings, 0 errors, format PASS, 0 vulnerable, 0 deprecated.
10. The seven §14 decisions are reviewed and accepted, or replaced with reviewer-supplied
    values, before implementation begins.

Completion of GC-DHI-05A authorizes **no** release, tag, NuGet publication or CLI work. The
recommended successor is `GC-DHI-05B` — CLI and reporting.
