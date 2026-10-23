# The Harness Contract: harness.yaml, harness.lock, validator

Three files, three jobs:

| File | Authored by | Job |
|---|---|---|
| `harness.yaml` | human (generated first, then edited) | **the contract** — what must be true for work to be accepted |
| `harness.lock` | `harness.py lock` | **the record** — what was actually resolved, plus ratchet floors and the repo fingerprint |
| `harness.py validate` | — | **the enforcer** — runs in CI, fails on an invalid or weakened contract |

## Layout

Vendor-neutral, so the same harness serves every agent tool the org uses:

```
.agent/                          # canonical, vendor-neutral, committed
├── harness.yaml                 # the contract
├── harness.lock                 # generated; committed
├── instructions.md              # the operating protocol (host-agnostic)
├── project-context.md           # stack, architecture map, conventions, boundaries
├── progress.md                  # cross-session memory
├── tools/
│   ├── task.py                  # task state machine
│   ├── harness.py               # gate runner, validator, lock, doctor
│   ├── parsers.py               # evidence parsers
│   └── hyaml.py                 # dependency-free YAML subset reader
├── adapters/
│   ├── codestudio.md            # how to delegate/read files in Code Studio
│   ├── claude-code.md
│   └── generic.md
├── knowledge/                   # cited framework rules; hashed in the lock
├── agents/
│   ├── reviewer.md              # generated, framework-aware
│   └── builder.md
├── tasks/
│   ├── index.json               # never hand-edited
│   └── journal.jsonl            # append-only audit trail
├── evidence/                    # gate output; gitignored
└── archive/

.codestudio/codestudio-instructions.md   # thin pointer + host adapter
CLAUDE.md                                 # thin pointer + host adapter
AGENTS.md                                 # thin pointer (Codex, Cursor, Copilot)
```

**Commit `.agent/`.** Only `.agent/evidence/` is gitignored. A harness that lives
on one developer's machine is not a harness — CI must be able to run
`harness.py validate` on the same contract the agent used.

## harness.yaml

```yaml
harnessVersion: "1.0"        # contract version; harness.py refuses a major mismatch
profile: dotnet-blazor       # which profile generated this
overlays: [syncfusion]       # composed overlays
tier: L1                     # L0..L3 — a claim about what has been verified

loop:
  maxActive: 1               # concurrent build tasks
  maxVerifying: 3            # concurrent async outer-lane runs
  reviewBlocksPick: true     # a task in review blocks new picks
  requireEvidenceForDone: true   # THE core switch — see below
  verifyRetryLimit: 3

lanes:
  inner: { budgetSeconds: 120 }     # written in block style; see the subset note
  outer: { budgetSeconds: 10800 }

gates:
  - id: coverage-diff
    tier: L1                 # minimum tier at which this gate runs
    lane: inner              # inner | outer
    claim: "lines changed by this task are covered by tests"
    tool: coverlet
    license: "MIT (free, commercial use OK)"
    command: dotnet test --collect:"XPlat Code Coverage" ...
    parser: cobertura-diff
    evidence: "{EVIDENCE_DIR}/cov/**/coverage.cobertura.xml"
    threshold: diffLineRate >= 0.80
    blocking: true
    ratchet: true
    installHint: "add coverlet.collector to each test project"
    docs: https://github.com/coverlet-coverage/coverlet
    rationale: "why this gate exists at all"

risk:
  requiresHumanApproval:
    - "**/Auth/**"
    - "**/Migrations/**"
    - "**/*.csproj"

flaky:
  quarantine: []
  retries: 2
  expiresAfterDays: 30
```

### Substitutions available in `command` and `evidence`

| Token | Expands to |
|---|---|
| `{TASK}` | active task id |
| `{BASE}` | the task's base commit (for `--since` / diff scoping) |
| `{EVIDENCE_DIR}` | `.agent/evidence/<TASK>/` |
| `{REPO}` | repository root |
| `{SCOPE}` | `diff` or `full` |

### `requireEvidenceForDone` is the whole design

With it true, `task.py done` refuses unless
`.agent/evidence/<TASK>/gate-summary.json` exists, its `verdict` is `pass`, and
its `commit` matches HEAD. That last condition matters as much as the first — it
means evidence goes stale the moment new code lands, so an agent cannot run gates
early and then keep committing.

Turning it off converts the harness back into a process ritual. It exists as a
switch only so an L0 project can bootstrap before any gate exists.

## The YAML subset

`.agent/tools/hyaml.py` reads a documented subset with no third-party dependency, so
the harness runs on a bare Python 3.8+ install with no pip access. PyYAML is used
automatically when present. The subset is a strict sub-language of real YAML, so
your editor, CI, and any YAML tool read these files correctly.

Supported: nested block mappings, block sequences of scalars and of mappings,
scalars (string/int/float/bool/null), quoted strings with escapes, inline flow
sequences `[a, b]`, block scalars `|` and `>`, `#` comments.

Rejected with a line number (rather than silently mis-parsed): tab indentation,
anchors and aliases, multi-document streams, inline flow **mappings** `{a: 1}`.

## harness.lock

Generated. Records:

| Field | Purpose |
|---|---|
| `fingerprint` | hash of manifest set + dependency **names** — changes when a framework or package is added, not on a version bump |
| `manifests` | every manifest found, so `doctor` can diff the set |
| `tools` | resolved version of each gate's tool, or `missing`/`error` |
| `ratchetFloors` | per-gate threshold floors; lowering one fails `validate` |
| `knowledge` | SHA-256 and extracted `Source:` URLs per knowledge file |
| `configSha256` | detects `harness.yaml` edited without re-locking |
| `gateIds`, `tier`, `profile`, `overlays` | what was generated |

## What `validate` enforces

Run it in CI. It fails on:

- missing or malformed required gate keys
- an unknown parser, or a threshold referencing a metric the parser does not emit
- **an empty threshold** — a gate that cannot fail is worse than no gate, because
  it reads as coverage in the summary
- an artifact-reading parser with no `evidence` path
- **a threshold below its ratchet floor**
- **tier obligations unmet** — declaring `tier: L3` with no mutation gate fails
- `mode: async` on an inner-lane gate, or with no `pollCommand`
- contradictory flags (`allowFailure` with `blocking`)
- a major `harnessVersion` mismatch against `harness.py`

And warns on: no inner-lane gates, a declared tier with no outer-lane gates, gates
added since the lock, knowledge files with no citations, a changed fingerprint.

## Versioning and upgrade

`harnessVersion` is the contract version, and `harness.py` refuses a major
mismatch rather than mis-parsing. Upgrade path:

| Path | Action |
|---|---|
| `tools/*.py` | overwrite — these are harness code |
| `adapters/` | overwrite |
| `harness.yaml` | **merge** — new gates appended, existing thresholds and edits preserved, removed profile gates reported not deleted |
| `harness.lock` | regenerate |
| `project-context.md` | **merge** — regenerate the architecture map, preserve human-written sections |
| `knowledge/` | re-fetch and re-cite; report changed rules |
| `tasks/`, `progress.md`, `archive/`, `journal.jsonl` | **never touched** |

The merge behaviour on `harness.yaml` is the important one: a team that raised a
threshold or added a custom gate must not lose it to a regeneration. Upgrade
reports every decision it made.
