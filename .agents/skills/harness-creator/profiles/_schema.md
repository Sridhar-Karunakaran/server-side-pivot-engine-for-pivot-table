# Profile Schema

A profile is **data, not logic**. It is the reason one universal skill can cover
React, Angular, MAUI, Blazor, WinForms, ASP.NET Core and more without six
diverging skills: the loop, the gate machinery, the tier system and the ratchet
are identical everywhere, and only these tables change.

Profiles are written in the same restricted YAML subset that `harness.yaml` uses,
so `hyaml.py` can read them and the generator can validate them.

```yaml
id: dotnet-blazor              # stable identifier, recorded in harness.lock
name: Blazor                   # human label
extends: dotnet-common         # inherit gates/knowledge; child entries win by gate id
catalog: catalogs/defects-blazor.md   # defect catalog for the reviewer agent

detect:                        # how the scanner recognises this stack
  requires:                    # ALL must be present or the profile does not apply
    - glob: "**/*.csproj"
  signals:                     # weighted; highest total score wins
    - kind: packageRef         # packageRef | glob | fileContains | manifestKey
      value: Microsoft.AspNetCore.Components.Web
      weight: 10
  excludes:                    # if matched, this profile is ruled out
    - packageRef: Microsoft.Maui.Controls

runtime:                       # facts the generator needs to write commands
  testCommand: dotnet test
  buildCommand: dotnet build

knowledge:                     # provenance-tracked sources; NEVER model memory
  - id: blazor-lifecycle
    url: https://learn.microsoft.com/aspnet/core/blazor/components/lifecycle
    topics: [lifecycle, disposal]
    required: true

mcp:                           # optional MCP servers that serve live docs
  - id: microsoft-docs
    why: "authoritative Blazor/.NET API lookups at build time"
    optional: true

skills:                        # skill slots, resolved by the chain in
  - id: blazor-development     # references/knowledge-acquisition.md
    optional: true

gates:                         # each tagged with the minimum tier that needs it
  - id: build
    tier: L0
    appliesTo: [documents]     # OPTIONAL: only emit when these products are detected
    ...                        # full gate shape: see references/harness-contract.md
```

## Gate filtering at generation time

A gate reaches `harness.yaml` only if it clears all three filters. Skipping any
of them produces a harness that fails on arrival, which teaches the team to
ignore gate output.

| Filter | Rule |
|---|---|
| **tier** | `TIERS.index(gate.tier) <= TIERS.index(chosen_tier)` |
| **tool** | the gate's binary resolves on PATH. Otherwise it goes into PROPOSE as an `installHint`, and is **not** emitted |
| **appliesTo** | if present, at least one named product from the overlay's `products:` must have been detected |

`appliesTo` exists because overlay gates are often product-specific. The
Syncfusion overlay's `document-resource-disposal` and `document-memory` gates
apply to `Syncfusion.DocIO` / `XlsIO` / `Pdf` / `Presentation` consumers; emitting
them for a Blazor grid project adds a redundant build invocation and dilutes the
gate list.

## Signal kinds

| Kind | Meaning |
|---|---|
| `packageRef` | dependency **name** appears in any manifest (`PackageReference`, `dependencies`, etc.) |
| `glob` | a file matching the glob exists |
| `fileContains` | `value` is `path::regex`; the regex matches inside that file |
| `manifestKey` | a dotted key exists in a JSON manifest (e.g. `scripts.build`) |

## Resolution

1. Score every profile whose `requires` are all satisfied and no `excludes` match.
2. Highest score wins. A tie, or a top score under the `minScore` floor, is a
   question for the user — never a silent guess.
3. Monorepos legitimately resolve to several profiles. The generator emits one
   gate set per detected sub-project, scoped with `cwd`, and says so in PROPOSE.
4. `extends` merges parent gates first; a child gate with the same `id` replaces
   the parent's entirely (no deep merge — partial gate overrides are how
   configuration becomes unreadable).

## Adding a profile

1. Copy the closest existing profile.
2. Fill `detect` and verify against a real repo: the scan must pick it correctly.
3. Write the gates, every one with `claim`, `tool`, `license`, `parser`,
   `threshold`, `installHint`, `docs`, `rationale`.
4. Check every tool against the licence policy in `references/tools-of-record.md`.
5. Write the defect catalog. Pair each entry with an analyzer rule id where one
   exists — an entry with no rule and no gate must be marked `manual` so the
   reviewer is obliged to declare it unverified.
6. List knowledge sources with real URLs. No uncited rules.
