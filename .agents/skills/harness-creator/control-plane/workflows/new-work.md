---
id: new-work
kind: workflow
version: 1
appliesTo: [all]
invokes: [task-planner]
requires: []
binding: [one-task-in-flight, spec-before-code, autonomy-boundary]
---

# new-work — turning a user request into a tracked task

This micro-workflow bridges the gap between a user saying "do X" and the task loop
having a properly specced, trackable unit of work. It runs **before** routing to
`feature`, `bugfix`, or any typed workflow.

The rule is simple: **never work outside a task.** The task is what produces the
evidence trail, and the evidence trail is what makes `task.py done` succeed.

## When to use

- The user requests a feature, fix, or change that is not already a task
- The user describes work during a conversation and expects the agent to act on it
- A review, incident, or investigation surfaces work that needs its own task

## The workflow

### 1. CLASSIFY — what kind of work is this?

Before creating a task, determine the type. This decides which workflow the task
loop will route it to:

| Signal | Type | Routes to |
|---|---|---|
| New capability, user-facing behaviour | feature | `feature` |
| Something is broken, wrong output, crash | defect | `bugfix` |
| Structure change, no behaviour change | refactor | `refactor` |
| Public API signature change | api-change | `api-change` |
| Vulnerability, auth, secrets, crypto | security | `security` |
| Performance: latency, memory, bundle | performance | `performance` |
| Package or framework version bump | dependency-upgrade | `dependency-upgrade` |
| Adding tests to untested code | test-backfill | `test-backfill` |

If the request is ambiguous, ask one clarifying question — not a questionnaire.

### 2. CREATE — register the task

```bash
python3 .agent/tools/task.py add "<clear one-line title>" [--needs T-XXX] [--backlog]
```

Guidelines:

- **Title**: action-oriented, specific. "Add dark mode toggle to settings page" not
  "dark mode".
- **Dependencies** (`--needs`): if this task cannot start until another finishes.
- **Backlog** (`--backlog`): if the work is not urgent and should wait for
  prioritisation. Without `--backlog`, the task enters the ready queue immediately.

### 3. SPEC — write `## What` in the task file

Open the task file created by `task.py add` and write:

- **Behaviour**: what changes, from the user's perspective
- **Scope boundary**: what this task explicitly does *not* cover
- **Acceptance criteria**: each one checkable by a tool or a test
- **Type**: the classification from step 1

> A task without a spec is not a task. `task.py next` will pick it, but every
> downstream workflow begins with SPEC — and an empty spec means the agent has to
> stop and write one before doing anything else. Writing it now saves a round trip.

### 4. ENTER THE LOOP

```bash
python3 .agent/tools/task.py next
```

The task loop takes over:

- **SELECT** picks the newly created task (or the highest-priority ready task)
- **ROUTE** sends it to the typed workflow based on `## What`
- **EXECUTE** runs the workflow: SPEC → PLAN → BUILD → GATES → REVIEW → DONE

From here, follow the workflow — `feature.md`, `bugfix.md`, etc. This micro-workflow
is done.

## Multiple requests at once

If the user describes several pieces of work in one message:

1. Create a task for **each** distinct unit of work
2. Set dependencies between them where order matters
3. Run `task.py next` — it picks the first eligible one
4. Complete that task before starting the next

Never combine unrelated work into a single task. Each task produces its own evidence
trail, its own review, and its own commit history.

## When NOT to create a task

- **Answering a question** — if the user asks "how does X work?" and no code change
  is needed, answer directly. Tasks are for work, not conversation.
- **Reading or explaining code** — no task needed for exploratory reading.
- **Harness maintenance** — `doctor`, `verify`, `gate list` are operational, not tasks.

## Anti-patterns

1. Starting to code immediately on a user request without creating a task first
2. Creating a task with a vague title like "fix stuff" or "improvements"
3. Combining multiple unrelated changes into one task
4. Creating a task and skipping the spec
5. Adding to the backlog when the user clearly wants it done now
