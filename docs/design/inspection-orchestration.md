# Inspection Orchestration and Risk Summary

**Gate:** GC-DHI-03B — Inspection Orchestration and Risk Summary
**Backlog item:** CORE-04
**Scope:** `DbHealthInspector.Core.Inspections` — orchestration, execution records, risk and
summary.
**Status:** Implemented; corrected per Codex review GC-DHI-03B-R1 (GC-DHI-03B-C1); pending
further Codex review.

## 0. Corrections applied in GC-DHI-03B-C1

Codex review GC-DHI-03B-R1 returned five findings. This revision reflects the corrected
design:

- **DHI-B-R1-001** — Not every `OperationCanceledException` a rule throws represents the
  requested inspection's cancellation. See [§9](#9-cancellation-handling), which replaces the
  earlier, overly broad claim that any such exception always propagates.
- **DHI-B-R1-002** — Cancellation now takes priority over recording an ordinary exception as a
  rule failure: the requested token is checked again immediately before converting a
  recoverable exception into `DiagnosticExecution.Failed`, not only before/after `Evaluate`
  itself. See [§9](#9-cancellation-handling).
- **DHI-B-R1-003** — `IsRecoverableRuleException` now also excludes
  `AccessViolationException`, alongside `OutOfMemoryException` and
  `StackOverflowException`. See [§8](#8-failure-handling).
- **DHI-B-R1-004** — `DiagnosticExecution.UnavailableCapabilities` is now canonically ordered
  (ascending `CapabilityKind` numeric value), not input order, so two logically equivalent
  registrations always produce identical observable output. See
  [§7](#7-execution-states).
- **DHI-B-R1-005** — Test-only: the OCE association matrix, the cancellation/exception
  priority ordering, the three process-exception types, and canonical capability order are now
  each tested as separate, explicit scenarios.

This document describes the orchestration model added in GC-DHI-03B. It builds on, and does
not change, the domain contracts from GC-DHI-03A described in
docs/design/core-domain-contracts.md (`Finding`, `DatabaseSnapshot`, `IInspectionRule`, the
`fp1` fingerprint format). It does not implement DBH001–DBH005, PostgreSQL access, CLI
behavior, exit-code mapping or JSON report serialization — those remain out of scope; see
[§16](#16-deferred).

## 1. Objective

`InspectionOrchestrator` runs one inspection: it captures exactly one `DatabaseSnapshot`,
evaluates every enabled rule whose required capabilities are available, isolates each rule's
failures from the others, validates every rule's output against the rule contract, and
assembles a single, immutable, internally-consistent `InspectionResult` — with deterministic
ordering, deterministic counts and a deterministic overall risk classification.

## 2. Diagram (textual)

```text
InspectionOrchestrator(IDatabaseSnapshotProvider, IReadOnlyCollection<InspectionRuleRegistration>)
        │
        ▼
InspectAsync(CancellationToken)
        │
        ├─ 1. snapshotProvider.CaptureAsync(token)  — exactly once
        │       null            -> InvalidOperationException
        │       exception       -> propagates
        │       cancellation    -> propagates
        │
        ├─ 2. order registrations by Rule.Code.Value (ordinal)
        │
        ├─ 3. for each registration, in that order:
        │       check cancellation
        │       check required capabilities against snapshot.Capabilities
        │         any Unavailable/Disabled -> DiagnosticExecution.SkippedUnavailableCapability
        │       else: rule.Evaluate(snapshot)
        │         OperationCanceledException, associated with requested token (§9.1)
        │             -> propagates immediately
        │         OperationCanceledException, NOT associated (e.g. CancellationToken.None,
        │         a different token) -- OR any other exception
        │             -> check cancellation (§9.2, priority over recording a failure)
        │             -> DiagnosticExecution.Failed(UnhandledRuleException)
        │       check cancellation
        │       validate contract (TryValidateRuleOutput)
        │         invalid -> DiagnosticExecution.Failed(RuleContractViolation), findings discarded
        │         valid   -> DiagnosticExecution.Completed(findingCount), findings accepted
        │
        └─ 4. sort accepted findings and executions; construct InspectionResult

InspectionResult
├── Snapshot
├── DiagnosticExecutions  (one per registration, sorted by Code ordinal)
├── Findings              (accepted only, sorted by Code then Fingerprint, both ordinal)
├── Summary               (counts derived from the two collections above)
├── OverallRisk           (derived from Findings severities only)
└── HasErrors             (derived from DiagnosticExecutions: any Failed?)
```

## 3. Snapshot provider contract

```csharp
public interface IDatabaseSnapshotProvider
{
    Task<DatabaseSnapshot> CaptureAsync(CancellationToken cancellationToken);
}
```

Engine-neutral: no Npgsql type, no connection string, no SQL, no logging. A concrete
PostgreSQL implementation belongs to `DbHealthInspector.PostgreSql` in a future gate; this
gate defines the contract and a set of small test fakes only.

`InspectionOrchestrator` calls `CaptureAsync` **exactly once** per `InspectAsync` call,
passing through the identical `CancellationToken` it received. Three rules govern the
relationship:

1. A `null` snapshot is a contract violation by the *provider*, not the rule contract:
   `InspectAsync` throws `InvalidOperationException` immediately, before any rule runs.
2. Any exception the provider throws — including `OperationCanceledException` — propagates
   unchanged. There is no meaningful partial inspection result without a snapshot, so nothing
   catches around the `CaptureAsync` call.
3. If the provider throws, no rule's `Evaluate` is ever called.

## 4. Enabled-rule registration

`IInspectionRule` is unchanged from GC-DHI-03A. `InspectionRuleRegistration` pairs a rule with
the capabilities it needs:

```csharp
public sealed class InspectionRuleRegistration
{
    public IInspectionRule Rule { get; }
    public IReadOnlyList<CapabilityKind> RequiredCapabilities { get; }
}
```

The collection of registrations passed to `InspectionOrchestrator`'s constructor **is** the
set of enabled rules. There is no `Enabled` flag, no global configuration object, no service
locator, no dependency-injection container and no reflection-based discovery: a future
composition layer (the CLI, in a later gate) disables a rule simply by not registering it.

`InspectionRuleRegistration`'s constructor validates `Rule` is non-null and copies
`RequiredCapabilities` defensively, rejecting an undefined `CapabilityKind` or a duplicate,
while preserving the caller's order (the orchestrator imposes its own execution order
separately; see [§6](#6-deterministic-order)).

`InspectionOrchestrator`'s constructor validates the registration set as a whole:

- The registration collection and every registration in it must be non-null.
- No two registrations may share the same `Rule.Code.Value`.
- Each registration's `Rule.Code` and `Rule.Version` must be non-null, `Rule.Name` must be
  non-blank, and `Rule.Category` must be a defined `FindingCategory` value.

These last four checks exist because `IInspectionRule`'s properties are typed as non-nullable,
but nothing at runtime prevents a badly-behaved implementation from returning `null` anyway;
the orchestrator treats that as an invalid *registration*, not a runtime failure during
inspection, since it can be — and is — detected before any inspection ever runs.

An **empty** registration collection is valid: it means no rule is enabled, and
`InspectAsync` still captures a snapshot and returns a coherent, empty-but-valid result.

## 5. Required capabilities

Before calling a rule's `Evaluate`, the orchestrator checks every capability the rule's
registration declares against `snapshot.Capabilities`. If every declared capability is
`CapabilityStatus.Available`, the rule runs. If **any** declared capability is `Unavailable`
**or** `Disabled`, the rule does not run at all: the orchestrator records a
`SkippedUnavailableCapability` execution listing every unavailable capability (not just the
first one found) and moves to the next rule. This is never treated as an error — it does not
set `InspectionResult.HasErrors` — because a capability being unavailable is an expected,
reportable condition of the inspected environment, not a defect in the tool.

No special-case logic exists for `CapabilityKind.DataProfiling`; it is checked exactly like
any other capability. See docs/design/core-domain-contracts.md §7 for why Core keeps
`DataProfiling` engine-neutral rather than hard-coding a policy about it.

## 6. Deterministic order

Rules run **sequentially, never in parallel**, ordered by `FindingCode.Value` using ordinal
comparison — never by registration order, dictionary iteration order, the scheduler, or task
completion order. The orchestrator re-derives this order itself from `_registrations` at the
start of every `InspectAsync` call, so registering the same rules in a different order
produces identical output (see the `InspectAsync_DifferentRegistrationOrderProducesIdenticalOutput`
test). The final `InspectionResult.DiagnosticExecutions` and `InspectionResult.Findings`
collections are independently, explicitly re-sorted before construction (not merely assumed to
already be in order from the loop), so the guarantee does not silently depend on loop
mechanics remaining unchanged.

## 7. Execution states

```csharp
public enum DiagnosticExecutionStatus
{
    Completed,
    SkippedUnavailableCapability,
    Failed,
}
```

`DiagnosticExecution` is only ever produced by three `internal` factory methods —
`Completed`, `SkippedUnavailableCapability` and `Failed` — never by a public constructor, so
the invariant tying `Status` to `FindingCount`, `UnavailableCapabilities` and `Failure` cannot
be violated through the API:

| Status | `Failure` | `UnavailableCapabilities` | `FindingCount` |
|---|---|---|---|
| `Completed` | `null` | empty | `>= 0` |
| `SkippedUnavailableCapability` | `null` | non-empty | `0` |
| `Failed` | non-null | empty | `0` |

Each factory also validates the rule identity fields it's given (`Code`/`Version` non-null,
`RuleName` non-blank, `Category` defined) and, respectively, that `FindingCount >= 0`, that
`UnavailableCapabilities` is non-empty, and that `Failure` is non-null — so an instance can
only exist in one of exactly these three shapes.

`SkippedUnavailableCapability` additionally **canonicalizes** `UnavailableCapabilities`: after
validating (rejecting undefined values and duplicates) and copying defensively, it sorts the
result by ascending `CapabilityKind` numeric value — currently `CatalogMetadata` (0),
`UsageStatistics` (1), `DataProfiling` (2) — before wrapping it read-only. This is the single
authoritative point where the canonical order is produced (GC-DHI-03B-C1, DHI-B-R1-004): the
orchestrator's own capability-checking loop still discovers unavailable capabilities in
whatever order `InspectionRuleRegistration.RequiredCapabilities` happens to be declared in
(that order is preserved by `InspectionRuleRegistration` itself and is not, on its own,
canonical), but by the time that list reaches `DiagnosticExecution.SkippedUnavailableCapability`
it is always re-sorted. Two registrations that require the same capabilities in a different
declared order therefore always produce an identical `UnavailableCapabilities` sequence.

## 8. Failure handling

`DiagnosticFailureKind` is intentionally small:

```csharp
public enum DiagnosticFailureKind
{
    UnhandledRuleException,
    RuleContractViolation,
}
```

`DiagnosticExecutionFailure { Kind, Message }` never stores the original exception, its
message, its stack trace, a connection string, SQL, or any other potentially sensitive
detail. `Message` is always one of exactly two fixed, generic, deterministic strings, chosen
by `Kind`:

```text
UnhandledRuleException:  "The diagnostic rule failed during evaluation."
RuleContractViolation:   "The diagnostic rule returned an invalid result."
```

**Rule throws a normal exception.** The orchestrator catches it, discards it (the `Exception`
object itself is never retained anywhere in the result), records a `Failed` execution with
`UnhandledRuleException`, and continues with the next rule — unless the requested
`CancellationToken` has since become canceled, in which case cancellation takes priority; see
[§9](#9-cancellation-handling). The catch uses an explicit filter, `IsRecoverableRuleException`,
that excludes exactly three exception types that indicate the process itself may be
compromised — `OutOfMemoryException`, `StackOverflowException` and
`AccessViolationException` (GC-DHI-03B-C1, DHI-B-R1-003) — from ever being treated as an
isolated, recoverable rule failure. It is not a catch-all disguised as a filter, and the list
is intentionally not speculative: exactly these three, no more.

**Rule violates the contract.** See [§10](#10-contract-validation). Same outcome shape:
`Failed` with `RuleContractViolation`, and the rule's raw findings — valid or not — are
discarded entirely. No partial acceptance and no silent deduplication: a single invalid
finding invalidates the whole rule's output for that run.

**Explicitly not conflated:** `Rule failure != finding severity`. A rule that throws or
violates its contract contributes zero findings; it cannot itself produce a `Critical` (or any)
finding. `Rule failure does not change OverallRisk directly` — [§12](#12-risk-matrix)'s matrix
looks only at accepted findings, never at how many rules failed. `Rule failure sets HasErrors`
— that is the only channel through which a failure is visible on `InspectionResult`.
`Skipped unavailable diagnostics are visible but are not failures` — they appear in
`DiagnosticExecutions` and in `InspectionSummary.SkippedDiagnostics`, but never set
`HasErrors` and never affect `OverallRisk`.

## 9. Cancellation handling

The `CancellationToken` passed to `InspectAsync` is checked:

1. Immediately, before capturing the snapshot.
2. Immediately after a snapshot is successfully captured, before ordering registrations.
3. At the top of every loop iteration, before checking that rule's capabilities.
4. Immediately after a rule's `Evaluate` returns (successfully), before validating its output.
5. Immediately before converting a caught, recoverable exception into
   `DiagnosticExecution.Failed` (see below) — added in GC-DHI-03B-C1 (DHI-B-R1-002).

Each of these uses `cancellationToken.ThrowIfCancellationRequested()` directly, so whenever the
requested token has been canceled, `OperationCanceledException` propagates immediately and
unchanged: no further rule runs, no `DiagnosticExecution` is recorded for the interrupted step,
and `InspectAsync`'s task ends up Canceled/Faulted rather than returning any
`InspectionResult`. `Cancellation never becomes a DiagnosticExecution`: there is no fourth
`DiagnosticExecutionStatus` for it.

### 9.1 Not every `OperationCanceledException` is the requested cancellation

**Corrected in GC-DHI-03B-C1 (DHI-B-R1-001).** An earlier revision of this document claimed
"any `OperationCanceledException` a rule throws propagates." That was too broad: nothing
prevents a rule from throwing an `OperationCanceledException` that carries
`CancellationToken.None`, a token from some unrelated source, or a token that is not the one
the orchestrator was actually asked to observe. None of those represent cancellation of *this*
inspection.

`InspectionOrchestrator.IsRequestedCancellation(exception, requestedToken)` decides whether an
`OperationCanceledException` caught around `Evaluate` is genuinely associated with the
requested token:

```csharp
private static bool IsRequestedCancellation(
    OperationCanceledException exception, CancellationToken requestedToken)
{
    if (requestedToken.IsCancellationRequested)
    {
        return true;
    }

    return requestedToken.CanBeCanceled
        && exception.CancellationToken.CanBeCanceled
        && exception.CancellationToken == requestedToken;
}
```

Two independent conditions establish association:

1. **The requested token is already canceled.** In that case the exception's own token does
   not matter — the inspection's own cancellation has unambiguously been requested, so the
   exception is treated as that cancellation regardless of what it happens to carry.
2. **The exception's token is exactly the requested token, and both are cancelable.** The two
   `CanBeCanceled` checks exist specifically so that `CancellationToken.None` compared against
   another `CancellationToken.None` is never treated as association: two "no cancellation
   possible" tokens are structurally equal to each other (`default == default`) but represent
   no relationship at all. Without this guard, an `OperationCanceledException` thrown with the
   default token would look "associated" with an `InspectAsync()` call that also used the
   default token, even though neither one can ever actually be canceled.

| Scenario | Association? | Outcome |
|---|---|---|
| Requested token already canceled | Yes (condition 1) | Propagate immediately |
| Exception carries the same cancelable requested token | Yes (condition 2) | Propagate immediately |
| Exception carries `CancellationToken.None` | No | `Failed` / `UnhandledRuleException`; continue |
| Exception carries a different, non-canceled cancelable token | No | `Failed` / `UnhandledRuleException`; continue |
| Exception carries a different, already-canceled cancelable token | No | `Failed` / `UnhandledRuleException`; continue |

When an `OperationCanceledException` is *not* associated, it is treated exactly like any other
recoverable exception (§8): its message and its `CancellationToken` are both discarded, it
becomes a `Failed` execution with `DiagnosticFailureKind.UnhandledRuleException` and the same
generic message every other unhandled exception uses, `FindingCount` is `0`, `HasErrors`
becomes `true`, `OverallRisk` is unaffected (it only looks at accepted findings), and the next
rule still runs.

### 9.2 Cancellation takes priority over an ordinary exception

**Corrected in GC-DHI-03B-C1 (DHI-B-R1-002).** If a rule cancels the requested token itself
(directly or as a side effect) and *then* throws an ordinary exception — or an unassociated
`OperationCanceledException` — the orchestrator must not record a `Failed` execution and
continue as if nothing were requested. Immediately before converting any caught, recoverable
exception into `DiagnosticExecution.Failed`, the orchestrator calls
`cancellationToken.ThrowIfCancellationRequested()` again. If the token became canceled during
that rule's execution, this throws and propagates instead of recording anything — even though
the exception that was actually caught was not itself cancellation-related. This check applies
whether the rule is the last registered rule or not: there is no reliance on a "next iteration"
to notice the cancellation.

### 9.3 Why a rule's own cancellation exceptions are handled this carefully

Because `IInspectionRule.Evaluate` is synchronous and receives no `CancellationToken` of its
own (unchanged from GC-DHI-03A: "pure evaluation, no I/O, no cancellation dependency"), a rule
has no legitimate way to observe the orchestrator's token directly. Any
`OperationCanceledException` it throws is therefore either a defect in the rule (most likely,
if unassociated) or, in principle, a rule that happens to be given the exact token object and
chooses to honor it — both are handled correctly by §9.1's association check, and neither
requires the orchestrator to guess.

## 10. Contract validation

A rule's raw `Evaluate` output is accepted only when **all** of the following hold; the first
violation found rejects the whole batch:

1. The returned collection is not `null`.
2. It contains no `null` element.
3. Every finding's `Code` equals the rule's `Code`.
4. Every finding's `RuleVersion` equals the rule's `Version`.
5. Every finding's `Category` equals the rule's `Category`.
6. Every finding's `Engine` equals `snapshot.Metadata.Engine`.
7. No two findings in the batch share a fingerprint.
8. No finding's fingerprint was already accepted from an earlier rule in this same inspection.

On success, the accepted findings are re-ordered by `Fingerprint.Value` (ordinal) before being
returned; on failure, **nothing** from that rule is kept, and the failing rule's fingerprints
are never added to the running "already accepted" set — a rejected rule cannot poison later
rules' duplicate checks, and an accepted rule's fingerprints are only committed to that set
*after* its own validation fully succeeds.

Check 8 (cross-rule duplication) is real, wired, defense-in-depth code, but it cannot
currently be triggered through two *different*, legitimately-registered rules: a
`Finding`'s fingerprint always embeds its own `Code` (docs/design/core-domain-contracts.md
§9.3), and `InspectionOrchestrator`'s constructor already rejects two registrations sharing a
code, so two different rules' accepted findings can never legitimately collide without an
actual SHA-256 collision. The internal method that implements checks 1–8,
`InspectionOrchestrator.TryValidateRuleOutput`, is exercised directly in tests with a
pre-populated "already seen" set to cover that branch — the same pattern already used for
`FindingFingerprintGenerator.EncodeCanonicalField` in GC-DHI-03A for a similarly
unreachable-through-the-public-API scenario.

## 11. Summary

```csharp
public sealed class InspectionSummary
{
    public int TotalFindings { get; }
    public int InfoFindings { get; }
    public int WarningFindings { get; }
    public int CriticalFindings { get; }
    public int TotalDiagnostics { get; }
    public int CompletedDiagnostics { get; }
    public int SkippedDiagnostics { get; }
    public int FailedDiagnostics { get; }
}
```

Every count is computed in the constructor by iterating the final `Findings` and
`DiagnosticExecutions` collections — never received as independently supplied, separately
trusted numbers. `TotalFindings = InfoFindings + WarningFindings + CriticalFindings` and
`TotalDiagnostics = CompletedDiagnostics + SkippedDiagnostics + FailedDiagnostics` therefore
hold *by construction*: there is no code path that could make them disagree, because the
"total" fields are literally the sum of the other fields computed in the same pass, not a
separately-stored value. This gate does not add a per-`FindingConfidence` breakdown.

## 12. Risk matrix

```csharp
public enum OverallRisk { None, Low, Medium, High }
```

`OverallRiskCalculator.Calculate` (kept `internal` — the assembly already exposes internals
to `DbHealthInspector.UnitTests` — to keep the public surface small) implements exactly:

```text
High   = at least one Critical finding
Medium = at least one Warning finding, and no Critical finding
Low    = one or more findings, and every one of them is Info
None   = zero findings
```

The calculator looks at `FindingSeverity` only. It never considers `FindingConfidence`,
weighting, percentages, object counts, object sizes, the number of failed rules, or the
number of skipped diagnostics — those are represented separately, on `InspectionResult`, as
`Summary` counts and `HasErrors`, never folded into `OverallRisk`. A result can legitimately
be `OverallRisk.None` with `HasErrors = true`: zero findings were accepted, but a rule failed
along the way.

## 13. Immutability

`InspectionResult`'s constructor is `internal`: only `InspectionOrchestrator` (and tests, via
`InternalsVisibleTo`) can build one. This is what makes it structurally impossible for
`Summary`, `OverallRisk` or `HasErrors` to contradict `DiagnosticExecutions` and `Findings` —
they are *computed from* those two collections inside the constructor, not supplied
independently and trusted. Both collections are copied defensively and wrapped with
`Array.AsReadOnly` (via `Guard.CopyDefensivelyRejectingNullElements`), so casting either to
`IList<T>`/`ICollection<T>` and calling `Add`, index-assigning, `Insert`, `Remove`, `RemoveAt`
or `Clear` all throw `NotSupportedException` — matching the guarantee already established for
every Core collection in GC-DHI-03A-C2 (DHI-R2-001). `InspectionRuleRegistration
.RequiredCapabilities` and `DiagnosticExecution.UnavailableCapabilities` use the same
mechanism. No timestamp or tool-version metadata is added anywhere in this gate; that belongs
to the future report model.

## 14. Security decisions

- `DiagnosticExecutionFailure` never stores an `Exception`, a message derived from one, or a
  stack trace — see [§8](#8-failure-handling).
- The snapshot provider contract carries no connection string or SQL, and the orchestrator
  never logs (there is no logging dependency anywhere in this gate).
- Capability checks never special-case `DataProfiling`; Core's engine-neutral capability model
  from GC-DHI-03A-C2 is preserved exactly.

## 15. Limitations

- Cross-rule fingerprint duplication (contract check 8, [§10](#10-contract-validation)) is
  real but currently unreachable through two independently-registered, differently-coded
  rules, for the cryptographic and structural reasons explained there. It remains valuable as
  defense in depth and is tested directly via the internal validation method.
- `InspectionRuleRegistration.RequiredCapabilities` preserves the caller's supplied order, but
  the orchestrator does not use that order for anything (capability checks are order-independent:
  every declared capability is checked, and *all* unavailable ones are recorded, not just the
  first).
- There is no retry, backoff or timeout logic for a rule's `Evaluate` call; a rule is expected
  to be synchronous, pure and fast, matching its GC-DHI-03A contract.

## 16. Deferred

Explicitly not implemented in GC-DHI-03B, per its prompt:

- DBH001–DBH005 as executable rules.
- Any PostgreSQL integration, including a production `IDatabaseSnapshotProvider`.
- CLI behavior, option parsing or composition wiring.
- Exit-code mapping.
- JSON report serialization or any other report format.
- Timestamps or tool-version metadata on any result type.
- Configurable thresholds.
- Logging.
