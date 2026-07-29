# ADR-0002 — Read-Only Metadata Mode

- **Status:** Accepted
- **Date:** 2026-07-28
- **Decision owners:** Project owner and technical architecture
- **Related gate:** GC-DHI-01

## Context

A database diagnostic tool can create operational and privacy risk if it:

- Modifies database objects.
- Executes corrective scripts.
- Reads sensitive business rows.
- Runs expensive full-table scans.
- Exposes credentials.
- Presents uncertain recommendations as definitive actions.

The product's value depends on professional trust and safe execution against business databases.

## Decision

DbHealth Inspector v0.1.0 will be:

- Read-only.
- Metadata-only.
- Non-remediating.
- Non-invasive.
- Explicit about unavailable capabilities.

All inspections will run in an explicitly read-only transaction.

The tool will query PostgreSQL catalogs, information-schema views, statistics views and size functions. It will not query business rows in v0.1.0.

The tool will produce findings, evidence and non-destructive recommendations only.

## Defense in depth

The implementation must use:

1. A recommended least-privilege database role.
2. Explicit read-only transaction mode.
3. SQL command allowlisting.
4. Static reviewed SQL.
5. Parameterized user input.
6. Statement and lock timeouts.
7. Secret redaction.
8. Safety contract tests.
9. Before/after database-state verification in integration tests.

## Consequences

### Positive

- Lower operational risk.
- Better suitability for demos and consulting assessments.
- No extraction of business records.
- Predictable execution cost.
- Strong product differentiation from tuning tools.
- Easier security documentation.

### Negative

- Some valuable diagnostics are postponed.
- Null-ratio calculations are unavailable.
- Data-quality findings remain limited.
- Statistics-dependent findings may have reduced confidence.
- The tool cannot repair confirmed problems.

## Prohibited behavior

The production application must never execute:

- DML.
- DDL.
- Maintenance commands.
- Permission changes.
- Automatic index creation or deletion.
- Data export from business tables.

## Report behavior

The report must distinguish:

- Completed diagnostics.
- Skipped diagnostics.
- Unavailable diagnostics.
- Failed diagnostics.

A diagnostic must not silently disappear because of missing permissions or unsupported server capabilities.

## Rejected alternatives

### Automatic remediation

Rejected because structural changes require workload context, maintenance planning and human review.

### Optional write mode

Rejected because it weakens the product's core trust boundary and complicates safety testing.

### Data profiling enabled by default

Rejected because it could read sensitive rows and produce expensive scans.

### Sampling business rows

Rejected for v0.1.0 because even samples can expose confidential information.

## Review trigger

Review this ADR before v0.2.0 controlled data profiling. Any profiling feature must be opt-in and governed by a new ADR.
