# AGENT_OPERATING_MODEL — DbHealth Inspector

**Version:** 0.1  
**Status:** Approved  
**Effective date:** 2026-07-28  
**Related gate:** GC-DHI-01 amendment

---

## 1. Purpose

This document establishes the official division of responsibilities between the two AI-assisted engineering agents used by the project:

- **Claude Code:** primary software implementation agent.
- **Codex:** technical coordinator, DevOps agent, integration reviewer and GitHub operator.

The model preserves project continuity, assigns each agent to its strongest role and reduces unnecessary consumption of Codex capacity.

---

## 2. Operating principle

```text
ChatGPT / Project Owner
        ↓ defines scope, gates and approved prompts

Claude Code
        ↓ implements authorized software changes locally

Codex
        ↓ reviews, validates, integrates and operates GitHub/DevOps

Human approval
        ↓ authorizes sensitive gates

GitHub / CI / Releases
```

> Claude Code writes the product. Codex protects the project and integrates the product.

---

## 3. Claude Code responsibilities

Claude Code is the primary programmer.

It is responsible for:

- Implementing authorized backlog items.
- Writing and refactoring production code.
- Writing unit and integration tests associated with its implementation.
- Updating technical documentation affected by its changes.
- Running local restore, build and relevant tests.
- Reporting changed files, validation results, limitations and risks.
- Leaving the working tree in a reviewable state.
- Following `AGENTS.md`, `PROJECT_RULES.md`, accepted ADRs and the current gate prompt.

Claude Code should be used for:

- Core models.
- Diagnostic rules.
- PostgreSQL adapters and catalog queries.
- CLI behavior.
- Report generation.
- Demo implementation.
- Test implementation.
- Documentation coupled to product behavior.

### Claude Code prohibitions

Claude Code must not, unless a future approved rule changes this model:

- Push to remote repositories.
- Open or merge pull requests.
- Change branch-protection rules.
- Create or publish tags.
- Publish NuGet packages.
- Create GitHub releases.
- Modify GitHub repository settings.
- Approve its own integration.
- Expand product scope.
- Change accepted ADRs or canonical rules without authorization.
- Perform release operations.

Claude Code may propose a local commit plan, but remote GitHub operations belong to Codex.

---

## 4. Codex responsibilities

Codex is the DevOps agent and technical integration controller.

It is responsible for:

- Preserving cross-project context and technical direction.
- Preparing or validating tightly scoped implementation prompts for Claude Code.
- Reviewing Claude Code's completed changes.
- Checking scope compliance and architecture boundaries.
- Running complete validation required by the current gate.
- Reviewing dependency, security and licensing implications.
- Managing CI/CD workflows.
- Managing GitHub branches, commits, pushes and pull requests when authorized.
- Reviewing and correcting integration or DevOps defects.
- Merging only after the applicable quality gate is approved.
- Creating annotated tags and releases only after explicit human authorization.
- Verifying package, commit and tag integrity.
- Updating canonical project state after verified integration.

Codex should be used for:

- Repository bootstrap.
- Build governance.
- CI/CD.
- Dependency review.
- GitHub integration.
- Release engineering.
- Branch and pull-request management.
- Cross-cutting architecture review.
- Final gate validation.

### Codex scope restraint

To preserve Codex capacity:

- Codex must not reimplement feature code already completed by Claude Code merely for stylistic preference.
- Codex should request a targeted Claude correction when a feature defect is substantial.
- Codex may directly correct small DevOps, build, packaging, merge-conflict or integration defects when this is lower risk than another handoff.
- Codex reports findings compactly and references canonical documents instead of repeating them.
- Codex uses diffs and validation evidence rather than rewriting entire files unnecessarily.

---

## 5. Human responsibilities

The project owner retains final authority over:

- Product scope.
- Architecture gates.
- Acceptance of ADRs.
- Merge authorization when required.
- Release authorization.
- Public package publication.
- Stable tags.
- Security-sensitive exceptions.
- Changes to this operating model.

ChatGPT acts as technical coordinator and prompt designer but does not replace the owner's approval.

---

## 6. Standard delivery workflow

### Step 1 — Definition

ChatGPT and the project owner define the gate, authorized backlog items, acceptance criteria, prohibited work, validation and expected report.

### Step 2 — Implementation by Claude Code

Claude Code:

1. Inspects the repository.
2. Implements only authorized work.
3. Adds tests and documentation.
4. Runs local validation.
5. Produces a structured handoff report.
6. Does not push, merge, tag or publish.

### Step 3 — Review and integration by Codex

Codex:

1. Reads the Claude handoff.
2. Reviews the actual diff.
3. Verifies scope and architecture.
4. Runs the complete gate validation.
5. Fixes only minor integration or DevOps issues directly.
6. Returns substantial feature defects to Claude Code.
7. Performs authorized GitHub operations.
8. Updates project state.
9. Stops at the next human-approval gate.

### Step 4 — Human gate

The project owner approves, rejects or requests correction.

---

## 7. Required Claude-to-Codex handoff

Claude Code must provide:

```text
1. Objective completed
2. Authorized backlog items addressed
3. Files created
4. Files modified
5. Implementation summary
6. Tests added or updated
7. Commands executed
8. Validation results
9. Known limitations
10. Risks or deviations
11. Working-tree status
12. Recommended Codex review focus
```

---

## 8. Required Codex review output

Codex must provide:

```text
1. Scope compliance
2. Architecture compliance
3. Security and dependency review
4. Validation evidence
5. Defects found
6. Corrections applied by Codex
7. Corrections returned to Claude Code
8. Git/GitHub actions performed
9. Gate verdict
10. Next authorized action
```

---

## 9. Responsibility matrix

| Activity | Claude Code | Codex | Human |
|---|---:|---:|---:|
| Product implementation | Responsible | Reviews | Approves gates |
| Unit/integration tests | Responsible | Verifies | — |
| Feature documentation | Responsible | Verifies | — |
| Architecture direction | Consulted | Enforces | Approves |
| Dependency review | Proposes/uses | Responsible | Approves exceptions |
| CI/CD | May edit when tasked | Responsible | Approves major changes |
| Remote Git operations | No | Responsible | Authorizes sensitive actions |
| Pull requests | No | Responsible | Reviews when desired |
| Merge | No | Executes when authorized | Final authority |
| Tags/releases | No | Executes when authorized | Explicit approval |
| GitHub settings | No | Responsible when authorized | Explicit approval |
| Scope changes | No | No unilateral change | Responsible |

---

## 10. Availability and continuity rule

When Codex capacity is unavailable:

- Claude Code may continue already authorized local development tasks.
- Remote integration, merge, tag and release remain under the Codex DevOps role.
- Claude Code must preserve a clean handoff for later Codex review.

When Claude Code capacity is unavailable:

- Codex may continue DevOps, review, documentation validation and integration tasks.
- Codex should avoid substantial feature development unless the project owner explicitly authorizes an exception.

---

## 11. Exception process

Any temporary role exception must state:

- Why the normal agent is unavailable or unsuitable.
- Exact work being reassigned.
- Duration of the exception.
- Additional review required.
- Who authorizes the exception.

Exceptions do not permanently change this document.

---

## 12. Token-efficiency rules

- Prompts reference canonical files instead of reproducing their contents.
- Each agent receives only the context needed for the current gate.
- Claude Code performs implementation-heavy work.
- Codex focuses on review evidence, DevOps and integration.
- Repeated explanations are replaced with file paths and backlog IDs.
- Failed reviews return focused correction lists rather than full restatements.
- Codex does not rewrite accepted code solely to impose stylistic preferences.
