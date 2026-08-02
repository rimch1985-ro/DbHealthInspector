# PostgreSQL Server Metadata and Capability Probe

**Gate:** GC-DHI-04C — Server Metadata and Capability Probe
**Backlog:** PG-03
**Predecessors:** GC-DHI-04A and GC-DHI-04B approved and closed
**Scope:** `DbHealthInspector.PostgreSql.Capabilities`, plus the C001–C004 additions to
`Sessions/` and `Sql/`
**Status:** Implemented; pending Codex review.

## 1. Objetivo

Answer three questions before any later gate reads a single catalog row: *which server is this*,
*may we read what we will need*, and *may we read the optional statistics*. The probe runs only
inside a verified GC-DHI-04B session, executes only inventoried statements, and returns one
immutable result composed entirely of existing Core models.

## 2. Dependencia con 04A y 04B

The probe opens nothing and manages nothing. GC-DHI-04A supplies the connection boundary and the
cancellation-association rule; GC-DHI-04B supplies the `RepeatableRead`, read-only, rollback-only
session, the frozen statement inventory, the fail-closed validator, the typed row seams and the
sanitized error boundary. Neither contract was modified. B001–B003 remain byte-for-byte
unchanged, remain reserved to the runner, and remain unreachable from the callback.

## 3. Tipos internos

| Type | Visibility | Responsibility |
|---|---|---|
| `PostgreSqlServerCapabilityProbe` | internal static method on an internal sealed class | Sequencing and capability composition |
| `PostgreSqlServerProbeResult` | internal sealed | The immutable outcome |
| `PostgreSqlServerIdentity` | internal sealed | C001's three raw values |
| `PostgreSqlVersionSupportStatus` | internal enum | `Supported` / `Unsupported` |
| `PostgreSqlServerVersionNormalizer` | internal static | Numeric normalization and the 15–18 policy |
| `PostgreSqlServerVersionException` | internal sealed | Fixed-message mapping failure |
| `PostgreSqlRequiredCatalogCapabilityException` | internal sealed | Fixed-message required-capability failure |

Everything is `internal`. Core is untouched, and no Npgsql type, SQLSTATE, connection,
transaction, command, connection string or raw SQL crosses into it.

## 4. Resultado

```csharp
internal sealed class PostgreSqlServerProbeResult
{
    DatabaseMetadata Metadata { get; }
    CapabilitySnapshot Capabilities { get; }
    StatisticsSnapshot Statistics { get; }
    int ServerVersionNumber { get; }
    int MajorVersion { get; }
    PostgreSqlVersionSupportStatus VersionSupport { get; }
}
```

Constructor-validated, get-only, no setters, no mutable collection.

The constructor enforces the result's own invariants rather than trusting its caller
(GC-DHI-04C-C1, R1-11):

```text
Metadata.Engine == DatabaseEngine.PostgreSql              (value equality on the Core contract)
                                    else -> "Probe metadata must identify PostgreSQL."

Metadata.EngineVersion == Normalize(ServerVersionNumber)
MajorVersion           == MajorVersionOf(ServerVersionNumber)
VersionSupport         == SupportStatusOf(MajorVersion)
                                    else -> "Probe version fields are inconsistent."
```

Both messages are fixed and name neither the received engine nor any version value. The three
expectations are re-derived by calling `PostgreSqlServerVersionNormalizer`, never by
re-implementing its arithmetic, and nothing derived is stored in an extra field — the type still
has exactly six. A `PostgreSqlServerProbeResult` that exists at all is therefore internally
consistent no matter who constructed it.

Deliberately **not** a `record`: a record's generated `ToString()` would render the database name
and current user structurally. Both are authorized *result* metadata, reachable through
`Metadata`, but they must never appear in an exception, a capability reason, a log or a test
display name — so the inherited `object.ToString()`, which returns only the type name, is the
safer default. `PostgreSqlServerIdentity` is a plain class for the same reason.

## 5. Normalización

The integer `server_version_num` is the only source. Nothing parses `version()`, textual
`server_version`, a vendor suffix, a platform string or a build string.

```text
versionNumber >= 100000:  major = n / 10000, minor = n % 10000        -> "major.minor"
versionNumber <  100000:  major = n / 10000, minor = n / 100 % 100,
                          patch = n % 100                              -> "major.minor.patch"
```

| Input | Normalized | Major | Support |
|---:|---|---:|---|
| 90624 | 9.6.24 | 9 | Unsupported |
| 150000 | 15.0 | 15 | Supported |
| 150016 | 15.16 | 15 | Supported |
| 180004 | 18.4 | 18 | Supported |
| 190000 | 19.0 | 19 | Unsupported |

Formatting uses `CultureInfo.InvariantCulture`, verified under `de-DE`, `fr-FR`, `ar-SA` and
`tr-TR`. Zero, negative and structurally impossible encodings (anything below 10000, which would
imply major 0) raise `PostgreSqlServerVersionException` with a fixed message that never contains
the offending value. No arbitrary upper bound is imposed beyond what the encoding itself implies.

## 6. Política 15–18

```text
15 <= MajorVersion <= 18  ->  Supported
```

An unsupported major is **not** an exception. C001 runs, the version is normalized, C002–C004 are
skipped entirely, and a complete result is returned with both real capabilities `Unavailable` and
the fixed version reason. The reason never names the actual version, database or user.

## 7. C001–C004

The productive inventory now contains exactly seven statements in order: B001, B002, B003, C001,
C002, C003, C004. All four C statements take no parameters, use static SQL, contain no dynamic
identifier, no interpolated value, no semicolon and no second statement.

C001 reads the server's own identity. C002 and C003 ask PostgreSQL about privileges and read no
catalog row at all. C004 is the only statement in the entire inventory with a `FROM` clause, and
it reads the statistics view — never a business row. A test asserts exactly that: the single
`FROM` in the inventory is `FROM pg_catalog.pg_stat_database`.

The C002 allowlist is the frozen 04C baseline — `pg_catalog` USAGE plus SELECT on
`pg_namespace`, `pg_class`, `pg_inherits`, `pg_index`, `pg_attribute`, `pg_am`, `pg_constraint`,
`pg_collation` and `pg_opclass`. Those relations appear **only** as string arguments to
`has_table_privilege`; a test asserts none of them is ever queried. Anything GC-DHI-04D or
GC-DHI-04E needs beyond the list must be added in its own gate before use.

### El validador de dos capas

`PostgreSqlSqlSafetyValidator` applies two layers to every definition, in order
(GC-DHI-04C-C1, R1-01):

```text
Layer 1  ValidateLexicalSafety      the fail-closed scanner, plus command-family agreement
                                    and the placeholder/declaration rules
Layer 2  ValidateFrozenStatementContract
                                    statement id -> exact kind, exact SQL (ordinal), exact
                                    ordered parameter types
```

Layer 1 alone proves only that a statement is *some* safely classified `SELECT`; on its own it
accepts `SELECT 1`, `SELECT version()`, `SELECT * FROM business_table`, a `SELECT` over
`pg_catalog.pg_class` and `SELECT ... UNION SELECT ...`. Layer 2 is what makes those unauthorized.

**The command kind classifies the shape; the statement ID freezes the only authorized SQL; both
must match.** A shared kind is therefore never permission to run an arbitrary statement of that
shape — C002 and C003 are both `SelectCapabilityCheck`, yet neither can carry the other's SQL. The
frozen table reads the SQL from the inventory's canonical `const` fields rather than duplicating
it, and because those are compile-time constants there is no initialization cycle even though the
inventory's constructor calls the validator. There is no relaxed mode, no runtime registration and
no test-only bypass: `ValidateText` runs layer 1 only, resolves no statement id and cannot
authorize anything.

Across all 7 ids × 6 kinds × 7 canonical SQL texts, exactly seven combinations are accepted.

## 8. Shapes

| Statement | Rows | Columns | Nullability | Projection |
|---|---:|---:|---|---|
| C001 | Exactly 1 | Exactly 3 | None null | Int32, string, string |
| C002 | Exactly 1 | Exactly 1 | Non-null | Boolean |
| C003 | Exactly 1 | Exactly 1 | Non-null | Boolean |
| C004 | Exactly 1 | Exactly 1 | Nullable | `DateTimeOffset` when non-null |

Zero rows, a second row, a wrong column count or an unexpected NULL all raise
`PostgreSqlSqlResultShapeException`. C004 is the only statement that opts into a nullable column;
its projection checks `IsNull` before calling `GetDateTimeOffset`, and a non-zero `Offset` is a
mapping failure rather than something to normalize silently. The reader is released through the
existing EDI-safe cleanup on every one of these paths, so a shape failure is never replaced by a
disposal failure.

## 9. Boundary tipado

`PostgreSqlInspectionOperationExecutor` no longer has a generic ID-dispatching method. It exposes
exactly four typed operations:

```text
ReadServerIdentityAsync
CheckCatalogMetadataAccessAsync
CheckUsageStatisticsAccessAsync
ReadStatisticsResetAsync
```

B001–B003 are therefore not merely rejected at run time — there is no surface through which a
caller could name them. Reflection tests assert that no declared method takes a
`PostgreSqlSqlStatementId`, a `string`, or a parameter collection, that no member returns the
executor, a connection, a transaction, a command, the gateway or a reader, and that the type has
no properties at all.

## 10. Catálogo obligatorio

C002 true → `CatalogMetadata` `Available` with a null reason.

C002 false → C003 and C004 are not executed, no partial result is returned, and
`PostgreSqlRequiredCatalogCapabilityException` is thrown with exactly:

```text
Required PostgreSQL catalog metadata is unavailable.
```

Its only constructor is parameterless, so no code path anywhere in the assembly can attach a
message, an inner exception or `Data`. It carries no object name, SQL, current user, database
name, SQLSTATE or PostgreSQL message. An Npgsql error *during* C002 is an ordinary operational
failure and reaches the sanitized GC-DHI-04B boundary; it never becomes optional degradation.

## 11. Estadísticas opcionales

C003 true → `UsageStatistics` `Available` with a null reason, and C004 runs.

C003 false → `UsageStatistics` `Unavailable` with exactly
`Usage statistics are unavailable for this inspection.`, C004 does **not** run, and
`StatisticsResetAtUtc` is null. A test asserts C004's execution count is zero, so "not executed"
is observed rather than assumed.

A null `stats_reset` is a valid answer meaning the server reported no reset. It does not make the
capability unavailable.

## 12. `42501`

The single authorized degradation. It applies only when C003 already returned true and C004 then
fails with exactly SQLSTATE `42501`. The sequence is:

1. catch `PostgresException` filtered to that exact SQLSTATE, scoped to the C004 call alone;
2. re-check requested cancellation and let cancellation win;
3. discard the exception entirely — not stored, wrapped, logged or copied into `Data`;
4. report `UsageStatistics` `Unavailable` with the same generic reason C003-false uses.

Using one reason for both is deliberate: a caller cannot distinguish "never had access" from
"lost access mid-probe", so neither reveals the server's privilege timeline. A test asserts the
two results are indistinguishable.

Every other outcome propagates: any other SQLSTATE, `42501` raised at C001/C002/C003, a shape
failure, or an unexpected exception. Tests cover each.

## 13. DataProfiling

Always `Disabled` with exactly `Data profiling is disabled by product policy.`, in every scenario
including unsupported versions. This is product policy, not a server condition. No business row is
read anywhere in this gate.

## 14. Cancelación

The GC-DHI-04A/04B association rule is reused unchanged — no second algorithm exists. A
pre-canceled token prevents C001 entirely (asserted by an empty execution list). The caller's
exact token reaches all four statements. Cancellation raised from inside any statement's seam
propagates as the same instance and prevents the next statement. Cancellation racing a C004
`42501` lets cancellation win, with a control test proving the same failure degrades when no
cancellation is present. `CancellationToken.None` versus `CancellationToken.None` is never
association. Rollback still uses `CancellationToken.None`.

## 15. Error sanitization

| Situation | Outcome |
|---|---|
| Unsupported version | Reported result; no exception |
| C002 false | Fixed `PostgreSqlRequiredCatalogCapabilityException` |
| C003 false | Reported degradation; no exception |
| C004 exact `42501` after C003 true | Reported degradation; exception discarded |
| Any other Npgsql/Postgres error, any stage | Propagates to the GC-DHI-04B sanitized boundary |
| Shape failure | Propagates as `PostgreSqlSqlResultShapeException` |
| Unexpected exception | Propagates unchanged, same instance |
| Invalid version encoding | Fixed `PostgreSqlServerVersionException` |

No catch-all classifier is added. The only new catch is the typed, stage-local
`catch (PostgresException) when (IsInsufficientPrivilege(...))` around C004.

## 16. Leakage policy

Database name and current user are authorized result metadata and appear only on
`DatabaseMetadata`. They must never appear in an exception message, a capability reason, a log,
`Data`, an inner exception, a parameterized test display name or CI output. Leakage tests plant
synthetic markers in the identity and in every populated `PostgresException` field — message,
SQLSTATE, detail, hint, schema, table, column, constraint, internal query, where, routine — and
assert their absence from every reason, from both exception types, from stack traces and from the
result's `ToString()`. Capability reasons are exactly the three frozen strings.

Two hygiene rules govern the leakage tests themselves (GC-DHI-04C-C1, R1-13). Marker sets are
produced fresh per call by an iterator over constants rather than held in a shared mutable array,
so no test can alter what another test checks. And a leak is asserted with
`Assert.False(leaked, "Sensitive data was exposed.")` rather than `Assert.DoesNotContain`, because
the latter prints the marker and the surrounding surface on failure — which would put the very
value under test into CI output. No marker is used as theory data, so none can reach a test display
name. Coverage is unchanged and still spans `Message`, `ToString()`, `StackTrace`, `Data`,
`InnerException`, every capability reason, the result's `ToString()`, and every field of the result
— which must hold neither the discarded exception nor any delegate whose closure could carry it.

## 17. Permission-loss fixture

The optional-statistics degradation is proven against a real server in a **dedicated, disposable**
PostgreSQL 18 container, never the normal fixture's, because the revocation is a database-wide
`pg_catalog` ACL change.

Revoking only the role's direct grant would prove nothing: `PUBLIC` holds `SELECT` on
`pg_stat_database` and `pg_stat_all_indexes` by default, so an effective path would remain. The
fixture therefore revokes from `PUBLIC` **and** from the role, grants no statistics membership,
and keeps the role `NOSUPERUSER` — a superuser bypasses every privilege check and would make the
whole exercise meaningless.

Before any probe runs, the suite asserts through PostgreSQL's own effective computation:

```text
has_table_privilege(role, 'pg_catalog.pg_stat_database',    'SELECT') = false
has_table_privilege(role, 'pg_catalog.pg_stat_all_indexes', 'SELECT') = false
rolsuper = false
role memberships = (none)
required catalog allowlist = still true
```

It then proves C003 returns false, C004 is never executed, the probe returns successfully,
`UsageStatistics` is `Unavailable` with the exact reason, the other capabilities remain correct,
and no server detail leaks. Every `GRANT`/`REVOKE` lives only in IntegrationTests; none is in the
product.

### Qué prueba el servidor real y qué prueban los unit tests

The two are deliberately **not** interchangeable (GC-DHI-04C-C1, R1-06 and R1-12):

| Evidence | Source |
|---|---|
| C003 actually executed against PostgreSQL | Real server, via the recording gateway |
| The boolean C003 actually read at ordinal 0 (`false` revoked, `true` normal) | Real server |
| C004 actually absent from / present in the executed sequence | Real server |
| B001–B003 actually executed before the probe | Real server |
| The final composed capability result | Real server |
| C004 execution *count* under a scripted gateway | Unit tests only |
| The C004 `42501` race | Unit tests only |
| Non-`42501`, other-stage and unexpected-exception matrices | Unit tests only |
| Version normalization vectors and cultures | Unit tests only |

The observation itself uses a passive, test-only `RecordingPostgreSqlStatementGateway` wrapped
around the real `NpgsqlStatementGateway`. It delegates every execution verbatim, returns exactly
the rows the real gateway produced, forwards disposal unchanged and injects nothing, so removing it
would leave the observed sequence identical. It accepts only an already-resolved
`PostgreSqlPreparedStatement`, opens no raw-SQL path, exposes no connection, transaction, command
or connection string, hands out only a copy of the recorded sequence, and is never referenced by
the product. It is inserted solely by the test-owned harness; no productive API was widened to
support it.

### Topología de fixtures

Each fixture has its **own** collection and its own single container (GC-DHI-04C-C1, R1-07):

| Collection | Fixture | Parallelization | Container |
|---|---|---|---|
| `PostgreSqlServer` | `PostgreSqlServerFixture` | disabled | 1 |
| `PostgreSqlStatisticsRevoked` | `PostgreSqlStatisticsRevokedFixture` | disabled | 1 |

Neither collection registers the other's fixture, so running one suite in isolation never starts
the other's container; both disable parallelization, so when the whole category runs the two go
sequentially. Measured directly: a focused normal run and a focused permission-loss run each show
exactly one `postgres:18.4` container, the whole suite shows two distinct containers with at most
one alive at a time, and every run ends with none left behind.

### Deadlines y cleanup

Both fixtures initialize through the shared test-only `TestFixtureLifecycle` (GC-DHI-04C-C1,
R1-05):

```text
initialization deadline   120 s   linked to the runner token, covering container start,
                                  administrative setup, role/schema/table creation, ACL
                                  revocation and privilege verification
cleanup deadline           30 s   for releasing a partially started fixture
per-test deadline          30 s   every permission-loss test body, fixture start-up excluded
```

If any stage fails, the primary failure is captured with `ExceptionDispatchInfo`, cleanup is
attempted **immediately** under its own budget, and the primary is re-thrown with its stack
intact. A cleanup failure or overrun is discarded: it never replaces the primary, is never
attached as an inner exception and adds nothing to `Data`. The catch-all is strictly transparent —
it does not inspect, classify or sanitize — so an exceeded deadline surfaces as the framework's own
neutral `OperationCanceledException` rather than a message the helper invents. No message names a
connection string, password, host, port, container detail, SQL, database name or role name.

The container reference is cleared before release, so failed initialization and normal disposal can
never dispose it twice, and normal disposal still propagates a genuine disposal failure. The
revoked fixture also verifies its own revocation during initialization, so it can never hand a
suite a container in which the ACL change silently did not apply.

## 18. Ownership y cleanup

The probe owns nothing: no connection, transaction, command or reader, and no cached state
between sessions. Every GC-DHI-04B guarantee still holds — `RepeatableRead`, read-only,
rollback-only, transaction disposed before connection, all cleanup steps attempted, primary
failure and cancellation dominating cleanup, no commit path, no retry, no logging, no
sync-over-async.

## 19. UnitTests

Server-free and deterministic: no Docker, DNS, sleeps or assumed ports. Coverage includes the five
frozen normalization vectors plus range edges, invalid encodings and four non-English cultures;
the seven-statement inventory with exact SQL, kinds, order and zero parameters for C001–C004; the
allowlist's exact ten entries; the "catalog relations are privilege-checked, never queried" rule;
every C001–C004 shape contract including per-ordinal NULLs, non-zero offsets and reader cleanup;
the full probe matrix (supported with and without a timestamp, statistics false, unsupported below
and above range, catalog false, `42501`, non-`42501`, `42501` at other stages, unexpected
exception, exact call order, skip assertions, three capability states, exact reasons); the typed
boundary's reflection constraints; and the leakage markers.

GC-DHI-04C-C1 adds two suites. The frozen-contract matrix proves exactly seven combinations are
accepted across every id/kind/SQL pairing, that every declared statement id has a contract, that
each canonical SQL is rejected under every wrong kind and under every other id, that lexically safe
impostors are still unauthorized, and that each C statement rejects its full mutation list —
prefix, suffix, removed token, changed function, changed function schema, changed object, changed
object schema, changed string literal, extra `FROM`, `JOIN`, second row source, subquery, `UNION`,
`INTERSECT`, `EXCEPT`, `LATERAL`, placeholder, semicolon, comment, `FOR UPDATE`, `SELECT INTO` and
a business table. The scanner is additionally exercised by mutating **canonical** statements rather
than invented text. The result-invariant suite covers a foreign engine, a wrong normalized version,
a wrong major, a wrong support status, null components, impossible encodings and the accepted
supported and unsupported cases. Mutations are addressed by name, so no SQL text reaches a test
display name.

## 20. PostgreSQLServer tests

Reuses only:

```text
docker.io/library/postgres:18.4
sha256:3a82e1f56c8f0f5616a11103ac3d47e632c3938698946a7ad26da0df1334744a
```

Normal suite: real identity (`180004`, `18.4`, major 18, `Supported`, expected database and role),
normal capabilities, UTC-or-null statistics reset, rollback with unchanged persistent state and no
lingering transaction, pool reuse, real cancellation, a bounded duration, and the directly observed
`C003 == true` / C004 executed control. Permission-loss suite: the fixture in §17, plus the
directly observed `C003 == false` / C004 absent case and its own per-test deadlines. All
GC-DHI-04B server tests are retained unchanged.

The non-server IntegrationTests suite also covers `TestFixtureLifecycle` itself with fakes: failure
before the container is active, failure after start, verification failure, cleanup succeeding,
cleanup failing, cleanup throwing synchronously, cleanup overrunning its deadline, initialization
overrunning its deadline, linkage to the runner token, stack preservation and the argument
contract. No destructive failure is provoked against real Docker.

## 21. CI

`.github/workflows/ci.yml` is unchanged. Ubuntu runs UnitTests, non-server IntegrationTests, the
`Category=PostgreSqlServer` suite, pack and upload; Windows runs UnitTests, non-server
IntegrationTests and the CLI smoke test. Zero skipped tests on both.

```text
Unit-test list entries:              1238
Unit-test runtime executions:        1243
Non-server IntegrationTests:           13
PostgreSQLServer IntegrationTests:     30
Local total:                         1286
Expected Ubuntu total:               1286
Expected Windows total:              1256
```

## 22. Limitaciones

- The C002 allowlist is a *privilege* check, not a proof that a later query will succeed: a
  privilege can still be withdrawn between gates, exactly as C004 demonstrates for statistics.
- C003 checks two views. A server that granted one and not the other reports the pair as
  unavailable, which is the intended conservative answer.
- The `42501` degradation is scoped to C004 by construction; a future operational statement that
  wants the same treatment must ask for it explicitly in its own gate.
- The normalizer rejects anything below 10000 as structurally impossible. It imposes no upper
  bound, because the encoding itself does not.
- The permission-loss fixture proves the C003-false path, and both C003 outcomes are now observed
  directly on the real server. The C004 `42501` race remains proven by unit tests only, since
  deterministically revoking a privilege between two statements of one transaction is not something
  PostgreSQL lets a test arrange.
- `PostgreSqlSqlParameterType` has exactly one member, so a *wrong but valid* declared parameter
  type cannot be expressed. What is provable today is that an undefined type cannot even be
  constructed, and that count and position must match exactly; the frozen contract's type
  comparison becomes independently exercisable the moment a second type exists.
- A cleanup that overruns its budget is abandoned rather than cancelled: `DisposeAsync` on a
  Testcontainers container takes no token, so the deadline is enforced by racing it. The abandoned
  task's failure is observed so it cannot resurface elsewhere.

## 23. Trabajo diferido a 04D–04F

Table and index SQL; schema filters; `DatabaseSnapshot` composition; `IDatabaseSnapshotProvider`;
DBH001–DBH005; CLI, JSON, console and exit codes; the final minimum-role deployment recipe; the
permanent PostgreSQL 15/18 matrix; the invalid-index fixture; and full PG-06 completion.

## 24. Declaración

```text
No CLI behavior, JSON reporting, console output or exit code was added.
No diagnostic rule was implemented.
No table query, index query or snapshot provider exists.
No business row is read by the product.
The productive SQL inventory contains exactly B001, B002, B003, C001, C002, C003 and C004.
GC-DHI-04D through GC-DHI-04F were not started.
```
