# Syncfusion Pivot Data Server (backend) — Agent Operating Protocol

> **This file governs all work in this project.** It is host-agnostic: capability
> names in CAPITALS (`DELEGATE`, `READ_FILE`, `RUN`) are bound to your specific
> tool by `.agent/adapters/generic.md` (or `claude-code.md` / `codestudio.md`).
> Read the adapter file once at session start.

**Harness**: tier L0 · profile dotnet-aspnetcore + syncfusion overlay · contract v1.0

---

## The one rule everything else serves

**You may narrate. You may not assert.**

For any claim a tool can measure, the tool decides — not you. This is enforced,
not requested: `task.py done` refuses to complete a task without passing gate
evidence whose commit matches HEAD.

## Task loop

1. `RUN` `python3 .agent/tools/task.py pick` — one task in flight
2. Plan against the governing OpenSpec change (`admin-panel-integration`)
3. Implement (spec-first: every behavior traces to a delta spec requirement)
4. `RUN` inner-lane gates → `python3 .agent/tools/harness.py gate run --lane inner --scope diff`
5. Review via `.agent/agents/reviewer.md` (catalog-weighted, hotspot-weighted)
6. Outer-lane evidence for live claims (`verify-https.sh`, `verify-cache.sh`)
7. `RUN` `python3 .agent/tools/task.py done` — refuses without green evidence at HEAD

## What this repo forbids

- Plaintext credentials in tracked files, logs, or API responses (D6)
- Changes to the frozen Phase-1 REST surface (additive deltas only)
- Weakening a gate threshold to pass (`policies/never-weaken.md`)
- Asserting a gate you did not run (`policies/not-run-is-not-pass.md`)
- Implementing without the OpenSpec artifacts (`policies/spec-before-code.md`)

## Risk paths (human approval required)

`**/Security/**` · `**/appsettings*.json` · `**/DataServer/*.json` · `**/*.csproj` ·
`**/Controllers/**` — plus the program decisions parked in `DECISIONS-REQUIRED.md`.