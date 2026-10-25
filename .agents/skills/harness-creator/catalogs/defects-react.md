# React Defect Catalog

For the reviewer agent. `Rule` = the ESLint rule that catches it. Note how many
are covered by `eslint-plugin-react-hooks` — **the reviewer's first check is
whether that plugin is enabled and set to `error`.**

## Hooks correctness

| # | Defect | Why missed | Rule |
|---|---|---|---|
| R1 | Missing dependency in `useEffect`/`useMemo`/`useCallback` → stale closure | Works on first render; breaks on update | `react-hooks/exhaustive-deps` |
| R2 | Dependency array suppressed with an eslint-disable comment | Deliberately hidden | `manual` (grep the diff for disables) |
| R3 | Effect with no cleanup for a subscription/timer/listener | Leak; no test fails | `manual` |
| R4 | `useEffect` that sets state it also depends on → render loop | Often "fixed" by removing the dep, which reintroduces R1 | `react-hooks/exhaustive-deps` |
| R5 | Hook called conditionally or in a loop | | `react-hooks/rules-of-hooks` |
| R6 | Async function passed directly to `useEffect` | Returns a promise, not a cleanup | `manual` |
| R7 | State updated after unmount (no abort/mounted guard) | Warning in dev, leak in prod | `manual` |
| R8 | `useState` initialised from a prop and never resynced | Silently stale | `manual` |
| R9 | Non-idempotent effect broken by StrictMode double-invoke (v18) | Only in dev, so it gets suppressed | `manual` |

## Rendering and performance

| # | Defect | Why missed | Rule |
|---|---|---|---|
| R10 | Array index as `key` in a reorderable list | State attaches to the wrong row | `react/no-array-index-key` |
| R11 | Object/array/function literal created inline as a prop, breaking `memo` | | `manual` |
| R12 | Context value recreated every render → all consumers re-render | Invisible until the app is large | `manual` |
| R13 | Single context carrying unrelated state (re-render storm) | | `manual` |
| R14 | Expensive computation not memoised in a hot path | | `manual` + Lighthouse gate |
| R15 | Uncontrolled → controlled input switch (`value={undefined}` then a value) | Console warning only | `manual` |
| R16 | Missing `React.memo` on a pure leaf rendered in a large list | | `manual` |
| R17 | Large list with no virtualisation | | `manual` + perf gate |

## Data, state, and security

| # | Defect | Why missed | Rule |
|---|---|---|---|
| R18 | `fetch` in an effect without `AbortController` → out-of-order responses | Intermittent wrong data | `manual` |
| R19 | Direct state mutation instead of a new reference | `OnPush`-style bailouts skip the update | `manual` |
| R20 | Derived state stored in `useState` instead of computed | Two sources of truth | `manual` |
| R21 | `dangerouslySetInnerHTML` from user input | XSS | `react/no-danger` + Semgrep |
| R22 | Secret/API key in client code or `NEXT_PUBLIC_*` | Shipped to the browser | `secrets` gate |
| R23 | `target="_blank"` without `rel="noopener noreferrer"` | Tabnabbing | `react/jsx-no-target-blank` |
| R24 | Auth check in the component only, not on the API route | | `manual` + risk gate |

## Accessibility

| # | Defect | Rule |
|---|---|---|
| R25 | Click handler on a `div`/`span` with no keyboard handler | `jsx-a11y/click-events-have-key-events` |
| R26 | Image with no alt | `jsx-a11y/alt-text` |
| R27 | Form control with no label | `jsx-a11y/label-has-associated-control` |
| R28 | Positive `tabIndex` | `jsx-a11y/tabindex-no-positive` |
| R29 | Custom widget missing ARIA role/state | `axe` gate |

## Syncfusion React (EJ2) specifics

| # | Defect | Rule |
|---|---|---|
| R30 | `registerLicense()` not called before the first render | `syncfusion-license` gate |
| R31 | License key committed in source | `secrets` gate |
| R32 | Barrel import of `@syncfusion/ej2-react-*` instead of the needed module | `bundle-budget` gate |
| R33 | EJ2 component instance held in a ref and not destroyed on unmount | `manual` + `memlab` |
| R34 | `dataSource` as a plain array where `DataManager` server paging is required | `manual` |
| R35 | New `dataSource` array identity every render → full grid rebuild | `manual` |
| R36 | Mismatched `@syncfusion/*` versions across the workspace | `version-consistency` gate |

## Reviewer instructions

Same report format as the other catalogs. React-specific obligations:

1. **Check for `eslint-disable` in the diff first.** R2 is the highest-value
   finding in AI-generated React: the model hits `exhaustive-deps`, cannot resolve
   it, and disables the rule. Every disable in the diff needs a written
   justification or it is a finding.
2. **Leak findings (R3, R7, R33) must cite the `memlab` gate** or state that no
   leak gate exists and the fix is therefore unverified.
