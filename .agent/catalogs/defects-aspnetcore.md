# ASP.NET Core / Backend Defect Catalog

For the reviewer agent.

## Dependency injection

| # | Defect | Why missed | Rule |
|---|---|---|---|
| N1 | Singleton depending on a scoped service (captive dependency) | Works in dev, corrupts state under concurrency | `manual` + `ValidateScopes`/`ValidateOnBuild` in dev host |
| N2 | `DbContext` registered as singleton | Not thread-safe; connection leak | `manual` |
| N3 | Service resolved from the root provider inside a request | Scoped instance never disposed | `manual` |
| N4 | `IServiceProvider` injected and used as a service locator | Hides real dependencies | `manual` |
| N5 | `HttpClient` newed up per request instead of `IHttpClientFactory` | Socket exhaustion | CA2000 partial, `manual` |
| N6 | `IHttpContextAccessor` captured in a singleton | Null or wrong context | `manual` |

**Highest-value fix:** enable `ValidateScopes` and `ValidateOnBuild` in the host
builder. N1–N3 then fail at startup rather than in production, which converts a
review concern into a gate. The reviewer should check this first.

## Async and concurrency

| # | Defect | Rule |
|---|---|---|
| N7 | `async void` outside an event handler | VSTHRD100 |
| N8 | `.Result` / `.Wait()` / `GetAwaiter().GetResult()` on a request path | VSTHRD002, CA1849 |
| N9 | Missing `ConfigureAwait(false)` in a library | CA2007 (library folders only) |
| N10 | `CancellationToken` not accepted or not forwarded | CA2016 |
| N11 | `Task.Run` used to "make it async" on a request thread | Thread-pool starvation | `manual` |
| N12 | Fire-and-forget task with no exception observation | VSTHRD110 |
| N13 | Shared mutable static without synchronisation | `manual` |

## Entity Framework Core

| # | Defect | Why missed | Rule |
|---|---|---|---|
| N14 | N+1 query — lazy loading inside a loop | Fast on 10 rows, dies on 10,000 | **EF `ConfigureWarnings(...Throw(...))` in test fixtures** |
| N15 | `.ToList()` before filtering — whole table into memory | | `manual` |
| N16 | Cartesian explosion from multiple collection `Include`s | | `MultipleCollectionIncludeWarning` → throw |
| N17 | No `AsNoTracking()` on a read-only query | Memory + change-tracker cost | `manual` |
| N18 | Client-side evaluation of a predicate that should be SQL | | `manual` |
| N19 | Missing index for a hot query path | | `manual` + query plan review |
| N20 | Migration generated but not reviewed (destructive column drop) | **Data loss** | risk gate — migrations require human approval |
| N21 | `SaveChanges` in a loop instead of one batch | | `manual` |

N14 and N16 are worth converting from review items into gates: throwing on those
EF warnings inside the integration-test fixture makes an N+1 a failing test.

## Security and API surface

| # | Defect | Rule |
|---|---|---|
| N22 | Missing `[Authorize]` on a controller/endpoint | `manual` + risk gate |
| N23 | `[AllowAnonymous]` added without justification | risk gate |
| N24 | SQL built by string concatenation / interpolation | **CA2100, CA3001** |
| N25 | Mass assignment — binding directly to an entity | Semgrep custom rule |
| N26 | Missing anti-forgery on a state-changing form post | `manual` |
| N27 | Secrets in `appsettings.json` rather than a secret store | `secrets` gate |
| N28 | Over-permissive CORS (`AllowAnyOrigin` + credentials) | Semgrep custom rule |
| N29 | Unvalidated redirect from user input | CA3003 |
| N30 | Exception detail returned to the client in production | `manual` |
| N31 | No rate limiting on an expensive or auth endpoint | `manual` |
| N32 | IDOR — resource fetched by id with no ownership check | `manual` + risk gate |
| N33 | Weak or deprecated crypto | CA5350, CA5351 |
| N34 | Cert validation disabled | CA5359 |

## Reliability and observability

| # | Defect | Rule |
|---|---|---|
| N35 | No timeout on an outbound HTTP call | `manual` |
| N36 | No retry/circuit breaker on a critical dependency | `manual` |
| N37 | Exception swallowed with an empty catch | `manual`, Sonar S2486 |
| N38 | Logging a secret, token, or PII | Semgrep custom rule |
| N39 | Log level wrong (error for expected conditions) | `manual` |
| N40 | Health check absent or not covering dependencies | `manual` |

## Reviewer instructions

1. **Check `ValidateScopes`/`ValidateOnBuild` and the EF warning configuration
   before reviewing instances.** Both convert whole defect classes into startup
   or test failures. Reporting the missing configuration outranks reporting any
   individual instance.
2. **N20 and N32 are risk-gated.** Migrations and authorization changes must be
   flagged for human approval regardless of gate status — `task.py done` enforces
   this via the `requiresHumanApproval` globs, and the reviewer must confirm the
   globs cover the changed paths.
3. **Never assert absence of an injection or authorization flaw** on the basis of
   reading the diff. Route to Semgrep and to the risk gate, and say which.
