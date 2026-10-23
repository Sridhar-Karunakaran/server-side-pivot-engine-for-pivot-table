# Knowledge Acquisition

An agent generating Angular code from model memory will produce Angular that was
current at its training cutoff, mixed with React and Vue idioms, expressed with
total confidence. That is the same failure class as an asserted memory leak, and
it needs the same answer: **get the knowledge from a source, and record where it
came from.**

## Rank the mechanisms — do not treat them as equivalent

| Rank | Mechanism | Why it ranks here |
|---|---|---|
| **1** | **Machine-enforced** — linter rules, analyzers, `.editorconfig`, compiler flags | A rule the build enforces cannot be forgotten, skipped, or argued with. It applies to every future change with no agent cooperation. |
| **2** | **Runtime lookup** — MCP doc servers, framework CLI MCP, internal docs MCP | Cannot go stale silently. Costs a call per lookup. |
| **3** | **Written rules file** — `.agent/knowledge/rules-<stack>.md` | Cheapest to produce and the easiest to get wrong. Last resort, and every rule needs a citation. |

**Always convert down the ranks.** If a rule can be an ESLint rule, it must be an
ESLint rule and not a bullet in a markdown file. The generator is explicitly
instructed to attempt rank 1 first, and the reviewer agent's highest-value finding
is "this rule exists but is not enabled".

Example of the conversion:

| Rule as prose (rank 3) | Same rule machine-enforced (rank 1) |
|---|---|
| "Always use OnPush change detection" | `@angular-eslint/prefer-on-push-component-change-detection: error` |
| "Unsubscribe in ngOnDestroy" | `rxjs-angular/prefer-takeuntil: error` |
| "Dispose IDisposable fields" | `dotnet_diagnostic.CA2213.severity = error` |
| "Don't use `any`" | `@typescript-eslint/no-explicit-any: error` |
| "Include all effect dependencies" | `react-hooks/exhaustive-deps: error` |

## Provenance rules (non-negotiable)

Every knowledge file the harness generates must satisfy all of these, and
`harness.py doctor` checks them:

1. **Every rule carries a citation.** Format:
   ```markdown
   - Components with `@Input` bindings should use `OnPush`.
     Source: https://angular.dev/best-practices/runtime-performance (retrieved 2026-09-01)
   ```
2. **Only first-party or explicitly-approved sources.** Official framework docs,
   the framework's own repository, the analyzer's rule documentation, or an
   internal docs system named in the overlay. Not blog posts, not Stack Overflow,
   not the model's recollection.
3. **Recorded in the lock.** `harness.py lock` stores each knowledge file's
   SHA-256 and its extracted `Source:` URLs. `doctor` reports files that changed
   since the lock, and files with **zero** citations:
   ```
   WARN  knowledge/rules-angular.md: no 'Source:' citations. Uncited rules are
         model memory, not knowledge.
   ```
4. **Version-pinned where the framework version matters.** Angular 16 and Angular
   19 disagree about a great deal. The knowledge file states which version it
   describes, taken from the actual manifest.
5. **A rule with no source is deleted, not kept.** An uncited rule is worse than
   no rule: it looks authoritative and is not.

## Generation procedure

When the generator needs knowledge for a detected stack:

1. Read the profile's `knowledge:` list — these are curated, first-party URLs.
2. Fetch each one. **Fetch, do not recall.** If fetching is unavailable
   (offline, no web tool), the generator must say so plainly and write a stub
   that names the URL and states that the content was not retrieved. It must not
   fill the gap from memory.
3. Extract rules that are **actionable and checkable**. Discard prose.
4. For each rule, attempt rank 1: is there a lint/analyzer rule id? If yes, the
   output is a config change, and the rules file merely references the rule id.
5. Write the remainder to `.agent/knowledge/rules-<stack>.md` with citations.
6. Run `harness.py lock`.

## MCP servers

MCP is rank 2 — live lookup instead of a snapshot. Worth wiring where available:

| Server | Serves | Notes |
|---|---|---|
| Angular CLI MCP (`npx ng mcp`) | version-correct Angular docs, schematics | first-party, ships with the CLI |
| Microsoft Learn MCP | .NET / ASP.NET / Blazor / MAUI API docs | first-party |
| Context7 | pinned library docs across ecosystems | third-party aggregator; verify what it returns |
| internal docs MCP | your own component and architecture docs | the highest-value one for an established org |

The profile lists candidates under `mcp:`. The generator checks which are actually
configured in the host, records the result in the lock, and — importantly — **does
not pretend an unconfigured server is available.** A missing MCP means the harness
falls back to rank 3 with citations, and says so.

## Skill slots

Profiles and overlays declare **skill slots** rather than hard paths, because a
skill that does not exist is worse than no skill: the agent is told to read a file
that is missing and falls back to memory, which is the exact failure being
designed out. (The original harness had this bug — it emitted skill references
unconditionally while warning that they might be absent.)

A slot resolves through a chain, and **what resolved is recorded in the lock**:

```yaml
skills:
  - id: syncfusion-blazor-components
    resolution:
      - kind: localSkill          # 1. an installed skill, if present
        paths:
          - "~/.claude/skills/syncfusion-blazor/SKILL.md"
          - "~/.agents/skills/syncfusion-blazor/SKILL.md"
      - kind: orgRegistry         # 2. an internal registry path or git URL
      - kind: generateFromDocs    # 3. generate a cited knowledge file
        sources: [blazor-components]
```

The generated instructions reference **only** slots that actually resolved. If a
slot resolves to nothing, the instructions say so explicitly:

```markdown
### Knowledge for Syncfusion Blazor
Resolved to: .agent/knowledge/syncfusion-blazor.md (generated from official docs,
5 sources cited, retrieved 2026-09-01)

NOT AVAILABLE: a dedicated syncfusion-blazor skill. Component API details beyond
the cited docs are UNVERIFIED - look them up rather than recalling them.
```

### Vendor-published skill registries

Some vendors publish their own agent skills. When one exists it **outranks
everything else** for that vendor's APIs, because it is maintained by the people
who change the API.

**Syncfusion publishes exactly this** at `ai.syncfusion.com`: an onboarding
skill, 775 component skills across 18 platforms, and per-platform MCP servers.
So the overlay's skill slots resolve to real installed skills at rank 2, not to
generated documentation summaries at rank 3.

Their own first rule is *"Do not write Syncfusion code from memory"* — the same
principle this file argues for, stated by the vendor.

```bash
npx skills add syncfusion/agent-onboarding-skill --agent claude-code -y
```

Two details that matter and are easy to get wrong:

1. **Install narrow, not wide.** Syncfusion is explicit: *"Do not load every skill
   or reference into context."* The Blazor pack alone is 72 skills. The harness
   generates `scripts/detect-syncfusion-components.sh`, which greps for components
   actually in use and installs only those — typically 3–6 skills.

2. **Resolve identifiers against the inventory; never construct them.** Skill
   names do not follow from component names as reliably as they appear to.
   Measured against the real inventory, naive construction got half of a
   four-component sample wrong:

   | Component in code | Constructed | Actual `skill_name` |
   |---|---|---|
   | `SfGrid` | `syncfusion-blazor-datagrid` | `syncfusion-blazor-datagrid` |
   | `SfPdfViewer2` | `syncfusion-blazor-pdf-viewer` | `syncfusion-blazor-pdf-viewer` |
   | `SfChart` | `syncfusion-blazor-chart` | `syncfusion-blazor-**charts**` |
   | `SfSchedule` | `syncfusion-blazor-schedule` | `syncfusion-blazor-**scheduler**` |
   | `ExcelEngine` | `syncfusion-document-sdk-excel` | `syncfusion-**dotnet-excel**` |

   The detector therefore fetches
   `https://ai.syncfusion.com/<slug>/inventory.txt` and resolves each candidate
   against the `skill_name` column. A candidate with no match is **reported, not
   installed** — installing a guessed identifier is the same failure as an
   uncited rule.

Full flow, MCP configuration, and the platform slug map:
`references/syncfusion-onboarding.md`.

### When no vendor registry exists

Fall back through the chain: an installed local skill, then an internal org
registry (`orgSkillRoot`), then `generateFromDocs` — a cited knowledge file built
from official docs, with `Source:` lines and retrieval dates. That fallback is
second-best and the generated instructions must say so:

```markdown
NOT AVAILABLE: a dedicated <x> skill. API details beyond the cited docs are
UNVERIFIED - look them up rather than recalling them.
```
