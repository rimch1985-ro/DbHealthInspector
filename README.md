# DbHealth Inspector

DbHealth Inspector is an open-source command-line utility for safe, reproducible
diagnostics of PostgreSQL database metadata.

> **Current status:** `dbhealth inspect postgresql` inspects a live database and
> reports DBH001-DBH005 findings on the console. JSON and file reports are not
> available yet.

## Product boundaries

- PostgreSQL 15-18 is the initial supported range.
- Inspections are explicitly read-only.
- v0.1.0 inspects metadata and permitted statistics only.
- Business-table rows are never queried.
- Findings include evidence and non-destructive recommendations.
- The tool never applies automatic repairs, DDL or DML.

## Quick start

```bash
export DBHEALTH_CONNECTION="Host=localhost;Port=5432;Database=mydb;Username=inspector;Password=..."
dbhealth inspect postgresql
```

Supply the connection through the environment, not on the command line: a value
passed as `--connection` may be visible in shell history and in process listings.
`--connection-env <NAME>` reads any variable you choose.

The command reports which tables lack a primary key, which have crossed a size or
row threshold, which indexes are exact structural duplicates, which sizeable
indexes have recorded no scans, and which indexes the engine has marked invalid.

### Commands

```text
dbhealth
└── inspect
    └── postgresql
```

### Options

| Option | Meaning |
|---|---|
| `--connection <STRING>` | Connection string. Visible in shell history; prefer the alternatives below. |
| `--connection-env <NAME>` | Name of an environment variable holding the connection string. |
| `--large-table-row-threshold <N>` | DBH002 row threshold. Default `1000000`. |
| `--large-table-size-threshold-mb <N>` | DBH002 size threshold. Default `1024`. |
| `--unused-index-size-threshold-mb <N>` | DBH004 minimum index size. Default `10`. |

Connection precedence is `--connection`, then the variable named by
`--connection-env`, then `DBHEALTH_CONNECTION`. Naming a variable that is missing
or empty fails rather than falling back.

**The `-mb` options use binary units: one unit is exactly 1,048,576 bytes.** So
`--large-table-size-threshold-mb 1024` is exactly 1,073,741,824 bytes and
`--unused-index-size-threshold-mb 10` is exactly 10,485,760 bytes — the defaults.

### Exit codes

```text
0 = completed with no findings, or with Info-only findings
1 = completed with at least one Warning or Critical finding
2 = usage, configuration, connection or inspection failure
```

### Reading a clean result

When nothing is reported the command says so explicitly, and says what that does
and does not mean: no issues were detected **by the enabled diagnostics**. Five
structural rules finding nothing is not a guarantee that the database has no
other problems.

Connection strings, passwords, hosts and usernames are never printed, on any
path, including error output.

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
