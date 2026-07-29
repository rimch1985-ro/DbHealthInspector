# Contributing

DbHealth Inspector is currently in a controlled bootstrap phase.

Before proposing a change, read:

1. [`AGENTS.md`](AGENTS.md)
2. [`PROJECT_RULES.md`](docs/agent-governance/PROJECT_RULES.md)
3. [`PROJECT_STATE.md`](docs/agent-governance/PROJECT_STATE.md)
4. The accepted ADRs under [`docs/adr`](docs/adr)
5. [`INITIAL_BACKLOG.md`](docs/backlog/INITIAL_BACKLOG.md)

## Workflow

1. Work from a `feature/*` branch.
2. Keep the change within an authorized backlog item.
3. Add or update tests and documentation with behavioral changes.
4. Run restore, Release build and relevant tests.
5. Report changed files, commands, results, limitations and deviations.
6. Open a pull request; do not push directly to `master`.

## Safety

Contributions must preserve the read-only, metadata-only v0.1.0 boundary. Never
include real credentials, customer data or production database extracts.

## Current limitation

Production feature work is not authorized until GC-DHI-02 is approved by the
project owner.
