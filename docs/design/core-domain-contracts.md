# Core Domain Contracts and Fingerprinting

**Gate:** GC-DHI-03A — Core Domain Contracts and Fingerprinting
**Scope:** `DbHealthInspector.Core` findings, fingerprinting, snapshots and rule contract.
**Status:** Implemented; corrected per Codex reviews GC-DHI-03A-R1 (GC-DHI-03A-C1) and
GC-DHI-03A-R2 (GC-DHI-03A-C2); pending final Codex review.

This document describes the domain model added in GC-DHI-03A. It does not describe
CORE-04 (orchestration), DBH001–DBH005 rule implementations, PostgreSQL integration or
reporting — those remain out of scope per `AGENTS.md` and are listed under
[Deferred to later gates](#deferred-to-later-gates).

## 0. Corrections applied

### GC-DHI-03A-C1 (Codex review R1, seven findings)

- **C1** — Optional strings across the whole model now reject empty/whitespace-only values
  (they were previously accepted by some types). See [§3](#3-invariants) and
  [§11](#11-limitations).
- **C2** — `Finding` now stores `Engine` as a public property instead of taking it only as a
  discarded constructor parameter. See [§9.2](#92-design-finding-owns-its-fingerprint-inputs).
- **C3** — Every public collection now rejects null elements, and rejects duplicate keys where
  the underlying real-world object requires uniqueness. See [§3](#3-invariants).
- **C4** — `IndexSnapshot` now also requires `BacksConstraint` when `IsPrimaryKey` is `true`;
  `CapabilityKind.DataProfiling` documentation no longer asserts a Core-level policy that it
  must be `Disabled`. See [§6](#6-snapshot-model) and [§7](#7-capability-reporting).
- **C5** — `IndexSnapshot` now has hand-written, order-sensitive structural equality. See
  [§6](#6-snapshot-model).
- **C6/C7** — Test-only corrections (a neutral rule fake, exact exception-type assertions);
  no production-code impact, not described further in this document.

### GC-DHI-03A-C2 (Codex review R2, three findings)

- **DHI-R2-001** — Every collection-returning `Guard` helper, and `CapabilitySnapshot.States`,
  now wraps its defensive copy with `Array.AsReadOnly`, so casting an exposed collection to
  `IList<T>`/`ICollection<T>` and calling `Add`, index-assigning, `Insert`, `Remove`,
  `RemoveAt` or `Clear` all throw `NotSupportedException`. Previously the copy was a plain
  array, which already rejected `Add`/`Remove`/`Insert` (fixed size) but **not** index
  assignment (`list[0] = x`), silently allowing evidence, key parts or included columns to be
  swapped out after a `Finding`'s or `IndexSnapshot`'s identity had already been computed from
  them. See [§3](#3-invariants).
- **DHI-R2-002** — `CapabilityState.Reason`'s policy was corrected: `Available` now requires
  `Reason` to be exactly `null` (a non-null reason, blank or not, throws); `Unavailable`/
  `Disabled` now allow `Reason` to be `null` (previously it was required, i.e. rejected `null`
  with `ArgumentNullException`) in addition to a non-blank value, while still rejecting a
  blank one. See [§7](#7-capability-reporting).
- **DHI-R2-003** — `DatabaseObjectReference`'s constructor now throws
  `ArgumentNullException` (not `ArgumentException`) when `ObjectType` is `Index` and
  `ParentObjectName` is `null`, so a caller can distinguish "missing" from "blank" the same
  way every other required-in-context field does. Blank (`""`/whitespace) for an `Index`
  parent, and blank for any optional parent, remain `ArgumentException`. See
  [§3](#3-invariants).

## 1. Purpose

`DbHealthInspector.Core` defines the engine-neutral vocabulary the rest of the product is
built on: what a finding is, what a database snapshot looks like, and how a rule turns one
into the other. It depends on nothing but the .NET base class library (see ADR-0003) so that
diagnostic logic can be unit-tested without a PostgreSQL connection, and so a future second
engine adapter can reuse the same model (ADR-0001).

## 2. Relationship diagram (textual)

```text
IInspectionRule.Evaluate(DatabaseSnapshot) -> IReadOnlyList<Finding>

DatabaseSnapshot
├── DatabaseMetadata      (Engine, EngineVersion, DatabaseName, CurrentUser)
├── SchemaSnapshot[]
├── TableSnapshot[]
├── IndexSnapshot[]
│   └── IndexKeyPartSnapshot[]   (ordered, one per key column/expression)
├── CapabilitySnapshot    (one CapabilityState per CapabilityKind)
└── StatisticsSnapshot    (server-wide statistics reset timestamp)

Finding
├── FindingCode, RuleVersion, FindingCategory, FindingSeverity, FindingConfidence
├── DatabaseObjectReference  (ObjectType, SchemaName, ObjectName, ParentObjectName)
├── Message, Recommendation, DocumentationReference
├── EvidenceItem[]           (Key, Value, Unit, FingerprintParticipation)
└── FindingFingerprint       (derived, not settable)

FindingFingerprintInput (Engine, FindingCode, DatabaseObjectReference, EvidenceItem[])
        -> FindingFingerprintGenerator.Generate(...) -> FindingFingerprint
```

`DatabaseEngine` and `Guard` live at the `DbHealthInspector.Core` root namespace, not under
`Snapshots/`, because both `Findings` and `Fingerprinting` need `DatabaseEngine` too; placing
it under `Snapshots/` would have made `Findings` depend on `Snapshots`, which is backwards
(a finding is not a kind of snapshot).

## 3. Invariants

- Every public value type validates its own arguments at construction time; there is no way
  to obtain a partially-valid instance through the public API.
- Every collection accepted by a constructor is copied defensively (`Guard
  .CopyDefensively` / `CopyDefensivelyRejectingNullElements` /
  `CopyDefensivelyRejectingBlankElements`, plus `CapabilitySnapshot.States`, which builds its
  own copy inline). Every one of these wraps its array copy with `Array.AsReadOnly` before
  returning it as `IReadOnlyList<T>`, so the result is genuinely non-modifiable: casting it to
  `IList<T>`/`ICollection<T>` and calling `Add`, `Insert`, `Remove`, `RemoveAt`, `Clear`, or
  assigning through the indexer (`list[0] = x`) all throw `NotSupportedException`. A plain
  array would already reject the first group (fixed size) but silently *allow* index
  assignment, which would let a caller swap out evidence or key parts after a `Finding`'s or
  `IndexSnapshot`'s identity had already been computed from them — this is exactly the gap
  Codex review GC-DHI-03A-R2 (DHI-R2-001) found and this correction closes. Mutating the
  caller's original source collection after construction never changes the constructed object
  either, since the array underlying the read-only wrapper is never the caller's own array.
- **Optional strings follow one rule everywhere**: `null` means "absent" and is always
  allowed; an empty string or a whitespace-only string is never a valid value for an optional
  field and always throws `ArgumentException` — it is rejected the same way a blank value in a
  *required* field is rejected, the only difference being that `null` is additionally allowed.
  Values are never trimmed and empty is never silently normalized to `null`; whatever a caller
  passes is either rejected or stored exactly as given. This applies to
  `DatabaseObjectReference.SchemaName`, `EvidenceItem.Unit`, `DatabaseMetadata.CurrentUser`,
  `IndexKeyPartSnapshot.ColumnName`, `IndexKeyPartSnapshot.Expression`,
  `IndexKeyPartSnapshot.Collation`, `IndexKeyPartSnapshot.OperatorClass` and
  `IndexSnapshot.PartialPredicate`. `Guard.AgainstEmptyOrWhiteSpace` implements this rule once,
  centrally. Two fields deliberately deviate from the plain "optional string" rule because they
  encode more than presence/absence, and both changed shape in GC-DHI-03A-C2:
  `DatabaseObjectReference.ParentObjectName` is required (non-`null`) specifically when
  `ObjectType` is `Index` — and a `null` value there throws `ArgumentNullException`, not
  `ArgumentException`, distinguishing "missing" from "blank" (DHI-R2-003) — while remaining a
  plain optional string for every other object type. `CapabilityState.Reason` follows its own,
  status-dependent policy described in [§7](#7-capability-reporting) (DHI-R2-002), not the
  generic optional-string rule.
- **Every public collection rejects a `null` element**, and every public collection whose
  elements have a real-world uniqueness constraint rejects duplicates of that key, using
  ordinal string comparison: `Finding.Evidence` (unique `EvidenceItem.Key`),
  `DatabaseSnapshot.Schemas` (unique `SchemaName`), `DatabaseSnapshot.Tables` (unique
  `(SchemaName, TableName)`), `DatabaseSnapshot.Indexes` (unique `(SchemaName, IndexName)`,
  matching PostgreSQL's per-schema index-name uniqueness), `CapabilitySnapshot` (unique
  `CapabilityKind`, already enforced before this correction), `IndexSnapshot.KeyParts`
  (unique `Position`, already enforced before this correction) and
  `IndexSnapshot.IncludedColumns` (unique column name, and — being a collection of strings —
  also rejects blank elements). Order is never silently changed: `KeyParts` and
  `IncludedColumns` keep exactly the order the caller supplied, because that order is part of
  what a rule and (for `IncludedColumns`) equality observe; only `EvidenceItem`s marked
  `Include` are sorted, and only inside fingerprint computation (§9.5), never in
  `Finding.Evidence` itself.
- `FindingCode`, `RuleVersion`, `DatabaseEngine` and `FindingFingerprint` are declared as
  `sealed record` **classes**, not `record struct`. A struct's `default(T)` bypasses every
  constructor and would silently produce an unvalidated instance (for example a `RuleVersion`
  with `Value == 0`, which the constructor would otherwise reject). Using reference-type
  records removes that loophole: `default(FindingCode)` is `null`, and every non-null
  instance has necessarily gone through the validating constructor.
- Aggregates that primarily hold collections (`Finding`, `DatabaseSnapshot`,
  `CapabilitySnapshot`) are plain `sealed class` types without generated structural equality;
  equality is not a meaningful operation for them (they are not compared, they are consumed).
  Everything else that behaves like a value (`DatabaseObjectReference`, `EvidenceItem`,
  `TableSnapshot`, `IndexSnapshot`, `IndexKeyPartSnapshot`, `CapabilityState`, ...) is a
  `sealed record` so structural equality is available for tests and future callers.
  `IndexSnapshot` is the one exception that needed *hand-written* equality rather than the
  record-generated one — see [§6](#6-snapshot-model).

## 4. Finding codes

`FindingCodes` defines the five codes approved in `PROJECT_RULES.md` §4
(`DBH001`–`DBH005`) as `FindingCode` instances. `FindingCode` itself only validates the
`DBH###` shape; it carries no knowledge of what each code means. No rule logic for any code
is implemented in this gate.

## 5. Severity and confidence

`FindingSeverity` (`Info`, `Warning`, `Critical`) and `FindingConfidence`
(`Low`, `Medium`, `High`) match `PROJECT_RULES.md` §4 and §10 exactly. `Error` is
intentionally absent from `FindingSeverity`, per §8 of the technical definition: it is
reserved for tool execution failures, never for a finding about the database.

## 6. Snapshot model

`DatabaseSnapshot` aggregates everything a rule needs without depending on Npgsql or any
PostgreSQL-specific type (verified by the absence of a `DbHealthInspector.PostgreSql`
project reference from `DbHealthInspector.Core`, and by an explicit grep-style check in
validation — see §13 of the gate prompt).

Two modeling choices are worth calling out because the gate prompt's field list could be
read more than one way:

- **Collation, operator class, sort direction and nulls ordering are modeled per key part**
  (`IndexKeyPartSnapshot`), not duplicated as single scalar fields on `IndexSnapshot`. In
  PostgreSQL, a multi-column index can have a different collation, operator class, sort
  direction and nulls placement for each column; a single index-level value would either be
  meaningless for multi-column indexes or would silently discard information DBH003 (exact
  duplicate index) needs to compare correctly.
- **Expression text is modeled per key part only**, not duplicated again as a separate
  index-level `Expression` field, because an index can mix plain columns and expressions
  within the same key list; a single index-level expression string could not represent that.
  `IndexSnapshot.PartialPredicate` remains index-level because a partial index's `WHERE`
  clause genuinely applies to the whole index, not to one column.

`TableSnapshot` rejects a table that claims to be both `IsPartitionedRoot` and `IsPartition`
at once. `IndexSnapshot` rejects `IsPrimaryKey == true` combined with `IsUnique == false` —
and, as of the C4 correction, also rejects `IsPrimaryKey == true` combined with
`BacksConstraint == false`: a primary key is always both unique and constraint-backed in
PostgreSQL, so both are impossible states, treated as programmer errors rather than valid
input. The implication is one-directional by design: `BacksConstraint == true` does **not**
imply `IsPrimaryKey == true` — an index can back a plain unique constraint without being a
primary key — so the constructor never infers `IsPrimaryKey` from `BacksConstraint`.

`EstimatedRowCount` is `long?`, sourced from catalog statistics (`reltuples`), never from
`COUNT(*)`, per §7 of the technical definition and ADR-0002.

### 6.1 `IndexSnapshot` structural equality (C5)

`IndexSnapshot` is a `sealed record`, but its `Equals`/`GetHashCode` are hand-written rather
than record-generated. The reason is that `KeyParts` and `IncludedColumns` are declared as
`IReadOnlyList<T>`, an interface with no built-in element-wise equality; the record-generated
equality would have compared the two *list objects*, not their contents, so two independently
constructed snapshots with identical values would never have compared equal — silently
breaking any future code (deduplication, test assertions, DBH003 comparisons) that assumes
value equality for a value-shaped type. The hand-written `Equals(IndexSnapshot?)` compares
every scalar property with ordinary equality and both list properties with
`SequenceEqual` — order-sensitively, since `KeyParts` order is the index's actual column
order and `IncludedColumns` order is preserved rather than normalized (§3). `GetHashCode`
combines the same members through `HashCode.Add`, iterating both lists so the hash stays
consistent with `Equals`. No reflection, serialization or external dependency is used.

## 7. Capability reporting

`CapabilitySnapshot` requires **exactly one** `CapabilityState` per defined
`CapabilityKind` value (`CatalogMetadata`, `UsageStatistics`, `DataProfiling`) — no more, no
fewer. This makes "a capability silently missing from the report" a construction-time error
instead of a possible bug in a future adapter or orchestrator.

`CapabilityState.Reason` describes *why* a capability is absent or turned off — the cause of
`Unavailable` or `Disabled` — never a description of a working capability. Its validation
policy (corrected in GC-DHI-03A-C2, DHI-R2-002) is:

```text
Status = Available,   Reason = null          -> valid
Status = Available,   Reason != null         -> ArgumentException
Status = Unavailable, Reason = null          -> valid
Status = Unavailable, Reason = non-blank     -> valid
Status = Unavailable, Reason = "" / blank    -> ArgumentException
Status = Disabled,    Reason = null          -> valid
Status = Disabled,    Reason = non-blank     -> valid
Status = Disabled,    Reason = "" / blank    -> ArgumentException
```

A reason is never required — the server may simply not have reported one, or the adapter may
not yet distinguish causes — but if one is present for `Unavailable`/`Disabled` it must be
genuinely informative (non-blank), and an `Available` capability must never carry a leftover
or speculative reason. The value is never trimmed; whatever a caller passes is either rejected
or stored exactly as given.

`CapabilitySnapshot` and `CapabilityState` permit **any** `CapabilityStatus`
(`Available`, `Unavailable` or `Disabled`) for **any** `CapabilityKind`, including
`DataProfiling`. Core enforces no policy about which status is "correct" for a given
capability — it only enforces the shape (one state per kind, a reason when not available).
The v0.1.0 product decision that `DataProfiling` starts out `Disabled` (ADR-0002) is a
*composition-time* policy, applied and validated by the CLI/composition layer in a later
gate, not a Core-level invariant; an earlier revision of this document and of
`CapabilityKind`'s XML documentation incorrectly implied Core itself enforced that policy,
which the C4 correction removed.

## 8. Rule contract

`IInspectionRule` is intentionally minimal: an identity (`Code`, `Version`, `Name`,
`Category`) and one pure method, `Evaluate(DatabaseSnapshot) -> IReadOnlyList<Finding>`. It
has no cancellation token (evaluation is in-memory and synchronous by design — I/O already
happened when the snapshot was built) and no mutable state. `tests/.../Rules/
InspectionRuleContractTests.cs` implements a private, test-only rule to prove the contract
is usable and deterministic; it is **not** DBH001 and must not be mistaken for it.

## 9. Fingerprint algorithm

### 9.1 Goal

The same logical finding must keep the same fingerprint even if its current size, row
estimate, message, recommendation, severity, confidence or rule-implementation version
changes between two inspections of the same database.

### 9.2 Design: `Finding` owns its fingerprint inputs

`FindingFingerprintInput` is a separate, minimal type carrying only what participates in
identity: format version, engine, finding code, object reference and the full evidence list
(the generator itself keeps only `Include` items). It does not carry message, recommendation,
severity, confidence or rule version — those fields do not exist on `FindingFingerprintInput`
at all, so they cannot be accidentally included by a future edit; the exclusion is structural,
not a rule someone has to remember to follow.

`Finding` stores `Engine` as a public `DatabaseEngine Engine { get; }` property — the same
required, validated constructor parameter every other value object receives — and computes
its own `Fingerprint` in its constructor from exactly its own stored properties: `Engine`,
`Code`, `ObjectReference` and `Evidence`. Nothing about a `Finding`'s identity is discarded
after construction: every input `FindingFingerprintGenerator` used to produce `Fingerprint`
can be read back from the `Finding` itself and fed into a fresh
`FindingFingerprintInput`/`FindingFingerprintGenerator.Generate` call to reproduce the exact
same value — this is asserted directly in
`FindingTests.Fingerprint_CanBeIndependentlyRecomputedFromFindingProperties`. This is stronger
than, and replaces, the original design (GC-DHI-03A), where `engine` was accepted by the
constructor purely to compute the fingerprint and then discarded without becoming a property;
Codex review GC-DHI-03A-R1 (finding DHI-R1-002) correctly identified that as information loss
— a caller holding only a `Finding` had no way to tell which engine it came from, or to
independently verify its own fingerprint.

`FindingFingerprintInput` and `FindingFingerprintGenerator` remain public and are exercised
directly in tests, independently of `Finding`, so every canonicalization scenario can still be
tested without constructing a full finding each time. `Finding` is not turned into a factory
or given an externally-supplied, unverified fingerprint: the only way to obtain a `Finding`
with a given `Fingerprint` is to construct it with the `Code` / `ObjectReference` / `Evidence`
/ `Engine` that fingerprint actually corresponds to, computed by the same
`FindingFingerprintGenerator` used everywhere else.

Because two findings that differ only in `Engine` must be distinguishable (a PostgreSQL
finding and a hypothetical future SQL Server finding about an object with the same code,
schema and name are not "the same finding"), `Engine` participates in `Fingerprint`
(§9.3) exactly as before; the correction only changed *where the value lives*, not what the
algorithm hashes.

### 9.3 Included fields

1. Format version (`fp1`, `FindingFingerprintInput.CurrentFormatVersion`).
2. Engine name (`DatabaseEngine.Name`).
3. Finding code.
4. Object type.
5. Schema name.
6. Parent object name.
7. Object name.
8. Evidence items whose `FingerprintParticipation` is `Include`.

### 9.4 Excluded fields

Message, recommendation, severity, confidence, rule version, evidence items marked
`Exclude`, and any timestamp or dynamic counter — because none of these exist on
`FindingFingerprintInput`, they cannot leak into the hash regardless of future changes to
`Finding` or to rule implementations.

### 9.5 Canonicalization

Each field is written by a private `WriteField` helper as:

```text
[1 byte: 0x00 if null, 0x01 if present]
[if present: 4-byte length prefix][UTF-8 bytes of the value, Unicode-normalized to Form C]
```

This is deliberately **not** delimiter-based concatenation (`value1|value2|...`). A
delimiter scheme is vulnerable to two different logical objects producing the same canonical
string — for example schema `"ab"` + name `"c"` and schema `"a"` + name `"bc"` would both
serialize to `"ab|c"` / `"a|bc"`... except they don't collide under naive concatenation
either unless the delimiter itself can appear inside a value; the point generalizes to any
identifier that might contain the chosen delimiter. Explicit length prefixes make every field
self-delimiting, so no identifier value (however it is spelled) can be crafted to produce the
same byte sequence as a different combination of fields. The presence byte additionally makes
`null` and `""` produce different bytes (`0x00` vs. `0x01 0x00000000`); no public domain field
can hold `""` (§3, §11), so this distinction is no longer reachable from a public constructor,
but it remains a real, verified property of the algorithm itself — see §11 for how it is
tested now.

Evidence items marked `Include` are sorted before hashing, ordinally, by `Key` then `Value`
then `Unit`, so the caller's original evidence order never affects the result. Text is
normalized with `string.Normalize(NormalizationForm.FormC)` before UTF-8 encoding, so a
precomposed character (`"é"` as U+00E9) and its decomposed equivalent (`"e"` + combining
U+0301) hash identically, while case is preserved exactly (no case folding), so
`"Sales"` and `"sales"` remain distinct identities.

The resulting bytes are hashed with `SHA256.HashData` (`System.Security.Cryptography`,
base class library only) and formatted as `sha256:` followed by 64 lowercase hex characters
via `Convert.ToHexStringLower`. The intermediate canonical byte buffer is never exposed
outside `FindingFingerprintGenerator` (`WriteField` is `private`); only the final
`FindingFingerprint` is public.

### 9.6 Why `RuleVersion` is excluded

`RuleVersion` tracks a rule's *implementation*, not the *problem* it reports. Fixing a bug in
how DBH001 detects a missing primary key (an implementation change) should not manufacture a
brand-new identity for a problem a previous run already reported on the same table — that
would break "same finding across runs" comparison, which is the entire purpose of having a
fingerprint. A genuine change in what a code *means* is a governance-level event (a new
finding code), not something `RuleVersion` is meant to express; see `AGENTS.md`
("Change stable finding codes" requires human authorization).

### 9.7 Golden vector

```text
Input:
  Engine:  PostgreSQL
  Code:    DBH001
  Object:  Table, schema "ops", name "import_batch_rows", no parent
  Evidence:
    estimatedRows  = "25000"   (rows)   — Exclude
    totalSizeBytes = "4194304" (bytes)  — Exclude
    hasPrimaryKey  = "false"            — Include

Fingerprint:
  sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444
```

This exact value is asserted in
`tests/DbHealthInspector.UnitTests/Fingerprinting/FindingFingerprintGeneratorTests.cs`
(`GoldenVector_StaysStableForAFixedInput`). If a future, intentional change to the
canonicalization format changes this value, `FindingFingerprintInput.CurrentFormatVersion`
must be incremented from `fp1` and this document and the golden test must be updated
together, as a deliberate, reviewed change — not as an incidental side effect.

## 10. Immutability decisions

- All exposed collections are `IReadOnlyList<T>` backed by a defensively-copied array; the
  constructor is the only place a collection is built from caller-supplied data.
- Value objects use `sealed record` (class-based) rather than `record struct`, for the
  default-bypass reason explained in §3.
- `FindingCode` implements `IComparable<FindingCode>` and, because analyzer rule CA1036
  requires it once `IComparable` is implemented, also defines `<`, `<=`, `>`, `>=`.
- No custom exception hierarchy was introduced; all validation failures throw standard
  `ArgumentException`, `ArgumentNullException` or `ArgumentOutOfRangeException`, per the gate
  prompt.

## 11. Limitations

- No public domain type can hold an empty or whitespace-only optional string (§3): every
  optional string is either `null` ("absent") or a genuinely non-blank value. An earlier
  revision of this document and of `DatabaseObjectReference` deliberately allowed
  `SchemaName`/`ParentObjectName` to hold `""` as a way to exercise the fingerprint
  canonicalization's null-vs-empty distinction end-to-end; Codex review GC-DHI-03A-R1
  (finding DHI-R1-001) correctly rejected that as weakening a domain contract to serve a test.
  The canonicalization still distinguishes `null` from `""` internally (§9.5) — that has not
  changed — but it is now verified through
  `FindingFingerprintGenerator.EncodeCanonicalField(string?)`, an `internal` method visible
  only to `DbHealthInspector.UnitTests` via `InternalsVisibleTo`
  (`src/DbHealthInspector.Core/AssemblyInfo.cs`), instead of through any public constructor.
- `IndexSnapshot.BacksConstraint` is a single boolean rather than a constraint name or type.
  It is enough to support DBH003/DBH004 recommendations ("this index backs a constraint,
  think twice") without modeling the full constraint catalog, which no approved v0.1.0
  diagnostic needs.
- `CapabilitySnapshot` fixes the set of tracked capabilities to the three
  `CapabilityKind` values that exist today. Adding a fourth capability is a small, explicit
  code change (a new enum member), not a data-driven extension point — consistent with
  "no plugin systems" in `AGENTS.md`.

## 12. Deferred to later gates

Explicitly not implemented in GC-DHI-03A, per its prompt:

- CORE-04 (inspection orchestration, summary construction, overall risk calculation,
  diagnostic execution status tracking).
- DBH001–DBH005 as executable rules (only the finding-code catalog and the pure rule
  contract exist).
- Any PostgreSQL integration (connections, catalog queries, statistics queries).
- CLI behavior and JSON report serialization.
- Docker demo and integration tests against a real PostgreSQL instance.
