# APM Export — reproducing the harness anywhere

Microsoft's [Agent Package Manager](https://github.com/microsoft/apm) is a dependency
manager for AI agent configuration: a manifest (`apm.yml`) plus a lockfile
(`apm.lock.yaml`), the npm/cargo shape applied to the files that configure coding
agents. `apm install` resolves the tree; `apm compile` writes the harness-native files
each tool already reads.

That is exactly the distribution problem this harness has. Cloning `.agent/` between
repositories means every copy drifts. Publishing it as an APM package means one
versioned source and `apm install` everywhere.

All schema details below were retrieved 2026-09-01 from
`microsoft.github.io/apm` and the `microsoft/apm` repository; `harness.py apm-export`
records the retrieval in the generated manifest.

## Why this replaces the vendor adapters

APM's `target` list covers `copilot`, `claude`, `grok-build`, `cursor`, `kiro`,
`gemini`, `opencode`, `codex`, `windsurf`, and `agent-skills`, and `apm compile`
writes `.github/`, `.claude/`, `.cursor/`, `.codex/` and the rest.

So the hand-written wrappers in `templates/wrappers/` become a fallback for projects
not using APM, and APM becomes the preferred path. One manifest replaces three
pointer files and the adapter directory.

## The mapping

`.agent/` maps onto APM's primitives almost exactly, because both are answering the
same question:

| harness-creator | APM primitive | Path in the package |
|---|---|---|
| `.agent/policies/*.md` | instructions — always-on rules | `.apm/instructions/` |
| `.agent/agents/*.md` | agents | `.apm/agents/<id>.agent.md` |
| `.agent/workflows/*.md` | prompts — invocable procedures | `.apm/prompts/<id>.prompt.md` |
| `.agent/tools/` + `scripts/` | skill, with sibling files | `.apm/skills/harness/SKILL.md` + siblings |
| `.agent/checklists/`, `templates/`, `knowledge/`, `design.md` | context — referenced, not standalone | `.apm/context/` |
| `.agent/harness.yaml`, `HARNESS-LOCK.json` | ship inside the skill | `.apm/skills/harness/` |
| MCP servers | `dependencies.mcp` | `apm.yml` |
| vendor wrappers | `targets` + `apm compile` | generated, not authored |

Three notes on the mapping, because the fit is not perfect:

- **Policies become instructions.** APM instructions attach to file globs, which is
  stronger than a policy file nothing points at — the `appliesTo` frontmatter becomes
  the glob.
- **Workflows become prompts** because they are invocable procedures, not always-on
  rules. `apm run feature` then routes work the way `task-loop` does.
- **`.agent/tasks/`, `evidence/`, `PROJECT-STATE.md` are never packaged.** They are
  per-repository state, not configuration. Packaging them would carry one project's
  task list into another.

## Two shapes, and which to pick

**Shape A — one harness package (start here).** The whole control plane as a single
versioned package. `apm install` in a new repo, then `harness.py` regenerates the
project-specific parts.

**Shape B — a package per concern.** Split into `harness-core`,
`harness-profile-blazor`, `harness-overlay-syncfusion`. More work, and worth it once
several teams consume different subsets — a WinForms team should not install Angular
knowledge.

Start with A. Split when a second team wants a different subset, not before.

## Generated manifest

```yaml
name: syncfusion-harness-blazor
version: 1.0.0
description: Verification harness and control plane for Syncfusion Blazor projects
author: Syncfusion
license: MIT
type: hybrid
targets: [claude, copilot, cursor, codex, windsurf, agent-skills]
includes: auto

scripts:
  doctor:  "python3 .apm/skills/harness/tools/harness.py doctor"
  verify:  "python3 .apm/skills/harness/tools/harness.py verify"
  gate:    "python3 .apm/skills/harness/tools/harness.py gate run --lane {lane}"
  next:    "python3 .apm/skills/harness/tools/task.py next"

dependencies:
  apm:
    # the vendor's own skills, installed narrowly - not the full 72-skill pack
    - syncfusion/agent-onboarding-skill
    - git: https://github.com/syncfusion/blazor-ui-components-skills.git
      skills: [syncfusion-blazor-datagrid, syncfusion-blazor-charts,
               syncfusion-blazor-scheduler, syncfusion-blazor-pdf-viewer]
  mcp:
    - name: sf-blazor-mcp
      registry: false
      transport: stdio
      command: dnx
      args: ["Syncfusion.Blazor.MCP", "--yes"]
      env:
        Syncfusion_API_Key_Path: ${env:Syncfusion_API_Key_Path}

compilation:
  target: all
  strategy: distributed
  exclude:
    - "apm_modules/**"
    - ".agent/evidence/**"

policy:
  fetch_failure_default: block
```

`fetch_failure_default: block` rather than `warn` is deliberate: a harness that
silently installs without its knowledge sources is the failure this whole system
exists to prevent. Better to fail the install.

Note `env: ${env:...}` — the manifest references the variable, never the value. The
same rule as everywhere else: the harness does not handle keys.

## What `apm-export` does

```bash
python3 .agent/tools/harness.py apm-export --name syncfusion-harness-blazor --version 1.0.0
```

1. Builds `.apm/` and copies each primitive to its APM path, rewriting cross-references
2. Converts each policy's `appliesTo` into an instruction glob
3. Writes `.apm/skills/harness/SKILL.md` with `tools/`, `scripts/`, `harness.yaml` and
   `HARNESS-LOCK.json` alongside
4. Writes `apm.yml` with targets, scripts, MCP servers, and vendor skill dependencies
5. **Excludes** `tasks/`, `evidence/`, `PROJECT-STATE.md`, `DECISIONS-REQUIRED.md`
6. Verifies every cross-reference still resolves at the new paths
7. Prints exactly what was excluded and why

## Consuming it in another repository

```bash
apm install                                     # resolve the manifest
apm compile                                     # write .claude/, .github/, .cursor/
python3 apm_modules/.../harness.py doctor       # confirm tools resolve here
python3 apm_modules/.../harness.py lock         # re-lock: different repo, different tools
```

The re-lock matters. `harness.lock` records *this* repository's tool versions,
dependency fingerprint, and ratchet floors. Carrying another repo's lock across
produces a harness that reports drift immediately and, worse, imports the wrong
ratchet floors.

**What is shared** — control plane, gate definitions, knowledge, design rules, agents,
workflows, policies.
**What is per-repository** — `harness.lock`, tasks, evidence, `PROJECT-STATE.md`,
ratchet floors, tier.

The tier is the one people get wrong: a mature repo at L3 and a new one at L1 can share
every definition while enforcing different thresholds, because the tier lives in the
consuming repo's `harness.yaml`, not in the package.

## Keeping consumers current

```bash
apm update && apm compile && harness.py verify
```

`verify` after `compile` is the important half: the control plane arrives with new
SHAs, so re-locking is a deliberate act that shows the diff rather than absorbing it.

## Honest limits

- The mapping above is derived from APM's documented schema and package anatomy. It has
  **not** been round-tripped through a real `apm install` — do that on one repository
  before rolling it out.
- APM is young. Pin the CLI version in CI.
- `apm compile` output for Code Studio specifically is not in APM's target list, so the
  `.codestudio/` pointer wrapper is still generated for that host.
