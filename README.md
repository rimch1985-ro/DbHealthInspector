# DbHealth Inspector

DbHealth Inspector is an open-source command-line utility for safe, reproducible
diagnostics of PostgreSQL database metadata.

> **Current status:** repository bootstrap only. This build does not inspect a
> database and does not implement DBH001-DBH005 yet.

## Product boundaries

- PostgreSQL 15-18 is the initial supported range.
- Inspections will be explicitly read-only.
- v0.1.0 will inspect metadata and permitted statistics only.
- Business-table rows will not be queried.
- Findings will include evidence and non-destructive recommendations.
- The tool will never apply automatic repairs, DDL or DML.

## Bootstrap command

The package is prepared as a .NET global tool with the future command:

```text
dbhealth
```

At this gate, only bootstrap help and version output are available:

```bash
dbhealth --help
```

## Repository structure

```text
src/
├── DbHealthInspector.Core
├── DbHealthInspector.PostgreSql
└── DbHealthInspector.Cli

tests/
├── DbHealthInspector.UnitTests
└── DbHealthInspector.IntegrationTests
```

## Build

The repository requires the .NET SDK pinned in `global.json`.

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet pack src/DbHealthInspector.Cli --configuration Release --no-build
```

## Governance

The canonical scope and safety rules are defined in:

- [`AGENTS.md`](AGENTS.md)
- [`PROJECT_RULES.md`](docs/agent-governance/PROJECT_RULES.md)
- [`PROJECT_STATE.md`](docs/agent-governance/PROJECT_STATE.md)
- [`AGENT_OPERATING_MODEL.md`](docs/agent-governance/AGENT_OPERATING_MODEL.md)
- [`INITIAL_BACKLOG.md`](docs/backlog/INITIAL_BACKLOG.md)

## Security

See [`SECURITY.md`](SECURITY.md). Do not place database passwords, connection
strings or other secrets in issues, logs, reports or test fixtures.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md).

## License

Licensed under the [MIT License](LICENSE).

Spanish documentation: [`README.es.md`](README.es.md).
