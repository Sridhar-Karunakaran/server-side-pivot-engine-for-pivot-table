# Host Adapters: Vendor-Neutral by Construction

The original harness hardcoded `runSubagent` — Code Studio's API — into the
generated protocol. That protocol is then wrong in Claude Code, Cursor, Codex,
and Copilot. A harness that only works in one tool cannot be an organisation's
standard.

## The split

**`.agent/instructions.md` is host-agnostic.** It names abstract capabilities in
CAPITALS and never a tool-specific API:

| Capability | Meaning |
|---|---|
| `DELEGATE` | run a sub-task in a fresh context and return its result |
| `READ_FILE` | read a file |
| `RUN` | run a shell command |
| `PARALLEL` | run independent work concurrently (optional) |

**`.agent/adapters/<host>.md` binds them.** Small files, one per host.

**Root pointer files** tell each tool where to look. They are pointers, not
copies — duplicated protocol text drifts, and then two tools follow two
different protocols in the same repo.

## codestudio.md

```markdown
# Host Adapter — Code Studio

| Capability | Binding |
|---|---|
| DELEGATE   | `runSubagent(prompt: "...", description: "...")` |
| READ_FILE  | `read_file` |
| RUN        | shell tool |
| PARALLEL   | not available — delegate sequentially |

## DELEGATE shape

runSubagent(
  prompt: "<the BUILD prompt assembled per instructions.md §BUILD>",
  description: "Build T-XXX: <title>"
)

Returns the sub-agent's final text. Expect `DONE: <summary>` or
`BLOCKED: <reason>` as instructed by the BUILD prompt.
```

## claude-code.md

```markdown
# Host Adapter — Claude Code

| Capability | Binding |
|---|---|
| DELEGATE   | `Agent` tool, `subagent_type: "general-purpose"` |
| READ_FILE  | `Read` |
| RUN        | `Bash` |
| PARALLEL   | multiple `Agent` calls in one message |

## DELEGATE shape

Agent(
  description: "Build T-XXX",
  prompt: "<the BUILD prompt assembled per instructions.md §BUILD>",
  subagent_type: "general-purpose"
)

Notes
- A generated reviewer lives at `.agent/agents/reviewer.md`. If the project
  registers it as a custom agent type, use that type for REVIEW; otherwise pass
  its content as the prompt preamble.
- Claude Code may already provide a `/code-review` skill. It is complementary,
  not a substitute: it does not know this project's defect catalog or its gate
  evidence.
```

## generic.md

```markdown
# Host Adapter — Generic (Cursor, Codex, Copilot, or no delegation)

| Capability | Binding |
|---|---|
| DELEGATE   | none available — see below |
| READ_FILE  | the host's file read |
| RUN        | the host's terminal |
| PARALLEL   | not available |

## No delegation available

Run BUILD inline, with two compensating rules, because a single context doing
orchestration and building will otherwise degrade:

1. **One task per session.** Complete PICK through LEARN, then start a fresh
   session. The task file and progress.md carry the state across, which is what
   they are for.
2. **Re-read `.agent/instructions.md` and `.agent/project-context.md` at the
   start of every session.** Without delegation there is no fresh-context
   guarantee, so this is manual.

The gate system is unaffected: `harness.py gate run` and `task.py done` behave
identically. Verification does not depend on delegation — which is the main
argument for keeping evidence in files rather than in an agent's context.
```

## Pointer files

`.codestudio/codestudio-instructions.md`:

```markdown
# {{PROJECT_NAME}} — Agent Protocol

This project uses a vendor-neutral agent harness. The protocol is NOT in this
file — it is at `.agent/instructions.md`, so every agent tool follows the same
rules.

## Do this now

1. READ `.agent/instructions.md`      — the operating protocol
2. READ `.agent/adapters/codestudio.md` — how capabilities map to Code Studio
3. READ `.agent/project-context.md`   — stack, architecture, conventions, boundaries
4. RUN  `python3 .agent/tools/task.py status`
5. RUN  `python3 .agent/tools/harness.py doctor`  (first session, or after a pull)

Do not implement anything before completing those steps. In particular, do not
mark work complete: `task.py done` requires passing gate evidence, and that is
deliberate.
```

`CLAUDE.md` and `AGENTS.md` are the same 12 lines with a different adapter named.
`AGENTS.md` covers Codex, Cursor, and Copilot, which all read it.

## Why a `.codestudio/` wrapper at all

Because Code Studio users have muscle memory and existing tooling pointed at that
path. The wrapper preserves the entry point while moving the substance to
`.agent/`. Two properties make it safe:

- **The wrapper contains no protocol content.** It cannot drift from the canonical
  protocol because it does not restate it.
- **The wrapper is regenerated on upgrade**, and it is 12 lines, so there is
  nothing in it worth preserving.

Adding a host later means adding one adapter file and one pointer. It does not
mean touching the protocol, the gates, or the scripts.
