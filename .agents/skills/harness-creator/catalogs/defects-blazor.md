# Blazor Defect Catalog

For the reviewer agent. Every entry states the defect, why it survives ordinary
review, and — critically — whether a **tool** can catch it. Where a tool exists,
the reviewer's job is to verify the tool is configured, not to eyeball the code.

Columns: **Rule** = analyzer id or gate that catches it. `manual` = no tool
exists, so the reviewer must inspect (and say so explicitly in findings).

## Lifecycle and disposal (highest yield — this is where Blazor leaks)

| # | Defect | Why missed | Rule |
|---|---|---|---|
| B1 | `DotNetObjectReference` created for JS interop, never disposed | Leak is invisible until a page is visited repeatedly; no compile error | `manual` + `leak-web` gate |
| B2 | Component subscribes to an event/`NavigationManager.LocationChanged` without unsubscribing in `Dispose` | Works fine in tests, leaks in a long session | CA2213 (fields), `manual` for event handlers |
| B3 | `IDisposable`/`IAsyncDisposable` not implemented although the component owns a timer, subscription, or `CancellationTokenSource` | Nothing forces the interface | CA1001 |
| B4 | `IAsyncDisposable` implemented but `DisposeAsync` not awaited internally | Silent | CA2213, VSTHRD110 |
| B5 | `OnInitializedAsync` starts work that outlives the component; no `CancellationToken` | Renders after disposal → `ObjectDisposedException` under load | CA2016 |
| B6 | `PeriodicTimer`/`Timer` field not disposed | | CA2213 |
| B7 | Injected scoped service captured in a singleton component context | Captive dependency; stale data across users | `manual` (DI analyzer partial) |

## Rendering correctness

| # | Defect | Why missed | Rule |
|---|---|---|---|
| B8 | `StateHasChanged` called from a non-UI thread without `InvokeAsync` | Intermittent; passes in dev | `manual` + VSTHRD001 |
| B9 | Missing `@key` on a dynamic `@foreach` list | Wrong element reused after reorder; state bleeds between rows | `manual` |
| B10 | `@key` bound to index rather than identity | Same as B9, but looks correct | `manual` |
| B11 | Method call in a template expression (`@GetTotal()`) re-evaluated every render | Perf cliff, not a bug — no test fails | `manual` |
| B12 | `ShouldRender` overridden with a condition that never returns true after a state change | UI silently stops updating | `manual` |
| B13 | Mutating a collection in place instead of reassigning; diff sees no change | | `manual` |
| B14 | `EventCallback<T>` declared as `Action<T>` — bypasses the render trigger | Compiles, renders once, then looks broken | `manual` |

## Render modes and prerendering (.NET 8+)

| # | Defect | Why missed | Rule |
|---|---|---|---|
| B15 | `OnInitializedAsync` runs twice (prerender + interactive) causing duplicate writes/HTTP calls | Only visible as double records in prod | `manual` |
| B16 | `@rendermode InteractiveServer` on a component that touches `HttpContext` | Null at interactive time | `manual` |
| B17 | Interactive component in a static SSR parent expecting interactivity | Events silently do nothing | `manual` |
| B18 | State assumed to persist across prerender boundary without `PersistentComponentState` | Flicker/reset | `manual` |
| B19 | WASM component pulling in a server-only package | Runtime load failure | `arch` gate |

## Forms, JS interop, security

| # | Defect | Why missed | Rule |
|---|---|---|---|
| B20 | `EditForm` without `DataAnnotationsValidator` | No client validation; server may accept bad input | `manual` |
| B21 | Validation only client-side; no server-side revalidation | Trivially bypassed | Semgrep custom rule |
| B22 | `JSRuntime.InvokeAsync` on a disposed component | `JSDisconnectedException` | `manual` |
| B23 | `MarkupString` built from user input | XSS | Semgrep custom rule |
| B24 | Secrets or connection strings in a WASM project (shipped to the browser) | Catastrophic and easy to do | `secrets` gate + `arch` gate |
| B25 | `[Authorize]` on the page but the underlying API endpoint unprotected | Page-level auth mistaken for API auth | `manual` + risk gate |

## Syncfusion Blazor specifics

| # | Defect | Why missed | Rule |
|---|---|---|---|
| B26 | `SyncfusionLicenseProvider.RegisterLicense` not called before the first component renders | Trial banner in production | `syncfusion-license` gate |
| B27 | License key committed in source instead of configuration | Leaks the key | `secrets` gate (custom Gitleaks rule) |
| B28 | `SfGrid` with `EnableVirtualization` and an unbounded `DataSource` | Memory blowup at scale | `manual` + `leak-web` |
| B29 | `SfDataManager` querying without server-side paging on a large set | Whole table over the wire | `manual` |
| B30 | Component `@ref` held in a field, never released → retains the render tree | Leak | CA2213 |
| B31 | Registering `AddSyncfusionBlazor()` more than once, or missing entirely | Styling/interop failures | `manual` |
| B32 | Mismatched Syncfusion package versions across projects in the solution | Subtle runtime breakage | `version-consistency` gate |

## Reviewer instructions

For each defect the reviewer reports, emit:

```
[B9] Missing @key in dynamic list
  File:   Components/OrderList.razor:34
  Rule:   manual (no analyzer covers this)
  Why:    Rows are reordered by the sort control. Without @key, Blazor reuses
          elements positionally, so per-row component state (expanded panel,
          edit buffer) attaches to the wrong order after a sort.
  Fix:    @foreach (var o in Orders) { <OrderRow @key="o.Id" Order="o" /> }
  Verify: no gate covers this - add a bUnit test asserting row identity
          survives a re-sort.
```

The `Verify:` line is mandatory. If no gate covers the finding, the reviewer must
say so rather than implying the fix is verified. A finding whose fix cannot be
verified is a candidate for a new gate — report it as such.
