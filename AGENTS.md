# AGENTS.md — DbHealth Inspector

## Purpose

This file defines the operating rules for human contributors and AI-assisted development agents working in this repository.

DbHealth Inspector is a read-only database diagnostic CLI. Its purpose is to inspect PostgreSQL metadata, produce evidence-based findings and generate reproducible reports without modifying database objects or business data.

## Canonical documents

Before proposing or implementing changes, read:

1. `docs/agent-governance/PROJECT_RULES.md`
2. `docs/agent-governance/PROJECT_STATE.md`
3. `docs/agent-governance/AGENT_OPERATING_MODEL.md`
4. Applicable ADRs under `docs/adr/`
5. `docs/backlog/INITIAL_BACKLOG.md`

When documents conflict, apply this precedence:

1. `PROJECT_RULES.md`
2. Accepted ADRs
3. `PROJECT_STATE.md`
4. Backlog
5. Issue or task description
6. Implementation details

## Agent role separation

The official operating model is defined in `docs/agent-governance/AGENT_OPERATING_MODEL.md`.

- **Claude Code** is the primary programmer and implements authorized product changes locally.
- **Codex** is the DevOps agent, technical integration reviewer and GitHub operator.
- Claude Code must not push, merge, tag, publish or change repository settings.
- Codex must avoid reimplementing feature work except for small DevOps or integration corrections.
- Substantial product defects found by Codex should be returned to Claude Code as focused correction tasks.
- Human approval remains mandatory for scope changes, protected merges, tags and releases where required.

## Mandatory gates

No agent may perform the following without explicit human authorization:

- Change approved product scope.
- Add a new database engine.
- Add data-row profiling.
- Add automatic repair or tuning.
- Modify release tags.
- Publish packages or GitHub releases.
- Merge into the protected default branch.
- Change stable finding codes.
- Change the report schema version.
- Introduce a dependency with unclear or restrictive licensing.

## Implementation principles

- PostgreSQL first.
- Read-only always.
- Metadata-only for v0.1.0.
- Findings and recommendations, never automatic changes.
- Prefer explicit SQL over hidden abstractions.
- Keep the architecture small.
- No speculative framework adoption.
- No secrets in logs, console output, reports or tests.
- Tests must verify behavior, not only line coverage.
- Documentation and implementation must remain synchronized.

## Scope protection

The following are forbidden in v0.1.0:

- SQL Server, MySQL, MariaDB or Oracle support.
- Query-plan analysis.
- Query monitoring.
- Data-row scanning.
- Null-ratio profiling.
- Foreign-key inference.
- Index creation suggestions.
- Automatic DDL or DML.
- Dashboard, API or agent service.
- AI features.
- Plugin systems.
- Historical report persistence.

## SQL safety

Production code may use only the following SQL command classes:

- `SELECT`
- `SHOW`
- `SET LOCAL`
- `BEGIN`
- `COMMIT`
- `ROLLBACK`

The following command classes are prohibited:

- `INSERT`
- `UPDATE`
- `DELETE`
- `MERGE`
- `CREATE`
- `ALTER`
- `DROP`
- `TRUNCATE`
- `VACUUM`
- `ANALYZE`
- `REINDEX`
- `GRANT`
- `REVOKE`

All inspections must run inside an explicitly read-only transaction.

## Change workflow

For every implementation task:

1. Confirm the task is present in the approved backlog or is explicitly authorized.
2. Identify affected canonical documents.
3. Implement the smallest coherent change.
4. Add or update tests.
5. Update documentation when behavior or contracts change.
6. Run restore, build and relevant tests.
7. Report changed files, validation results, risks and remaining work.
8. Do not merge, tag or publish without authorization.

## Required completion report

Every completed task must report:

- Objective.
- Files changed.
- Behavior implemented.
- Tests added or updated.
- Commands executed.
- Validation results.
- Known limitations.
- Scope deviations, if any.
- Recommended next gate.

## Release integrity

Every release artifact must be traceable to the exact tagged commit.

A release is invalid if:

- The package metadata references a different commit.
- The tag is lightweight when an annotated tag is required.
- CI did not pass on the tagged commit.
- The report schema or finding catalog differs from the documented version.
- Hashes are missing for published binary artifacts.
