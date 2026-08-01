# PostgreSQL Connection Boundary and Secret Hygiene

**Gate:** GC-DHI-04A — Connection Boundary and Secret Hygiene (PG-01 — connection factory)
**Backlog item:** PG-01
**Scope:** `DbHealthInspector.PostgreSql.Connections` — connection-string validation and
normalization, `NpgsqlDataSource` ownership, asynchronous connection open, exception
sanitization, disposal.
**Status:** Implemented; corrected per Codex review GC-DHI-04A-R1 (GC-DHI-04A-C1); ready for
human integration review after Codex GC-DHI-04A-R2.

This document describes the connection boundary added in GC-DHI-04A. Every type it introduces
is `internal`: this gate has no public API surface. The boundary will be consumed by later
subgates (GC-DHI-04B onward) from within the same assembly.

## 1. Objetivo

Give the rest of `DbHealthInspector.PostgreSql` exactly one way to turn an already-resolved
PostgreSQL connection string into open `NpgsqlConnection` instances, such that:

- No caller-controlled value (host, database, username, password, options, or anything else)
  can ever leak through an exception, a log line, or a piece of metadata.
- Security-relevant `Npgsql` settings are forced to safe values, never left to whatever the
  caller's connection string happened to specify.
- Ownership of the pooled `NpgsqlDataSource` and of each opened `NpgsqlConnection` is
  unambiguous.
- Cancellation is honored precisely, and never confused with an ordinary connection failure.

## 2. Alcance de GC-DHI-04A

In scope:

- Parsing, validating and normalizing a connection string (`PostgreSqlConnectionStringPolicy`).
- Rejecting a non-empty `Options` value outright.
- Building and owning exactly one `NpgsqlDataSource` per `PostgreSqlConnectionFactory`
  (`PostgreSqlConnectionFactory.Create`).
- Opening connections asynchronously, with correct cancellation propagation
  (`PostgreSqlConnectionFactory.OpenConnectionAsync`).
- Sanitizing any connection-open failure into a fixed, information-free
  `PostgreSqlConnectionException`.
- Exposing a small, explicit metadata allowlist (`PostgreSqlConnectionMetadata`).
- Idempotent asynchronous disposal of the owned data source.

Out of scope (see [§16](#16-elementos-diferidos)):

- Executing any SQL. **No SQL or transaction is created in GC-DHI-04A.**
- Opening a connection against a real PostgreSQL server. **A real PostgreSQL open test is
  deferred to GC-DHI-04B.**
- Any public API: every type in this gate is `internal`.
- Reporting hostname, database name or username anywhere, even in diagnostics. **Hostname,
  database and username reporting policies remain deferred.**

## 3. Diagrama textual

```text
PostgreSqlConnectionFactory.Create(connectionString)
        │
        ├─ PostgreSqlConnectionStringPolicy.ParseAndNormalize(connectionString)
        │       null/empty/whitespace        -> ArgumentNullException / ArgumentException
        │       unparsable syntax            -> ArgumentException (fixed message)
        │       non-empty Options            -> ArgumentException (fixed message)
        │       otherwise: apply the 8 mandatory security overrides, return builder
        │
        ├─ PostgreSqlConnectionStringPolicy.DeriveMetadata(builder)
        │       -> PostgreSqlConnectionMetadata (TargetKind, Port, SslMode, Pooling,
        │          ConnectionTimeoutSeconds only)
        │
        ├─ new NpgsqlDataSourceBuilder(builder.ConnectionString).Build()
        │       not caught — a failure here is a broken invariant or Npgsql misuse against
        │       already-validated, already-normalized configuration, not invalid caller input
        │       (GC-DHI-04A-C1, F-01); it propagates with full, untouched fidelity
        │
        └─ new PostgreSqlConnectionFactory(dataSource, metadata, opener)   — the raw connection
               string and the NpgsqlConnectionStringBuilder are both discarded here; only the
               NpgsqlDataSource, the sanitized metadata and the opener survive. `opener`
               defaults to NpgsqlDataSourceConnectionOpener.Default (production) or a fake
               (tests) — see §5.

PostgreSqlConnectionFactory (IAsyncDisposable)
        │
        ├─ Metadata { get; }                          — readable before and after disposal
        │
        ├─ OpenConnectionAsync(cancellationToken)
        │       disposed                     -> ObjectDisposedException, opener never invoked
        │       token already canceled       -> OperationCanceledException, opener never invoked
        │       opener(dataSource, token)                     [PostgreSqlConnectionOpener seam]
        │           success                       -> NpgsqlConnection (caller-owned)
        │           OCE associated with token      -> propagates unchanged (IsRequestedCancellation)
        │           OCE NOT associated with token  -> SanitizeOrThrowIfCanceled:
        │                                               token canceled meanwhile -> OCE
        │                                               otherwise                -> PostgreSqlConnectionException
        │           NpgsqlException                -> SanitizeOrThrowIfCanceled (same as above)
        │           anything else (ObjectDisposedException, InvalidOperationException,
        │           ArgumentException, NullReferenceException, process-corruption types, …)
        │                                           -> not caught; propagates unchanged (F-01)
        │
        └─ DisposeAsync()
                Interlocked.Exchange(ref _dataSource, null)
                first call  -> disposes the exchanged NpgsqlDataSource
                later calls -> no-op
```

## 4. Ownership

**The factory owns the NpgsqlDataSource.** `PostgreSqlConnectionFactory` builds exactly one
`NpgsqlDataSource` in `Create` and holds it in a single private field for its entire lifetime.
Nothing else in the assembly is allowed to build a second one from the same factory instance,
and the field is never exposed, not even through an `internal` accessor.

**The caller owns each returned NpgsqlConnection.** `OpenConnectionAsync` returns a connection
the factory does not retain any reference to; disposing the factory never implicitly disposes
a connection a caller is still holding, and disposing a returned connection never affects the
factory or any other connection obtained from it. The factory does not track how many
connections it has handed out.

**The original connection string is not retained in a separate application field and is not
exposed by the factory or metadata.** It exists only as a local value during `Create` (via
`PostgreSqlConnectionStringPolicy.ParseAndNormalize`) and is never stored on
`PostgreSqlConnectionFactory`, `PostgreSqlConnectionMetadata`, or any exception type in this
boundary. The built `NpgsqlDataSource` necessarily retains private connection configuration so
that it can open connections; the boundary neither exposes that data source nor promises that
Npgsql retains no connection information internally.

## 5. API interna

Every type below is `internal`. `Properties/AssemblyInfo.cs` grants
`InternalsVisibleTo("DbHealthInspector.UnitTests")` and
`InternalsVisibleTo("DbHealthInspector.IntegrationTests")` so both test projects can exercise
this boundary directly; nothing is public.

```csharp
internal enum PostgreSqlConnectionTargetKind { NetworkHost, UnixDomainSocket, MultiHost }

internal sealed class PostgreSqlConnectionMetadata
{
    internal PostgreSqlConnectionTargetKind TargetKind { get; }
    internal int Port { get; }
    internal string SslMode { get; }
    internal bool Pooling { get; }
    internal int ConnectionTimeoutSeconds { get; }
}

internal sealed class PostgreSqlConnectionException : Exception
{
    internal PostgreSqlConnectionException(); // fixed message, no other constructor
}

internal static class PostgreSqlConnectionStringPolicy
{
    internal const string InvalidConnectionStringMessage;
    internal static NpgsqlConnectionStringBuilder ParseAndNormalize(string connectionString);
    internal static PostgreSqlConnectionMetadata DeriveMetadata(NpgsqlConnectionStringBuilder builder);
    // IsExpectedConnectionStringParsingException is private: it is scoped to, and used only by,
    // ParseAndNormalize's own catch clause (GC-DHI-04A-C1, F-01).
}

// The seam OpenConnectionAsync calls through, so its cancellation/sanitization logic can be
// exercised deterministically without a real PostgreSQL server (GC-DHI-04A-C1, F-02).
internal delegate ValueTask<NpgsqlConnection> PostgreSqlConnectionOpener(
    NpgsqlDataSource dataSource, CancellationToken cancellationToken);

internal static class NpgsqlDataSourceConnectionOpener
{
    // The only production implementation: delegates to NpgsqlDataSource.OpenConnectionAsync
    // and nothing else.
    internal static readonly PostgreSqlConnectionOpener Default;
}

internal sealed class PostgreSqlConnectionFactory : IAsyncDisposable
{
    internal PostgreSqlConnectionMetadata Metadata { get; }
    internal static PostgreSqlConnectionFactory Create(string connectionString);
    internal static PostgreSqlConnectionFactory Create(string connectionString, PostgreSqlConnectionOpener opener);
    internal ValueTask<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();

    internal static bool IsRequestedCancellation(OperationCanceledException exception, CancellationToken requestedToken);
    internal static PostgreSqlConnectionException SanitizeOrThrowIfCanceled(Exception exception, CancellationToken requestedToken);
    internal static PostgreSqlConnectionException SanitizeOpenFailure(Exception exception);
}
```

The three static helpers on `PostgreSqlConnectionFactory` are genuine production code, wired
into `OpenConnectionAsync`'s catch clauses and body — not test-only duplicates. They exist as
separately callable `internal static` methods for the same reason
`InspectionOrchestrator.IsRequestedCancellation` and `FindingFingerprintGenerator
.EncodeCanonicalField` do (see docs/design/inspection-orchestration.md §9.1 and
docs/design/core-domain-contracts.md): this gate explicitly defers testing against a real
PostgreSQL server to GC-DHI-04B, and there is no way to make a live `NpgsqlDataSource` throw a
specific, controlled exception without one. Extracting the decision logic into small pure
functions makes the cancellation-association rule and the sanitization step directly,
deterministically testable now, while `OpenConnectionAsync` itself is still the only place
that actually calls them during real use. (`IsRequestedCancellationException`, a thin
`Exception`-typed wrapper around `IsRequestedCancellation`, was removed in GC-DHI-04A-C1: once
`OpenConnectionAsync`'s catch clause is typed as `OperationCanceledException` directly, the
wrapper had no production caller left and would have become exactly the kind of test-only
duplicate this codebase avoids.)

The `Create(string, PostgreSqlConnectionOpener)` overload is not a test-only branch: it is the
same production `Create` method, generalized to accept an explicit opener instead of always
defaulting to `NpgsqlDataSourceConnectionOpener.Default`. `Create(string)` simply calls it with
the default. Nothing in `PostgreSqlConnectionFactory` branches on "is this a test" — the seam is
an ordinary constructor-injection point, and a fake opener is just another value of the same
delegate type a production caller could (in principle) supply.

## 6. Connection-string parsing

`PostgreSqlConnectionStringPolicy.ParseAndNormalize` is the single entry point:

1. `ArgumentException.ThrowIfNullOrWhiteSpace(connectionString, nameof(connectionString))` —
   `null` raises `ArgumentNullException`; empty or whitespace-only raises `ArgumentException`.
   Neither case silently trims the input into something else first.
2. `new NpgsqlConnectionStringBuilder(connectionString)` inside a `try`/`catch` filtered by the
   private `IsExpectedConnectionStringParsingException`, which is deliberately narrow —
   **GC-DHI-04A-C1, F-01**: it enumerates exactly the two exception types confirmed, by direct
   reproduction against Npgsql 10.0.3, to represent genuinely invalid connection-string input —
   `ArgumentException` (malformed value, wrong type, unrecognized keyword) and
   `KeyNotFoundException` (input with no recognizable key/value pairs at all). Anything else
   propagates unchanged: this filter is scoped to parsing only and is never reused to guard
   `Build()` or connection opening, each of which has its own separate handling (see below and
   [§11](#11-cancelación)). A match is translated into
   `ArgumentException(InvalidConnectionStringMessage, nameof(connectionString))`; the original
   exception, its message and the caller's input are never attached: no `InnerException`, no
   `Data` copy.
3. The mandatory security overrides ([§7](#7-normalización-obligatoria)) are applied to the
   parsed builder.
4. The normalized `NpgsqlConnectionStringBuilder` is returned. Nothing has opened a socket or
   touched the filesystem at this point — `NpgsqlConnectionStringBuilder` construction is pure
   string parsing.

`PostgreSqlConnectionFactory.Create` performs one further step after normalization: building the
`NpgsqlDataSource` via `NpgsqlDataSourceBuilder(builder.ConnectionString).Build()`. This call was
confirmed (by direct inspection against Npgsql 10.0.3, including with bogus certificate,
passfile and search-path values) to be entirely lazy — it does not resolve DNS, open a socket,
or validate that a referenced certificate/passfile path exists. **GC-DHI-04A-C1, F-01:** because
the configuration reaching `Build()` is already validated and normalized, this call is **not**
wrapped in a `try`/`catch` at all. A failure here — which real-world experience with the older,
shared filter never actually observed — would represent a broken invariant, defective internal
configuration, or Npgsql misuse, not invalid caller input, and propagates with full, untouched
fidelity rather than being folded into the "invalid connection string" `ArgumentException`.

## 7. Normalización obligatoria

Applied unconditionally to every successfully parsed connection string, regardless of what the
caller supplied:

| Setting | Forced value |
|---|---|
| `PersistSecurityInfo` | `false` |
| `IncludeErrorDetail` | `false` |
| `LogParameters` | `false` |
| `IncludeFailedBatchedCommand` | `false` |
| `NoResetOnClose` | `false` |
| `Enlist` | `false` |
| `Multiplexing` | `false` |
| `ApplicationName` | `"DbHealthInspector"` |

No caller-supplied `ApplicationName` is ever preserved, including when the caller supplied
none at all — the override is unconditional, not "fill in if absent." Every other setting the
caller specifies (host, port, database, username, password, SSL mode, pooling, timeout, and so
on) passes through untouched; this policy only ever narrows the eight settings above, never
anything else.

## 8. Rechazo de Options

A non-empty `Options` value is rejected outright: `!string.IsNullOrEmpty(builder.Options)`
throws `ArgumentException(InvalidConnectionStringMessage, nameof(connectionString))`. `Options`
carries arbitrary server-side session parameters (for example `-c some_setting=value`); a
future gate must own every session/transaction setting explicitly rather than accept whatever a
caller's `Options` string happens to contain, so no value is accepted at all — not even one
that looks harmless.

This is checked with `IsNullOrEmpty`, not `IsNullOrWhiteSpace`, deliberately: a value that
Npgsql's own parsing reduces to `null` or `""` (an absent or explicitly empty `Options=`) is
allowed, but a quoted whitespace-only value (`Options='   '`) is not `IsNullOrEmpty` and is
therefore rejected — confirmed directly against `NpgsqlConnectionStringBuilder` and covered by
`PostgreSqlConnectionStringPolicyOptionsTests.ParseAndNormalize_Rejects_WhenOptionsIsWhitespaceOnly`.
The rejected value itself is never included in the exception message, and no inner exception or
`Data` entry is attached — identical hygiene to every other configuration failure in
[§6](#6-connection-string-parsing).

## 9. Metadata allowlist

`PostgreSqlConnectionMetadata` exposes **exactly** five properties, verified by a reflection
test against the type's full property surface (`PostgreSqlConnectionMetadataTests
.PropertySurface_ExposesExactlyTheAllowlistedFiveProperties`):

| Property | Type | Source |
|---|---|---|
| `TargetKind` | `PostgreSqlConnectionTargetKind` | Derived from `builder.Host` (§ below), the host string itself is discarded |
| `Port` | `int` | `builder.Port` |
| `SslMode` | `string` | `builder.SslMode.ToString()` |
| `Pooling` | `bool` | `builder.Pooling` |
| `ConnectionTimeoutSeconds` | `int` | `builder.Timeout` |

Nothing else — no host, database name, username, password, passfile, application name, search
path, options, certificate paths, or the connection string itself. All properties are
get-only; the type is a plain `sealed class`, deliberately **not** a `record`, so its inherited
`object.ToString()` renders only the type name and never a structural dump of the five values —
verified directly (`ToString_DoesNotRenderAnyPropertyValue`).

`TargetKind` derivation from `builder.Host`:

```text
host starts with "/"    -> UnixDomainSocket
host contains ","       -> MultiHost
otherwise (incl. empty) -> NetworkHost
```

The host string is used only to classify it into one of these three values; it is never stored
anywhere, on `PostgreSqlConnectionMetadata` or otherwise.

## 10. Secret denylist

None of the following ever appear in `PostgreSqlConnectionMetadata`, its `ToString()`, the
fixed `ArgumentException` messages from [§6](#6-connection-string-parsing)–[§8](#8-rechazo-de-options),
or `PostgreSqlConnectionException`'s message: host, database name, username, password,
passfile path, caller-supplied application name, search path, SSL password, SSL certificate
path, SSL key path, root certificate path, or the `Options` value. `PostgreSqlConnectionSecretLeakageTests`
verifies this for every one of these fields individually, using synthetic markers, both through
the metadata/`ToString()` path and — for every field except `Host` — through an end-to-end
sanitized `PostgreSqlConnectionException` produced by a **fake** `PostgreSqlConnectionOpener`
(GC-DHI-04A-C1, F-02) configured to throw an `NpgsqlException` whose message embeds the marker;
no real socket is opened. `Host` is verified only through the metadata/`ToString()` path:
`PostgreSqlConnectionMetadata` never retains the raw host string regardless of what it is set
to, so the metadata-only check is sufficient for it.

## 11. Cancelación

`OpenConnectionAsync(cancellationToken)`:

1. Checks disposal first (`ObjectDisposedException`), then `cancellationToken
   .ThrowIfCancellationRequested()` before calling the opener at all. A token that is already
   canceled never reaches `_opener` — the opener is not invoked in this case.
2. Passes the identical `cancellationToken` through to `_opener(dataSource, cancellationToken)`
   (the `PostgreSqlConnectionOpener` seam described in [§5](#5-api-interna)).
3. **GC-DHI-04A-C1, F-01:** two separate, narrowly typed catch clauses, no `catch (Exception)`
   anywhere in this method:
   - `catch (OperationCanceledException exception) when (!IsRequestedCancellation(exception, cancellationToken))`
     — an OCE genuinely associated with the requested token is never caught by this clause at
     all and propagates with full, untouched fidelity; everything reaching the clause body is an
     OCE unrelated to the requested cancellation.
   - `catch (NpgsqlException exception)` — the one exception type the opener is expected to use
     to report an ordinary, recoverable connection failure.

   Every other exception type — `ObjectDisposedException`, `InvalidOperationException`,
   `ArgumentException`, `NullReferenceException`, `TimeoutException`, the three process-corruption types, or
   anything else the opener might throw — matches neither clause and propagates unchanged, since
   it represents a defect or lifecycle violation rather than an expected open failure. There is
   no `throw;` anywhere in this method, because there is no catch-all to re-throw from.
4. Both catch clauses call `SanitizeOrThrowIfCanceled(exception, cancellationToken)`, which
   checks `cancellationToken` one more time before sanitizing, so a cancellation that happened
   *during* the failed open attempt still takes priority over recording a sanitized connection
   failure — whether the underlying failure was an unrelated OCE or an ordinary
   `NpgsqlException`.

`IsRequestedCancellation(exception, requestedToken)` — reused, not reinvented, from the same
rule `InspectionOrchestrator.IsRequestedCancellation` already established
(docs/design/inspection-orchestration.md §9.1) — establishes association through either of two
independent conditions:

| Condition | Association? |
|---|---|
| `requestedToken.IsCancellationRequested` | Yes, regardless of the exception's own token |
| `requestedToken.CanBeCanceled && exception.CancellationToken.CanBeCanceled && exception.CancellationToken == requestedToken` | Yes |
| Exception carries `CancellationToken.None`, requested token uncanceled | No |
| Exception carries a different, unrelated cancelable token | No |
| Both tokens are `CancellationToken.None` | No — the `CanBeCanceled` guards prevent two structurally-equal-but-uncancelable default tokens from counting as association |

## 12. Sanitización de excepciones

`SanitizeOpenFailure(exception)` always returns a fresh `PostgreSqlConnectionException()` —
the parameterless constructor is the *only* one that type exposes, so there is no code path,
anywhere in the assembly, that can attach a caller message, an inner exception, or extra `Data`
to it. The original exception parameter exists solely to prove the seam is wired to a real
failure (see [§5](#5-api-interna)); nothing about it — message, type, stack trace, `Data` — is
read or copied. The resulting message is always exactly:

```text
The PostgreSQL connection could not be opened.
```

`SanitizeOrThrowIfCanceled` is the only caller of `SanitizeOpenFailure` in production code: it
calls `requestedToken.ThrowIfCancellationRequested()` first, so cancellation that raced with the
failure is never misreported as a sanitized connection failure ([§11](#11-cancelación)).

## 13. Disposal

`PostgreSqlConnectionFactory` implements `IAsyncDisposable` only — no synchronous `Dispose`,
no finalizer. `DisposeAsync` is idempotent via a single atomic operation:

```csharp
NpgsqlDataSource? dataSource = Interlocked.Exchange(ref _dataSource, null);
if (dataSource is not null)
{
    await dataSource.DisposeAsync().ConfigureAwait(false);
}
```

The first call observes the live data source, exchanges it for `null`, and disposes it. Every
later call observes `null` and is a pure no-op — it neither disposes anything again nor throws.
Concurrent calls are safe with respect to each other (the exchange is atomic, so at most one
call ever gets the non-null reference), but a `DisposeAsync` racing an in-flight
`OpenConnectionAsync` on the same instance is explicitly **not** a supported scenario — its
outcome (an `ObjectDisposedException`, a failed open, or a connection that opens against a
data source that is disposed moments later) is intentionally left unspecified, matching the
kind of ordinary single-owner lifetime assumption already used elsewhere in this codebase.
`Metadata` remains fully readable after disposal, since it is a separate, already-computed
object with no dependency on the data source's lifetime.

## 14. Testing

No Testcontainers, PostgreSQL server, Docker container, or **any network I/O of any kind** is
started or performed anywhere in this gate's tests (GC-DHI-04A-C1, F-02) — every open-path test
goes through `FakePostgreSqlConnectionOpener` (`tests/DbHealthInspector.UnitTests/Connections
/TestSupport/`) instead of `NpgsqlDataSourceConnectionOpener.Default`. A real-server open test
remains deferred to GC-DHI-04B ([§16](#16-elementos-diferidos)). Test files, all under
`tests/DbHealthInspector.UnitTests/Connections/`:

| File | Covers |
|---|---|
| `PostgreSqlConnectionStringPolicyValidationTests` | null/empty/whitespace rejection, malformed syntax (including a case Npgsql itself surfaces as `KeyNotFoundException`), fixed message, `ParamName`, no inner exception/`Data`, no original value in the message |
| `PostgreSqlConnectionStringPolicyOptionsTests` | Full `Options` matrix ([§8](#8-rechazo-de-options)): absent, unquoted/quoted-empty, quoted-whitespace (single- and double-quoted), a session parameter, keyword casing, and Npgsql's real last-wins semantics for a repeated key |
| `PostgreSqlConnectionStringPolicyNormalizationTests` | Each of the 8 mandatory overrides individually forced, all 8 forced together via literal keyword syntax, `ApplicationName` always overridden (including keyword-casing variants), a repeated dangerous key cannot survive normalization, unrelated settings left untouched — exercised through the real `ParseAndNormalize`, not a duplicate parser |
| `PostgreSqlConnectionMetadataTests` | Full `TargetKind` matrix (network host, IPv4, IPv6, one Unix-socket directory, two/three network hosts, whitespace-separated multi-host, multiple Unix-socket directories showing the `/`-before-`,` precedence), `Port`/`SslMode`/`Pooling`/`ConnectionTimeoutSeconds` correctness, exact 5-property surface via reflection, no setters, no `ConnectionString` property, `ToString()` leaks nothing, constructor validation |
| `PostgreSqlConnectionSecretLeakageTests` | Synthetic markers for every secret field absent from metadata/`ToString()`/exceptions ([§10](#10-secret-denylist)), open-failure half via the fake opener |
| `PostgreSqlConnectionFactoryCreateTests` | `Create` end-to-end: same validation surface as the policy, successful metadata derivation, lazy `Build()` (no I/O against an unreachable host) |
| `PostgreSqlConnectionFactoryOpenCancellationTests` | The full cancellation matrix via the fake opener (already-canceled token short-circuits before the opener is invoked at all; every `IsRequestedCancellation` combination; cancellation-dominates-sanitization for both an `NpgsqlException` and an unrelated OCE; the exact requested token reaches the opener; a success case returns exactly what the opener returned), plus the `IsRequestedCancellation`/`SanitizeOrThrowIfCanceled`/`SanitizeOpenFailure` helpers directly |
| `PostgreSqlConnectionFactoryNpgsqlExceptionSanitizationTests` | A synthetic `NpgsqlException` carrying the marker `synthetic-npgsql-secret-04a` in its message, `Data`, and inner exception, driven through the fake opener and the real `catch (NpgsqlException)` clause, confirmed absent from every property of the sanitized result |
| `PostgreSqlConnectionFactoryUnexpectedExceptionPropagationTests` | `InvalidOperationException`, `ObjectDisposedException`, `ArgumentException`, a genuine runtime `NullReferenceException` and `TimeoutException` each propagate with their original type, identity and message unchanged — never sanitized, never retried, opener invoked exactly once |
| `PostgreSqlConnectionFactoryDisposalTests` | First-call disposal, idempotency (sequential and concurrent), `Metadata` readable after disposal, `ObjectDisposedException` from `OpenConnectionAsync` after disposal, and — added in GC-DHI-04A-C1 — the opener is never invoked after disposal |

All assertions use xUnit v3 native `Assert`/`Assert.Throws<T>`/`Assert.ThrowsAsync<T>` with
exact exception types — no `ThrowsAnyAsync`, no mocking library, no reflection into production
internals in `FakePostgreSqlConnectionOpener`. Every test that controls cancellation itself
either uses `CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current
.CancellationToken)` or passes `TestContext.Current.CancellationToken` directly; no `xUnit1051`
suppression appears anywhere in this gate's tests.

`FakePostgreSqlConnectionOpener` is a plain, hand-written delegate target: it records the
`NpgsqlDataSource` and `CancellationToken` it was called with, counts invocations, and either
returns a caller-supplied `NpgsqlConnection` or throws a caller-supplied exception (optionally
running a callback immediately beforehand, used to simulate a caller cancelling its token while
the fake open attempt is "in flight"). It never touches a socket, a port, DNS, a thread, or a
sleep, and it never retains a secret marker in its own state beyond what the test explicitly
hands it to relay.

## 15. Limitaciones

- The cancellation-association and sanitization helpers ([§5](#5-api-interna)) are `internal`
  and called directly by tests, in addition to being reached through `OpenConnectionAsync` in
  normal use; this is the same accepted test-seam pattern used for `InspectionOrchestrator
  .IsRequestedCancellation` and `FindingFingerprintGenerator.EncodeCanonicalField`.
- The `PostgreSqlConnectionOpener` seam and its `Create(string, PostgreSqlConnectionOpener)`
  overload ([§5](#5-api-interna)) exist so `OpenConnectionAsync`'s behavior can be tested
  deterministically; only `Create(string)`, which always defaults to
  `NpgsqlDataSourceConnectionOpener.Default`, is expected to be used outside tests.
- Both the "invalid syntax" and the "rejected `Options`" failures reuse the same
  `InvalidConnectionStringMessage` constant. The prompt for this gate specified an exact string
  for the syntax case and only "a fixed, generic message" (without a distinct string) for the
  `Options` case; reusing one constant for both is a deliberate choice, not an oversight, since
  both represent the same class of problem from the caller's point of view: "this connection
  string cannot be used."
- `DisposeAsync` racing `OpenConnectionAsync` on the same instance is unsupported and its
  outcome unspecified ([§13](#13-disposal)).
- Authentication-failure specific messages (for example, a real PostgreSQL server's "password
  authentication failed for user X") are never exercised against a real server, since none is
  started in this gate; the sanitization logic itself is verified structurally
  (`SanitizeOpenFailure` never reads or copies anything from its input) and end-to-end against a
  synthetic `NpgsqlException` carrying a marker in its message, `Data`, and inner exception
  (`PostgreSqlConnectionFactoryNpgsqlExceptionSanitizationTests`), which is stronger evidence
  than a single real-server example would be.
- `Options` has no distinct keyword synonym in Npgsql 10.0.3 — confirmed directly against
  `NpgsqlConnectionStringPropertyAttribute.Synonyms`, which is empty for `Options` and for every
  setting `PostgreSqlConnectionStringPolicy` normalizes. The "aliases and casing" tests
  ([§14](#14-testing)) therefore cover case-insensitive keyword matching (Npgsql's real
  behavior), not a fabricated alternate keyword.
- A successfully opened `NpgsqlConnection` intrinsically exposes its own `ConnectionString`
  property. GC-DHI-04A forces `PersistSecurityInfo=false`, but cannot verify the post-open value
  without a real server; that behavior must be verified with the deferred real-server open test
  in GC-DHI-04B. The factory and metadata do not add another exposure path.

## 16. Elementos diferidos

Explicitly out of scope for GC-DHI-04A, per its prompt:

- Executing any SQL or starting any transaction. **No SQL or transaction is created in
  GC-DHI-04A.**
- Opening a connection against a real PostgreSQL server, and any Testcontainers/Docker-based
  integration test. **A real PostgreSQL open test is deferred to GC-DHI-04B.**
- Deciding whether — and how — hostname, database name or username are ever surfaced in
  higher-level reporting or logs. **Hostname, database and username reporting policies remain
  deferred.**
- Session/transaction parameter handling (the reason `Options` is rejected outright rather than
  partially validated).
- Retry, backoff, or pool-sizing policy beyond whatever `NpgsqlDataSourceBuilder` defaults to.
- Mapping PostgreSQL-specific errors (SQLSTATE, `Detail`, `Hint`) to any domain concept — this
  gate discards all of that unconditionally.
- Any public API surface: everything here becomes internal implementation reachable only by
  future PostgreSQL subgates within the same assembly.

## 17. Prohibiciones

- No type in this gate may be `public`.
- No SQL, `NpgsqlCommand`, or `BeginTransaction` call anywhere in production code.
- No environment-variable access and no logging dependency.
- No property exposing the raw connection string, the `NpgsqlDataSource`, or the
  `NpgsqlConnectionStringBuilder` beyond the boundary's own internal call chain.
- No `Options` value, however it is spelled or quoted, is ever accepted.
- No `xUnit1051` suppression in this gate's tests.
- No Testcontainers, PostgreSQL server process, Docker container, or **any real network I/O** —
  socket, port, or DNS — started or performed by any test in this gate (GC-DHI-04A-C1, F-02).
- No shared, generically-named catch-all exception filter reused across parsing, `Build()`, and
  connection opening (GC-DHI-04A-C1, F-01): each stage catches only the exception types it
  specifically expects.
