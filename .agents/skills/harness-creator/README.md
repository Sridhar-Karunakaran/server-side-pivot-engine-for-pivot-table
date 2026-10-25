# Agent Harness Creator

Generates a **verification harness** that makes AI-generated code provably
correct instead of plausibly correct.

## The problem it solves

Most agent harnesses are process rituals. They have a task list and a sequence of
stages, and every quality gate resolves to *the agent reads a checklist and forms
an opinion*. That is an LLM judging LLM output. It produces confident,
unverifiable claims — and "I reviewed this for memory leaks" is worth nothing,
because leak detection requires observing heap state over time, which is
information that does not exist in source code.

**One rule governs this harness:**

> For any claim where a deterministic tool exists, the agent may not assert the
> conclusion. It must run the tool of record and cite the evidence artifact.

Mechanically, every gate is `command + parser + threshold + evidence path`. The
agent runs the command, a parser extracts a number from real tool output, the
harness compares it to a declared threshold, and `task.py done` **refuses** to
complete a task without a passing `gate-summary.json` whose commit matches HEAD.

```
$ python3 .agent/tools/task.py done
REFUSED: cannot mark T-014 done without passing gate evidence.
  0/1 gates pass, 1 not run

This is the harness working as designed: a task is done when tools say
so, not when an agent says so.
```

## What that looks like in practice

An agent adds a function and a test that only checks the code does not crash —
the signature failure mode of AI-written tests:

```
$ python3 .agent/tools/harness.py gate run --lane inner --scope diff
GATES  task=T-001  lane=inner  scope=diff  base=2efe5815

  FAIL coverage-diff   diffLineRate=0.4444  [0.4s]
       Add tests covering these changed lines (they are yours, not legacy):
           src/calc.py:12
           src/calc.py:16
           src/calc.py:17

  VERDICT: FAIL
  task.py will refuse to mark this task done. That is the design.
```

Note *diff* coverage, not global. Global coverage on a fifteen-year-old codebase
is 4%: set a threshold there and it is either unreachable or meaningless. Diff
coverage asks an answerable question — of the lines this task changed, how many
are covered — and is enforceable at 80% on any codebase from day one.

## Tiers, so you can start where you actually are

| Tier | Gates | Promise |
|---|---|---|
| **L0** Unverified | build, analyzers, secrets | *Nothing about behaviour.* Labels output `UNVERIFIED` and forces characterization tests. |
| **L1** Diff-verified | + tests, **diff coverage** | Changed lines are tested. Legacy code is not held to a bar it can never meet. |
| **L2** Verified | + full suite, CVEs, dependency scanning | The whole project is green. |
| **L3** Hardened | + **diff mutation**, leak / perf / a11y budgets, arch rules | Tests are *meaningful*, not merely present. Runtime properties are measured. |

`harness.py validate` **fails** if a declared tier's obligations are unmet — a
tier is a claim about what has been verified, so it cannot be aspirational.

### The ratchet

A threshold that can move down is not a threshold. Thresholds marked
`ratchet: true` record a floor in `harness.lock`, and lowering one fails
validation in CI:

```
$ python3 .agent/tools/harness.py validate
FAIL  gate 'coverage-diff': threshold diffLineRate >= 0.3 is below the ratchet
      floor diffLineRate >= 0.8 recorded in harness.lock. Quality bars ratchet
      up, not down.
```

This matters more with agents than with people. An agent told to make the gates
pass will discover that editing the threshold is the cheapest path.
`harness.py ratchet` works the other way, proposing raises where the project
already clears the bar comfortably.

## Slow and cloud-based test suites

Two lanes, split by feedback latency:

| Lane | Budget | Contents |
|---|---|---|
| **inner** | **< 2 min** | build, analyzers, typecheck, *impacted* unit tests, diff coverage |
| **outer** | minutes to hours | full suite, E2E, mutation, leak/perf, cloud runs |

For a suite that runs on a build farm for three hours, the outer lane goes async.
A new `verifying` state holds the task — **not** `done` — while the loop picks up
independent work:

```bash
harness.py gate run --lane inner --scope diff   # fast feedback per subtask
harness.py gate submit                          # queue the cloud run
task.py verify-submit --job "build-48213"       # -> verifying
task.py next                                    # keep working
harness.py gate poll                            # reconcile when it lands
```

The whole CI integration surface is two scripts: a submit that prints a job id,
and a poll that **exits 75 while still running**. Azure DevOps, Jenkins, GitHub
Actions, TeamCity or an internal farm are all details of those two scripts.

## Vendor-neutral, with thin host wrappers

```
.agent/                     # canonical, committed, host-agnostic
├── harness.yaml            # the contract
├── harness.lock            # resolved tools, ratchet floors, repo fingerprint
├── instructions.md         # the protocol (uses DELEGATE/READ_FILE/RUN)
├── project-context.md      # architecture map from real tooling
├── tools/                    # task.py, harness.py, parsers.py, hyaml.py
├── adapters/               # codestudio.md, claude-code.md, generic.md
├── knowledge/              # cited framework rules, hashed in the lock
├── agents/reviewer.md      # framework-aware, works a defect catalog
├── tasks/                  # index.json + journal.jsonl (append-only audit)
└── evidence/               # gate output (gitignored)

.codestudio/codestudio-instructions.md   # 12-line pointer
CLAUDE.md                                 # 12-line pointer
AGENTS.md                                 # 12-line pointer (Codex/Cursor/Copilot)
```

The protocol names abstract capabilities; each adapter binds them to one host.
Wrappers contain **no protocol content**, so they cannot drift.

## Free tools only

Every recommended tool is free for commercial closed-source use. Notable
exclusions, with the free substitute:

| Commonly recommended | Problem | Use instead |
|---|---|---|
| CodeQL | free only for open source; commercial needs paid GitHub Advanced Security | **Semgrep OSS** |
| dotMemory / dotTrace | commercial JetBrains licence | **dotnet-gcdump / dotnet-counters** (MIT, Microsoft) |
| SonarQube Developer+ | paid | **SonarQube Community Build** + free `SonarAnalyzer.*` NuGet |
| Percy / Chromatic | paid SaaS | **Playwright `toHaveScreenshot()`** |
| Snyk | capped free tier | **OSV-Scanner** or **Trivy** |
| NDepend | commercial | **ArchUnitNET** + `dotnet list reference` |

`harness.py doctor` prints each gate's `licence:` field, so the licence posture is
auditable straight off the harness.

## Roslyn / FxCop enforcement for .NET

FxCop's successor is **Microsoft.CodeAnalysis.NetAnalyzers**, in-SDK and free. The
harness generates `Directory.Build.props` and `.editorconfig` rather than telling
the agent to follow best practices — a rule the build enforces cannot be
forgotten, skipped, or argued with.

The escalation set targets what models actually get wrong: `CA2000`/`CA2213`/
`CA1063` (disposal — the leak source in WinForms, MAUI and the document
libraries), `VSTHRD100`/`VSTHRD002` (`async void`, sync-over-async),
`CA2100`/`CA5350` (injection, weak crypto), `CA2016` (cancellation token
forwarding).

Landing this on a legacy repo with 4,000 warnings: gate the **diff** first,
baseline the rest with a dated register, then ratchet. Enforcing globally in one
step always ends with the team turning analyzers off.

## Stacks

Profiles are data, not logic — the loop, gates, tiers and ratchet are identical
everywhere, so one skill covers every stack:

`dotnet-aspnetcore` · `dotnet-blazor` · `dotnet-maui` · `dotnet-winforms` ·
`js-angular` · `js-react` · `js-common` · `python` · `dotnet-common`

Each carries detection signals, tool-of-record bindings, cited knowledge sources,
and a defect catalog in `catalogs/` where every entry is paired with the analyzer
rule id that catches it — so the reviewer's highest-value finding becomes *"this
rule exists but is not enabled"*, which prevents the class rather than one
instance.

**Overlays** compose org-specific behaviour onto any profile.

### Syncfusion

`overlays/syncfusion.yaml` wires the official registry at **ai.syncfusion.com** —
an onboarding skill, 775 component skills across 18 platforms, and per-platform
MCP servers. Syncfusion's own first rule is *"Do not write Syncfusion code from
memory"*, which is this harness's principle stated by the vendor.

Two things the overlay does that matter:

**Installs narrow, not wide.** Syncfusion is explicit that agents should not load
whole packs. `scripts/detect-syncfusion-components.sh` greps for components
actually in use — typically 5 skills instead of 72.

**Resolves identifiers instead of constructing them.** Skill names do not follow
from component names as reliably as they look. Against the real inventory, naive
construction got half a four-component sample wrong:

| In code | Constructed | Actual |
|---|---|---|
| `SfChart` | `syncfusion-blazor-chart` | `syncfusion-blazor-`**`charts`** |
| `SfSchedule` | `syncfusion-blazor-schedule` | `syncfusion-blazor-`**`scheduler`** |
| `ExcelEngine` | `syncfusion-document-sdk-excel` | **`syncfusion-dotnet-excel`** |

So the detector fetches `ai.syncfusion.com/<slug>/inventory.txt` and resolves
against the `skill_name` column. An unmatched candidate is **reported, never
installed on a guess**.

Four gates come from Syncfusion's own mandatory rules:

| Gate | Catches |
|---|---|
| `syncfusion-license` | registration missing or after first component use; key as a literal; key in a WASM project |
| `syncfusion-banner-suppression` | a banner hidden by CSS, removed from the DOM, or a swallowed licensing exception |
| `syncfusion-blazor-setup` | missing `AddSyncfusionBlazor()` (renders **empty**, no error), missing theme stylesheet, missing render mode in .NET 8+ (events **never fire**) |
| `syncfusion-version-consistency` | mixed package versions — unsupported, and produces `MissingMethodException` that looks like an app bug |

Every one of those fails silently in a normal build and test run, which is exactly
why they are gates.

## Getting started

```bash
git clone <this repo> ~/.claude/skills/harness-creator
```

Then, in a project: *"set up harness"*, *"create harness"*, or
*"evaluate my harness"*.

The skill scans, then **proposes before writing anything** — detection evidence, a
Mermaid diagram of the tailored loop, the gate table with licences and estimated
lane times, gates deliberately *not* proposed with their cost and rationale, gaps,
and the exact file list. Nothing lands until you approve.

```bash
python3 .agent/tools/harness.py doctor      # tools, drift, knowledge citations
python3 .agent/tools/harness.py tier        # current tier and next-tier requirements
python3 .agent/tools/harness.py gate list   # every gate, threshold, metric
python3 .agent/tools/harness.py explain "no memory leak"   # tool of record for a claim
python3 .agent/tools/task.py next
```

`explain` exiting non-zero is meaningful: no gate covers that claim, so the agent
must record it as `UNVERIFIED` rather than assert it.

## Anti-gaming

Agents optimise for the stated objective, so the cheats are named and the
mechanical ones blocked:

| Cheat | Countermeasure |
|---|---|
| Lower the threshold | ratchet floor; `validate` fails in CI |
| Delete or skip the failing test | test-count metric; reviewer checks the diff |
| Assert nothing (`assert x is not None`) | mutation gate — the mutant survives |
| Cover lines without asserting | diff coverage passes, mutation catches it |
| Add a coverage-exclusion attribute | counted as a metric; flagged in review |
| Suppress the analyzer inline | suppressions in the diff need written justification |
| `--override` the gate | requires a reason; written to `journal.jsonl` |
| Hand-edit `index.json` | `task.py doctor` detects it; the journal has no matching event |

None is airtight against a determined adversary. All are airtight against an
agent taking the cheapest path — which is the actual threat.

## License

MIT
