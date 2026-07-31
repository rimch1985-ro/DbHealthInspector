# Initial Backlog — DbHealth Inspector

**Backlog version:** 0.1  
**Status:** Approved baseline  
**Target:** v0.1.0  
**Current next gate:** GC-DHI-04A

---

## Priority model

| Priority | Meaning |
|---|---|
| P0 | Required for the target gate or release |
| P1 | Important but may follow the minimum vertical path |
| P2 | Optional improvement; not release blocking unless promoted |

---

# Phase 1 — Governance and Bootstrap

## GOV-01 — Install governance baseline

**Priority:** P0  
**Gate:** GC-DHI-02

### Objective

Install the canonical project rules, project state, ADRs and agent instructions in the repository.

### Acceptance criteria

- `AGENTS.md` exists at repository root.
- `PROJECT_RULES.md` exists under `docs/agent-governance/`.
- `PROJECT_STATE.md` exists under `docs/agent-governance/`.
- ADR-0001 through ADR-0003 exist and are marked Accepted.
- Document links are valid.
- No contradictions exist among canonical documents.

---

## GOV-02 — Install agent operating model

**Priority:** P0  
**Gate:** GC-DHI-02

### Objective

Install and enforce the approved separation between Claude Code and Codex.

### Acceptance criteria

- `AGENT_OPERATING_MODEL.md` exists.
- Claude Code is documented as primary programmer.
- Codex is documented as DevOps and integration controller.
- Claude Code has no remote GitHub, merge, tag or release authority.
- Codex review and integration outputs are defined.
- Claude-to-Codex handoff format is documented.
- Prompts reference canonical documents to minimize token consumption.

---

## FND-01 — Create .NET solution baseline

**Priority:** P0  
**Gate:** GC-DHI-02

### Objective

Create the .NET 10 solution with approved production and test projects.

### Acceptance criteria

- `DbHealthInspector.Core` exists.
- `DbHealthInspector.PostgreSql` exists.
- `DbHealthInspector.Cli` exists.
- `DbHealthInspector.UnitTests` exists.
- `DbHealthInspector.IntegrationTests` exists.
- Project references follow ADR-0003.
- Restore succeeds.
- Build succeeds without warnings.
- Tests execute successfully.

---

## FND-02 — Configure repository-wide build rules

**Priority:** P0  
**Gate:** GC-DHI-02

### Objective

Establish deterministic, strict and centrally managed build configuration.

### Acceptance criteria

- `global.json` pins an approved .NET 10 SDK.
- `Directory.Build.props` enables nullable references.
- Warnings are treated as errors.
- Recommended analyzers are enabled.
- Deterministic and CI build flags are enabled.
- `Directory.Packages.props` centralizes dependency versions.
- `.editorconfig` exists.
- Build works on Windows and Linux.

---

## FND-03 — Select and review dependencies

**Priority:** P0  
**Gate:** GC-DHI-02

### Objective

Select exact package versions and document licensing.

### Candidate dependencies

- System.CommandLine.
- Npgsql.
- xUnit.
- Microsoft.NET.Test.Sdk.
- Testcontainers.PostgreSql.
- JSON Schema validation package only if justified.

### Acceptance criteria

- Exact versions are centrally pinned.
- License is recorded for each direct dependency.
- No dependency has unresolved commercial-use ambiguity.
- No unnecessary runtime dependency is introduced.
- Package restore has no known Critical vulnerability.

---

## FND-04 — Configure CLI package metadata

**Priority:** P1  
**Gate:** GC-DHI-02

### Objective

Prepare the CLI project for eventual packaging as a global tool.

### Acceptance criteria

- Tool command is `dbhealth`.
- Package ID is validated or an alternative is documented.
- Authors, description, repository URL and license metadata are present.
- Package is not publicly published.
- A local package can be generated successfully.

---

## CI-01 — Add bootstrap CI

**Priority:** P0  
**Gate:** GC-DHI-02

### Objective

Validate restore, build and tests on every pull request.

### Acceptance criteria

- Ubuntu job restores, builds and tests.
- Windows job restores, builds and tests.
- Warnings fail the build.
- CI uses the pinned SDK.
- CI passes on the bootstrap commit.

---

# Phase 2 — Core Contracts

## CORE-01 — Implement finding model

**Priority:** P0

### Acceptance criteria

The core model supports:

- Stable finding code.
- Rule version.
- Category.
- Severity.
- Confidence.
- Object reference.
- Message.
- Recommendation.
- Evidence.
- Documentation reference.
- Stable fingerprint.

All required values are validated.

---

## CORE-02 — Implement snapshot model

**Priority:** P0

### Acceptance criteria

The model represents:

- Database metadata.
- Schema metadata.
- Table metadata.
- Index metadata.
- Relevant statistics.
- Capability state.
- Statistics reset timestamp where available.

No Npgsql type appears in Core.

---

## CORE-03 — Implement diagnostic rule contract

**Priority:** P0

### Acceptance criteria

- Rules accept engine-neutral snapshots.
- Rules return deterministic findings.
- Rule identity and version are explicit.
- Rules do not perform I/O.
- Rules are independently unit-testable.

---

## CORE-04 — Implement inspection orchestration

**Priority:** P0

### Acceptance criteria

The orchestrator:

- Requests a snapshot.
- Executes enabled rules.
- Records diagnostic execution status.
- Builds summary counts.
- Calculates overall risk.
- Returns an immutable result.
- Supports cancellation.

---

## CORE-05 — Implement stable fingerprint

**Priority:** P1

### Acceptance criteria

- Same logical finding produces the same fingerprint.
- Order-dependent evidence does not change the fingerprint.
- Sensitive values are excluded.
- Algorithm and canonical input are documented.
- Unit tests include stability and collision-oriented cases.

---

# Phase 3 — PostgreSQL Metadata Adapter

## PG-01 — Implement connection factory

**Priority:** P0
**Gate:** GC-DHI-04A

### Acceptance criteria

- Uses `NpgsqlDataSource`.
- Supports connection string from approved CLI sources.
- Does not log secrets.
- Supports cancellation.
- Produces sanitized connection metadata.

---

## PG-02 — Implement read-only inspection session

**Priority:** P0
**Gate:** GC-DHI-04B

### Acceptance criteria

- Begins an explicit transaction.
- Sets the transaction read-only.
- Applies statement timeout.
- Applies lock timeout.
- Applies idle transaction timeout.
- Rolls back safely on failure.
- Safety test proves write statements fail.

---

## PG-03 — Implement server capability probe

**Priority:** P0
**Gate:** GC-DHI-04C

### Acceptance criteria

The probe returns:

- PostgreSQL version.
- Database name.
- Current user.
- Availability of catalog metadata.
- Availability of required statistics.
- Statistics reset timestamp when available.
- Supported/unsupported version state.

Missing optional statistics produce capability status, not silent omission.

---

## PG-04 — Implement table snapshot query

**Priority:** P0
**Gate:** GC-DHI-04D

### Acceptance criteria

The query returns:

- Schema.
- Table name.
- Relation kind.
- Partition state.
- Estimated rows.
- Table size.
- Index size.
- Total size.
- Primary-key state.

System schemas and excluded relations are filtered correctly.

---

## PG-05 — Implement index snapshot query

**Priority:** P0
**Gate:** GC-DHI-04E

### Acceptance criteria

The query returns sufficient metadata for:

- Index validity.
- Readiness.
- Liveness.
- Uniqueness.
- Primary-key support.
- Constraint association.
- Access method.
- Key columns.
- Included columns.
- Expressions.
- Predicate.
- Collation.
- Operator classes.
- Scan count.
- Size.

---

## PG-06 — Enforce SQL safety allowlist

**Priority:** P0
**Gate:** GC-DHI-04B and final verification in GC-DHI-04F

### Acceptance criteria

- All production SQL resources are inventoried.
- A test rejects prohibited statement classes.
- No user-provided SQL is accepted.
- Schema filters are parameterized.
- Safety documentation references the mechanism.

---

# Phase 4 — Diagnostic Rules

## RULE-01 — DBH001 TABLE_WITHOUT_PRIMARY_KEY

**Priority:** P0

### Acceptance criteria

- Detects ordinary user tables without a primary key.
- Handles partitioned roots according to the approved rule.
- Excludes partitions, views, foreign tables and system objects.
- Returns Warning/High.
- Includes schema, table, size and estimated rows.
- Unit and integration tests pass.

---

## RULE-02 — DBH002 LARGE_TABLE

**Priority:** P0

### Acceptance criteria

- Triggers on estimated-row threshold or total-size threshold.
- Does not execute `COUNT(*)`.
- Returns Info/Medium.
- Evidence identifies the threshold exceeded.
- Thresholds are validated.
- Unit and integration tests pass.

---

## RULE-03 — DBH003 EXACT_DUPLICATE_INDEX

**Priority:** P0

### Acceptance criteria

- Detects exact structural equivalence on the same table.
- Considers access method, keys, include columns, expressions, predicates, uniqueness, collation and operator classes.
- Does not classify prefix indexes as exact duplicates.
- Returns Warning/High.
- Reports both index identities and sizes.
- Unit and integration tests cover edge cases.

---

## RULE-04 — DBH004 UNUSED_INDEX_CANDIDATE

**Priority:** P0

### Acceptance criteria

- Requires `idx_scan = 0`.
- Applies minimum-size threshold.
- Excludes primary-key and unique indexes.
- Requires valid and live index state.
- Includes statistics reset evidence when available.
- Uses Info severity.
- Confidence reflects statistics context.
- Recommendation explicitly forbids automatic deletion.
- Unit and integration tests pass.

---

## RULE-05 — DBH005 INVALID_INDEX

**Priority:** P0

### Acceptance criteria

- Detects `indisvalid = false`.
- Captures readiness and liveness states.
- Returns Critical/High.
- Recommendation is non-destructive.
- Unit tests pass.
- Integration fixture is implemented or the limitation is documented and covered by a focused test strategy.

---

# Phase 5 — CLI and Reporting

## CLI-01 — Implement command tree

**Priority:** P0

### Acceptance criteria

- `dbhealth --help` works.
- `dbhealth inspect --help` works.
- `dbhealth inspect postgresql --help` works.
- Approved options are present.
- Invalid values produce exit code 2.
- Help text warns against command-line secrets.

---

## CLI-02 — Implement connection resolution

**Priority:** P0

### Acceptance criteria

Precedence is:

1. `--connection`.
2. Variable named by `--connection-env`.
3. `DBHEALTH_CONNECTION`.

Missing connection produces a clear error without exposing secrets.

---

## CLI-03 — Implement console summary

**Priority:** P0

### Acceptance criteria

The summary displays:

- Target label.
- Engine and version.
- Schemas analyzed.
- Tables analyzed.
- Indexes analyzed.
- Findings by severity.
- Overall risk.
- Report path.
- Partial capability warning when applicable.

Output remains readable without color support.

---

## RPT-01 — Implement JSON report 0.1

**Priority:** P0

### Acceptance criteria

- Matches the approved report contract.
- Uses UTC timestamps.
- Contains capability status.
- Contains diagnostic execution status.
- Contains deterministic ordering.
- Contains no secrets.
- Validates against JSON Schema 0.1.

---

## RPT-02 — Implement atomic report writing

**Priority:** P0

### Acceptance criteria

- Writes to a temporary file.
- Flushes and replaces/moves to final path.
- Does not leave a misleading partial report on failure.
- Creates parent directory when appropriate.
- Output failure maps to exit code 2.

---

## CLI-04 — Implement exit-code mapping

**Priority:** P0

### Acceptance criteria

- Info-only result returns 0.
- Warning returns 1.
- Critical returns 1.
- Usage failure returns 2.
- Connection failure returns 2.
- Required-inspection failure returns 2.
- Optional unavailable diagnostic does not automatically return 2.

---

# Phase 6 — Demo, QA and Documentation

## DEMO-01 — Create PostgreSQL Docker demo

**Priority:** P0

### Acceptance criteria

- Docker Compose starts PostgreSQL reliably.
- Demo uses synthetic data only.
- DBH001–DBH004 scenarios are reproducible.
- Demo README contains exact commands.
- Health check is configured.
- Reset procedure is documented.

---

## DEMO-02 — Define invalid-index laboratory scenario

**Priority:** P1

### Acceptance criteria

- Scenario is separated from normal demo initialization.
- It cannot be confused with production guidance.
- It is reproducible or explicitly marked as best-effort.
- It is never created by the DbHealth Inspector CLI.

---

## QA-01 — Add safety contract suite

**Priority:** P0

### Acceptance criteria

Tests verify:

- Read-only transaction state.
- Write command rejection.
- No schema changes.
- No control-row changes.
- SQL allowlist.
- Secret redaction.
- No business-table row query in production SQL.

---

## QA-02 — Add CLI process tests

**Priority:** P0

### Acceptance criteria

- Help commands.
- Exit codes 0/1/2.
- Invalid arguments.
- Missing connection.
- Report generation.
- Output failure.
- Redaction behavior.

---

## QA-03 — Add PostgreSQL version matrix

**Priority:** P0

### Acceptance criteria

Integration tests pass on:

- PostgreSQL 15.
- PostgreSQL 18.

Any version-specific query branch is tested.

---

## DOC-01 — Create bilingual README

**Priority:** P0

### Acceptance criteria

Both README files include:

- Product value.
- Safety statement.
- Installation.
- Quick start.
- Docker demo.
- Diagnostics.
- Exit codes.
- Limitations.
- Security guidance.
- Roadmap.
- Contribution links.

---

## DOC-02 — Document diagnostics

**Priority:** P0

### Acceptance criteria

DBH001–DBH005 each have a document covering:

- Meaning.
- Detection method.
- Severity.
- Confidence.
- Evidence.
- Limitations.
- False-positive considerations.
- Non-destructive recommendation.
- JSON example.

---

## DOC-03 — Document permissions and safety

**Priority:** P0

### Acceptance criteria

Documents explain:

- Recommended PostgreSQL role.
- Optional statistics privilege.
- Read-only transaction.
- Secret handling.
- Metadata-only limitation.
- Unsupported operations.
- Capability degradation.

---

# Phase 7 — Release Candidate

## REL-01 — Prepare v0.1.0-rc.1

**Priority:** P0

### Acceptance criteria

- All v0.1.0 functional criteria pass.
- CI is green on the exact release commit.
- Package installs locally.
- Package metadata references the exact commit.
- Annotated tag is created after authorization.
- SHA-256 is generated.
- Release notes list features and limitations.
- Demo report is attached.
- Release is marked prerelease.

---

## REL-02 — Observation and stable release

**Priority:** P0

### Acceptance criteria

- RC installation is tested on Windows and Linux.
- Demo is executed from clean instructions.
- No Critical defects remain open.
- Documentation is corrected where necessary.
- Stable tag points to the approved commit.
- `v0.1.0` package and release are published only after explicit authorization.

---

# Deferred backlog

The following items are intentionally deferred:

- Controlled null-ratio profiling.
- Sampling.
- Foreign-key warnings.
- FK supporting-index checks.
- Unvalidated constraints.
- SQL Server adapter.
- HTML report.
- Markdown report.
- Historical comparison.
- Finding suppression.
- Baseline acceptance.
- Query plans.
- Continuous monitoring.
