# Angular Defect Catalog

For the reviewer agent. `Rule` names the ESLint/analyzer rule that catches the
defect; `manual` means no tool covers it and the reviewer must say so.

Many of these have rules that teams simply have not enabled. **The reviewer's
first action is to check whether the rule is enabled, and if not, report the
missing configuration as the finding** — a configured rule prevents the whole
class forever, while a review comment fixes one instance.

## Subscriptions and memory (highest yield)

| # | Defect | Why missed | Rule |
|---|---|---|---|
| A1 | `.subscribe()` with no unsubscribe | Leak invisible until navigation is repeated | `rxjs-angular/prefer-takeuntil` |
| A2 | Manual `ngOnDestroy` teardown instead of `takeUntilDestroyed()` (v16+) | Works, but drifts out of sync as subscriptions are added | `manual` |
| A3 | `takeUntil` placed before other operators, so later operators keep the sub alive | Looks correct; still leaks | `rxjs/no-unsafe-takeuntil` |
| A4 | Nested `subscribe()` instead of `switchMap`/`concatMap` | Race conditions and leaked inner subs | `rxjs/no-nested-subscribe` |
| A5 | `setInterval`/`setTimeout` not cleared in `ngOnDestroy` | | `manual` |
| A6 | Manual DOM listener via `addEventListener` not removed | | `manual` |
| A7 | Missing `switchMap` cancellation → stale response overwrites fresh (out-of-order responses) | Intermittent, hard to reproduce | `manual` |

## Change detection and performance

| # | Defect | Why missed | Rule |
|---|---|---|---|
| A8 | Component without `ChangeDetectionStrategy.OnPush` | Everything works, just slowly; no test fails | `@angular-eslint/prefer-on-push-component-change-detection` |
| A9 | Function or getter called in a template (`{{ computeTotal() }}`) | Runs every CD cycle | `manual` |
| A10 | Missing `trackBy` on `*ngFor` (or `track` in v17 `@for`) | Full DOM teardown per change | `@angular-eslint/template/use-track-by-function` |
| A11 | Impure pipe where a pure one would do | | `manual` |
| A12 | `ExpressionChangedAfterItHasBeenCheckedError` — state mutated in `ngAfterViewInit` | Dev-mode-only error, often suppressed rather than fixed | `manual` |
| A13 | New object/array literal in a template binding → fails `OnPush` identity checks | | `manual` |
| A14 | Large list rendered without virtual scroll | | `manual` + bundle/perf gate |

## Dependency injection and architecture

| # | Defect | Why missed | Rule |
|---|---|---|---|
| A15 | Service `providedIn: 'root'` but holding per-component state | Cross-component bleed | `manual` |
| A16 | Provider declared at component level unintentionally → new instance per component | | `manual` |
| A17 | Feature module imported into `AppModule`, defeating lazy loading | Bundle size only | `bundle-budget` gate |
| A18 | Circular dependency between services | | `dependency-cruiser` gate |
| A19 | Cross-feature import bypassing the public barrel | Erodes boundaries silently | `dependency-cruiser` gate |

## Forms, templates, types

| # | Defect | Why missed | Rule |
|---|---|---|---|
| A20 | Untyped `FormGroup` (not `FormGroup<T>`) | Loses all type safety on form values | `manual` |
| A21 | `[(ngModel)]` mixed with reactive forms on one control | Two sources of truth | `@angular-eslint/template/no-negated-async` family |
| A22 | `any` used to silence a template type error | | `@typescript-eslint/no-explicit-any` |
| A23 | Non-null assertion `!` hiding a genuinely nullable value | | `@typescript-eslint/no-non-null-assertion` |
| A24 | `async` pipe subscribed multiple times in a template without `shareReplay` | Duplicate HTTP calls | `manual` |
| A25 | `innerHTML` bound from user input | XSS — Angular sanitizes, `bypassSecurityTrustHtml` does not | `@angular-eslint/template/no-inline-styles` adjacent; Semgrep custom |
| A26 | `bypassSecurityTrust*` on any user-influenced value | Deliberate sanitizer bypass | Semgrep custom rule |

## Accessibility

| # | Defect | Rule |
|---|---|---|
| A27 | Click handler on a non-interactive element with no keyboard equivalent | `@angular-eslint/template/click-events-have-key-events` |
| A28 | Image without alt | `@angular-eslint/template/alt-text` |
| A29 | Form control with no associated label | `@angular-eslint/template/label-has-associated-control` |
| A30 | Custom control without ARIA role/state | `axe` gate |

## Syncfusion Angular (EJ2) specifics

| # | Defect | Why missed | Rule |
|---|---|---|---|
| A31 | `registerLicense()` not called before the first component bootstraps | Trial banner in production | `syncfusion-license` gate |
| A32 | License key hard-coded in a committed source file | | `secrets` gate |
| A33 | Importing the whole `@syncfusion/ej2` barrel instead of the specific module | Bundle bloat | `bundle-budget` gate |
| A34 | Grid `dataSource` set to a plain array where `DataManager` with server paging is needed | Memory + payload blowup | `manual` |
| A35 | Component instance `@ViewChild` retained after destroy → detached DOM | Leak | `manual` + `memlab` |
| A36 | Missing `destroy()` on a programmatically created EJ2 component | Leak; framework teardown does not cover manual instantiation | `manual` + `memlab` |
| A37 | Mismatched `@syncfusion/*` package versions across the workspace | Runtime errors from incompatible internals | `version-consistency` gate |
| A38 | CSS imported per-component rather than once, duplicating theme payload | | `bundle-budget` gate |

## Reviewer instructions

Report format is identical to the Blazor catalog. Two Angular-specific
obligations:

1. **When a rule exists but is not enabled, the finding is the missing rule.**
   Report it as a configuration change with the exact config snippet, not as a
   per-instance code comment. One config change beats fifty review comments.
2. **Leak findings (A1–A7) must reference the `memlab` gate.** If the project has
   no leak gate, say so explicitly: *"no dynamic leak gate is configured, so this
   fix is unverified — recommend adding one."* Never state that a leak is fixed
   on the strength of reading the diff.
