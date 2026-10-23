# The Control Plane

The verification plane (gates, tiers, ratchet, evidence) answers *is this change
acceptable?* The control plane answers everything else: what kind of work is this,
who does it, what may they decide alone, what must a human decide, what do they
write down, and what is binding on all of it.

A harness with only a verification plane is a very good linter. A software factory
needs both.

## Composition

```
task-loop.md                 the outer loop — the single entry point
   │
   ├── routes to ──> WORKFLOWS      typed procedures, one per kind of work
   │                     │
   │                     └── invokes ──> AGENTS   roles with scoped authority
   │                                        │
   │                                        └── uses ──> SKILLS  named real commands
   │
   ├── bound by ──> POLICIES        binding on every change, no exceptions
   ├── gated by ──> CHECKLISTS      must be satisfied before a state change
   └── records via ──> TEMPLATES    the shape of every artifact produced

HARNESS-LOCK.json    SHA-256 of every control-plane file, verified in the gate
```

Four properties make this a factory rather than a folder of markdown:

1. **Workflows are typed.** A security fix and a refactor are not the same procedure.
   Routing by task type is what lets each get the right gates, the right reviewer,
   and the right checklist without a human choosing each time.
2. **Agents have scoped authority.** An implementer may not approve its own work; a
   reviewer may not edit source. Scope is what makes multi-agent output trustworthy.
3. **Skills are commands, not prose.** A skill named `mutation-test` runs a real
   command and returns real output. If a skill cannot be executed, it is a policy or
   a checklist, and belongs in one of those directories instead.
4. **The control plane is locked.** Every file is hashed into `HARNESS-LOCK.json` and
   verified inside the quality gate, so an agent cannot quietly relax a policy the
   way it would otherwise relax a threshold.

## Skills and gates are the same vocabulary

> **A skill is a named command. A gate is a skill with a threshold and an evidence
> artifact.**

This unification matters: it means the harness has exactly one executable vocabulary
rather than two that drift. `harness.py gate list` shows the thresholded subset;
`harness.py skill list` shows all of them.

```yaml
skills:
  - id: unit-test              # a skill: runs, returns output
    command: dotnet test --nologo
  - id: coverage-diff          # a gate: skill + threshold + evidence
    command: dotnet test --collect:"XPlat Code Coverage"
    parser: cobertura-diff
    threshold: diffLineRate >= 0.80
    evidence: "{EVIDENCE_DIR}/cov/**/coverage.cobertura.xml"
```

## What gets generated

Counts are the floor for a mature project, not a target to pad toward. The generator
emits a workflow only when the project can actually run it, an agent only when a
workflow invokes it, and a skill only when its command resolves.

| Directory | Typical | Contents |
|---|---|---|
| `workflows/` | 12–15 | task-loop, feature, bugfix, refactor, api-change, security, performance, dependency-upgrade, test-backfill, escalation, milestone, release, session-handoff, project-bootstrap, incident |
| `agents/` | 10–14 | requirements-analyst, spec-writer, task-planner, implementer, test-engineer, code-reviewer *(stack-specialised)*, security-auditor, performance-analyst, api-steward, docs-writer, release-manager, escalation-analyst |
| `skills/` | 15–25 | one per real command the project can run — derived from detected tooling |
| `policies/` | 15–20 | binding rules; each states what it forbids, why, and what enforces it |
| `checklists/` | 5–7 | definition-of-done, api-review, security-review, performance-review, release-readiness |
| `templates/` | 10–12 | requirement, spec, task, ADR, blocked-task, decision-required, milestone-report, session-handoff, incident, test-plan, review-findings |

## Generated layout

```
.agent/
├── harness.yaml                contract: gates, skills, tiers, risk paths
├── harness.lock                tools, thresholds, fingerprint
├── HARNESS-LOCK.json           SHA-256 of EVERY control-plane file
├── instructions.md             entry point; points at task-loop
├── project-context.md
├── PROJECT-STATE.md            live state: phase, open decisions, blockers
├── DECISIONS-REQUIRED.md       the parking lot — see autonomy-boundary.md
├── workflows/
├── agents/
├── skills/
├── policies/
├── checklists/
├── templates/
├── adr/                        accepted architectural decisions
├── knowledge/
├── tasks/                      index.json + journal.jsonl + <ID>.md
├── evidence/
└── tools/
```

`PROJECT-STATE.md` and `DECISIONS-REQUIRED.md` are new and load-bearing.
`PROJECT-STATE.md` is what a fresh session reads to know where things stand;
`DECISIONS-REQUIRED.md` is where an agent parks a question rather than blocking on
it. Without the second file, the prime directive in
`references/autonomy-boundary.md` cannot be honoured.

## Every control-plane file has the same frontmatter

```yaml
---
id: feature
kind: workflow          # workflow | agent | skill | policy | checklist | template
version: 1
appliesTo: [all]        # or profile ids
invokes: [task-planner, implementer, test-engineer, code-reviewer]
requires: [definition-of-done]
binding: [no-weakening, one-task-in-flight, spec-before-code]
---
```

The generator validates this graph: every `invokes` resolves to an agent that
exists, every `requires` to a checklist, every `binding` to a policy.
`harness.py verify` fails on a dangling reference — a workflow naming an agent that
was never generated is exactly the kind of quiet incompleteness that makes a harness
look finished when it is not.

## Completeness is checked, not assumed

`harness.py verify` enforces:

- every control-plane file's SHA matches `HARNESS-LOCK.json`
- every cross-reference resolves
- every skill's command resolves on PATH
- every workflow terminates — reaches done, blocked, or a checkpoint
- every agent named by a workflow exists, and has a declared authority scope
- no orphans: no agent no workflow invokes, no policy nothing binds to
- every task type routable by `task-loop` has a workflow

The orphan check matters most. It is what stops the control plane accumulating
impressive-looking files that nothing ever reads.
