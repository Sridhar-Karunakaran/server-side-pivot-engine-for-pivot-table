# Loop Protocol

## Architecture

```
┌──────────────────────────────────────────────────────────────────────┐
│ ORCHESTRATOR                                                         │
│   owns task state, specs, plans, gate runs, review dispatch          │
│   stays small: task file + project-context + gate summary per turn   │
│                                                                      │
│   PICK ─ SPEC ─ PLAN ──┐                                            │
│     ▲                   │  DELEGATE (fresh context, full window)     │
│     │        ┌──────────▼─────────────┐                             │
│     │        │ BUILD AGENT            │  implements ## Plan          │
│     │        │  + project-context     │  runs inner gates per subtask│
│     │        │  + cited knowledge     │  returns DONE | BLOCKED      │
│     │        └──────────┬─────────────┘                             │
│     │                   │                                            │
│     │      ┌────────────▼───────────┐                               │
│     │      │ harness.py gate run    │  ◄── the only source of truth  │
│     │      │  command→parser→       │      about whether work is     │
│     │      │  threshold→evidence    │      acceptable                │
│     │      └────────────┬───────────┘                               │
│     │                   │                                            │
│     │        ┌──────────▼─────────────┐                             │
│     │        │ REVIEW AGENT           │  works the defect catalog    │
│     │        │  + gate evidence       │  never re-litigates gates    │
│     │        └──────────┬─────────────┘                             │
│     │                   │                                            │
│     └── LEARN ◄─ DONE ◄─┘   (done refuses without passing evidence)  │
└──────────────────────────────────────────────────────────────────────┘
```

The orchestrator delegates BUILD so its own context never fills, and delegates
REVIEW so the reviewer is not biased by having written the code. But the
load-bearing component is neither: it is the gate runner. Delegation is an
efficiency measure; the gate runner is what makes the output trustworthy.

## States

```
backlog ──promote──> todo ──next──> active ──verify-submit──> verifying
                       ▲              │  │                      │
                       │              │  └──gate run (sync)─┐   │
                  unblock             │                     ▼   ▼
                       │              └──block──> blocked   review ──approve──> done
                       │                                     │                    │
                       └─────────────────reject──────────────┘              archive
```

| State | Blocks new picks? | Meaning |
|---|---|---|
| `backlog` | no | not eligible; must be promoted or explicitly deferred |
| `todo` | no | ready, dependencies met |
| `active` | **yes** (`maxActive`) | a build agent is working |
| `verifying` | **no** (capped by `maxVerifying`) | async outer gates in flight — the loop moves on |
| `review` | yes | gates passed, awaiting review sign-off |
| `blocked` | no | needs external action; reason recorded |
| `done` | no | passing evidence at the current commit |

`verifying` is the state that makes hours-long cloud suites workable: it is the
only non-terminal state that does not block progress. See
`references/async-gates.md`.

## Invariants (enforced in code, not by agent discipline)

1. **At most `maxActive` active tasks.** `task.py next` refuses beyond it.
2. **Dependencies resolve against a durable done-ledger.** Archived tasks stay in
   the ledger, so a task depending on an archived one is still pickable. (In the
   previous generation, archiving silently deadlocked every dependent task.)
3. **Task ids are monotonic and never reused.** A persisted `nextId` counter, not
   `max(existing)+1` — which reused ids after archiving.
4. **`--needs` validates existence and rejects cycles.** A typo cannot create a
   permanently unpickable task.
5. **The index is written atomically** with a one-generation backup, so a crash
   mid-write cannot destroy the state the whole design depends on.
6. **`done` requires passing evidence at the current commit.** Stale evidence from
   before the last commit is rejected.
7. **Risk-gated paths require a named human approver.** No gate result overrides
   this.
8. **Every transition is journalled** to `tasks/journal.jsonl`, including every
   `--override` and its reason.

Run `task.py doctor` to check 1–5 and `harness.py doctor` for the rest.

## Stages

**PICK** — `task.py next`. Read its output carefully: "nothing eligible" has four
distinct causes (unmet dependencies, deadlock behind a blocked task, async gates
pending, backlog only) and each needs a different response.

**SPEC** — `## What`: scope, boundaries, and acceptance criteria that a tool or a
test can check. "Handles errors gracefully" is not a criterion.

**PLAN** — `## Plan`: dependency-ordered subtask checkboxes, each one commit's
worth. Each behaviour-adding subtask names the test that will cover it; if you
cannot name it, the subtask is not specified.

**BUILD** — delegated. Prompt carries project-context, cited knowledge, the spec,
the plan, the inner gate command, and the explicit prohibition on weakening gates.

**INNER GATES** — after every subtask, budget ~120s. Failure output is actionable:
it names the uncovered line, the surviving mutant, the analyzer rule id.

**OUTER GATES** — once per task. Sync, or async via `gate submit` / `gate poll`.

**REVIEW** — delegated to the generated reviewer with the gate evidence attached.
It works the framework defect catalog and must not re-litigate the gates.

**DONE** — `task.py done`. Refuses without evidence; halts on risk-gated paths.

**LEARN** — `## Log`, a line in `progress.md`, `project-context.md` if the
architecture moved, and occasionally `harness.py ratchet`.

## Autonomous completion

The loop does not stop when `todo` empties. Completion requires all of:

1. every `todo` task `done`
2. every `backlog` item promoted **or explicitly deferred with a reason** —
   `task.py defer <id> "<reason>"`. An unevaluated backlog is not completion, and
   a deferral with no reason is not a decision.
3. full-scope gates green: `harness.py gate run --lane all --scope full`
4. `harness.py doctor` healthy
5. the goals in `progress.md` satisfied

Task discovery during BUILD and REVIEW is expected: missing error handling, an
untested edge case, a needed dependency. Add them with `task.py add "..." --needs
T-current` and the loop picks them up.

## Rollback

`task.py next` records the HEAD commit as the task's `baseSha`. That anchor serves
three purposes: it scopes every diff gate, it scopes the review, and it enables

```bash
python3 .agent/tools/task.py rollback T-042            # dry run: shows what is lost
python3 .agent/tools/task.py rollback T-042 --force
```

This is why git is a hard prerequisite rather than an optional stage. An
autonomous loop without rollback has no way to recover from a build agent that
went twelve files off course.
