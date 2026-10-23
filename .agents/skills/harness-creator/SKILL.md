---
name: harness-creator
description: Generates or upgrades a verification harness that makes AI-generated code provably correct instead of plausibly correct. Detects the stack (React, Angular, Blazor, MAUI, WinForms, ASP.NET Core, Python and more), proposes a tailored gate ladder with a loop diagram, and writes a vendor-neutral .agent/ harness with contract, lock, validator, evidence-backed gates, cited framework knowledge, and a framework-aware review agent. Also evaluates and improves an existing harness. Use when the user says set up harness, create harness, bootstrap project, autonomous loop, agent harness, improve harness, or audit harness.
---

# Harness Creator

## What this builds and why

Most agent harnesses are process rituals: a task list plus a sequence of stages
where every quality gate resolves to *the agent reads a checklist and forms an
opinion*. That is an LLM judging LLM output, and it produces confident,
unverifiable claims.

This harness replaces assertion with evidence. **One rule governs it:**

> For any claim where a deterministic tool exists, the agent may not assert the
> conclusion. It must run the tool of record and cite the evidence artifact.

Mechanically: every gate is `command + parser + threshold + evidence path`. The
agent runs the command, a parser extracts a number from the real output, the
harness compares it to a declared threshold, and `task.py done` **refuses** to
complete a task without a passing `gate-summary.json` whose commit matches HEAD.

An agent can say "no memory leaks here" with total fluency and no information. A
profiler cannot.

## Route by mode

| Situation | Go to |
|---|---|
| No `.agent/` and no `.codestudio/` | **Phase 1 — SCAN**, then PROPOSE, GENERATE, VERIFY |
| `.agent/` exists | **Phase 0 — EVALUATE**, then upgrade |
| `.codestudio/` exists, no `.agent/` | **Phase 0 — EVALUATE** (legacy migration) |
| User asks to audit / improve / score a harness | **Phase 0 — EVALUATE**, stop after the report |

Read only the references a phase names. Do not preload them all.

---

## Phase 0 — EVALUATE (existing harness)

Score the harness against the eight properties that separate a verification
harness from a process ritual. Report before changing anything.

```
python3 .agent/tools/harness.py doctor    # if the harness is already this generation
```

For a legacy or foreign harness, assess manually:

| # | Property | How to check | If missing |
|---|---|---|---|
| 1 | **Evidence gates** | is there a `harness.yaml` with `command`+`parser`+`threshold`? | Everything else is cosmetic. This is the upgrade. |
| 2 | **Completion is blocked on evidence** | does `done` refuse without a passing summary? | The harness cannot enforce anything. |
| 3 | **Diff-scoped coverage** | a `cobertura-diff`/`lcov-diff` gate? | Global coverage on a brownfield repo is unenforceable. |
| 4 | **Mutation on changed code** | a `stryker` gate? | Nothing distinguishes a real test from `assert x is not None`. |
| 5 | **Ratchet floors** | `ratchetFloors` in `harness.lock`? | An agent will lower thresholds to pass. |
| 6 | **Vendor-neutral** | is the protocol in `.agent/`, with host adapters? | Hardcoded `runSubagent` breaks every other tool. |
| 7 | **Cited knowledge** | do `knowledge/*.md` files carry `Source:` lines? | Framework rules are model memory. |
| 8 | **Framework-aware review** | does the reviewer work a defect catalog with rule ids? | Review finds naming issues, not shipped defects. |
| 9 | **Typed workflows** | `.agent/workflows/` with one per task type? | Every task runs the same undifferentiated loop; a security fix gets a refactor's rigour. |
| 10 | **Scoped agent roles** | `.agent/agents/` with `authority` / `mayNot` in frontmatter? | One agent doing everything reviews its own work. |
| 11 | **Policies as files** | `.agent/policies/`, bound by workflows? | Rules live in prose an agent can reinterpret. |
| 12 | **Autonomy boundary** | `DECISIONS-REQUIRED.md` + the three lanes? | The agent either asks about everything or decides things it shouldn't. |
| 13 | **Control plane locked** | `HARNESS-LOCK.json` + `harness.py verify` in the gate? | **Relaxing a policy is cheaper than satisfying it.** |
| 14 | **Oracle hierarchy** | is the validation hierarchy recorded? | Own-test-suite evidence gets treated as proof. |

Report as a table with verdicts, then propose the upgrade in PROPOSE format.
State the biggest gap first. #1 and #2 are the verification game; #9–#13 are the
difference between a verified repo and a software factory.

**If the project already has a control plane** — its own `.agent/`, `AGENTS.md`
workflows, or a set of agent definitions — do not replace it. Read it, map its
concepts onto the model in `references/control-plane.md`, and report:

- which workflows, agents, policies and checklists it already has, by name
- which of the 14 properties it satisfies
- what is **dangling** — a workflow naming an agent that does not exist, a policy
  nothing binds to, a task type nothing routes
- what is **missing** against the completeness floor
- what is **better than the default** and should be kept verbatim

Then propose additive changes only. A team that has invested in a control plane has
usually encoded real project knowledge in it; regenerating over that is destructive
even when the replacement is technically better.

**Upgrade rules** (from `references/harness-contract.md`):

| Path | Action |
|---|---|
| `.agent/tools/*.py`, `.agent/adapters/`, wrappers | overwrite |
| `.agent/harness.yaml` | **merge** — append new gates, preserve every threshold and custom gate |
| `.agent/project-context.md` | **merge** — regenerate GENERATED sections, keep hand-written ones |
| `.agent/knowledge/` | re-fetch, re-cite, report changed rules |
| `.agent/tasks/`, `progress.md`, `archive/`, `journal.jsonl` | **never touch** |
| `.agent/harness.lock` | regenerate last |

Migrating from a legacy `.codestudio/` harness: move tasks into
`.agent/tasks/index.json` (`task.py` reads the old bare-array format and migrates
it), then reduce `.codestudio/codestudio-instructions.md` to a pointer.

---

## Phase 1 — SCAN

Read `references/detection-rules.md`.

Detect with tools, never by impression:

1. **Manifests and dependency names** — `find` for `*.csproj`, `package.json`,
   `pyproject.toml` etc. Extract dependency *names*; they select the profile.
2. **Profile match** — score every profile in `profiles/`. Highest wins. A tie or
   a low top score is a question for the user, never a silent guess.
3. **Overlays** — `overlays/*.yaml`. Syncfusion detection is on `Syncfusion.` /
   `@syncfusion/` package prefixes.
4. **Test and coverage reality** — which test projects exist, how many tests, what
   the current coverage is. This sets the tier.
5. **Architecture, from a tool** — `dotnet list reference`, `depcruise --output-type json`,
   or `madge --json`. Not by reading three files and inferring.
6. **Hotspots** — `git log --format= --name-only | sort | uniq -c | sort -rn | head -20`.
   Cheapest high-value signal available: defects concentrate in churn.
7. **Tool availability** — `which` every gate's binary. A missing tool becomes an
   `installHint` in PROPOSE, never a silently broken gate.
8. **Tier** — from what actually exists (`references/tiers-and-ratchet.md`):
   no tests → L0 · some tests → L1 · good tests → L2 · mature → L3.
9. **Design system** — per `references/design-system.md`. Check for an existing
   design document **first**; if one exists, reference it and never regenerate.
   Otherwise extract from token files, theme objects, Tailwind config, CSS custom
   properties, or — failing all of those — by colour frequency, labelled as an
   observation rather than a specification. **Never ask the user for this; read it.**
10. **Tool-per-scenario** — for every row of the scenario matrix in
    `references/interview.md`, record detected / not detected. This is what decides
    which interview questions get asked, and which get skipped.
11. **Git** — required. No `.git` means no rollback, so `git init` is the first
    task, not a skipped stage.

**Greenfield** (no source): ask six questions, one at a time — what are you
building, for whom, which stack, which architecture, which conventions, what is
the first feature. Then proceed with the chosen profile at L1.

---

## Phase 2 — INTERVIEW

Read `references/interview.md`.

Detection covers what the repo can tell you. The interview covers intent, the tools
nothing is configured for yet, and the things no profile anticipated.

Three rules:

1. **Never ask what you detected.** Report it and move on.
2. **Every question is skippable and states its default.** A user should be able to
   press enter through the whole thing and get a sane harness.
3. **One question at a time.** Each answer changes the next.

Open by reporting detection so the user can correct it and can see why the remaining
questions remain. Then ask only the undetected rows of the scenario matrix — unit
testing, coverage, linting, E2E, memory profiling, perf profiling, security scanning,
dependency CVEs, accessibility, visual regression, mutation testing, API surface,
architecture rules, CI.

**Ask about the concern, not the tool.** Someone who has not configured memory
profiling does not know whether they want `dotnet-gcdump`; they do know whether their
app leaks. Pick the tool yourself from `references/tools-of-record.md` and name it,
with its licence, in the proposal.

Three questions to ask even when partly detected, because detection cannot see intent:

- **How long does the suite take, and where does it run?** — decides the lane split and
  whether outer gates go async
- **What must never break?** — becomes the risk-gated paths and the first
  characterization-test target
- **What kind of bug has reached your customers?** — the highest-yield question here;
  every answer maps to a gate

Then always ask the open-ended one, **with examples**:

> "Anything else you want the harness to do? It can generate custom skills, gates, or
> workflows — a log extractor for your error tracker, an internal API client, a
> deployment check, a report generator."

Custom requests are classified and generated per `references/interview.md`: a command
that returns output is a **skill**; with a threshold it is a **gate**; a multi-step
procedure is a **workflow**; a binding rule is a **policy**; live API access is an
**MCP server**. Check for an existing vendor MCP server before writing a script.

Carry into PROPOSE: what was answered, **what was skipped and which default applied**,
and every custom request.

## Phase 3 — PROPOSE (never skip)

Read `references/propose-stage.md` for the exact output shape.

Show, and write nothing until approved:

1. **What was detected, with the evidence for each conclusion** — so a wrong guess
   is visible and correctable
2. **A Mermaid diagram of the tailored loop** — the two lanes, the async path, the
   risk gate
3. **The gate table** — tool, licence, threshold, status (ready / needs install /
   needs setup), and estimated inner and outer lane times
4. **Gates deliberately not proposed**, each with a one-line rationale and a cost.
   *This is the teaching surface* — it is how a user learns that mutation testing
   exists and what it costs
5. **Gaps** and what the harness will do about each
6. **Knowledge and skill slots**, honestly — never claim an unavailable skill or MCP
7. **The exact file list**, including which files are merged rather than written

Then: `Proceed? [y / adjust tier / adjust gates / explain <gate>]`

---

## Phase 4 — GENERATE

Read `references/harness-contract.md`, `references/tools-of-record.md`,
`references/knowledge-acquisition.md`, and `templates/manifest.json`.

`templates/manifest.json` is authoritative for every output path, its variables,
and its upgrade behaviour. Follow it.

Order matters:

1. **`.agent/tools/`** — `task.py`, `harness.py`, `parsers.py`, `hyaml.py`. Copied
   verbatim; no substitutions.
2. **`.agent/harness.yaml`** — compose profile + overlays, then apply all three
   gate filters (`profiles/_schema.md`): **tier**, **tool resolved on PATH**, and
   **`appliesTo` product match**. A gate for an absent tool is a broken harness;
   the install hint belongs in PROPOSE. A gate for an undetected product is noise.
3. **Enforcement files** — for .NET, `Directory.Build.props` and `.editorconfig`
   per `references/roslyn-enforcement.md`, merged additively. For JS,
   `eslint.config.js` additions. **This is rank-1 knowledge: a rule the build
   enforces beats a rule in a markdown file, every time.**
4. **Vendor skill registries first, then `.agent/knowledge/`.** If a vendor
   publishes agent skills, install those — they outrank any documentation summary.
   **Syncfusion does**: follow `references/syncfusion-onboarding.md` (onboarding
   skill, then *narrow* component skills resolved against the platform inventory,
   then MCP). Only then fetch the profile's remaining cited URLs into
   `.agent/knowledge/`, each rule carrying
   `Source: <url> (retrieved YYYY-MM-DD)`. If fetching is unavailable, write a
   stub naming the URL and stating the content was **not** retrieved. Never fill
   the gap from memory.
5. **The control plane** — per `references/control-plane.md`. Copy
   `control-plane/{workflows,agents,policies,checklists,templates,skills}` into
   `.agent/`, then specialise:
   - `agents/code-reviewer.md` gets the stack's defect catalog and the real
     architecture map (`references/review-agents.md`)
   - `policies/autonomy-boundary.md` gets the park categories from the detected
     `risk.requiresHumanApproval` globs, so the boundary and the risk gate agree
   - workflows drop any step whose tool the project lacks, and say so
   - `skills/` is generated from resolved commands (`control-plane/skills/_catalog.md`)
   - the authority and validation hierarchies are filled per stack
     (`references/oracles.md`), including the **oracle ceiling** where no independent
     validator exists
   Then create `PROJECT-STATE.md` and `DECISIONS-REQUIRED.md`. The second one matters:
   a parking lot that does not exist is one the agent will not use, and it will block
   instead.
6. **`.agent/design.md`** — only when a design system was found or extracted
   (`references/design-system.md`). If a design document already exists anywhere in
   the repo, **reference it and do not regenerate**; offer to append missing sections,
   never to replace. Generate `policies/design-system.md` only alongside a real
   `design.md` — never against an invented system.
7. **Custom skills, gates, workflows and policies from the interview.** Generate each
   completely: the script reading credentials from the environment, the gate with a
   parser and threshold, `usedBy` wired to the agents and workflows that need it. A
   custom primitive nothing references is an orphan and `verify` will say so.
8. **`.agent/instructions.md`**, `project-context.md`, `progress.md`, adapters.
9. **Scripts** — Syncfusion license and version checks if the overlay applies;
   `leak-probe.sh` if a leak gate was selected; `check-suppressions.sh` for JS.
10. **Wrappers** — `.codestudio/`, `CLAUDE.md`, `AGENTS.md`. Pointers only, 12 lines,
    no protocol content. Skip these where the project uses APM, which generates them
    (`references/apm-export.md`).
11. **`.gitignore`** — append `.agent/evidence/` and nothing else. Commit the rest.
12. **`harness.py cp-lock`** then **`harness.py lock`**. The first hashes every
    control-plane file into `HARNESS-LOCK.json`; the `harness-verify` gate checks it
    on every task, which is what makes policies binding rather than advisory.

### Document quality is enforced, not hoped for

`templates/manifest.json` carries `rejectIfOutputContains`. Before showing the
user anything, check every generated file for `{{`, `TODO:`, `FIXME`,
`<describe`, `PLACEHOLDER`. Any hit means the generation failed — fix it, do not
ship it.

And do not emit a file to be thorough. No gate for an uninstalled tool. No
knowledge file that could not be fetched and cited. No empty section headers. A
harness of twelve real files beats one of forty with eight useful.

---

## Phase 5 — VERIFY

Prove the harness works before handing it over:

```bash
python3 .agent/tools/harness.py validate     # contract is well-formed
python3 .agent/tools/harness.py lock
python3 .agent/tools/harness.py doctor       # tools present, no drift, knowledge cited
python3 .agent/tools/harness.py verify       # control plane: integrity, refs, orphans, routing
python3 .agent/tools/harness.py gate list    # every gate, threshold, and metric
python3 .agent/tools/harness.py skill list   # every executable capability
python3 .agent/tools/task.py doctor          # task state consistent
python3 .agent/tools/harness.py gate run --lane inner --scope full
```

`verify` must be clean before you hand anything over. It catches the failure this
skill is most prone to: a workflow naming an agent that was never generated, or a
policy nothing binds to — incompleteness that looks like a finished harness.

The gate run is the other real test: the inner lane must actually run and pass on
the untouched codebase. If it fails, the harness is misconfigured — a gate that
fails on unmodified code teaches everyone to ignore gates.

Then seed tasks. For brownfield, convert real signals — the gaps found in SCAN,
TODO comments, failing tests — not invented work. At L0, the first tasks are
"establish a reproducible build" and "add characterization tests for <hotspot>".

Report: tier, gate count with lane split, estimated lane times, what needs
installing, what is unverified and why, and the first command to run.

---

## Phase 6 — PACKAGE (offer it)

Read `references/apm-export.md`.

Once the harness is verified, offer to package it so the same setup reproduces in
sibling repositories without cloning:

```bash
python3 .agent/tools/harness.py apm-export --name <org>-harness-<stack> --version 1.0.0
```

This writes an [APM](https://github.com/microsoft/apm) package — `apm.yml` plus a
`.apm/` tree — mapping policies to instructions, agents to agents, workflows to
prompts, and the machinery to a skill with siblings. Cross-references are rewritten to
their APM paths, and `apm compile` then generates the per-host files, replacing the
hand-written wrappers.

**Per-repository state is never packaged**: `tasks/`, `evidence/`, `harness.lock`,
`PROJECT-STATE.md`, `DECISIONS-REQUIRED.md`. The consuming repo runs `harness.py lock`
to establish its own tool versions, fingerprint, ratchet floors, and tier — which is
what lets a mature repo at L3 and a new one at L1 share every definition while
enforcing different bars.

Offer this; do not do it unasked. Publishing is outward-facing.

## References

Load on demand:

| File | When |
|---|---|
| `references/detection-rules.md` | SCAN |
| `references/interview.md` | **INTERVIEW** — scenario matrix, skippable questions, custom-skill generation |
| `references/design-system.md` | **any project with UI** — detect, extract, never invent |
| `references/apm-export.md` | **PACKAGE** — reproducing the harness in another repo via APM |
| `references/propose-stage.md` | PROPOSE |
| `references/harness-contract.md` | GENERATE, EVALUATE — contract, lock, validator, layout, upgrade |
| `references/tools-of-record.md` | choosing gates — claim→tool matrix, **licence policy** |
| `references/tiers-and-ratchet.md` | choosing a tier, diff coverage, anti-gaming |
| `references/async-gates.md` | slow or cloud test systems, flaky tests, impact selection |
| `references/roslyn-enforcement.md` | any .NET project |
| `references/knowledge-acquisition.md` | knowledge, MCP, skill slots, provenance |
| `references/syncfusion-onboarding.md` | **any Syncfusion project** — official skill packs, slug map, MCP |
| `references/review-agents.md` | generating the reviewer |
| `references/loop-protocol.md` | the loop, states, invariants |
| `references/control-plane.md` | **workflows, agents, skills, policies, checklists, templates** |
| `references/autonomy-boundary.md` | decide / assume-pin-record / park, the 3 checkpoints, bounded retry |
| `references/oracles.md` | authority precedence and the validation hierarchy |
| `profiles/_schema.md` | adding or editing a profile |

`profiles/` holds stack data. `overlays/` holds org data. `catalogs/` holds framework
defect catalogs. `control-plane/` holds the workflows, agents, policies, checklists
and document templates copied into every project. `templates/` holds the harness
machinery, governed by `templates/manifest.json`.

## Non-negotiables

1. **Never generate a gate for a tool that is not installed.**
2. **Never write an uncited framework rule.** Fetch it or mark it unretrieved.
3. **Never claim a skill or MCP is available when it is not.** Record an MCP with
   no API key file as `configured-but-unauthenticated`.
4. **Never install a skill identifier you constructed rather than looked up.**
   Resolve it against the vendor's inventory; report unmatched candidates instead.
5. **Never skip PROPOSE.** It is where the user learns what they are getting.
6. **Never emit a file containing a placeholder.**
7. **Never recommend a tool that is not free for commercial closed-source use**
   without saying so explicitly — see the licence policy in
   `references/tools-of-record.md`.
8. **Never generate a dangling reference.** A workflow naming an agent that does not
   exist, or a policy nothing binds to, is incompleteness dressed as completeness.
   `harness.py verify` must pass before hand-off.
9. **Never ask a question the repo already answers**, and never ask an
   unskippable one. Report detection first; state every default.
10. **Never regenerate an existing design document.** Reference it, or offer to
   append. Never invent a design system that is not in the code.
11. **Never leave the control plane unlocked.** Run `cp-lock`; without it a policy is
   a suggestion.
12. **Never set `requireEvidenceForDone: false`** except to bootstrap an L0 project
   with no gates yet, and say so out loud when you do.

---

## What happens next — from harness to productive work

The harness is created. **This skill's job is done.** From here the agent operates
under `.agent/instructions.md` and the control plane — both self-contained.

### Every session starts the same way

```bash
python3 .agent/tools/harness.py doctor       # contract valid, tools present, no drift
python3 .agent/tools/task.py status          # where things stand
```

Then read `.agent/instructions.md` — it carries the full loop protocol.

### Starting new work

When a user requests a feature, fix, or any change:

1. **Create a task:** `python3 .agent/tools/task.py add "Add dark mode support"`
2. **Enter the loop:** `python3 .agent/tools/task.py next` — picks the new task
3. **The loop routes it** to the right workflow (`feature`, `bugfix`, `refactor`, etc.)
4. **The workflow runs** through SPEC → PLAN → BUILD → GATES → REVIEW → DONE

The agent must never work outside a task. The task is what produces the evidence
trail, and the evidence trail is what makes `task.py done` succeed.

### When there is no roadmap yet

If `PROJECT-STATE.md` has no frozen roadmap, the task loop runs `project-bootstrap`
first — gathering requirements, writing a spec, and planning a roadmap. This ends at
**CHECKPOINT 1 (human): roadmap scope approval**. No production code is written until
that approval.

### Quick reference

| I want to… | Command |
|---|---|
| See current status | `python3 .agent/tools/task.py status` |
| Start the next task | `python3 .agent/tools/task.py next` |
| Add a new task | `python3 .agent/tools/task.py add "title"` |
| Add a task with dependency | `python3 .agent/tools/task.py add "title" --needs T-001` |
| Add to backlog (not urgent) | `python3 .agent/tools/task.py add "title" --backlog` |
| Check harness health | `python3 .agent/tools/harness.py doctor` |
| Run verification gates | `python3 .agent/tools/harness.py gate run --lane inner --scope diff` |
| See all gates | `python3 .agent/tools/harness.py gate list` |
