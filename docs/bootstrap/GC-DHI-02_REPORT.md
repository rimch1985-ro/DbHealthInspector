# GC-DHI-02 Bootstrap Report

**Date:** 2026-07-28  
**Gate verdict:** APPROVED

## 1. Environment inspected

- Branch: `master`.
- Initial working tree: governance pack only; Git was not initialized.
- Current Git state: public remote configured, `master` published and protected.
- Repository: `https://github.com/rimch1985-ro/DbHealthInspector`.
- Initial commit: `e150d54b1b77b3fee37934c4561961d036f50194`.
- Validated bootstrap commit after the CI correction:
  `f8bf94c870889531cd26e374785604133e0883f6`.
- SDK: .NET SDK `10.0.200`; runtime `Microsoft.NETCore.App 10.0.4`.
- Existing baseline: canonical governance documents, ADRs, backlog and prompts.
- Solution format selected: `DbHealthInspector.slnx`, supported by the pinned SDK.
- No conflicting source project or meaningful implementation existed.

## 2. Files created

### Governance

- No canonical governance file was recreated. The existing pack was verified.
- `docs/bootstrap/GC-DHI-02_REPORT.md` records this gate.

### Solution/build

- `DbHealthInspector.slnx`
- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `.editorconfig`
- `.gitignore`
- `.gitattributes`
- Local `.git/` repository metadata

### Source

- `src/DbHealthInspector.Core/DbHealthInspector.Core.csproj`
- `src/DbHealthInspector.Core/AssemblyMarker.cs`
- `src/DbHealthInspector.PostgreSql/DbHealthInspector.PostgreSql.csproj`
- `src/DbHealthInspector.PostgreSql/AssemblyMarker.cs`
- `src/DbHealthInspector.Cli/DbHealthInspector.Cli.csproj`
- `src/DbHealthInspector.Cli/Program.cs`

### Tests

- `tests/DbHealthInspector.UnitTests/DbHealthInspector.UnitTests.csproj`
- `tests/DbHealthInspector.UnitTests/BootstrapSmokeTests.cs`
- `tests/DbHealthInspector.IntegrationTests/DbHealthInspector.IntegrationTests.csproj`
- `tests/DbHealthInspector.IntegrationTests/BootstrapSmokeTests.cs`

### Documentation

- `README.es.md`
- `LICENSE`
- `CHANGELOG.md`
- `SECURITY.md`
- `CONTRIBUTING.md`
- `docs/dependencies.md`

### CI

- `.github/workflows/ci.yml`

## 3. Files modified

| File | Reason |
|---|---|
| `.github/workflows/ci.yml` | Pin official actions to immutable full commit SHA values and correct the initially truncated `upload-artifact` SHA. |
| `README.md` | Replace the governance-pack landing text with an accurate English bootstrap/product README. |
| `docs/agent-governance/PROJECT_STATE.md` | Record only proven SDK, dependency, CI, validation and gate-readiness facts. |
| `docs/bootstrap/GC-DHI-02_REPORT.md` | Record verified remote, CI, artifact and branch-protection evidence. |

No approved product decision, ADR, finding code or scope definition was changed.

## 4. Dependency decisions

| Package | Version | License | Scope | Decision |
|---|---:|---|---|---|
| System.CommandLine | 2.0.10 | MIT | Runtime, CLI | Accepted for the minimal command baseline. |
| Npgsql | 10.0.3 | PostgreSQL | Runtime, adapter | Accepted as the PostgreSQL provider; no connection logic added. |
| xunit.v3 | 3.2.2 | Apache-2.0 | Test only | Accepted instead of the deprecated `xunit` package. |
| xunit.runner.visualstudio | 3.1.5 | Apache-2.0 | Test only, private asset | Accepted for VSTest discovery. |
| Microsoft.NET.Test.Sdk | 18.8.1 | MIT | Test only | Accepted for `dotnet test`. |
| Testcontainers.PostgreSql | 4.13.0 | MIT | Integration test only | Accepted for future fixtures; no container was started. |

Exact official sources and acceptance reasons are recorded in
[`docs/dependencies.md`](../dependencies.md).

## 5. Validation

| Command/check | Result | Evidence |
|---|---|---|
| `dotnet restore` | PASS | Five projects restored. |
| `dotnet build --configuration Release --no-restore` | PASS | 0 warnings, 0 errors. |
| `dotnet test --configuration Release --no-build` | PASS | 2 passed, 0 failed, 0 skipped. |
| `dotnet pack src/DbHealthInspector.Cli --configuration Release --no-build` | PASS | `C:\PROYECTOS\PORTAFOLIO\Utilidades Empresariales\DbHealthInspector\artifacts\packages\DbHealthInspector.Tool.0.1.0-alpha.0.nupkg` |
| `dotnet format DbHealthInspector.slnx --verify-no-changes --no-restore` | PASS | No formatting changes required. |
| Vulnerability, deprecation and outdated audits | PASS | No vulnerable, deprecated or newer stable direct package reported. |
| Isolated tool installation | PASS | `dbhealth --help` and `dbhealth --version` returned exit code 0. |
| Package metadata and contents | PASS | ID, version, MIT license, repository data, `dbhealth` command and project assemblies verified. |
| Package ID query | PASS | NuGet flat-container API returned 404; the candidate ID was unused at review time. |
| JSON/XML and Markdown local links | PASS | Files parsed and no broken local link was found. |
| Forbidden dependency and production SQL scans | PASS | None found. |
| `git diff --check` and extended untracked-file audit | PASS | 38 untracked repository files inspected; no whitespace error found. |
| Initial GitHub Actions run `30423760906` | FAIL, corrected | Windows passed; Ubuntu rejected a truncated `upload-artifact` SHA during job setup. |
| Corrected GitHub Actions run `30423850599` | PASS | Ubuntu and Windows passed every required step on commit `f8bf94c870889531cd26e374785604133e0883f6`. |
| CI build and tests | PASS | Each job reported 0 warnings, 0 errors, 2 passed, 0 failed and 0 skipped. |
| CI artifact audit | PASS | Metadata, contents, exact repository commit and isolated installation verified. |

Local package SHA-256:

```text
69851899BDFC427269379080C4A2C5AA0FCDBEE32F53D9C556579B1C35594CA1
```

CI package:

```text
Artifact: dbhealth-bootstrap-package
File: DbHealthInspector.Tool.0.1.0-alpha.0.nupkg
Package size: 829637 bytes
Package SHA-256: E63D2E76658C85F467FD76945A98F594E4A45377A6426A34B84133489FF305EB
Artifact archive size: 824892 bytes
Artifact archive SHA-256: 77F70AA0D8984469854DB35D7E6DC8B6C67D5E5565571D989CB91EC873AB57FB
```

The CI package declares `DbHealthInspector.Tool` version `0.1.0-alpha.0`, MIT,
the expected repository URL and commit, the `DotnetTool` package type and the
`dbhealth` command.

## 6. Architecture verification

```text
DbHealthInspector.Core -> no project references
DbHealthInspector.PostgreSql -> DbHealthInspector.Core
DbHealthInspector.Cli -> DbHealthInspector.Core
DbHealthInspector.Cli -> DbHealthInspector.PostgreSql
DbHealthInspector.UnitTests -> DbHealthInspector.Core
DbHealthInspector.IntegrationTests -> Core, PostgreSql and Cli
```

The production graph matches ADR-0003. Core has no infrastructure dependency.
No Entity Framework Core, Dapper, MediatR, AutoMapper, Spectre.Console,
logging framework, generic host or FluentAssertions dependency exists.

No finding model, snapshot model, PostgreSQL query, database connection,
diagnostic rule, JSON report behavior, Docker setup or automatic repair was
implemented.

## 7. Deviations and risks

- Initial run `30423760906` failed because the `upload-artifact` SHA lacked its
  final hexadecimal character. Corrective commit
  `f8bf94c870889531cd26e374785604133e0883f6` fixed only that workflow
  reference; history was not amended or rewritten.
- Source Link remains pending. Repository URL and exact commit metadata are
  already embedded in the package, and no new dependency was introduced.
- System.CommandLine displays the managed assembly name in the help usage line
  when installed as a tool; the installed command and package metadata are
  correctly named `dbhealth`.
- Branch protection does not require third-party approval and is not enforced
  for repository administrators, preserving the owner's individual workflow.
- No pull request, tag, package publication or release was performed.

## 8. Gate verdict

READY FOR FINAL HUMAN APPROVAL

The local and remote bootstrap criteria pass. `master` requires strict Ubuntu
and Windows checks; force push and deletion are disabled.

## 9. Next authorized gate

Final human approval is recorded below. The next authorized gate is GC-DHI-03 —
Core Contracts and Domain Model, assigned to Claude Code and subject to Codex
review and integration.

## 10. Final approval record

- Final human approval: `2026-07-28`
- Final verified run: `30424123966`
- Verified HEAD before approval record: `285ab0386af9fec80410951b50a6c97ec8f3b9d2`

A later documentation-only commit produced run `30424123966`. Its Ubuntu and
Windows jobs passed. The package from that run was independently verified
against commit `285ab0386af9fec80410951b50a6c97ec8f3b9d2`.

No production diagnostic implementation was performed during GC-DHI-02.
Diagnostic implementation remains subject to the separately authorized
GC-DHI-03 workflow.
