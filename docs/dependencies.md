# Direct Dependency Review

**Reviewed:** 2026-07-28  
**Target framework:** `net10.0`  
**Policy:** exact versions are managed centrally in `Directory.Packages.props`.

| Package | Version | Purpose | License | Scope | Decision |
|---|---:|---|---|---|---|
| [System.CommandLine](https://www.nuget.org/packages/System.CommandLine/2.0.10) | 2.0.10 | CLI parsing and generated help | MIT | Runtime, CLI | Accepted |
| [Npgsql](https://www.nuget.org/packages/Npgsql/10.0.3) | 10.0.3 | PostgreSQL data provider | PostgreSQL | Runtime, PostgreSQL adapter | Accepted |
| [xunit.v3](https://www.nuget.org/packages/xunit.v3/3.2.2) | 3.2.2 | Test framework and native runner | Apache-2.0 | Test only | Accepted |
| [xunit.runner.visualstudio](https://www.nuget.org/packages/xunit.runner.visualstudio/3.1.5) | 3.1.5 | VSTest and Test Explorer adapter | Apache-2.0 | Test only, private asset | Accepted |
| [Microsoft.NET.Test.Sdk](https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.8.1) | 18.8.1 | VSTest integration | MIT | Test only | Accepted |
| [Testcontainers.PostgreSql](https://www.nuget.org/packages/Testcontainers.PostgreSql/4.13.0) | 4.13.0 | Disposable PostgreSQL integration-test environments | MIT | Integration tests only | Accepted |

## Rationale

- All direct dependencies use permissive open-source licenses compatible with
  an MIT-licensed project and commercial consulting use.
- `xunit.v3` is used instead of the deprecated `xunit` 2.9.3 metapackage.
- VSTest support is retained through `xunit.runner.visualstudio` and
  `Microsoft.NET.Test.Sdk` for broad IDE and CI compatibility.
- `Testcontainers.PostgreSql` is referenced only by the integration-test project.
- No JSON Schema package is required during the bootstrap gate.
- FluentAssertions, Entity Framework Core, Dapper, MediatR, AutoMapper,
  Spectre.Console, logging frameworks and generic-host packages are not used.

## External-access review

- Runtime packages do not initiate external connections on their own. Npgsql
  can access PostgreSQL only when future product code explicitly invokes it.
- Testcontainers can access the local or configured Docker daemon only when
  integration tests explicitly create a container. The bootstrap smoke test
  does not create one.
- Microsoft test tooling includes telemetry components transitively. CI sets
  both `DOTNET_CLI_TELEMETRY_OPTOUT` and
  `TESTINGPLATFORM_TELEMETRY_OPTOUT`; local validation should set either
  variable to `1` when outbound telemetry is not permitted.

## Repository metadata

Package metadata uses the portfolio convention:
`https://github.com/rimch1985-ro/DbHealthInspector`.
The GitHub repository does not exist yet and must be created during an
explicitly authorized remote-integration step.
