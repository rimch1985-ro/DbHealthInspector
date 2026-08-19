# PROJECT_STATE — DbHealth Inspector

**Last updated:** 2026-08-19  
**GC-DHI-04F implementation integration date:** 2026-08-19  
**GC-DHI-04F closure date:** 2026-08-19  
**GC-DHI-04F definition date:** 2026-08-10  
**GC-DHI-04F D1 correction date:** 2026-08-10  
**GC-DHI-04E closure date:** 2026-08-10  
**GC-DHI-04E integration date:** 2026-08-10  
**GC-DHI-04E definition date:** 2026-08-10  
**GC-DHI-04E D1 correction date:** 2026-08-10  
**GC-DHI-04D integration authorization date:** 2026-08-10  
**GC-DHI-04D closure date:** 2026-08-10  
**GC-DHI-04C integration authorization date:** 2026-08-01  
**GC-DHI-04C closure date:** 2026-08-01  
**GC-DHI-04B closure date:** 2026-08-01  
**Current phase:** PostgreSQL Metadata Adapter completed; transition to Phase 4 — Diagnostic Rules  
**Current gate:** GC-DHI-04F approved and closed  
**Authorized next action:** Define the next Phase 4 Diagnostic Rules gate; no Phase 4 implementation is authorized  
**PG-01:** completed  
**PG-02:** completed  
**PG-03:** completed  
**PG-04:** completed  
**PG-05:** completed  
**PG-06 foundation:** completed in GC-DHI-04B  
**PG-06 full completion:** completed in GC-DHI-04F  
**GC-DHI-04B:** approved and closed  
**GC-DHI-04C:** approved and closed  
**GC-DHI-04D:** approved and closed  
**GC-DHI-04E:** approved and closed  
**GC-DHI-04F:** approved and closed  
**GC-DHI-03B closure date:** 2026-07-30  
**GC-DHI-04A closure date:** 2026-07-31  
**Target release:** v0.1.0-rc.1

---

## 1. Executive status

DbHealth Inspector has a locally and remotely validated .NET 10 repository at
`https://github.com/rimch1985-ro/DbHealthInspector`.

GC-DHI-04A through GC-DHI-04F are approved and closed. GC-DHI-04F — Snapshot
Provider Composition and PostgreSQL Verification — was implemented through the
authorized Claude Code/Codex review sequence and integrated through pull request
`#8`. Implementation commit
`657e1596c1bbc34d592136933abd823df4e89f58` was followed by two separately
reviewed integration corrections: CI provenance commit
`0624652fd0d117e03ac72d1bf53e30c21cda852a` and test-only SSH.NET security
commit `57b4d3f76a6fb1cf6d84b05c051895f3f4468b77`. PR #8 was merged by explicit
merge commit `1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177`.

The PostgreSQL adapter now exposes the approved
`PostgreSqlDatabaseSnapshotProvider`, composes one complete engine-neutral
`DatabaseSnapshot` through one verified read-only Repeatable Read transaction,
reuses one validated schema filter, preserves atomicity and deterministic
ordering, and retains the exact ten-statement productive inventory. No new
productive SQL, Core semantic, diagnostic rule, CLI inspection command or JSON
reporting behavior was added by GC-DHI-04F.

The productive inventory remains exactly B001–B003, C001–C004, D001, E001 and
E002: ten static statements, eight command kinds, two parameter types, ten
inventory definitions and ten frozen contracts. The fail-closed validator
accepts exactly ten and rejects 790 of 800 ID/kind/SQL combinations. Product
code accepts no raw or caller-provided SQL; schema filters remain parameterized
as `text[]` values.

PR CI run `32296079038` (#45) passed Ubuntu, PostgreSQL 15 and Windows. Ubuntu
reported 1935 UnitTests, 13 non-server integration tests and 174 PostgreSQL 18
integration tests; PostgreSQL 15 reported 24 tests; Windows reported 1935
UnitTests, 13 non-server integration tests and a passing bootstrap CLI smoke.
Every partition had zero failures and zero skipped tests and every build had
zero warnings and zero errors.

The implementation was merged to `master` by
`1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177`. Canonical master CI run
`32297138214` (#46) passed the same three protected jobs with the same counts and
zero failures/skips/warnings/errors. On master, `CI_SOURCE_REVISION` resolved to
the merge SHA; both the CLI informational version and the package `.nuspec`
repository commit were verified as exactly
`1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177` before artifact upload.

The canonical master artifact `dbhealth-bootstrap-package` is artifact ID
`9381656515`, 973024 bytes, with GitHub digest
`sha256:367da3484178f003432898f9a58e7d6a475efa2fc5da094cbcc0e60aeafcf890`.
The independently downloaded ZIP has the matching SHA-256. Its sole package,
`DbHealthInspector.Tool.0.1.0-alpha.0.nupkg`, is 977570 bytes with SHA-256
`36F56758865227B2C8C873E4D9BD1922D46D257A47DAE5CFF287C598A69D2197`.
The package contains no SSH.NET, Renci.SshNet, Testcontainers or xUnit runtime
asset and references the exact merge commit.

The human project owner independently installed that exact canonical package
from a temporary local NuGet source on Windows. Installation succeeded,
`dbhealth --version` returned
`0.1.0-alpha.0+1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177`, and `dbhealth --help`
remained bootstrap-only with only help/version behavior. The temporary tool and
source were removed after verification.

PG-06's five final acceptance criteria are satisfied together: every productive
SQL resource is inventoried; prohibited classes and 790 wrong tuples fail
closed; no product path accepts user SQL; schema values use one validated
parameterized filter; and the permanent safety documentation, PostgreSQL 15/18
matrix and package/leakage scans are green. PG-06 is therefore completed.

`master` remains protected and requires `Ubuntu`, `Windows` and `PostgreSQL 15`.
No force push, rebase, squash, tag, release, NuGet publication or branch-
protection mutation was performed. GC-DHI-04F and the PostgreSQL Metadata
Adapter phase are closed; the next work is definition of the Phase 4 Diagnostic
Rules gate under separate human authorization.

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
- `master` protection requires strict GitHub Actions checks. Force pushes and
  branch deletion are disabled; no third-party review is required.
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
  subgates.
- GC-DHI-04A completed PG-01 and was approved and closed on 2026-07-31.
- GC-DHI-04B was defined as the read-only session and SQL safety kernel gate,
  covering PG-02 and the foundation of PG-06 without implementing product
  behavior.
- GC-DHI-04B completed PG-02 and the PG-06 foundation, passed focused local and
  multiplatform CI validation, and was integrated through pull request `#4`
  with an explicit merge commit on 2026-08-01.
- The GC-DHI-04B master artifact was independently audited and installed only
  in a temporary isolated tool path, which was removed after verification.
- Human closure of GC-DHI-04B was approved on 2026-08-01 after review of the
  complete implementation, correction, PR, CI, PostgreSQL safety, artifact,
  package and governance evidence.
- GC-DHI-04C implemented and integrated PG-03 through pull request `#5` with
  an explicit merge commit, green Ubuntu/Windows CI and an independently
  audited merge artifact and isolated tool installation.
- The human project owner approved and closed GC-DHI-04C on 2026-08-01 after
  reviewing its complete definition, implementation, correction, CI, artifact,
  package, installation and governance record. PG-03 is completed.
- GC-DHI-04D implemented and integrated PG-04 through pull request `#6` with
  an explicit merge commit, green Ubuntu/Windows CI, an independently audited
  canonical merge artifact and a successful isolated tool installation.
- D001, C002, `TextArray`, strict ten-column mapping, ordinal ordering,
  cancellation, cleanup, 17/13 relation-state classification and the complete
  PostgreSQL 18.4 empirical matrix are integrated.
- The human project owner approved and closed GC-DHI-04D on 2026-08-10. PG-04
  is completed.
- The GC-DHI-04E definition and D1 correction were integrated as governance-only
  prerequisites on 2026-08-10 before the separately authorized PG-05
  implementation.
- D1-01 established the 31-column E001 shape and preserves exact ordered
  operator-class options in structural identity.
- GC-DHI-04E completed its three authorized Claude implementation phases,
  Codex R1, Claude C1 and Codex R2 with all five R1 findings resolved.
- PG-05 was integrated through pull request `#7` with explicit merge commit
  `f78720891766f831e1fd7d46a68c2aef9dbb83f2`, green Ubuntu/Windows CI, an
  independently audited canonical merge artifact and isolated tool install.
- The human project owner approved and closed GC-DHI-04E on 2026-08-10. PG-05
  is completed.
- GC-DHI-04F implemented the final PostgreSQL snapshot provider composition,
  permanent PostgreSQL 15.18/18.4 compatibility matrix, same-session and
  same-transaction evidence, lifecycle/cancellation coverage and package safety
  verification without adding productive SQL.
- PR #8 integrated the implementation through merge commit
  `1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177`; canonical master CI
  `32297138214` passed Ubuntu, PostgreSQL 15 and Windows with zero failures,
  skips, warnings or errors.
- The canonical master artifact ID `9381656515` and its sole NuGet package were
  independently audited; the package SHA-256 is
  `36F56758865227B2C8C873E4D9BD1922D46D257A47DAE5CFF287C598A69D2197`.
- The human project owner independently installed the exact canonical package;
  bootstrap help/version behavior and merge-SHA provenance were verified and
  the temporary installation was removed.
- PG-06's final allowlist acceptance criteria are satisfied and PG-06 is
  completed.
- The human project owner approved and closed GC-DHI-04F on 2026-08-19. The
  PostgreSQL Metadata Adapter phase is completed.

---

## 7. Work authorized next

Define the technical scope and authorization criteria for the next Phase 4 —
Diagnostic Rules — gate. No Phase 4 diagnostic implementation is authorized by
GC-DHI-04F closure.

---

## 8. Work not yet authorized

The following are not yet authorized:

- Implement production diagnostic rules.
- Start implementation of the next functional gate before its definition and
  explicit human authorization.
- Publish a NuGet package.
- Publish a GitHub release.
- Create release tags.
- Add another database engine.
- Add data profiling.
- Add report formats other than JSON.
- Change the approved architecture.
- Change stable finding codes.

These require completion of the relevant future gate or separate release
authorization.

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
  xunit.runner.visualstudio 3.1.5, Microsoft.NET.Test.Sdk 18.8.1,
  Testcontainers.PostgreSql 4.13.0 and test-only SSH.NET 2026.0.0 with
  `PrivateAssets=all`.
- Analyzer baseline: built-in .NET analyzers at `latest-recommended`, style
  enforcement during build and warnings as errors.
- GitHub Actions required matrix: `Ubuntu`, `Windows` and `PostgreSQL 15`.
- Remote: `https://github.com/rimch1985-ro/DbHealthInspector.git`.
- Required `master` checks: `Ubuntu`, `Windows` and `PostgreSQL 15`; protection
  remains enabled.

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

Resolved during GC-DHI-04F:

- The PostgreSQL adapter has one public snapshot provider over the approved
  04A–04E primitives.
- PostgreSQL 15.18 and 18.4 are permanent CI targets with shared cross-version
  Core contract assertions.
- One validated schema filter is reused by D001/E001/E002 composition.
- Productive SQL remains the exact ten-statement closed inventory.
- PG-06 is fully completed after final inventory, validator, parameterization,
  documentation, 15/18 and package-safety verification.
- Pull-request package provenance uses the canonical feature head while tests
  continue to execute the synthetic merge result; push provenance uses
  `github.sha`.

Pending product decisions:

- Reproducible invalid-index test strategy for the diagnostic-rule phase.
- Console rendering format.
- Final CLI error format.
- Connection-source precedence.
- Exact minimum PostgreSQL role permissions.
- Final hostname policy for reports.
- Source Link activation remains pending; package repository URL and exact
  commit metadata are already present without adding a new dependency.

---

## 10. Known risks

| Risk | Current control |
|---|---|
| Scope growth | Frozen v0.1.0 exclusions |
| Unsafe SQL | Read-only transaction and closed SQL allowlist |
| Secret exposure | Mandatory redaction and leakage tests |
| Statistics misinterpretation | Confidence and evidence model |
| Duplicate-index false positives | Exact structural equivalence only |
| PostgreSQL version differences | Permanent 15.18/18.4 CI matrix and shared assertions |
| Overengineering | Three-project architecture |
| Package licensing ambiguity | Dependency review before adoption |
| Unreliable invalid-index fixture | Separate diagnostic-rule strategy |
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

| Item | Verified value |
|---|---|
| Phase | PostgreSQL Metadata Adapter |
| Sequence | `GC-DHI-04A → GC-DHI-04B → GC-DHI-04C → GC-DHI-04D → GC-DHI-04E → GC-DHI-04F` |
| Closed | `GC-DHI-04A — Connection Boundary and Secret Hygiene`; `GC-DHI-04B — Read-Only Session and SQL Safety Kernel`; `GC-DHI-04C — Server Metadata and Capability Probe`; `GC-DHI-04D — Table Snapshot Query and Mapping`; `GC-DHI-04E — Index Snapshot Query and Mapping`; `GC-DHI-04F — Snapshot Provider Composition and PostgreSQL Verification` |
| Phase status | `COMPLETED` |
| Architecture | `PostgreSql → Core`; Core has no infrastructure dependency |
| Safety | Static inventoried SQL, parameterized external values, explicit read-only transaction |
| GC-DHI-04B transaction | `RepeatableRead`, read-only, non-deferrable, rollback only |
| GC-DHI-04B timeouts | Statement 30 s; lock 5 s; idle-in-transaction 60 s |
| Productive inventory | Exactly B001–B003, C001–C004, D001, E001 and E002; ten statements total |
| Inventory totals | Ten statements, eight kinds, two parameter types, ten definitions and ten frozen contracts |
| Validator | 800 ID/kind/SQL combinations; exactly 10 accepted and 790 rejected |
| Supported versions | PostgreSQL 15.18 and 18.4 permanently verified |
| Product implementation | GC-DHI-04A through GC-DHI-04F approved and closed |
| PG-06 | Completed by GC-DHI-04F final verification |

All six PostgreSQL Metadata Adapter subgates completed their required definition,
implementation/review where applicable, human authorization, PR integration,
canonical CI/artifact verification, governance registration and closure. No
Phase 4 implementation is authorized by completion of GC-DHI-04.

## 15. GC-DHI-04A integration record

| Item | Verified value |
|---|---|
| Human authorization date | `2026-07-31` |
| Backlog item | `PG-01` completed |
| Pull request | `#3` — `https://github.com/rimch1985-ro/DbHealthInspector/pull/3` |
| Implementation commit | `8b838721c742b94e7ea0857019d49f5a8798ef79` |
| Merge commit | `923ca38be1698f568665f7eacb3d760530e4a1ee` |
| Integrator | `rimch1985-ro` through Codex DevOps workflow |
| Merge timestamp | `2026-08-01T02:24:40Z` |
| Pull-request CI | `30679883155` — Ubuntu `91314638107`, Windows `91314638092` |
| Master CI | `30679948734` — Ubuntu `91314823473`, Windows `91314823529` |
| Tests per job | 479 passed, 0 failed, 0 skipped |
| Build per job | 0 warnings, 0 errors |
| Master artifact | `dbhealth-bootstrap-package`, ID `8811851264` |
| Package | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg`, 886611 bytes |
| Package SHA-256 | `F640EDAB051AE54A864ECF5A55BCDE45CB0D15316FCF2CA4FB5E08FF91FD4428` |
| Artifact ZIP | 881858 bytes |
| Artifact ZIP SHA-256 | `CC48F43D23BBBDFBEB69BA163819268E12FAAB1BE67BEE1328B222302E5BD037` |
| Tool version | `0.1.0-alpha.0+923ca38be1698f568665f7eacb3d760530e4a1ee` |
| Golden fingerprint | `sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444` |
| Functional exclusions | No SQL, session, capability probe, snapshot mapping, DBH rules, CLI inspection or JSON reporting |
| Subsequent state | GC-DHI-04D approved and closed; GC-DHI-04E definition may be prepared |

## 16. GC-DHI-04B integration record

| Item | Verified value |
|---|---|
| Integration authorization date | `2026-08-01` |
| Backlog coverage | `PG-02` completed; `PG-06` foundation completed; full `PG-06` deferred to GC-DHI-04F |
| Pull request | `#4` — `https://github.com/rimch1985-ro/DbHealthInspector/pull/4` |
| Files integrated | 44 files; 7737 insertions and 4 deletions |
| Implementation commit | `fcefe276a78c0945defcfd4062998a441cf2f44c` |
| Merge commit | `c67c62fbd262c4159cb8fe3a381e2ad299b8f9ce` |
| Merge parents | `a6cae28eeeb30c5ebec75604c586bf1699641139`, `fcefe276a78c0945defcfd4062998a441cf2f44c` |
| Merge timestamp | `2026-08-01T20:34:02Z` |
| Pull-request CI | `30717182433` — Ubuntu `91414655331`, Windows `91414655345` |
| Master CI | `30717262246` — Ubuntu `91414883472`, Windows `91414883480` |
| Governance integration commit | `8c29dd9396aae210f3d9503dba2889398eb06b4d` |
| Governance CI | `30717628722` — Ubuntu `91415872807`, Windows `91415872820` |
| Ubuntu tests | 968 passed, 0 failed, 0 skipped |
| Windows tests | 956 passed, 0 failed, 0 skipped |
| Build per platform | 0 warnings, 0 errors |
| PostgreSQL image | `postgres:18.4` |
| PostgreSQL digest | `sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a` |
| Master artifact | `dbhealth-bootstrap-package`, ID `8823728204`, 919184 bytes |
| GitHub artifact digest | `sha256:dfd55fc0a2cf01ca03e9377cc349a80c81c4a25eef1db0813e3b3f691977b721` |
| Artifact ZIP SHA-256 | `DFD55FC0A2CF01CA03E9377CC349A80C81C4A25EEF1DB0813E3B3F691977B721` |
| Package | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg`, 923753 bytes |
| Package SHA-256 | `5DFAD5257E08599F20B6F96623FB47F1509A669D3644587A2148C761AD2854C3` |
| Package metadata | `DbHealthInspector.Tool`; `DotnetTool`; command `dbhealth`; MIT; repository commit equals merge SHA |
| Tool version | `0.1.0-alpha.0+c67c62fbd262c4159cb8fe3a381e2ad299b8f9ce` |
| Golden fingerprint | `sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444` |
| Publication state | 0 tags; 0 releases; no NuGet publication |
| Functional exclusions | No capability probe, table/index query, snapshot mapping, DBH rule, CLI behavior or JSON reporting |
| Closure date | `2026-08-01` |
| Gate state | `APPROVED AND CLOSED` |

## 17. GC-DHI-04C definition record

| Item | Verified value |
|---|---|
| Definition date | `2026-08-01` |
| Backlog item | `PG-03` completed |
| Definition | `docs/gates/GC-DHI-04C_DEFINITION.md` |
| Predecessor | GC-DHI-04B approved and closed |
| Result | Internal immutable server-probe result mapped to existing Core contracts |
| Version source | Numeric `server_version_num` only |
| Supported range | PostgreSQL majors 15–18 |
| Productive inventory | B001–B003 plus C001–C004; exactly seven statements |
| Capabilities | Required catalog metadata; optional usage statistics; data profiling disabled |
| PostgreSQL fixture | `postgres:18.4` at the existing immutable digest |
| Implementation | Integrated through pull request `#5` and human-approved |
| Closure date | `2026-08-01` |
| Verdict | `APPROVED AND CLOSED` |

## 18. GC-DHI-04C integration record

| Item | Verified value |
|---|---|
| Integration authorization date | `2026-08-01` |
| Backlog coverage | `PG-03` implemented and integrated |
| Files integrated | 36 files; 5262 insertions and 217 deletions |
| Pull request | `#5` — `https://github.com/rimch1985-ro/DbHealthInspector/pull/5` |
| Implementation commit | `55bb7b93b2a21c5bd24ded21f9df7b5e881c10c5` |
| Merge commit | `6d6772cf59c711ca522902b6135223e0ca00c6a1` |
| Merge parents | `6d1b044aace4567335defbeff17c97a27b97c315`, `55bb7b93b2a21c5bd24ded21f9df7b5e881c10c5` |
| Merge timestamp | `2026-08-02T04:14:10Z` |
| Pull-request CI | `30731960075` — Ubuntu `91453798149`, Windows `91453798196` |
| Master CI | `30732019651` — Ubuntu `91453966554`, Windows `91453966613` |
| Governance integration commit | `73e9a91108d4044963f108b2cb7610e8276c1acc` |
| Governance CI | `30732349125` — Ubuntu `91454852256`, Windows `91454852236` |
| Ubuntu tests | 1286 passed, 0 failed, 0 skipped |
| Windows tests | 1256 passed, 0 failed, 0 skipped |
| Build per platform | 0 warnings, 0 errors |
| PostgreSQL image | `postgres:18.4` |
| PostgreSQL digest | `sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a` |
| Normal evidence | C003 observed `true`; C004 observed; usage statistics available |
| Revoked evidence | PUBLIC/direct grants revoked; C003 observed `false`; C004 absent; usage statistics unavailable |
| Fixture topology | Separate normal/revoked fixtures; serialized collections; 120/30/30-second deadlines |
| Validator evidence | 7 IDs; 6 kinds; 7 definitions; 7 frozen contracts; exactly 7/294 canonical combinations accepted |
| Master artifact | `dbhealth-bootstrap-package`, ID `8828280555`, 931378 bytes |
| GitHub artifact digest | `sha256:dbc0cd2bb489868807245aeeb17583429add5866f936ef0eda16d4a3ea6acd46` |
| Artifact ZIP SHA-256 | `DBC0CD2BB489868807245AEEB17583429ADD5866F936EF0EDA16D4A3EA6ACD46` |
| Package | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg`, 936051 bytes |
| Package SHA-256 | `F5A15EE3608E6D86C13351A60F4CEC6B621F534645DA86B88DAC976881FF51F1` |
| Package metadata | `DbHealthInspector.Tool`; `0.1.0-alpha.0`; `DotnetTool`; `dbhealth`; MIT; repository commit equals merge SHA |
| Isolated installation | Help remained bootstrap-only; version `0.1.0-alpha.0+6d6772cf59c711ca522902b6135223e0ca00c6a1` |
| Golden fingerprint | `sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444` |
| Publication state | No tag, release or NuGet publication |
| Closure date | `2026-08-01` |
| Gate state | `APPROVED AND CLOSED` |

## 19. GC-DHI-04D integration and closure record

| Item | Verified value |
|---|---|
| Definition date | `2026-08-01` |
| Backlog item | `PG-04` completed |
| Definition | `docs/gates/GC-DHI-04D_DEFINITION.md` |
| Predecessor | GC-DHI-04C approved and closed |
| Productive query | D001 — `ReadTableSnapshots`; 1816 characters; SHA-256 `13b4e88d7ac0053d87cf760b3e6a64ae879effa91de66a15bd693ba458680b87` |
| Productive inventory | Eight statements: B001–B003, C001–C004 and D001 |
| Command kinds | Seven, including `SelectTableMetadata` |
| Parameter types | Two, including `TextArray` |
| Result | Internal defensive `TableSnapshot` collection in ordinal canonical order |
| Schema filters | Exact ordinal include/exclude names bound as non-null `text[]` arrays |
| System exclusions | `pg_catalog`, `information_schema`, `pg_toast*`, `pg_temp_*` |
| Capability change | C002 retains its identity and adds the three required size-function `EXECUTE` checks |
| Pull request | `#6` — `https://github.com/rimch1985-ro/DbHealthInspector/pull/6` |
| Implementation commit | `f60057d9899dc541ea76c584b0af67225b147f5b` |
| Merge commit | `89a74c2e6a57c5ef732f5f46bac7e6a9ccc5e236` |
| Pull-request CI | `31422387953` — Ubuntu `93565994145`, Windows `93565993989` |
| Master CI | `31422585918` — Ubuntu `93566643563`, Windows `93566643623` |
| Governance integration commit | `e0180b718deaab6a0d4f415b195b4a0880c0eab6` |
| Governance CI | `31423561874` — Ubuntu `93569825851`, Windows `93569825907` |
| Tests | Ubuntu 1617; Windows 1517; zero failures and zero skipped |
| Build | Zero warnings and zero errors on both platforms |
| Canonical artifact | `dbhealth-bootstrap-package`; ID `9075942338`; 943676 bytes; digest `sha256:6ff2d0e5eea3e1f458ed9995f73123dc54e28264a3d93a6fea8eb579e5fe5812` |
| Package | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg`; 948373 bytes; SHA-256 `357156EB9BD9FC2140EB6F21D55DE29315CC2A7B521B9F788B437F03DCBC5492` |
| Isolated installation | Bootstrap-only help; version `0.1.0-alpha.0+89a74c2e6a57c5ef732f5f46bac7e6a9ccc5e236` |
| Publication state | No tag, release or NuGet publication |
| Closure date | `2026-08-10` |
| Gate state | `APPROVED AND CLOSED` |

## 20. GC-DHI-04E definition record

| Item | Verified value |
|---|---|
| Definition date | `2026-08-10` |
| Backlog item | `PG-05` implemented and integrated |
| Definition | `docs/gates/GC-DHI-04E_DEFINITION.md` |
| Predecessor | GC-DHI-04D approved and closed |
| Required query | E001 — `ReadIndexMetadata`; exact frozen structural metadata SQL |
| Optional query | E002 — `ReadIndexUsageStatistics`; exact frozen `idx_scan` SQL |
| Capability expansion | C002 adds only four E001 function checks; C003 unchanged |
| Integrated inventory | B001–B003, C001–C004, D001, E001–E002 |
| Integrated totals | Ten statements; eight kinds; two parameter types; ten frozen contracts |
| Integrated validator | 800 combinations; exactly ten accepted and 790 rejected |
| Schema filters | Existing GC-DHI-04D exact include/exclude `TextArray` contract |
| Structural shape | E001 has 31 typed columns and one row per index attribute |
| D1 structural identity | Ordered nullable `pg_attribute.attoptions` is encoded injectively in the existing Core `OperatorClass` string |
| Statistics shape | E002 has four scalar columns and one row per observed physical index |
| Partitioned policy | Physical `i` uses direct size/statistics; virtual `I` uses size zero and null scan count |
| Invalid-index fixture | Deterministic `CREATE INDEX ... ON ONLY` partitioned table |
| Implementation state | Integrated through pull request `#7`; final human closure approved |
| Closure date | `2026-08-10` |
| Verdict | `APPROVED AND CLOSED` |

## 21. GC-DHI-04E integration record

| Item | Verified value |
|---|---|
| Integration authorization date | `2026-08-10` |
| Backlog coverage | `PG-05` implemented and integrated |
| Candidate | 31 files; 6278 insertions and 121 deletions |
| Pull request | `#7` — `https://github.com/rimch1985-ro/DbHealthInspector/pull/7` |
| Implementation commit | `e50442baea7130a094584e5c5024fb92894f95ab` |
| Merge commit | `f78720891766f831e1fd7d46a68c2aef9dbb83f2` |
| Merge parents | `fa7f2eebecebb6230669c715b3f9b4e4ae9552ec`, `e50442baea7130a094584e5c5024fb92894f95ab` |
| Pull-request CI | `31454410407` — Ubuntu `93665147247`, Windows `93665147207` |
| Master CI | `31454525066` — Ubuntu `93665473589`, Windows `93665473508` |
| Ubuntu tests | 1831 unit, 13 non-server, 152 PostgreSQLServer; 1996 total; 0 failed; 0 skipped |
| Windows tests | 1831 unit, 13 non-server; 1844 total; 0 failed; 0 skipped |
| Build per platform | 0 warnings, 0 errors |
| PostgreSQL image | `postgres:18.4` at `sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a` |
| Inventory | B001, B002, B003, C001, C002, C003, C004, D001, E001, E002 |
| Validator | 800 combinations; exactly 10 accepted and 790 rejected |
| Frozen E001 | 6262 characters; SHA-256 `d45b8ed1e0d842b1474839a3beadf6d1a0d4233cfa847c3887c41cfd4b1184d7` |
| Frozen E002 | 737 characters; SHA-256 `fe8f23a5dff2cdfb8d08acf4fb7f7a3f90aef4b7e9eee4b678cde8c260624919` |
| Frozen C002 | 2027 characters; SHA-256 `777cb44afb178c299566f1a8c0251e3ab9ba47480bd578b6a339f4d1c24c5a90` |
| Frozen D001 | 1816 characters; SHA-256 `13b4e88d7ac0053d87cf760b3e6a64ae879effa91de66a15bd693ba458680b87` |
| Canonical artifact | `dbhealth-bootstrap-package`; ID `9087542583`; 964597 bytes |
| Artifact digest / downloaded ZIP | `sha256:89d76672dee68b32ee54ad4c2ef7c5747bd61d199f9b17775d5ffa048a552193` |
| Package | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg`; 969244 bytes; SHA-256 `D6CCDD2D2AF3EFCD750BAA2BB95F7FB698720F4203C0A2A9C84D8EBE892D7257` |
| Isolated installation | Bootstrap-only help; version `0.1.0-alpha.0+f78720891766f831e1fd7d46a68c2aef9dbb83f2` |
| Golden fingerprint | `sha256:34d49fc53bf780ac48ff7c076662687fee038d95701dc3272ee2cb6620cbd444` |
| Governance integration commit | `096a0ecb7ccf6f03861fa6882707e7f5704b24c0` |
| Governance CI | `31455181013` — Ubuntu `93667365741`, Windows `93667365758` |
| Publication state | No tag, release or NuGet publication |
| Functional exclusions | No snapshot provider, diagnostic rule, CLI inspection, JSON reporting or GC-DHI-04F implementation |
| Closure date | `2026-08-10` |
| Gate state | `APPROVED AND CLOSED` |

## 22. GC-DHI-04F definition record

| Item | Defined value |
|---|---|
| Definition date | `2026-08-10` |
| Definition | `docs/gates/GC-DHI-04F_DEFINITION.md` |
| D1 correction date | `2026-08-10` |
| Corrected definition commit | `b952012ed3ced4ef72c9d97039500e0a8c53d0f9` |
| Provider API | Exactly one new public type: `PostgreSqlDatabaseSnapshotProvider` |
| Capture topology | One connection, one verified session, one `RepeatableRead` read-only non-deferrable rollback-only transaction |
| Composition | C001–C004 → D001 → E001 and conditional E002; one immutable schema filter |
| Unsupported server | Complete metadata/capability snapshot with empty object collections; no C002–E002 |
| Object invariant | Every index closes to one table identity; schemas derived in ordinal order |
| Concurrency | Concurrent captures supported with independent scopes and coordinated asynchronous disposal |
| Custom statement timeout | Finite, positive, whole milliseconds, 100 ms through 5 min; validated before resource creation |
| Derived lock timeout | `min(5000, statementTimeoutMilliseconds / 2)` using non-negative integer division; exact whole milliseconds |
| Idle timeout | Exactly 60 seconds; not derived |
| Productive SQL | Unchanged B001, B002, B003, C001, C002, C003, C004, D001, E001 and E002 |
| Validator | Unchanged 800 combinations; exactly 10 accepted and 790 rejected |
| PostgreSQL 15 | `postgres:15.18@sha256:6eb0add3b77c081df18aa518ce43df58fdcc40f2e6d868a6fd08038dc7acd425` |
| PostgreSQL 18 | `postgres:18.4@sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a` |
| Definition probe | PostgreSQL 15.18 executed all ten frozen contracts successfully; disposable container removed |
| Final implementation state | Integrated and approved; see §23 |
| Final gate state | `APPROVED AND CLOSED` |
| PG-06 | `COMPLETED` |

## 23. GC-DHI-04F integration and closure record

| Item | Verified value |
|---|---|
| Implementation integration date | `2026-08-19` |
| Closure date | `2026-08-19` |
| Backlog coverage | Final composition of PG-01 through PG-05; PG-06 completed |
| Pull request | `#8` — `https://github.com/rimch1985-ro/DbHealthInspector/pull/8` |
| Implementation commit | `657e1596c1bbc34d592136933abd823df4e89f58` |
| CI provenance correction | `0624652fd0d117e03ac72d1bf53e30c21cda852a` |
| SSH.NET security correction | `57b4d3f76a6fb1cf6d84b05c051895f3f4468b77` |
| Merge commit | `1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177` |
| Merge parents | `b952012ed3ced4ef72c9d97039500e0a8c53d0f9`, `57b4d3f76a6fb1cf6d84b05c051895f3f4468b77` |
| Pull-request CI | `32296079038` (#45) — Ubuntu, PostgreSQL 15 and Windows passed |
| Master CI | `32297138214` (#46) — Ubuntu `96210964615`, PostgreSQL 15 `96210965000`, Windows `96210965118` |
| Ubuntu tests | 1935 unit, 13 non-server, 174 PostgreSQL 18; 2122 total; 0 failed; 0 skipped |
| PostgreSQL 15 tests | 24 passed, 0 failed, 0 skipped |
| Windows tests | 1935 unit, 13 non-server; 1948 total; 0 failed; 0 skipped; CLI smoke passed |
| Build | Zero warnings and zero errors in all three canonical jobs |
| PostgreSQL 18 | `postgres:18.4@sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a` |
| PostgreSQL 15 | `postgres:15.18@sha256:6eb0add3b77c081df18aa518ce43df58fdcc40f2e6d868a6fd08038dc7acd425` |
| Inventory | B001, B002, B003, C001, C002, C003, C004, D001, E001, E002 |
| Validator | 800 combinations; exactly 10 accepted and 790 rejected |
| Canonical artifact | `dbhealth-bootstrap-package`; ID `9381656515`; 973024 bytes |
| Artifact digest / downloaded ZIP | `sha256:367da3484178f003432898f9a58e7d6a475efa2fc5da094cbcc0e60aeafcf890` |
| Canonical package | `DbHealthInspector.Tool.0.1.0-alpha.0.nupkg`; 977570 bytes |
| Package SHA-256 | `36F56758865227B2C8C873E4D9BD1922D46D257A47DAE5CFF287C598A69D2197` |
| Package provenance | Repository commit and CLI version both reference `1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177` |
| Package leakage | No SSH.NET, Renci.SshNet, Testcontainers or xUnit runtime asset |
| Isolated installation | Exact canonical package installed successfully; bootstrap-only help; version `0.1.0-alpha.0+1206cedd2eebc4d3b0cbf05ef0cc8c359bb8b177`; temporary install removed |
| Required checks | `Ubuntu`, `Windows`, `PostgreSQL 15` |
| PG-06 | All five final criteria satisfied; `COMPLETED` |
| Publication state | No tag, GitHub Release or NuGet publication |
| Gate state | `APPROVED AND CLOSED` |
| Phase state | PostgreSQL Metadata Adapter `COMPLETED` |

The full evidence narrative is recorded in
`docs/gates/GC-DHI-04F_REPORT.md`.

## 24. Recommended next action

Define the next Phase 4 — Diagnostic Rules — gate and its authorization
criteria. No DBH001–DBH005 implementation, release, tag or NuGet publication is
authorized by GC-DHI-04F closure.
