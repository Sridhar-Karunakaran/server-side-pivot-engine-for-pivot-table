---
id: _catalog
kind: skill-catalog
version: 1
---

# Skill catalog

> **A skill is a named command. A gate is a skill with a threshold and an evidence
> artifact.**

One executable vocabulary, not two that drift. Skills live in `harness.yaml`
alongside gates; `harness.py skill list` shows all of them, `gate list` shows the
thresholded subset.

## The test for a skill

If it cannot be executed, it is not a skill. Prose belongs in a policy, a procedure
belongs in a workflow, and a list of things to confirm belongs in a checklist. A
directory of "skills" that are really documents is the most common way this layer
becomes decorative.

## Shape

```yaml
skills:
  - id: unit-test
    claim: "the test suite runs"          # what invoking this establishes
    command: dotnet test --nologo
    usedBy: [implementer, test-engineer]  # which agents may invoke it
    # no threshold -> a skill, not a gate
```

Adding `parser`, `threshold` and `evidence` promotes it to a gate. Nothing else
changes.

## Universal skills

Generated for every project, with the command filled from the detected stack.

| Skill | Claim | Used by |
|---|---|---|
| `build` | the project compiles | implementer, release-manager |
| `unit-test` | the suite runs | implementer, test-engineer |
| `test-one` | a single named test runs — the tight inner loop | implementer, test-engineer |
| `lint` | static analysis runs | implementer |
| `format` | formatting is applied or checked | implementer |
| `typecheck` | types resolve | implementer |
| `coverage` | coverage is measured | test-engineer |
| `coverage-diff` | changed lines are covered **(gate)** | test-engineer |
| `mutation-test` | tests are sensitive **(gate)** | test-engineer |
| `secret-scan` | no committed secrets **(gate)** | security-auditor |
| `deps-audit` | no known-vulnerable dependencies **(gate)** | security-auditor |
| `sast` | no insecure patterns **(gate)** | security-auditor |
| `knowledge-lookup` | retrieve a cited rule from `.agent/knowledge/` | all |
| `arch-map` | regenerate the dependency graph | task-planner, code-reviewer |
| `hotspots` | list high-churn files from git history | code-reviewer, task-planner |
| `quality-gate` | run the whole inner lane | implementer |
| `full-gate` | run every gate at full scope | release-manager |
| `harness-verify` | control plane matches `HARNESS-LOCK.json` | all |

`test-one` earns its place: without it an agent runs the whole suite to check one
assertion, which is how the inner lane blows its budget.

## Stack-derived skills

Emitted only when the tool resolves on PATH.

| Stack | Skills |
|---|---|
| .NET | `analyzers`, `api-baseline`, `banned-api`, `gcdump`, `benchmark`, `ef-warnings` |
| JS / TS | `bundle-size`, `dead-code`, `circular-deps`, `a11y-scan`, `visual-diff`, `memlab`, `lighthouse` |
| Web (any) | `browser-run`, `screenshot`, `console-errors` |
| Documents | `document-validate`, `roundtrip-test` |
| Syncfusion | `syncfusion-license-check`, `syncfusion-version-check`, `syncfusion-components` |

## Independent-oracle skills

The most valuable ones, and the ones that need naming explicitly, because they are
what lift evidence above rank 4 (`references/oracles.md`).

| Skill | Why it is stronger than your own tests |
|---|---|
| `schema-validate` | a validator that did not produce the artifact |
| `roundtrip-test` | a **different** implementation reads what you wrote |
| `consumer-compile` | a real consumer project compiles against your API |
| `browser-run` | the actual runtime, not a jsdom approximation |
| `document-validate` | a vendor validator independent of your writer |

Where a stack has none of these available, the generator records the oracle ceiling
in `project-context.md` and says so plainly rather than implying stronger evidence
than exists.

## Generation rules

1. Emit a skill only when its command resolves. A skill that always errors trains
   everyone to ignore skill output.
2. Every skill declares `usedBy`. A skill no agent uses is an orphan, and
   `harness.py verify` reports it.
3. Every agent's `uses:` must resolve to a skill that exists. `verify` fails on a
   dangling reference.
4. Promote to a gate whenever a meaningful threshold exists. A skill that is run but
   never thresholded produces output nobody acts on.
