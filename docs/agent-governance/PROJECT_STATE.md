# PROJECT_STATE — DbHealth Inspector

**Last updated:** 2026-07-28  
**Current phase:** Repository bootstrap final approval  
**Current gate:** GC-DHI-02 remotely validated; ready for final human approval  
**Next gate:** Final human approval of GC-DHI-02  
**Target release:** v0.1.0-rc.1

---

## 1. Executive status

DbHealth Inspector now has a locally and remotely validated .NET 10 repository
baseline at `https://github.com/rimch1985-ro/DbHealthInspector`.

The solution, projects, strict build configuration, exact dependencies,
documentation, package baseline and CI workflow have been created. Local and
GitHub-hosted restore, build, tests, pack and isolated-tool smoke validation
pass.

No production diagnostic behavior, PostgreSQL query or database connection has
been implemented. GC-DHI-02 is ready for final human approval but is not
approved until that review is complete.

---

## 2. Approved agent operating model

| Role | Assigned agent | Responsibility |
|---|---|---|
| Primary programmer | Claude Code | Implements authorized product code, tests and related documentation |
| DevOps and integration | Codex | Reviews, validates, manages CI/CD and authorized GitHub operations |
| Product and gate authority | Human project owner | Approves scope, architecture gates and releases |
| Technical coordination | ChatGPT | Maintains project direction and prepares scoped prompts |

Claude Code may continue authorized local implementation while Codex capacity is unavailable. Remote integration, merge, tagging and release remain under the Codex DevOps role.

Codex preserves capacity by avoiding duplicate feature implementation and focusing on review, integration, CI/CD and GitHub operations.

---

## 3. Approved product baseline

| Item | Current decision |
|---|---|
| Product | DbHealth Inspector |
| CLI | `dbhealth` |
| Runtime | .NET 10 |
| Language | C# |
| Initial engine | PostgreSQL |
| Supported range | PostgreSQL 15–18 |
| Inspection mode | Metadata-only |
| Safety model | Read-only |
| Report | JSON schema 0.1 |
| Console | Text summary |
| Distribution | .NET global tool |
| License | MIT |
| Repository flow | `feature/* -> PR -> master` |

---

## 4. Approved diagnostics

| Code | Name | Status |
|---|---|---|
| DBH001 | `TABLE_WITHOUT_PRIMARY_KEY` | Approved for v0.1.0 |
| DBH002 | `LARGE_TABLE` | Approved for v0.1.0 |
| DBH003 | `EXACT_DUPLICATE_INDEX` | Approved for v0.1.0 |
| DBH004 | `UNUSED_INDEX_CANDIDATE` | Approved for v0.1.0 |
| DBH005 | `INVALID_INDEX` | Approved for v0.1.0 |

---

## 5. Accepted architectural decisions

| ADR | Decision | Status |
|---|---|---|
| ADR-0001 | PostgreSQL first | Accepted |
| ADR-0002 | Read-only metadata mode | Accepted |
| ADR-0003 | Three-project architecture | Accepted |

---

## 6. Completed work

- Product vision defined.
- v0.1.0 scope defined.
- Out-of-scope list defined.
- Initial diagnostic catalog defined.
- Severity and confidence model defined.
- Preliminary CLI contract defined.
- Exit-code contract defined.
- JSON report structure defined.
- Architecture defined.
- Technology baseline selected.
- Demo database concept defined.
- Security model defined.
- Roadmap defined.
- Initial backlog defined.
- Acceptance criteria defined.
- Testing and documentation plans defined.
- Open-source publication strategy defined.
- Governance pack generated.
- Claude Code/Codex responsibility model approved and documented.
- Local Git repository initialized on `master`, with no commits or remote.
- `.NET SDK 10.0.200` pinned through `global.json`.
- `.slnx` solution created with the approved three production projects and two test projects.
- Strict compiler, analyzer, deterministic-build and central package-management configuration installed.
- Direct dependencies selected, licensed and pinned in `Directory.Packages.props`.
- Bilingual bootstrap README, MIT license, security and contribution documents installed.
- GitHub Actions workflow created for Ubuntu and Windows.
- `DbHealthInspector.Tool` package `0.1.0-alpha.0` built and installed successfully in an isolated tool path.
- Local validation completed with zero build warnings, zero build errors and two passing smoke tests.
- Public repository created at `https://github.com/rimch1985-ro/DbHealthInspector`
  with `master` as its default branch and Issues and Actions enabled.
- Initial commit `e150d54b1b77b3fee37934c4561961d036f50194` published without
  generated output, package binaries or detected secrets.
- GitHub Actions references pinned to immutable full commit SHA values.
- The initial run `30423760906` exposed a truncated `upload-artifact` SHA;
  Windows passed and Ubuntu stopped during job setup.
- Corrective commit `f8bf94c870889531cd26e374785604133e0883f6` fixed only the
  workflow reference without amending or rewriting history.
- GitHub Actions run `30423850599` passed on Ubuntu and Windows with zero build
  warnings, zero build errors and two passing tests per job.
- CI artifact `dbhealth-bootstrap-package` downloaded and audited. Its package
  SHA-256 is
  `E63D2E76658C85F467FD76945A98F594E4A45377A6426A34B84133489FF305EB`.
- The CI package installed successfully in an isolated tool path and returned
  success for `dbhealth --help` and `dbhealth --version`.
- `master` protection requires strict `Ubuntu` and `Windows` GitHub Actions
  checks. Force pushes and branch deletion are disabled; no third-party review
  is required.

---

## 7. Work authorized next

Only the following actions are authorized without opening a new product gate:

1. Final human review of the GC-DHI-02 bootstrap and remote-integration report.
2. Focused correction of bootstrap defects found during review.

Production diagnostic implementation remains unauthorized until GC-DHI-02 is
approved.

---

## 8. Work not yet authorized

The following are not yet authorized:

- Implement PostgreSQL catalog queries.
- Implement production diagnostic rules.
- Publish a NuGet package.
- Publish a GitHub release.
- Create release tags.
- Add another database engine.
- Add data profiling.
- Add report formats other than JSON.
- Change the approved architecture.
- Change stable finding codes.

These require completion of the relevant future gate.

---

## 9. Resolved bootstrap decisions and pending product decisions

Resolved during GC-DHI-02:

- SDK: `.NET SDK 10.0.200`.
- Solution: `DbHealthInspector.slnx`.
- Root namespaces: `DbHealthInspector.Core`, `DbHealthInspector.PostgreSql`,
  `DbHealthInspector.Cli`, `DbHealthInspector.UnitTests` and
  `DbHealthInspector.IntegrationTests`.
- Package ID: `DbHealthInspector.Tool`; the NuGet API returned 404 for the ID on
  2026-07-28.
- Direct packages: System.CommandLine 2.0.10, Npgsql 10.0.3, xunit.v3 3.2.2,
  xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.8.1 and
  Testcontainers.PostgreSql 4.13.0.
- Analyzer baseline: built-in .NET analyzers at `latest-recommended`, style
  enforcement during build and warnings as errors.
- GitHub Actions matrix: `ubuntu-latest` and `windows-latest`.
- Remote: `https://github.com/rimch1985-ro/DbHealthInspector.git`.
- Required `master` checks: `Ubuntu` and `Windows`, strict mode.

Pending product decisions:

- Exact timeout defaults after validation.
- Internal representation of evidence values.
- Fingerprint canonicalization algorithm.
- Reproducible invalid-index test strategy.
- Console rendering format.
- Source Link activation remains pending; package repository URL and exact
  commit metadata are already present without adding a new dependency.

---

## 10. Known risks

| Risk | Current control |
|---|---|
| Scope growth | Frozen v0.1.0 exclusions |
| Unsafe SQL | Read-only transaction and SQL allowlist |
| Secret exposure | Mandatory redaction and leakage tests |
| Statistics misinterpretation | Confidence and evidence model |
| Duplicate-index false positives | Exact structural equivalence only |
| PostgreSQL version differences | Adapter boundaries and 15/18 CI matrix |
| Overengineering | Three-project architecture |
| Package licensing ambiguity | Dependency review before adoption |
| Unreliable invalid-index fixture | Separate technical spike |
| Report contract drift | JSON Schema versioning |

---

## 11. Gate checklist — GC-DHI-02

GC-DHI-02 will be approved when:

- [x] Repository structure exists.
- [x] Governance documents are installed.
- [x] ADRs are installed.
- [x] Solution and projects restore.
- [x] Build succeeds without warnings.
- [x] Empty test suites execute.
- [x] Dependency licenses are reviewed.
- [x] Package metadata is present.
- [x] CI skeleton passes in GitHub on Ubuntu and Windows.
- [x] No production diagnostic behavior has been implemented prematurely.
- [x] Bootstrap report lists exact versions and commands.
- [x] `PROJECT_STATE.md` is updated.

---

## 12. Recommended next action

Perform final human review of `docs/bootstrap/GC-DHI-02_REPORT.md`.

Do not continue to GC-DHI-03 or implement PostgreSQL inspection logic or
diagnostic rules until GC-DHI-02 receives human approval.
