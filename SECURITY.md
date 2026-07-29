# Security Policy

## Supported versions

DbHealth Inspector is currently in pre-release bootstrap. No public version is
supported for production use yet.

## Reporting a vulnerability

Do not disclose database credentials, connection strings, customer data or
other secrets in a public issue.

Until a private security-contact channel is published, contact the repository
owner privately and provide only the minimum information required to reproduce
the issue.

## Product security invariants

- Every future inspection must run in an explicit read-only transaction.
- v0.1.0 must not query business-table rows.
- Production SQL is restricted to the approved allowlist.
- Secrets must not appear in console output, logs, reports, exceptions or tests.
- The product reports findings and never applies database changes.

The complete baseline is defined in
[`PROJECT_RULES.md`](docs/agent-governance/PROJECT_RULES.md).
