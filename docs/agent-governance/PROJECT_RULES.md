# PROJECT_RULES — DbHealth Inspector

**Version:** 0.2  
**Status:** Approved baseline  
**Gate:** GC-DHI-01 approved  
**Effective date:** 2026-07-28

---

## 1. Product identity

**Product name:** DbHealth Inspector  
**CLI command:** `dbhealth`  
**Initial package name:** `DbHealthInspector.Tool`  
**Product type:** Open-source command-line diagnostic utility  
**Initial engine:** PostgreSQL  
**Initial runtime:** .NET 10

### Product statement

> DbHealth Inspector is a read-only CLI that inspects PostgreSQL metadata, detects structural and indexing risks, and generates evidence-based diagnostic reports without modifying database objects or business data.

---

## 2. Product principles

The following principles are mandatory:

1. **Read-only always.**
2. **Metadata-only in v0.1.0.**
3. **PostgreSQL first.**
4. **Findings and evidence before recommendations.**
5. **Human decisions always.**
6. **No automatic database changes.**
7. **Safe by default.**
8. **Small enough to finish.**
9. **Deterministic reports where the database state is unchanged.**
10. **Documentation is part of the product.**

---

## 3. Official agent model

### Claude Code — Primary programmer

Claude Code implements authorized product code, refactoring, feature tests, related documentation and local validation. It produces a structured handoff to Codex.

Claude Code is not authorized to perform remote GitHub operations, merge, tag, release or publish packages.

### Codex — DevOps and integration controller

Codex enforces technical direction, reviews Claude Code changes, manages CI/CD, reviews dependencies and security, performs authorized GitHub integration and handles release engineering.

Codex must not duplicate substantial feature implementation. Significant product defects are returned to Claude Code through focused correction prompts.

Canonical model:

```text
docs/agent-governance/AGENT_OPERATING_MODEL.md
```

Human authorization remains the final gate for scope, architecture exceptions and releases.

---

## 4. v0.1.0 approved scope

v0.1.0 shall include:

- `dbhealth inspect postgresql`.
- PostgreSQL 15–18 support.
- Connection through Npgsql.
- Explicit read-only transactions.
- Metadata and statistics inspection.
- Schema include/exclude filters.
- Five diagnostic rules.
- Console summary.
- Versioned JSON report.
- Stable finding codes.
- Exit codes `0`, `1` and `2`.
- Capability reporting for unavailable diagnostics.
- Secret redaction.
- Configurable thresholds.
- Docker Compose demo.
- Unit tests.
- Integration tests against real PostgreSQL containers.
- Safety contract tests.
- GitHub Actions.
- English and Spanish README files.
- Diagnostic documentation.
- Security documentation.
- Packaging as a .NET global tool.
- MIT license.

---

## 5. Approved diagnostics

| Code | Stable name | Default severity | Confidence |
|---|---|---:|---:|
| DBH001 | `TABLE_WITHOUT_PRIMARY_KEY` | Warning | High |
| DBH002 | `LARGE_TABLE` | Info | Medium |
| DBH003 | `EXACT_DUPLICATE_INDEX` | Warning | High |
| DBH004 | `UNUSED_INDEX_CANDIDATE` | Info | Low/Medium |
| DBH005 | `INVALID_INDEX` | Critical | High |

Finding codes are stable public identifiers. They must not be reused for a different meaning.

---

## 6. Explicitly excluded from v0.1.0

The following are not permitted in v0.1.0:

- Other database engines.
- Data-row scanning.
- Null-ratio calculation.
- Sampling business data.
- Foreign-key inference.
- Missing-index recommendations.
- Query-plan analysis.
- `pg_stat_statements` dependency.
- Query-performance monitoring.
- Real-time monitoring.
- Historical report persistence.
- HTML or Markdown reporting.
- Web dashboard.
- REST API.
- Authentication.
- Multi-user capabilities.
- Automatic remediation.
- Automatic DDL or DML.
- AI features.
- Plugin architecture.
- Generic repository patterns.
- Entity Framework Core.
- MediatR.
- AutoMapper.
- A full application host unless justified by an accepted ADR.

Scope expansion requires a new approved gate and an ADR when architectural.

---

## 7. Architecture rules

The approved production structure is:

```text
src/
├── DbHealthInspector.Core
├── DbHealthInspector.PostgreSql
└── DbHealthInspector.Cli
```

The approved test structure is:

```text
tests/
├── DbHealthInspector.UnitTests
└── DbHealthInspector.IntegrationTests
```

### Dependency direction

```text
Cli -> Core
Cli -> PostgreSql
PostgreSql -> Core
Core -> no infrastructure dependency
```

### Core responsibilities

- Findings.
- Evidence.
- Severities.
- Confidence.
- Database snapshots.
- Diagnostic rules.
- Inspection orchestration.
- Risk calculation.
- Report model abstractions.

### PostgreSQL adapter responsibilities

- Connection and capability probing.
- Read-only session configuration.
- PostgreSQL catalog queries.
- Statistics queries.
- Mapping PostgreSQL metadata to core snapshots.
- Connection string redaction support.

### CLI responsibilities

- Commands and options.
- Input validation.
- Dependency composition.
- Console presentation.
- JSON serialization and file output.
- Exit-code mapping.

---

## 8. Security invariants

The following invariants are non-negotiable:

1. Every inspection runs in an explicit read-only transaction.
2. No business-row queries are executed in v0.1.0.
3. Passwords and secrets never appear in:
   - Console output.
   - Logs.
   - JSON reports.
   - Test snapshots.
   - Exceptions exposed to users.
4. SQL statements are static and reviewed.
5. User values are parameterized.
6. The adapter must use a SQL command allowlist.
7. Statement, lock and idle transaction timeouts are configured.
8. The recommended PostgreSQL role has minimum permissions.
9. The tool never executes corrective commands.
10. Safety contract tests must detect accidental write capability.

### Allowed SQL classes

```text
SELECT
SHOW
SET LOCAL
BEGIN
COMMIT
ROLLBACK
```

### Prohibited SQL classes

```text
INSERT
UPDATE
DELETE
MERGE
CREATE
ALTER
DROP
TRUNCATE
VACUUM
ANALYZE
REINDEX
GRANT
REVOKE
```

---

## 9. CLI contract

Approved command:

```bash
dbhealth inspect postgresql [options]
```

Approved v0.1.0 options:

- `--connection`
- `--connection-env`
- `--output`
- `--schema`
- `--exclude-schema`
- `--large-table-row-threshold`
- `--large-table-size-threshold-mb`
- `--unused-index-size-threshold-mb`
- `--statement-timeout-seconds`
- `--target-label`
- `--verbose`

Default connection environment variable:

```text
DBHEALTH_CONNECTION
```

### Exit codes

```text
0 = completed without Warning or Critical findings
1 = completed with at least one Warning or Critical finding
2 = usage, configuration, connection, inspection or output error
```

An unavailable optional diagnostic must be represented in the report and must not silently disappear.

---

## 10. Report contract

Initial report schema version:

```text
0.1
```

Mandatory report areas:

- Tool metadata.
- Inspection metadata.
- Target metadata.
- Capability status.
- Summary.
- Diagnostic executions.
- Findings.

Every finding must contain:

- Fingerprint.
- Finding code.
- Rule version.
- Category.
- Severity.
- Confidence.
- Object reference.
- Message.
- Non-destructive recommendation.
- Evidence.
- Documentation reference.

Reports must be written atomically.

---

## 11. Risk classification

The v0.1.0 overall risk is deterministic:

```text
High   = at least one Critical finding
Medium = at least one Warning and no Critical findings
Low    = only Info findings
None   = no findings
```

No opaque weighted risk formula is allowed in v0.1.0.

---

## 12. Technology rules

Approved technologies:

- .NET 10.
- C#.
- System.CommandLine.
- Npgsql.
- System.Text.Json.
- xUnit.
- Testcontainers for .NET.
- Docker Compose.
- GitHub Actions.
- Markdown.
- MIT license.

### Dependency policy

- Minimize dependencies.
- Review license and maintenance status before adoption.
- Avoid packages whose licensing creates ambiguity for consulting or commercial reuse.
- Pin versions centrally.
- Do not add libraries for trivial functionality already available in the runtime.
- No dependency may access or transmit data externally.

---

## 13. Testing rules

Required test categories:

- Unit tests for every diagnostic rule.
- Unit tests for risk calculation.
- Unit tests for fingerprints.
- Unit tests for option validation.
- Unit tests for connection redaction.
- Integration tests on PostgreSQL 15.
- Integration tests on PostgreSQL 18.
- CLI process tests.
- JSON schema validation.
- Golden report validation.
- Safety contract tests.
- Secret leakage tests.

Coverage percentage is supplementary. Explicit behavioral coverage is mandatory.

---

## 14. Documentation rules

The following documents are mandatory before v0.1.0:

- `README.md`
- `README.es.md`
- `SECURITY.md`
- `CONTRIBUTING.md`
- `CHANGELOG.md`
- `docs/architecture.md`
- `docs/cli-reference.md`
- `docs/permissions.md`
- `docs/postgresql-support.md`
- `docs/read-only-safety.md`
- `docs/report-format.md`
- `docs/roadmap.md`
- One document per finding code.
- JSON Schema for report version 0.1.

Any behavior change must update its corresponding documentation in the same pull request.

---

## 15. Branching and integration

Approved workflow:

```text
feature/* -> pull request -> master
```

Rules:

- No direct production changes on `master`.
- CI must pass before merge.
- Scope deviations must be declared.
- Release actions require explicit human approval.
- Tags must be annotated.
- Release artifacts must reference the exact tagged commit.

---

## 16. Definition of Done

A backlog item is Done only when:

1. Acceptance criteria pass.
2. Relevant tests exist and pass.
3. Build has no warnings.
4. Documentation is updated.
5. Security invariants remain satisfied.
6. No unapproved scope was introduced.
7. Changed files and validation commands are reported.
8. `PROJECT_STATE.md` is updated when project state changed.

Merge is not equivalent to release readiness.

---

## 17. Release rules

The initial sequence is:

```text
v0.1.0-rc.1
v0.1.0
```

A release candidate requires:

- Green CI on the exact commit.
- Annotated tag.
- Installable package.
- Correct package metadata.
- SHA-256 for published artifacts.
- Reproducible demo.
- English and Spanish documentation.
- No open Critical defect.
- Verification that package, tag and commit match.

---

## 18. Governance change process

A change requires an ADR when it affects:

- Supported database engines.
- Architecture boundaries.
- Report compatibility.
- Security model.
- Data access mode.
- Public CLI contract.
- Stable finding semantics.
- Dependency policy.
- Release strategy.

A change to this document requires human approval and a version increment.
