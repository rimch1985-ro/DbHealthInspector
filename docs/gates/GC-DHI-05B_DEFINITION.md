# GC-DHI-05B — Functional CLI Inspection Command

## 1. Gate identity

| Field | Value |
|---|---|
| Gate | `GC-DHI-05B` |
| Phase | Phase 5 — CLI and Reporting (functional subset) |
| Backlog items | `CLI-01`, `CLI-02`, `CLI-03` (partial), `CLI-04` |
| Deferred backlog items | `RPT-01`, `RPT-02` |
| Predecessor | `GC-DHI-05A` (`APPROVED AND CLOSED`) |
| Document state | Definition only; no implementation authorized by this document |

## 2. Baseline and provenance

| Field | Value |
|---|---|
| Canonical `origin/master` | `a354264bb830ae0a70cddb9090e11211c6a87e84` |
| GC-DHI-05A implementation merge | `2ca2b0a81290b90650315a7bbc358e159eeaf720` |
| GC-DHI-05A governance-closure merge | `a354264bb830ae0a70cddb9090e11211c6a87e84` |

`PROJECT_STATE.md` may still record the implementation merge in its "Current
master" field. That is documentation provenance lag, not a contradiction: the Git remote HEAD
is authoritative and is the baseline this definition was written against.

## 3. Functional objective

Make DbHealthInspector usable by a human being for the first time.

```text
dbhealth inspect postgresql
    ↓
PostgreSqlDatabaseSnapshotProvider
    ↓
DatabaseSnapshot
    ↓
ApprovedDiagnostics + InspectionOrchestrator
    ↓
InspectionResult
    ↓
plain-text console output
    ↓
process exit code
```

Every component in that chain already exists and is closed. **This gate creates no diagnostic
intelligence.** It wires the pieces together and presents the result. If a proposed requirement
is not necessary for `dbhealth inspect postgresql` to produce a useful visible diagnosis, it
does not belong here.

## 4. Existing components reused

All verified against the baseline commit. No API below may be modified.

| Component | Actual surface | Use |
|---|---|---|
| `PostgreSqlDatabaseSnapshotProvider` | `static Create(string connectionString)`; `static Create(string, IReadOnlyCollection<string>, IReadOnlyCollection<string>, TimeSpan)`; `Task<DatabaseSnapshot> CaptureAsync(CancellationToken)`; `ValueTask DisposeAsync()` | The single-argument `Create` overload. |
| `ApprovedDiagnostics` | `static IReadOnlyList<InspectionRuleRegistration> CreateRegistrations()`; `…CreateRegistrations(DiagnosticThresholds)` | The threshold-taking overload. |
| `DiagnosticThresholds` | `long LargeTableRowThreshold`, `long LargeTableSizeThresholdBytes`, `long UnusedIndexSizeThresholdBytes`; `static Default`; positive-only constructor | Built from CLI values or `Default`. |
| `InspectionOrchestrator` | `InspectionOrchestrator(IDatabaseSnapshotProvider, IReadOnlyCollection<InspectionRuleRegistration>)`; `Task<InspectionResult> InspectAsync(CancellationToken = default)` | Runs the inspection. |
| `InspectionResult` | `Snapshot`, `DiagnosticExecutions`, `Findings`, `Summary`, `OverallRisk`, `HasErrors` | Everything the console renders. |
| `InspectionSummary` | `TotalFindings`, `Info/Warning/CriticalFindings`, `TotalDiagnostics`, `Completed/Skipped/FailedDiagnostics` | Summary block. |
| `DiagnosticExecution` | `Code`, `RuleVersion`, `RuleName`, `Category`, `Status`, `FindingCount`, `UnavailableCapabilities`, `Failure` | Diagnostics block. |
| `DiagnosticExecutionFailure` | `Kind`, `Message` | Failure reporting. |
| `Finding` | `Code`, `Severity`, `Confidence`, `Category`, `ObjectReference`, `Message`, `Recommendation`, `Evidence`, `DocumentationReference` | Findings block. |
| `OverallRisk` | `None`/`Low`/`Medium`/`High` | Summary block. |

### 4.1 Existing project wiring — no dependency change needed

`src/DbHealthInspector.Cli/DbHealthInspector.Cli.csproj` **already** references
`System.CommandLine` (2.0.10, centrally pinned) plus both `DbHealthInspector.Core` and
`DbHealthInspector.PostgreSql`. Everything this gate needs is already on the CLI's reference
graph. **No package may be added.**

### 4.2 Current CLI behavior to be replaced

`Program.cs` is a bootstrap stub: a single `RootCommand` whose action prints
`"Database inspection is not implemented yet."`, dispatched via `rootCommand.Parse(args).Invoke()`.
Implementation must remove that message and replace the stub with the real command tree.

### 4.3 Binding constraint — adapter exception types are internal

**Verified:** `DbHealthInspector.PostgreSql` exports exactly **two** public types —
`AssemblyMarker` and `PostgreSqlDatabaseSnapshotProvider`. Every adapter exception
(`PostgreSqlConnectionException`, `PostgreSqlInspectionSessionException`,
`PostgreSqlSnapshotCompositionException`, `PostgreSqlRequiredCatalogCapabilityException`,
the mapping exceptions, `PostgreSqlSqlSafetyException`) is `internal`.

Consequences the implementer must not fight:

- The CLI **cannot** catch adapter exceptions by type, and **must not** be given a reason to
  by making them public. Exporting them would break the frozen two-type public surface.
- The CLI therefore catches `OperationCanceledException` (cancellation), `ArgumentException`
  and `ArgumentOutOfRangeException` (rejected connection string / arguments), and a general
  `Exception` fallback, mapping each to a **fixed** category message in §12.
- The CLI **must not print `exception.Message`**, not even for adapter exceptions whose
  messages are known to be constants. The adapter's sanitization boundary does produce fixed,
  information-free text today — `PostgreSqlConnectionException` carries a constant, and
  `PostgreSqlConnectionStringPolicy` throws with a constant — but those types are `internal`,
  so the CLI cannot tell them apart from anything else at the catch site. A raw `NpgsqlException`,
  a `SocketException`, or any future exception can cross the public provider boundary
  unchanged, and such a message may carry host, port, user or connection detail.
  **The CLI writes its own fixed strings and never relays a message it did not author.**
- The CLI must **never** print `exception.ToString()`, a stack trace, `InnerException`, or
  `Exception.Data`.

## 5. Backlog mapping

| Item | Status in this gate |
|---|---|
| `CLI-01` — command tree | **In scope**, with the option-set reduction in §6.1. |
| `CLI-02` — connection resolution | **In scope**, fully. |
| `CLI-03` — console summary | **In scope except "Report path" and "Target label"** (§5.1). |
| `CLI-04` — exit-code mapping | **In scope**, fully. |
| `RPT-01` — JSON report 0.1 | **DEFERRED — NOT REQUIRED FOR FUNCTIONAL MVP.** |
| `RPT-02` — atomic report writing | **DEFERRED — NOT REQUIRED FOR FUNCTIONAL MVP.** |

### 5.1 CLI-03 partial completion is intentional and must be stated

`CLI-03`'s acceptance list includes **"Report path"**, which cannot exist without `RPT-01`
and `RPT-02`. Forcing a report path into this gate would drag JSON serialization, schema
validation and atomic file writing along with it — precisely the reporting-platform scope this
gate exists to avoid.

`CLI-03` is therefore **partially satisfied** by GC-DHI-05B. It must not be recorded as
complete, and `RPT-01`/`RPT-02` must not be recorded as complete or partially complete. The
console is the functional output of this gate.

## 6. Command tree

```text
dbhealth
└── inspect
    └── postgresql
```

Required to work:

- `dbhealth --help`
- `dbhealth inspect --help`
- `dbhealth inspect postgresql --help`

`dbhealth inspect` with no subcommand is a usage failure (**exit 2**). Any invalid
command-line syntax or value is a usage failure (**exit 2**).

The bootstrap root action and its *"Database inspection is not implemented yet."* message must
be gone.

### 6.1 Options implemented by this gate

| Option | Type | Required |
|---|---|---|
| `--connection` | string | no |
| `--connection-env` | string (variable NAME) | no |
| `--large-table-row-threshold` | int64 | no |
| `--large-table-size-threshold-mb` | int64 (binary MB — see §8) | no |
| `--unused-index-size-threshold-mb` | int64 (binary MB — see §8) | no |

Deferred from this gate, and **not** to be added: `--output`, `--schema`, `--exclude-schema`,
`--statement-timeout-seconds`, `--target-label`, `--verbose`. See §19 and the conflict record
in §23.

### 6.2 Help text obligations

`dbhealth inspect postgresql --help` must state, in the `--connection` description, that a
connection string passed on the command line **may be visible in shell history and in process
listings**, and recommend `--connection-env` or `DBHEALTH_CONNECTION` for anything carrying a
password.

## 7. Connection resolution contract

Precedence, exactly as `CLI-02` freezes it:

1. `--connection` — the PostgreSQL connection string itself.
2. The environment variable **named by** `--connection-env`.
3. `DBHEALTH_CONNECTION`.

### 7.1 Resolution rules

| Situation | Behavior |
|---|---|
| `--connection` supplied and non-blank | Use it. `--connection-env` and `DBHEALTH_CONNECTION` are not consulted. |
| `--connection-env NAME` supplied, `NAME` set and non-blank | Use that variable's value. |
| `--connection-env NAME` supplied, `NAME` unset or blank | **Failure. Exit 2.** Do **not** fall through to `DBHEALTH_CONNECTION`. |
| Neither option supplied, `DBHEALTH_CONNECTION` set and non-blank | Use it. |
| Neither option supplied, `DBHEALTH_CONNECTION` unset or blank | **Failure. Exit 2.** |
| `--connection` supplied but blank | Usage failure. **Exit 2.** |

The explicit-`--connection-env`-does-not-fall-through rule matters: a user who names a variable
is being specific, and silently inspecting whatever `DBHEALTH_CONNECTION` happens to point at
could inspect the wrong database.

### 7.2 Secret handling

Binding requirements:

- **Never** print the connection string, in whole or in part.
- **Never** print a password, and never print the value of the variable named by
  `--connection-env`.
- **Never** print `exception.ToString()`, a stack trace, or an inner exception.
- Reuse the existing PostgreSQL sanitization boundary (§4.3). **No new secret-handling
  subsystem**, no redaction helper, no masking utility.
- The resolved connection string lives only in a local variable passed to
  `PostgreSqlDatabaseSnapshotProvider.Create`. It is never logged, echoed, written to a file,
  or placed in a message.

### 7.3 What successful output may identify

| Field | Displayed | Reason |
|---|:--:|---|
| Database name | **yes** | It is `DatabaseSnapshot.Metadata.DatabaseName`, already part of the captured snapshot, and it is what tells the user which database was inspected. |
| Engine, engine version | **yes** | Snapshot metadata; no secret content. |
| Host / port | **no** | Not approved for display by any existing policy. |
| Username | **no** | Not approved for display, even though `Metadata.CurrentUser` exists. |
| Password, full connection string | **never** | |

> **Decision D-05B-11.** `Metadata.CurrentUser` is available on the snapshot but is
> deliberately **not** rendered. Displaying the database is enough to identify the target;
> the account is not needed for that and is closer to credential material.

## 8. Threshold option contract

Three optional overrides, using the option names already approved in `PROJECT_RULES.md` §9.

| Option | Unit | Property | Default when omitted |
|---|---|---|---:|
| `--large-table-row-threshold` | rows (raw count) | `LargeTableRowThreshold` | `1_000_000` |
| `--large-table-size-threshold-mb` | binary MB (1,048,576 bytes) | `LargeTableSizeThresholdBytes` | `1_073_741_824` |
| `--unused-index-size-threshold-mb` | binary MB (1,048,576 bytes) | `UnusedIndexSizeThresholdBytes` | `10_485_760` |

Defaults come from `DiagnosticThresholds.Default` and **must not be redefined** in the CLI.
When no override is supplied the CLI passes `DiagnosticThresholds.Default` **directly and
unchanged** — no conversion is performed and no threshold object is rebuilt. When any override
is supplied it constructs a `DiagnosticThresholds` with the overridden values and the defaults
for the rest.

### 8.1 Unit semantics — binary megabytes

The two `-mb` options are the historically approved public names, and they carry **binary**
units. One CLI unit means **exactly 1,048,576 bytes**.

```text
--large-table-row-threshold  <value>  ->  LargeTableRowThreshold        = value
--large-table-size-threshold-mb <v>   ->  LargeTableSizeThresholdBytes  = checked(v * 1_048_576)
--unused-index-size-threshold-mb <v>  ->  UnusedIndexSizeThresholdBytes = checked(v * 1_048_576)
```

The row threshold takes a raw row count and is **not** converted.

Because the conversion factor is binary, both byte defaults are exactly reproducible from the
command line:

```bash
# Equivalent to the built-in defaults:
dbhealth inspect postgresql \
  --large-table-size-threshold-mb 1024 \
  --unused-index-size-threshold-mb 10
```

`1024 × 1,048,576 = 1,073,741,824` and `10 × 1,048,576 = 10,485,760` — the frozen
`DiagnosticThresholds.Default` values, exactly. That round-tripping is the reason the unit is
binary rather than decimal, and it must not be changed.

**Help text and both READMEs must state explicitly** that the `-mb` options use binary units of
1,048,576 bytes, so a user is never left guessing between 10^6 and 2^20.

### 8.2 Validation

Every supplied value must be a 64-bit integer **greater than zero**. Zero, negative,
non-numeric and out-of-`Int64`-range input is a **usage failure → exit 2**, reported before any
connection attempt.

The megabyte-to-byte multiplication is performed in a `checked` context. **Arithmetic overflow
is a usage/configuration failure → exit 2**, reported with the fixed usage-level message; an
`OverflowException` must never reach the user. This matters because any value above roughly
8.8 × 10^12 overflows `Int64` once multiplied, so a plausible typo can trigger it.

`DiagnosticThresholds`' own constructor also rejects non-positive values with
`ArgumentOutOfRangeException`; the CLI validates first and produces a usage-level message
rather than letting that exception surface.

### 8.3 Explicitly prohibited

No `-mib` aliases, no `-bytes` aliases, no decimal (10^6) interpretation, no size-suffix parser
(`"1GB"`, `"512MB"`), no presets, no profiles, no config file, no percentage thresholds, no
auto-tuning. Exactly three options, one fixed binary factor, one `checked` multiplication.

## 9. Schema filtering and timeouts

Both are **deferred**.

The CLI calls the single-argument `PostgreSqlDatabaseSnapshotProvider.Create(connectionString)`,
which delegates to `PostgreSqlSchemaFilter.IncludeEverything` and
`PostgreSqlInspectionSessionOptions.Default`. That means: every eligible user schema is
inspected, and the permanent system-schema exclusions still apply and cannot be disabled.

Neither `--schema`/`--exclude-schema` nor `--statement-timeout-seconds` is needed to prove the
user-facing inspection flow, and neither may be added by this gate. No timeout configuration
subsystem.

## 10. Functional composition

The required path, in the actual current API:

```text
DiagnosticThresholds thresholds = <from options, else DiagnosticThresholds.Default>;

await using PostgreSqlDatabaseSnapshotProvider provider =
    PostgreSqlDatabaseSnapshotProvider.Create(resolvedConnectionString);

var orchestrator = new InspectionOrchestrator(
    provider, ApprovedDiagnostics.CreateRegistrations(thresholds));

InspectionResult result = await orchestrator.InspectAsync(cancellationToken);
```

Binding constraints:

- The provider is disposed via `await using` — it owns the connection factory.
- The orchestrator **must** run the rules. The CLI must never invoke `DBH001`–`DBH005`
  individually, and must never construct a `Finding`.
- The CLI must not duplicate capability gating, rule ordering, finding ordering, summary
  counting, risk calculation or failure isolation. All six already exist and are frozen.
- The CLI reads `InspectionResult` and renders it. That is the whole of its responsibility.

## 11. Console presentation contract

Plain text. **No ANSI colour, no cursor control, no box-drawing characters, no console-rendering
library.** Output must stay readable in a Windows terminal, a Linux terminal, and when
redirected to a file or piped.

Successful inspection writes to **stdout**, in this order:

```text
DbHealth Inspector

TARGET
  Database        : <Metadata.DatabaseName>
  Engine          : <Metadata.Engine>
  Engine version  : <Metadata.EngineVersion>

INSPECTION
  Schemas analyzed : <Schemas.Count>
  Tables analyzed  : <Tables.Count>
  Indexes analyzed : <Indexes.Count>

CAPABILITIES
  <kind> : <status>[ — <sanitized reason>]
  (a warning line when a capability loss caused a diagnostic to be skipped)

DIAGNOSTICS
  <code>  <rule name>  <status>  findings=<n>[  skipped: <capability list>][  failed: <kind>]

FINDINGS
  (each finding, per §11.2)

SUMMARY
  Info      : <n>
  Warning   : <n>
  Critical  : <n>
  Total     : <n>
  Overall risk : <OverallRisk>
```

### 11.1 Capability degradation

When any diagnostic has status `SkippedUnavailableCapability`, the output must carry an
explicit warning naming the capability and the affected diagnostic — the user has to know the
picture is incomplete.

The reason text comes from the existing `CapabilityState.Reason` and
`DiagnosticExecution.UnavailableCapabilities`. **A skipped diagnostic must never be rendered as
"0 findings" in a way that implies it ran.** Status and finding count are separate fields
precisely so that a skip reads as a skip.

Absent `UsageStatistics` must never be presented as zero scans, zero unused indexes, or a clean
bill of health for DBH004.

### 11.2 Finding rendering

Per finding:

- Severity, finding code, and object identity (`SchemaName.ObjectName`, plus the parent object
  when `ObjectReference.ParentObjectName` is present — indexes always have one).
- `Message`.
- `Confidence`.
- Evidence items — key, value, and unit when present.
- `Recommendation`.

Evidence carries only schema/table/index names, counts, sizes, thresholds and state flags — no
row contents, no application data. The rules that produce it are frozen, so the CLI renders
what it is given and adds nothing.

### 11.3 Zero findings

A clean result must be explicit, and must be honest about what it means:

```text
No health issues were detected by the enabled diagnostics (DBH001-DBH005).
This does not guarantee the database has no other problems.
```

The second sentence is **required**, not optional polish. Five structural rules finding nothing
is not a clean bill of health, and the tool must not imply otherwise. See §16 for the empirical
case that made this concrete.

### 11.4 Failure state

When `InspectionResult.HasErrors` is true — at least one diagnostic has status `Failed` — the
output must say so plainly, name the affected diagnostics, and the process must exit 2 (§13).

## 12. Error and redaction contract

Errors go to **stderr**. Every message the CLI emits is a **fixed string the CLI itself
authors**. No message text is ever taken from an exception.

### 12.1 Exception mapping — conservative and total

| Condition | CLI message (fixed) | Exit |
|---|---|---|
| No connection resolved | `No PostgreSQL connection was provided.` (plus how to supply one) | 2 |
| Variable named by `--connection-env` missing or blank | `The environment variable named by --connection-env is not set or is empty.` | 2 |
| Threshold value non-positive, non-numeric, out of range, or overflowing conversion | `A diagnostic threshold value is invalid.` | 2 |
| `ArgumentException` / `ArgumentOutOfRangeException` from provider or threshold construction | `The PostgreSQL connection configuration is invalid.` | 2 |
| `OperationCanceledException` | `The inspection was cancelled.` | 2 |
| **Any other exception** crossing the CLI boundary from the provider or orchestrator | `The PostgreSQL inspection could not be completed.` | 2 |
| Invalid command-line syntax or value | `The command line could not be understood.` followed by `Run 'dbhealth inspect postgresql --help' to see the available options.` — see §12.4 | 2 |

The final row is the important one: it is a **total** fallback. There is no exception type for
which the CLI relays the exception's own text.

### 12.2 Prohibited in production CLI output

The CLI must **never** write to the user:

- `exception.Message` for any exception, from any source;
- `exception.ToString()`;
- a stack trace;
- `InnerException` in any form;
- `Exception.Data`;
- any raw `Npgsql` failure detail;
- the connection string, in whole or in part;
- a password;
- any host, port or user value carried in diagnostic text;
- **any unmatched command-line token value**;
- **any option argument value**;
- **any parser diagnostic containing a caller-controlled value**, including one produced by
  System.CommandLine rather than by DbHealthInspector code.

The environment **variable name** given to `--connection-env` may be echoed. Its **value** may
never be.

The last three items bind regardless of where the text originates. **A security invariant takes
precedence over framework-default usability**: text this CLI did not author is never trusted to
be free of caller-supplied values.

### 12.3 Prohibited implementation shortcuts

Do **not** make the PostgreSQL internal exception types public. Do **not** add an
`InternalsVisibleTo` grant **from `DbHealthInspector.PostgreSql` to the CLI**. Do **not**
introduce a new exception hierarchy. Do **not** add Npgsql-specific error handling to the CLI.
Do **not** modify `DbHealthInspector.PostgreSql`.

The point of the total fallback in §12.1 is precisely that none of these is necessary.

> A grant from the **CLI** to the test projects is a different thing and is permitted: it
> exposes the CLI's own command tree, resolvers and renderer for testing without widening any
> public API or touching the adapter's two-type surface.

### 12.4 The CLI owns the parse-error surface — D2 security decision

**Empirical finding.** System.CommandLine 2.0.10, the pinned version, echoes unmatched tokens
verbatim in its default parse diagnostics. A mistyped option name turns the *following*
argument into an unmatched token, so:

```text
dbhealth inspect postgresql --connectio "Host=db;Username=admin;Password=<secret>"
```

writes the whole connection string — password included — to standard error. Verified directly
against 2.0.10 during GC-DHI-05B implementation.

**Decision.** System.CommandLine's default parse diagnostics **must not be emitted** for the
`dbhealth inspect postgresql` command path when they may contain caller-controlled token
values. The CLI owns the public parse-error surface.

For any command-line parse or validation failure, standard error contains **only** fixed,
token-free, CLI-authored text:

```text
The command line could not be understood.
Run 'dbhealth inspect postgresql --help' to see the available options.
```

Exit code **2**. The definition text above and the implementation's fixed strings must remain
identical.

This deliberately trades some framework-default usability — the user is not told *which* token
was wrong — for the guarantee that a typo can never disclose a password. The `--help` pointer
carries the user forward.

**Help is not a parse diagnostic** and is unaffected (§12.5).

### 12.5 Help remains fully available

`dbhealth --help`, `dbhealth inspect --help` and `dbhealth inspect postgresql --help` must
continue to work and exit **0**.

Help may show option **names**, their static descriptions, usage syntax, and the approved
`--connection` warning. Help renders only text authored in this repository or generated from
symbol names; it never receives a supplied value, so it can never show one.

## 13. Exit-code contract

Exactly three values. **No fourth exit code may be introduced.**

| Code | Meaning |
|---:|---|
| `0` | Inspection completed successfully with no findings, or with `Info`-only findings. |
| `1` | Inspection completed successfully with at least one `Warning` or `Critical` finding. |
| `2` | The command or the inspection could not be considered successfully completed. |

`0` versus `1` is derived from the summary: `1` when
`Summary.WarningFindings + Summary.CriticalFindings > 0`, otherwise `0`. Equivalently, from
`OverallRisk`: `Medium` or `High` → 1; `None` or `Low` → 0. Both readings agree by
construction, since `OverallRisk` is derived from the same severities.

Exit `2` covers: invalid CLI syntax or value; missing connection; invalid connection string;
connection-open failure; an unsupported required PostgreSQL capability or version that prevents
inspection; snapshot-provider failure; required-inspection failure; cancellation; and
`HasErrors == true` where it reflects a **diagnostic execution failure** rather than findings.

### 13.1 Optional capability degradation is never exit 2

Explicitly frozen, because it is the easiest thing to get wrong:

```text
UsageStatistics unavailable
  → DBH004 recorded as SkippedUnavailableCapability
  → DBH001, DBH002, DBH003, DBH005 complete normally
  → HasErrors remains false (a skip is not a failure)
  → exit code determined from the findings that were produced
```

A skipped optional diagnostic must **never** force exit 2 by itself. `HasErrors` is true only
for `Failed` executions; `SkippedUnavailableCapability` does not set it.

### 13.2 The framework's parse-error exit code is overridden

**Empirical finding.** Pinned System.CommandLine 2.0.10 returns exit **1** for a parse error.
In this contract `1` means "the inspection succeeded and found something worth attention",
which is exactly the wrong signal for a command that never ran.

The CLI therefore inspects the parse result before invoking anything and returns
**`2`** when the parse produced errors, overriding the framework default.

This is intentional application behavior at the CLI boundary. It is **not** a reason to
upgrade, replace or add a dependency; the pinned version is used as-is.

## 14. Cancellation

`Ctrl+C` must propagate into the existing provider and orchestrator path via a
`CancellationToken`, using the console cancellation event to trigger a `CancellationTokenSource`.

- No background processing, no worker, no host.
- The orchestrator already checks cancellation before capture, after capture, and before and
  after every rule, and propagates without returning a partial result.
- The provider already rolls back and disposes its session on cancellation, so **no transaction
  is left open**. The CLI must still dispose the provider (`await using`) on every path.
- The CLI must not report a partial result as if it were complete.

No canonical cancellation exit value exists in this project. Rather than invent a fourth code,
**cancellation maps to exit 2** for this MVP.

## 15. Testing strategy

The smallest matrix that proves the command works. CLI-level tests should exercise the
composed command handler; only §15.6 needs a real database.

### 15.1 Help tree
- `dbhealth --help`, `dbhealth inspect --help`, `dbhealth inspect postgresql --help` all
  succeed and list the §6.1 options.
- The `--connection` help text carries the shell-history warning.
- `dbhealth inspect` with no subcommand exits 2.

### 15.2 Connection resolution
- `--connection` wins over both environment sources.
- `--connection-env NAME` wins over `DBHEALTH_CONNECTION`.
- `DBHEALTH_CONNECTION` is used when neither option is supplied.
- `--connection-env NAME` where `NAME` is unset → exit 2, **no** fallback to
  `DBHEALTH_CONNECTION` (assert the fallback value was *not* used).
- `--connection-env NAME` where `NAME` is blank → exit 2.
- No connection anywhere → exit 2 with the fixed message.
- **No secret echoed:** given a connection string containing a recognizable password token,
  assert that token appears in neither stdout nor stderr on both the success and failure paths.

### 15.3 Threshold parsing and conversion
- Omitted → `DiagnosticThresholds.Default` is passed through unchanged and its values reach
  the rules.
- Each override applied exactly, with the other two left at their defaults.
- **Binary-unit conversion:** `--large-table-size-threshold-mb 1024` yields exactly
  `1_073_741_824`, and `--unused-index-size-threshold-mb 10` yields exactly `10_485_760` —
  proving the two byte defaults round-trip through the CLI.
- `--large-table-row-threshold` is **not** converted: the supplied value reaches
  `LargeTableRowThreshold` unchanged.
- `0` rejected → exit 2 (each of the three options).
- Negative rejected → exit 2.
- Non-numeric rejected → exit 2.
- Out-of-`Int64`-range input rejected → exit 2.
- **Overflow in the `checked` megabyte multiplication** (for example `Int64.MaxValue`, or any
  value above ~8.8 × 10^12) → exit 2 with the fixed usage message; no `OverflowException`
  text reaches the user.

### 15.3.1 Error-boundary secret containment
- A seam or fake made to throw an exception whose `Message` contains the sentinel
  `SUPER_SECRET_SENTINEL` must produce **only** the fixed CLI error text and **exit 2**.
- Assert the sentinel appears in neither stdout nor stderr.
- Repeat for an exception carrying the sentinel in `InnerException.Message` and in
  `Exception.Data`, since §12.2 prohibits surfacing those too.
- Repeat for an `ArgumentException` carrying the sentinel.

### 15.3.2 Parse-diagnostic secret containment — required regression test

This case is a discovered defect in the pinned framework (§12.4), so it is a **required**
acceptance test rather than an optional one.

Invoke the real command tree with a deliberately mistyped option and a sentinel-bearing token:

```text
inspect postgresql --connectio "Host=db;Username=admin;Password=SUPER_SECRET_SENTINEL"
```

Assert all of:

- exit code is **2**;
- **stdout does not contain** `SUPER_SECRET_SENTINEL`;
- **stderr does not contain** `SUPER_SECRET_SENTINEL`;
- neither stream contains the full supplied token;
- stderr contains the exact fixed text `The command line could not be understood.`

Also cover the same sentinel supplied as a **stray positional argument**, which System
.CommandLine likewise reports as an unmatched token.

A test that only checks the exit code would pass against the unsafe framework default; the
stream assertions are what make this regression test meaningful.

### 15.4 Exit-code mapping
- Zero findings → 0.
- Info-only → 0.
- At least one Warning → 1.
- At least one Critical → 1.
- Usage failure / connection failure / inspection failure → 2.
- `HasErrors` from a failed diagnostic → 2.
- **DBH004 skipped for unavailable `UsageStatistics`, everything else clean → 0, not 2.**

### 15.5 Console rendering
- Zero findings prints the explicit §11.3 text, including the "does not guarantee" sentence.
- A positive finding renders severity, code, object identity, message, confidence, evidence and
  recommendation.
- No ANSI escape sequence appears in the output.
- Diagnostics render in `DBH001…DBH005` order and repeated runs over the same result produce
  byte-identical output.
- A skipped diagnostic renders as skipped, with its capability named — asserted not to read as
  a completed zero-finding run.

### 15.6 One PostgreSQL-backed functional path

Reuse the **existing** integration fixtures and pinned images. Do not build a new Docker
harness, do not add a fixture, do not add or modify SQL.

The test must prove the whole chain: real PostgreSQL → snapshot provider → `ApprovedDiagnostics`
→ orchestrator → CLI composition → visible console output → exit code.

## 16. earendel_db empirical evidence — NON-CANONICAL

A manual end-to-end experiment was run against a real application database before this
definition. It is **design input and acceptance evidence only**.

| Observation | Value |
|---|---|
| Database / container | `earendel_db` in `erp-postgres` |
| PostgreSQL | 17.8 |
| Snapshot | 3 schemas, 35 tables, 93 indexes |
| Capabilities | `CatalogMetadata` Available; `UsageStatistics` Available; `DataProfiling` Disabled by policy |
| Diagnostics | DBH001–DBH005 all `Completed`, 0 findings each |
| Summary | Info 0, Warning 0, Critical 0, `OverallRisk.None`, `HasErrors` false |

All counts were independently cross-checked against `pg_catalog`; the snapshot's 35/93 matched
the catalog exactly, and 0 tables lacked a primary key, 0 indexes were invalid, 0 duplicate
index groups existed, the largest table was ~991 KB and the largest index ~128 KB.

**What it establishes:** the internal pipeline works end to end on PostgreSQL 17.8 — a major
version neither pinned integration suite covers.

**How it shaped this definition:** the real first-contact experience of this tool is a
*zero-findings* result. That made §11.3 a hard requirement rather than a nicety — a bare
"0 findings" would read as "your database is fine", which is not what five structural rules
finding nothing means. It also produced the most instructive detail in the run: **22 indexes
had `ScanCount == 0` but every one sat below the 10 MiB DBH004 floor.** The console must make a
threshold-suppressed result comprehensible rather than mysterious.

**Hard boundaries.** Do not add `earendel_db` credentials anywhere. Do not make it a CI
dependency. Do not hard-code its counts in any automated test. Do not modify it. Do not treat
it as a project fixture. It is referenced here as a dated observation and nothing more.

## 17. Positive-path acceptance

Because the reference database is clean, a zero-findings path alone would leave the finding
renderer unproven. The implementation must therefore validate the console against **both**:

1. A zero-findings result (§11.3).
2. A result with at least one finding actually rendered.

Use the **existing** integration fixtures, which already build tables and indexes carrying the
approved defects. **Do not inject defects into `earendel_db`.** Do not create a new fixture
database.

The correctness of the findings themselves belongs to GC-DHI-05A and is already closed. This
gate proves only that findings which exist reach the console intact.

## 18. Documentation and package metadata

Minimal, and required — this gate is the moment the product's own documentation stops being
true.

| File | Current claim | Required change |
|---|---|---|
| `README.md` | *"repository bootstrap only. This build does not inspect a database…"* | Remove the bootstrap-only claim; add a minimal `dbhealth inspect postgresql` quick-start with `DBHEALTH_CONNECTION`, state the exit codes, and state that the `-mb` threshold options use binary units of 1,048,576 bytes. |
| `README.es.md` | *"únicamente bootstrap del repositorio… no inspecciona bases de datos"* | The same, in Spanish, kept in step with the English file. |
| `DbHealthInspector.Cli.csproj` | `Description`: *"Bootstrap baseline; inspection features are not implemented yet."*; `PackageReleaseNotes`: *"Repository bootstrap and technical baseline only. No database inspection behavior is included."* | Both must stop denying that inspection exists. |

Out of scope: rewriting the documentation set, per-diagnostic reference pages, JSON examples,
and screenshots. Those follow the reporting gate.

## 19. Deferred work

Each is `DEFERRED — NOT REQUIRED FOR FUNCTIONAL MVP`:

`RPT-01` JSON report · `RPT-02` atomic report writing · `--output` · report path in the console
summary · JSON Schema · report history · HTML · CSV · dashboards · `--schema` ·
`--exclude-schema` · `--statement-timeout-seconds` · `--target-label` · `--verbose` · colour
output · progress indicators · `dbhealth inspect` for any other engine.

## 20. Prohibited work

GC-DHI-05B must **not**:

- Modify DBH001–DBH005 semantics, or the `DiagnosticThresholds` default values.
- Modify `InspectionOrchestrator`, `OverallRiskCalculator`, `InspectionResult` or
  `InspectionSummary`.
- Add PostgreSQL productive SQL, or modify the frozen inventory (`B001`–`B003`, `C001`–`C004`,
  `D001`, `E001`–`E002`).
- Weaken read-only safety, or expose adapter internals to widen the two-type public surface.
- Add any package dependency unless a concrete blocker is proven and recorded.
- Add `Microsoft.Extensions.Hosting`, a DI container, a configuration framework, a logging
  framework, a plugin system, a provider registry, a command bus, a mediator, a generic engine
  abstraction, a console-rendering library, a JSON package, or an output-pipeline abstraction.
- Implement JSON or file reports; add `DBH006`+; add another database engine; add a GUI, an
  API, or history; add automatic repair; execute DDL or DML; query business rows.

## 21. Decisions frozen by this gate

| ID | Decision |
|---|---|
| D-05B-01 | Command tree is `dbhealth inspect postgresql`; `dbhealth inspect` alone is exit 2. |
| D-05B-02 | Connection precedence `--connection` → `--connection-env` → `DBHEALTH_CONNECTION`; an explicitly named but missing/blank variable fails and does **not** fall through. |
| D-05B-03 | Threshold options keep the canonical `PROJECT_RULES.md` §9 names, including `--large-table-size-threshold-mb` and `--unused-index-size-threshold-mb`. One `-mb` unit is exactly 1,048,576 bytes, converted with `checked` multiplication; overflow is a usage failure (§8). |
| D-05B-04 | Schema-filter options deferred; provider default (`IncludeEverything`) is used. |
| D-05B-05 | Timeout options deferred; `PostgreSqlInspectionSessionOptions.Default` is used. |
| D-05B-06 | Console layout and required fields per §11. |
| D-05B-07 | Zero findings prints the explicit two-sentence §11.3 text. |
| D-05B-08 | Exit codes 0/1/2 exactly; skipped optional diagnostic never forces 2. |
| D-05B-09 | Cancellation maps to exit 2; no fourth exit code. |
| D-05B-10 | The CLI maps `OperationCanceledException`, `ArgumentException`/`ArgumentOutOfRangeException` and — as a **total** fallback — every other exception onto fixed strings it authors itself. It never prints `exception.Message`, `ToString()`, a stack trace, `InnerException` or `Exception.Data` (§12). |
| D-05B-11 | Database name, engine and engine version are displayed; host, port and username are not; `Metadata.CurrentUser` is deliberately not rendered. |
| D-05B-12 | `CLI-03` is completed **partially**; `RPT-01`/`RPT-02` remain untouched and unclaimed. |
| D-05B-13 | Both READMEs and the CLI package `Description`/`PackageReleaseNotes` are updated; nothing else is. |
| D-05B-14 | **(D2)** The CLI owns the parse-error surface. System.CommandLine's default parse diagnostics are suppressed because they echo unmatched tokens verbatim; the CLI emits fixed token-free text and exit 2 instead. Help is unaffected (§12.4, §12.5). |
| D-05B-15 | **(D2)** The framework's parse-error exit code (`1` in 2.0.10) is overridden to `2` at the CLI boundary. No dependency is added or upgraded (§13.2). |

## 22. Conflicts with canonical material

Three genuine conflicts were found. None is resolved silently.

### C-1 — Threshold option naming — **RESOLVED**

An earlier revision of this candidate proposed `-bytes` option names, which would have
contradicted the `--large-table-size-threshold-mb` and `--unused-index-size-threshold-mb`
entries already approved in `PROJECT_RULES.md` §9.

**Resolution (human technical decision):** the canonical §9 names are kept. `PROJECT_RULES.md`
is **not** amended — the approved public CLI names already exist there and remain
authoritative.

The unit ambiguity that motivated the `-bytes` proposal is closed by fixing the factor rather
than renaming the option: one `-mb` unit is **exactly 1,048,576 bytes** (§8.1). This keeps the
approved public surface stable *and* makes both byte defaults exactly reproducible from the
command line (`1024` and `10`), so no precision is lost at the boundary. Conversion is a single
`checked` multiplication whose overflow is a usage failure.

No conflict remains. `--large-table-row-threshold` was identical in both proposals and was
never in conflict.

### C-2 — `CLI-03` requires "Report path" and "Target label"

`CLI-03`'s acceptance list includes both. "Report path" depends on `RPT-01`/`RPT-02`, which this
gate defers; "Target label" depends on `--target-label`, which is not in this gate's option set.

**Smallest resolution:** `CLI-03` is satisfied **partially** (§5.1). Both fields arrive with the
reporting gate. `CLI-03` must not be marked complete.

### C-3 — `CLI-01` says "Approved options are present"

`PROJECT_RULES.md` §9 approves eleven options for v0.1.0; this gate implements five.

**Classification — intentional partial scope, not a blocker.** The §9 list is the **v0.1.0**
contract, not the GC-DHI-05B contract. `CLI-01` is satisfied for the option subset this gate
defines. **No approved option is renamed, removed or redefined by this gate** — the three
threshold options use their canonical §9 names exactly (§8), and the following six remain
approved-but-deferred:

| Approved option | Status after GC-DHI-05B |
|---|---|
| `--output` | Deferred with `RPT-01`/`RPT-02`. |
| `--schema` | Deferred (§9 of this document). |
| `--exclude-schema` | Deferred (§9 of this document). |
| `--statement-timeout-seconds` | Deferred (§9 of this document). |
| `--target-label` | Deferred with the console "Target label" field. |
| `--verbose` | Deferred. |

None of these may be claimed as completed by GC-DHI-05B.

### C-4 — Framework parse diagnostics versus secret redaction — **RESOLVED IN D2**

The D1 revision of §12.1 permitted "System.CommandLine's own parse diagnostics", with a
parenthetical assuming only the offending *option name* would be echoed. Implementation proved
that assumption false: 2.0.10 echoes unmatched **token values** verbatim, so a mistyped option
name discloses the following argument — a connection string with its password — on standard
error.

That directly contradicted §12.2, which forbids the connection string ever reaching the
console. Two clauses of the same document could not both hold.

**Resolution.** §12.2 wins; it carries the security invariant. Framework parse diagnostics are
suppressed, and the CLI emits fixed token-free diagnostics with exit 2 (§12.4). The permissive
D1 wording is removed. Help is untouched (§12.5), and a required regression test locks the
behavior in (§15.3.2).

**Reason recorded for posterity:** default framework diagnostics can echo caller-controlled
values and violate secret-redaction invariants. Security invariants take precedence over
framework-default usability.

## 22a. Accepted limitations

Neither is a defect, and neither may be "fixed" by widening scope.

### L-1 — Size-threshold granularity

The approved public size options take positive integers in units of 1,048,576 bytes, so the
smallest expressible override is exactly **1,048,576 bytes**. A database whose largest index or
table sits below that cannot be probed with a finer threshold.

This is an accepted consequence of the v0.1.0 option contract (§8), observed in practice: on a
real reference database the largest index was 128 KiB, below the smallest expressible DBH004
floor. **Do not introduce byte or MiB aliases** to work around it — that reopens C-1.

### L-2 — Help localization

System.CommandLine renders its own help labels — section headings, the built-in `--help`
description — according to the runtime and OS locale, while every description authored in this
repository is English. Help output can therefore mix languages on a non-English machine.

Accepted for GC-DHI-05B. **Do not add localization infrastructure.** Tests must assert only on
repository-authored strings and on exit codes, never on framework-rendered labels.

## 23. Completion criteria

GC-DHI-05B is `COMPLETE` when all of the following hold:

1. `dbhealth inspect postgresql` connects to a real PostgreSQL database and prints a complete
   diagnosis per §11.
2. All three `--help` levels work and carry the §6.2 secret warning.
3. Connection resolution follows §7 exactly, including the no-fallthrough rule.
4. The three threshold options use their canonical `PROJECT_RULES.md` §9 names; the two `-mb`
   options convert with `checked(value * 1_048_576)`; `1024` and `10` reproduce the byte
   defaults exactly; non-positive values and conversion overflow are rejected as usage
   failures; and omitting all three passes `DiagnosticThresholds.Default` through unchanged.
4a. Help text and both READMEs state that the `-mb` options use binary units of 1,048,576
   bytes.
4b. No CLI output path prints `exception.Message`, `ToString()`, a stack trace,
   `InnerException` or `Exception.Data`, proven by the §15.3.1 sentinel-secret tests.
4c. No CLI output path emits a System.CommandLine parse diagnostic containing a
   caller-controlled token; a parse failure prints only the fixed §12.4 text and exits 2,
   proven by the §15.3.2 regression test.
5. Composition goes through `InspectionOrchestrator`; no rule is invoked directly.
6. Exit codes follow §13, including the skipped-diagnostic exemption.
7. Zero findings print the explicit §11.3 text.
8. The §15 test matrix passes, including one real-PostgreSQL path and one positive-finding
   rendering.
9. No secret appears in any output on any path, proven by test.
10. Both READMEs and the CLI package metadata no longer claim inspection is unimplemented.
11. Release build: 0 warnings, 0 errors; `dotnet format` passes; all existing tests still pass.
12. Frozen SQL inventory byte-identical; PostgreSQL exported types still exactly two.
13. No dependency added.
14. `RPT-01` and `RPT-02` remain unimplemented and unclaimed; `CLI-03` recorded as partial.

## 24. Human authorization boundary

This document authorizes **nothing**. It is a definition candidate.

Implementation of GC-DHI-05B requires explicit human authorization. All three recorded
conflicts are **resolved**: C-1 by the owner's decision to keep the canonical `-mb` option
names with a fixed binary unit factor, and C-2/C-3 as intentional partial scope. No open
question blocks implementation.

Completion of GC-DHI-05B authorizes no release, tag or NuGet publication. The recommended
successor is the reporting gate covering `RPT-01` and `RPT-02`, which also completes `CLI-03`.
