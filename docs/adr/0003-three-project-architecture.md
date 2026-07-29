# ADR-0003 — Three-Project Architecture

- **Status:** Accepted
- **Date:** 2026-07-28
- **Decision owners:** Project owner and technical architecture
- **Related gate:** GC-DHI-01

## Context

The initial concept proposed separate Core, Application, Infrastructure and CLI projects.

DbHealth Inspector v0.1.0 is a small CLI with:

- One use case.
- One database engine.
- One report format.
- No persistence.
- No web host.
- No user management.
- No distributed architecture.

A four-layer physical solution could introduce ceremony without providing proportional value.

At the same time, PostgreSQL-specific queries must remain isolated from engine-neutral rules and report models.

## Decision

Use three production projects:

```text
DbHealthInspector.Core
DbHealthInspector.PostgreSql
DbHealthInspector.Cli
```

And two test projects:

```text
DbHealthInspector.UnitTests
DbHealthInspector.IntegrationTests
```

## Responsibilities

### Core

Contains:

- Domain models.
- Findings.
- Evidence.
- Database snapshots.
- Diagnostic rules.
- Inspection orchestration.
- Risk calculation.
- Core abstractions.

### PostgreSql

Contains:

- Npgsql integration.
- PostgreSQL connection handling.
- Read-only session setup.
- Catalog queries.
- Statistics queries.
- Capability probing.
- Mapping to core snapshots.

### Cli

Contains:

- System.CommandLine configuration.
- Options and validation.
- Dependency composition.
- Console rendering.
- JSON report writing.
- Exit-code mapping.

## Dependency rules

```text
Core -> no infrastructure dependencies
PostgreSql -> Core
Cli -> Core
Cli -> PostgreSql
```

Circular references are prohibited.

## Consequences

### Positive

- Clear engine boundary.
- Smaller solution.
- Faster navigation.
- Fewer abstractions.
- Easier testing.
- Lower maintenance cost.

### Negative

- Application orchestration resides physically in Core.
- JSON writing resides in CLI for v0.1.0.
- A future second host or report adapter may justify a new project.

## Constraints

- PostgreSQL-specific concepts must not leak into Core public models unless unavoidable and documented.
- CLI types must not leak into Core.
- Infrastructure packages must not be referenced by Core.
- No additional production project may be created without a demonstrated responsibility boundary.
- Internal folders may express logical layers without requiring separate assemblies.

## Rejected alternatives

### Four projects from the beginning

Rejected because the separate Application and Infrastructure assemblies do not yet provide enough value.

### Single project

Rejected because PostgreSQL implementation should remain isolated from engine-neutral diagnostics.

### Plugin architecture

Rejected as premature and outside v0.1.0.

## Review trigger

Review this decision when:

- A second database engine is approved.
- A second host is added.
- Multiple report formats require independent adapters.
- Core orchestration becomes difficult to maintain.
