# GC-DHI-02 — Repository Bootstrap and Technical Baseline

## Role

Act as **Codex, the DevOps and technical integration agent** for DbHealth Inspector.

This gate is assigned to Codex because it concerns repository bootstrap, build governance, dependencies, CI/CD, packaging metadata and GitHub readiness. Do not perform feature implementation.

Work only on the repository bootstrap gate. Read and obey, in order:

1. `AGENTS.md`
2. `docs/agent-governance/PROJECT_RULES.md`
3. `docs/agent-governance/PROJECT_STATE.md`
4. `docs/agent-governance/AGENT_OPERATING_MODEL.md`
4. `docs/adr/0001-postgresql-first.md`
5. `docs/adr/0002-read-only-metadata-mode.md`
6. `docs/adr/0003-three-project-architecture.md`
7. `docs/backlog/INITIAL_BACKLOG.md`

If the repository state conflicts with these documents, stop and report the conflict. Do not silently choose a different design.

---

## Agent-boundary requirement

- Codex owns the bootstrap and DevOps work in this gate.
- Do not implement product features intended for Claude Code.
- Do not add finding models, rules, PostgreSQL queries or report behavior.
- Prepare the repository so later implementation tasks can be assigned to Claude Code.

---

## Objective

Create and validate the initial .NET 10 repository baseline for DbHealth Inspector.

This gate establishes governance, solution structure, build rules, dependency versions, test skeletons, package metadata and CI.

It must not implement PostgreSQL inspection logic or diagnostic rules.

---

## Authorized work

Complete only these backlog items:

- GOV-01
- GOV-02
- FND-01
- FND-02
- FND-03
- FND-04
- CI-01

### Required repository structure

```text
src/
├── DbHealthInspector.Core
├── DbHealthInspector.PostgreSql
└── DbHealthInspector.Cli

tests/
├── DbHealthInspector.UnitTests
└── DbHealthInspector.IntegrationTests
```

### Required project references

```text
DbHealthInspector.PostgreSql -> DbHealthInspector.Core
DbHealthInspector.Cli -> DbHealthInspector.Core
DbHealthInspector.Cli -> DbHealthInspector.PostgreSql
DbHealthInspector.UnitTests -> DbHealthInspector.Core
DbHealthInspector.IntegrationTests -> relevant production projects
```

`DbHealthInspector.Core` must not reference infrastructure or CLI projects.

---

## Tasks

### 1. Inspect the environment

Before changing files, report:

- Repository root.
- Git branch and working-tree status.
- Installed .NET SDKs.
- Existing files and projects.
- Whether any requested file already exists.
- Any blocker or conflict.

Do not overwrite meaningful existing work.

### 2. Install governance files

Verify that the approved governance pack is present at the documented paths.

Repair only broken internal relative links or formatting errors. Do not alter approved product decisions.

### 3. Create the solution

Create a .NET 10 solution using `.slnx` when supported by the pinned SDK and repository tooling. Otherwise use `.sln` and document why.

Create the five approved projects.

Production projects must target `net10.0`.

Use a console application for `DbHealthInspector.Cli`. The other production projects must be class libraries.

### 4. Configure build governance

Create or update:

- `global.json`
- `Directory.Build.props`
- `Directory.Packages.props`
- `.editorconfig`
- `.gitignore`
- `.gitattributes`

Enable at minimum:

```xml
<TargetFramework>net10.0</TargetFramework>
<Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings>
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>
<AnalysisLevel>latest-recommended</AnalysisLevel>
<Deterministic>true</Deterministic>
<ContinuousIntegrationBuild>true</ContinuousIntegrationBuild>
```

Avoid duplicate properties when a value is inherited centrally.

### 5. Select exact dependencies

Evaluate current stable versions compatible with .NET 10 for:

- System.CommandLine
- Npgsql
- xUnit
- Microsoft.NET.Test.Sdk
- Testcontainers.PostgreSql
- Any xUnit runner package required by the selected test setup

Use only official package metadata and primary documentation.

Record direct dependency licenses in:

```text
docs/dependencies.md
```

For each dependency include:

- Package.
- Exact version.
- Purpose.
- License.
- Official source.
- Runtime or test-only classification.
- Reason for acceptance.

Do not add FluentAssertions.

Do not add:

- Entity Framework Core.
- Dapper.
- MediatR.
- AutoMapper.
- Spectre.Console.
- Logging frameworks.
- Generic-host packages.
- JSON Schema libraries unless required in this gate.
- Any package not needed for restore/build/test/package baseline.

### 6. Add minimal compilable code

Add only enough code to compile and prove project references.

Allowed examples:

- Assembly marker types.
- A minimal CLI entry point.
- Placeholder version/help output if required to validate packaging.
- One trivial unit test and one integration-project skeleton test.

Do not add:

- Finding models.
- Snapshot models.
- Inspection interfaces.
- PostgreSQL SQL.
- Npgsql connection logic.
- Diagnostic rules.
- JSON report model.
- Docker demo.
- Production behavior beyond a minimal executable baseline.

### 7. Configure global-tool packaging

Configure `DbHealthInspector.Cli` for future packaging:

```text
Tool command: dbhealth
Candidate package ID: DbHealthInspector.Tool
Version: 0.1.0-alpha.0
```

Include:

- Authors.
- Description.
- Repository metadata.
- MIT license expression.
- Package tags.
- README packing when available.
- Source Link metadata where appropriate.

Do not publish the package.

Generate a local `.nupkg` and verify its metadata and contents.

If the package ID appears unavailable or invalid, report it; do not invent a permanent replacement without authorization.

### 8. Add repository documents

Create minimal, accurate placeholders where missing:

- `README.md`
- `README.es.md`
- `LICENSE`
- `CHANGELOG.md`
- `SECURITY.md`
- `CONTRIBUTING.md`

The README files must clearly state:

- PostgreSQL-only initial scope.
- Read-only and metadata-only guarantees.
- No automatic repair.
- Current pre-release/bootstrap status.
- No functional inspection available yet.

Do not advertise unimplemented diagnostics as available.

### 9. Add bootstrap CI

Create `.github/workflows/ci.yml`.

Required jobs:

#### Ubuntu

- Checkout.
- Setup pinned .NET SDK.
- Restore.
- Build Release with no restore.
- Test Release with no build.
- Pack CLI locally.
- Upload package as workflow artifact when appropriate.

#### Windows

- Checkout.
- Setup pinned .NET SDK.
- Restore.
- Build Release with no restore.
- Test Release with no build.
- Execute a minimal CLI smoke command.

Use least permissions:

```yaml
permissions:
  contents: read
```

Do not add release publishing.

### 10. Update project state

Update `docs/agent-governance/PROJECT_STATE.md` only with facts proven during this gate:

- Exact SDK.
- Exact dependency versions.
- Solution format.
- CI configuration.
- Validation results.
- Remaining blockers.
- GC-DHI-02 readiness status.

Do not mark GC-DHI-02 approved. State that it is ready for human review when all criteria pass.

---

## Prohibited work

Do not:

- Implement DBH001–DBH005.
- Add PostgreSQL catalog queries.
- Open database connections.
- Create Docker Compose.
- Add business-data profiling.
- Add any second database engine.
- Add JSON report implementation.
- Add automatic remediation.
- Change approved finding codes.
- Change v0.1.0 scope.
- Commit, push, merge, tag or publish.
- Create a GitHub release.
- Introduce unapproved architecture.
- Suppress warnings globally to obtain a green build.
- Disable analyzers without a documented, localized justification.

---

## Validation

Run the repository-equivalent commands for:

```bash
dotnet --info
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build
dotnet pack src/DbHealthInspector.Cli --configuration Release --no-build
```

Also verify:

- Project-reference graph.
- Package contents.
- Package repository metadata.
- Tool command metadata.
- No forbidden dependencies.
- No unexpected generated files.
- `git diff --check`.
- Working-tree diff.

When practical, install the generated package into an isolated local tool path and run:

```bash
dbhealth --help
```

Do not install it globally on the machine.

---

## Required final report

Return exactly these sections:

### 1. Environment inspected

- Branch.
- Initial working-tree state.
- SDK.
- Existing baseline.

### 2. Files created

Group by:

- Governance.
- Solution/build.
- Source.
- Tests.
- Documentation.
- CI.

### 3. Files modified

For each file, state why.

### 4. Dependency decisions

Compact table:

```text
Package | Version | License | Scope | Decision
```

### 5. Validation

Compact table:

```text
Command/check | Result | Evidence
```

Include test totals and package path.

### 6. Architecture verification

Confirm the project-reference graph and absence of forbidden dependencies or behavior.

### 7. Deviations and risks

List any deviation, unresolved package issue, tooling incompatibility or warning.

### 8. Gate verdict

Use one:

```text
READY FOR HUMAN REVIEW
BLOCKED
FAILED
```

### 9. Next authorized gate

State:

```text
No production diagnostic implementation is authorized until GC-DHI-02 receives human approval.
```

---

## Stop condition

Stop immediately after validating the bootstrap and producing the report.

Do not continue into Core contracts, PostgreSQL integration or diagnostic implementation.
