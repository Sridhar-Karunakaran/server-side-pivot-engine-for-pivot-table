# Design System Detection

An agent generating UI without the project's design system produces code that works
and looks wrong — hard-coded hex values, arbitrary spacing, a fourth button variant.
Every instance is cheap to write and expensive to unpick.

So the harness extracts the design system into `.agent/design.md` and binds it to
every UI change.

## The first rule

> **If a design system document already exists, do not touch it.**

Check, in order: `design.md`, `DESIGN.md`, `docs/design-system.md`,
`.agent/design.md`, `DESIGN-SYSTEM.md`, `docs/ui/*.md`, a Storybook `Introduction`
doc, `CONTRIBUTING.md` with a design section.

If one exists: **reference it, do not regenerate it.** Write a pointer in
`project-context.md` and record its path in the lock. A hand-written design document
encodes decisions and taste the generator cannot reconstruct, and overwriting it is
destructive even if the replacement is more complete.

If it exists but is thin or stale, say so and offer to *extend* it — never replace:

```
FOUND design.md (34 lines, last touched 14 months ago)
  Covers: colour, typography
  Missing: spacing scale, component variants, dark mode, motion
  -> will APPEND the missing sections, marked as generated. Your content is untouched.
```

## Extraction, not invention

Where no document exists, build one **from what the code actually does.** Every value
comes from a real file, cited. Do not invent a scale, and do not import a generic one.

### Signals, by stack

| Signal | Extract |
|---|---|
| `tailwind.config.*` | `theme.extend` — colours, spacing, fonts, breakpoints, radii |
| CSS custom properties in `:root` | the full token set, both themes |
| `tokens.css` / `tokens.json` / `*.tokens.*` | design tokens verbatim |
| Sass/Less variables (`_variables.scss`) | the variable set |
| styled-components / emotion theme object | the theme |
| `theme.ts` / `theme.json` / MUI `createTheme` | palette, typography, spacing |
| Angular Material theme (`$theme`, `mat.define-*`) | palette and typography config |
| Blazor / Syncfusion theme import | which theme, and any overrides |
| MAUI / WPF `Styles.xaml`, `Colors.xaml` | resource keys and values |
| WinForms base form / shared `Font`/`Color` statics | the de facto tokens |
| `.storybook/` | documented components and their variants |
| Figma link in README or a `figma.config` | record the URL as the source of truth |
| Component library in `package.json` | the library, its version, its theme |

### When there are no tokens

Most brownfield UI has no token file. Extract the **de facto** system by frequency:

```bash
# most-used colours in the codebase
grep -rhoE '#[0-9a-fA-F]{3,8}' src --include='*.css' --include='*.scss' \
  --include='*.tsx' --include='*.razor' | sort | uniq -c | sort -rn | head -20
```

Then say honestly what that is:

```
NO TOKEN FILE FOUND. Extracted the de facto system by frequency:
  #0B2340   142 uses   -> likely primary
  #00B7C3    88 uses   -> likely accent
  #F5F5F5    61 uses   -> likely surface
  ...plus 47 one-off colours, listed under Drift.

This is an OBSERVATION, not a specification. Confirm or correct the roles before
the harness enforces anything against it.
```

The one-off list is the useful part. It is the drift, and it is the backlog.

## What `design.md` contains

Generated from real values, with the source file cited for each section. Sections with
nothing to extract are **omitted**, not stubbed — an empty "Motion" heading tells an
agent nothing and pads the file.

- **Source of truth** — where tokens actually live, and what edits them
- **Colour** — every token, its role, and both theme values
- **Typography** — families, scale, weights, line heights
- **Spacing** — the scale, and whether it is enforced
- **Layout** — breakpoints, container widths, grid
- **Radii, borders, elevation**
- **Motion** — durations, easings, and the reduced-motion rule
- **Components** — the inventory, with variants, and where each lives
- **Iconography** — the set, and how icons are sized
- **Accessibility commitments** — contrast target, focus treatment, touch targets
- **Rules for generated UI** — the binding part, below
- **Drift** — hard-coded values found outside the system, as a backlog

## The rules section is the binding part

Everything above is description. This is what an agent must obey:

```markdown
## Rules for generated UI

1. Use a token. Never a literal colour, spacing, radius, or font size.
   Source: styles/tokens.css
2. No new component variant without an ADR. Three button variants exist; a fourth
   is a design decision, not an implementation detail.
3. Every interactive element has a visible focus state from --focus-ring.
4. Contrast meets WCAG AA (4.5:1 body, 3:1 large). Verified by the a11y gate.
5. Both themes. A colour defined only in one theme is a bug — see tokens.css.
6. Respect prefers-reduced-motion for every animation.
7. Spacing comes from the 4px scale. No arbitrary margins.
8. Reuse before creating. Check components/ first; a near-match plus a prop beats
   a new component.
```

Each rule names its source or its gate. A rule with neither is a suggestion.

## Binding it into the harness

Description alone changes nothing. Three bindings:

**1. A policy** — `.agent/policies/design-system.md`, bound by any workflow that can
touch UI. Generated only when a design system was found or extracted.

**2. The reviewer** — `code-reviewer` receives `design.md` for UI diffs and checks:
literal values where a token exists, new variants without an ADR, missing focus
states, single-theme colours, off-scale spacing.

**3. Gates, where they exist** — these are the mechanical subset:

| Rule | Gate |
|---|---|
| no literal colours | a stylelint rule, or a grep gate over the diff |
| contrast | `a11y` (axe-core) |
| focus states | `a11y` |
| bundle impact of a new dependency | `bundle-budget` |
| visual regression | Playwright screenshots |

Rules with no gate stay reviewer-checked and are marked `manual` in the policy, so
nobody mistakes them for enforced.

## Never ask the user for their design system

Extract it. A team with Tailwind and a token file has a design system whether or not
they call it one, and asking makes the harness look like it did not read the repo.

The only design question worth asking arrives after extraction, and only when roles
were inferred by frequency:

> "I extracted 3 primary colours and 47 one-off values. Is `#0B2340` your primary, or
> is that just the most common? [enter to accept]"
