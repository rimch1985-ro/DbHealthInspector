# ADR-0001 — PostgreSQL First

- **Status:** Accepted
- **Date:** 2026-07-28
- **Decision owners:** Project owner and technical architecture
- **Related gate:** GC-DHI-01

## Context

DbHealth Inspector is intended to become a professional open-source database diagnostic CLI.

Supporting multiple engines in the first release would multiply:

- Catalog-query design.
- Capability differences.
- Test environments.
- Documentation.
- Security review.
- Finding semantics.
- CI cost.
- Release risk.

The first release must remain small, reproducible and publishable.

## Decision

DbHealth Inspector v0.1.0 will support PostgreSQL only.

The supported range is PostgreSQL 15–18.

The architecture will isolate PostgreSQL-specific implementation behind the core inspection abstractions so that a future SQL Server adapter can be added without polluting core diagnostic models.

## Rationale

PostgreSQL provides:

- Open-source licensing.
- Reproducible Docker environments.
- Good CI suitability.
- Rich catalogs and statistics views.
- Strong alignment with the project's technical portfolio.
- No proprietary server license requirement for the demo.

Testing the oldest and newest supported versions provides reasonable compatibility coverage while controlling CI cost.

## Consequences

### Positive

- Smaller initial scope.
- Faster implementation.
- Better diagnostic quality.
- Reproducible integration tests.
- Clear documentation.
- Lower infrastructure cost.

### Negative

- SQL Server users are not served by v0.1.0.
- Some core abstractions may need revision when the second engine is added.
- PostgreSQL terminology may influence the first report model.

## Constraints

- PostgreSQL-specific types must not leak into `DbHealthInspector.Core`.
- Public finding semantics should remain engine-neutral where practical.
- SQL Server support requires a separate approved ADR and roadmap gate.
- No generic multi-engine abstraction may be added without an immediate v0.1.0 need.

## Rejected alternatives

### PostgreSQL and SQL Server in v0.1.0

Rejected because it would double implementation and validation effort before the first public release.

### SQL Server first

Rejected because the open-source demo and CI story are less straightforward and may introduce licensing or container constraints.

### Generic database adapter before any implementation

Rejected as premature abstraction. The first adapter should reveal the actual extension points needed.

## Review trigger

Review this decision after v0.1.0 is stable and before beginning v0.3.0 SQL Server support.
