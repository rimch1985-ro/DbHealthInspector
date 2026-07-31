# PROJECT_STATE — DbHealth Inspector

**Last updated:** 2026-07-30  
**Authorization date:** 2026-07-30  
**Current phase:** PostgreSQL Metadata Adapter  
**Current gate:** GC-DHI-04 defined  
**Authorized next subgate:** GC-DHI-04A  
**GC-DHI-04A status:** authorized for implementation after this definition is integrated  
**GC-DHI-04B–04F:** unauthorized  
**GC-DHI-03B closure date:** 2026-07-30  
**Target release:** v0.1.0-rc.1

---

## 1. Executive status

DbHealth Inspector has a locally and remotely validated .NET 10 repository at
`https://github.com/rimch1985-ro/DbHealthInspector`.

GC-DHI-03B was integrated through pull request
`https://github.com/rimch1985-ro/DbHealthInspector/pull/2`. The implementation
commit is `1b342433c170fb0cf6a1a4064f3db761b3d22fbb` and the merge commit is
`9c3054a0220f88ab6ecc6d8248de8b8a9cdffbd5`.

CORE-01 through CORE-05 are integrated. CORE-04 provides engine-neutral
inspection orchestration and deterministic risk summaries without adding an
engine adapter or executable diagnostic rules.

GC-DHI-03B remains approved and closed.

Pull-request run `30569512288`, master run `30569647753` and governance run
`30570469692` passed on Ubuntu and Windows. Each job completed 365 tests with
zero failures, zero skipped tests, zero build warnings and zero build errors.

The master artifact `dbhealth-bootstrap-package` was independently audited. Its
package SHA-256 is
`243761AB6AC299DD7630499172A899346EC72A6C0748433A59056E76F61DEB89`;
the artifact ZIP SHA-256 is
`669E473FDEB750C2960030080E7EA1DB5FC81A313BB27CADB445FBCFB7C8B606`.
The package references the merge commit, includes `DbHealthInspector.Core`, and
passed isolated `dbhealth --help` and `dbhealth --version` validation.

The stable golden fingerprint remains
`sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444`.
GC-DHI-04 defines the PostgreSQL Metadata Adapter as six sequential subgates.
GC-DHI-04A is the only implementation subgate authorized next; GC-DHI-04B
through GC-DHI-04F remain unauthorized.

No PostgreSQL integration, SQL, executable DBH rules, CLI inspection behavior
or JSON reporting has been implemented. Defining GC-DHI-04 does not start or
implement the adapter.

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
- GC-DHI-03A implemented the engine-neutral finding model, snapshot model,
  diagnostic-rule contract and stable fingerprint format `fp1`.
- Codex reviews R1–R3 completed; all R1/R2 findings were resolved before
  integration.
- Pull request `#1` passed its protected checks and was merged with an explicit
  merge commit on 2026-07-29.
- The GC-DHI-03A merge artifact was audited and installed only in a temporary
  isolated tool path.
- GC-DHI-03B implemented CORE-04: snapshot-provider abstraction, enabled-rule
  registration, sequential capability-aware orchestration, isolated execution
  outcomes, cancellation semantics, immutable inspection results, summary
  counts and deterministic overall risk.
- Codex reviews R1 and R2 completed after Claude Code correction C1; all
  findings were resolved before integration.
- Pull request `#2` passed its protected checks and was merged with an explicit
  merge commit on 2026-07-30.
- The GC-DHI-03B merge artifact was audited and installed only in a temporary
  isolated tool path, which was removed after verification.
- Human closure of GC-DHI-03B was approved on 2026-07-30 and recorded in
  closure commit `55c538c88360a0a16e58203f949499ae6db962e9`.
- GC-DHI-04 was defined as six sequential PostgreSQL Metadata Adapter
  subgates, with GC-DHI-04A authorized as the only next implementation
  subgate.

---

## 7. Work authorized next

Prepare the Claude Code implementation prompt for GC-DHI-04A — Connection
Boundary and Secret Hygiene.

After this definition is integrated, GC-DHI-04A is authorized for
implementation. It must complete its implementation, Codex review, corrections,
human approval, PR integration, green CI, governance record and closure before
GC-DHI-04B can be considered.

---

## 8. Work not yet authorized

The following are not yet authorized:

- Implement GC-DHI-04B through GC-DHI-04F.
- Skip or combine GC-DHI-04 subgates.
- Implement PostgreSQL catalog queries before their authorized subgate.
- Implement production diagnostic rules.
- Start the next functional gate.
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

Resolved during GC-DHI-03A:

- Evidence values use validated strings.
- Fingerprints use version `fp1`, length-prefixed UTF-8 fields normalized to
  Unicode Form C, sorted participating evidence and SHA-256 output.
- Core collections use defensive copies exposed through non-modifiable
  read-only wrappers.
- `IndexSnapshot` uses order-sensitive structural equality.

Resolved during GC-DHI-03B:

- The snapshot provider is engine-neutral and is invoked exactly once.
- Enabled rules execute sequentially in ordinal finding-code order.
- Unavailable capabilities are canonicalized by numeric `CapabilityKind`.
- Requested cancellation and associated cancellation exceptions propagate
  without partial results.
- Recoverable failures are isolated; process-level exceptions propagate.
- Findings, execution records and derived results are immutable.
- Summary counts and overall risk are derived deterministically from final
  collections.

Pending product decisions:

- Exact timeout defaults after validation.
- Reproducible invalid-index test strategy.
- Console rendering format.
- Final CLI error format.
- Connection-source precedence.
- Permanent PostgreSQL 15/18 CI matrix.
- Exact minimum PostgreSQL role permissions.
- Final hostname policy for reports.
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

## 12. GC-DHI-03A integration record

| Item | Verified value |
|---|---|
| Approval date | 2026-07-29 |
| Pull request | `#1` |
| Implementation commit | `55cd1faab22c3a10876b57cdcc01438a3c7a20a1` |
| Merge commit | `d11c17926064c12c4214195a361e7ac1c239da9e` |
| Pull-request CI | `30490286220` — Ubuntu and Windows passed |
| Master CI | `30490397279` — Ubuntu and Windows passed |
| Tests per job | 240 passed, 0 failed, 0 skipped |
| Build per job | 0 warnings, 0 errors |
| Package SHA-256 | `17EDBA00EE1DF7DA858082BD615A4594FF6FAB9DDD9196BD00CA47789F01966D` |
| Golden fingerprint | `sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444` |

## 13. GC-DHI-03B integration record

| Item | Verified value |
|---|---|
| Authorization date | 2026-07-30 |
| Backlog item | `CORE-04` integrated |
| Pull request | `#2` |
| Implementation commit | `1b342433c170fb0cf6a1a4064f3db761b3d22fbb` |
| Merge commit | `9c3054a0220f88ab6ecc6d8248de8b8a9cdffbd5` |
| Pull-request CI | `30569512288` — Ubuntu and Windows passed |
| Master CI | `30569647753` — Ubuntu and Windows passed |
| Tests per job | 365 passed, 0 failed, 0 skipped |
| Build per job | 0 warnings, 0 errors |
| Package SHA-256 | `243761AB6AC299DD7630499172A899346EC72A6C0748433A59056E76F61DEB89` |
| Artifact ZIP SHA-256 | `669E473FDEB750C2960030080E7EA1DB5FC81A313BB27CADB445FBCFB7C8B606` |
| Golden fingerprint | `sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444` |
| Functional exclusions | No PostgreSQL, SQL, executable DBH rules, CLI inspection or JSON reporting |

## 14. GC-DHI-04 definition record

| Item | Defined value |
|---|---|
| Phase | PostgreSQL Metadata Adapter |
| Sequence | `GC-DHI-04A → GC-DHI-04B → GC-DHI-04C → GC-DHI-04D → GC-DHI-04E → GC-DHI-04F` |
| Authorized next | `GC-DHI-04A — Connection Boundary and Secret Hygiene` |
| Unauthorized | `GC-DHI-04B` through `GC-DHI-04F` |
| Architecture | `PostgreSql → Core`; Core has no infrastructure dependency |
| Safety | Static inventoried SQL, parameterized external values, explicit read-only transaction |
| Supported versions | PostgreSQL 15–18; mandatory 15/18 verification in GC-DHI-04F |
| Product implementation | Not started by the definition gate |

Each subgate requires implementation by Claude Code, Codex review, correction
when needed, human approval, PR integration, green CI, governance registration
and closure before the next subgate may begin.

## 15. Recommended next action

Prepare the Claude Code implementation prompt for GC-DHI-04A.

No GC-DHI-04B through GC-DHI-04F implementation is authorized.
