# Tiers and the Ratchet

Most teams cannot start at full verification, and pretending otherwise is why
harnesses get abandoned. The tier system lets a project start where it actually
is, states honestly what has and has not been verified, and provides a mechanism
that only moves in one direction.

## The tiers

| Tier | Name | Gates | What the harness promises |
|---|---|---|---|
| **L0** | Unverified | build, lint, analyzers, secrets | *Nothing about behaviour.* Code compiles and is statically clean. No test evidence exists. |
| **L1** | Diff-verified | + test execution, **diff coverage** | Changed lines are tested and the suite is green. Legacy code is untouched by the bar. |
| **L2** | Verified | + full-suite, dependency CVEs, full coverage floor | The whole project is green, not just the diff. |
| **L3** | Hardened | + **diff mutation**, leak/perf/a11y budgets, arch rules | Tests are meaningful, not merely present. Runtime properties are measured. |

`harness.py tier` prints the current tier, its gates, and exactly what the next
tier requires. `harness.py validate` **fails** if the declared tier's obligations
are not met — the tier is a claim about what has been verified, so it cannot be
aspirational.

## L0 is a real tier, not a failure state

A brownfield repo with no tests, or a greenfield repo on day one, is L0. The
harness does three things there that matter:

1. **Labels output honestly.** Every task file gets an `UNVERIFIED` marker and
   `progress.md` records it. Nobody downstream can mistake L0 output for
   verified work.
2. **Refuses to pretend.** `harness.py explain "tests pass"` exits non-zero.
   There is no gate, so the agent must state the claim as unverified rather than
   assert it.
3. **Forces the exit.** At L0 the harness requires every task to add
   **characterization tests** for the code it touches — tests that pin current
   behaviour, whatever that behaviour is. This is the only test-writing strategy
   that works on undocumented legacy code, and it converts L0 into L1 as a side
   effect of doing normal feature work, with no separate "testing project" that
   never gets funded.

The characterization-test obligation is enforced, not suggested: at L0 the
harness adds a `characterization` gate requiring `tests > 0` growth on changed
modules, so a task that adds code without pinning behaviour fails.

## Why diff coverage, not global coverage

This is the single most important design decision for brownfield repos.

Global coverage on a fifteen-year-old WinForms codebase might be 4%. Setting a
global threshold has exactly two outcomes: the number is unreachable so the gate
is disabled, or the number is set to 4% so the gate is meaningless. Either way
you get no enforcement.

Diff coverage asks a different, answerable question: **of the lines this task
changed, how many are covered?** That is enforceable at 80% from day one on any
codebase, because it only ever concerns code that was just written — by an agent
that could have written a test for it.

```
Global coverage  4%   -> unenforceable, demoralizing, gamed
Diff coverage   80%   -> enforceable today, improves the codebase monotonically
```

Mechanically: `parsers.py` intersects the coverage report's per-line hit data
with the added/modified lines from `git diff -U0 <baseSha>..HEAD`. Lines that are
not executable (blanks, braces, comments) are excluded because they are absent
from the coverage report. The failure message names the exact uncovered lines,
so the remediation is unambiguous:

```
FAIL coverage-diff   diffLineRate=0.4444
     Add tests covering these changed lines (they are yours, not legacy):
         src/calc.py:12
         src/calc.py:16
```

The same logic applies to mutation testing: `Stryker.NET --since` and
`StrykerJS --since` mutate only changed code, which is what makes mutation
testing affordable on a large codebase at all.

## The ratchet

A threshold that can move down is not a threshold. The ratchet makes bars
monotonic:

1. Any gate marked `ratchet: true` gets its threshold recorded as a **floor** in
   `harness.lock` when you run `harness.py lock`.
2. `harness.py validate` **fails** if `harness.yaml` later declares a threshold
   below that floor. This runs in CI, so lowering a bar cannot land quietly.
3. `harness.py ratchet` reads accumulated evidence and proposes raising
   thresholds a team already clears — turning achieved quality into enforced
   quality.
4. Lowering a floor requires an explicit, justified command:
   `harness.py lock --lower-floor <gate> --reason "..."`, and the lock keeps
   `loweredFrom` and `reason` permanently.

```
$ harness.py validate
FAIL  gate 'coverage-diff': threshold diffLineRate >= 0.3 is below the ratchet
      floor diffLineRate >= 0.8 recorded in harness.lock. Quality bars ratchet
      up, not down.
```

This matters more with agents than with humans. An agent under instruction to
"make the gates pass" will find that editing the threshold is the cheapest path.
The ratchet closes it, and the journal records every attempt.

## Anti-gaming

Agents optimise for the stated objective, so the harness names the cheats and
blocks the mechanical ones:

| Cheat | Countermeasure |
|---|---|
| Lower the threshold | ratchet floor in the lock; `validate` fails in CI |
| Delete or `[Skip]` the failing test | `tests` metric is thresholded on count, not just failures; skip counts are gated |
| Assert nothing (`assert x is not None`) | mutation gate — a mutant survives, so the score drops |
| Write a test that only touches lines | diff coverage passes but mutation catches it; this is precisely why L3 exists |
| Add `# pragma: no cover` / `ExcludeFromCodeCoverage` | exclusion directives in the diff are flagged by the review agent and counted as a gate metric |
| Suppress the analyzer inline | suppressions in the diff require a written justification; the review agent checks for one |
| Override the gate | `--override` requires a reason and is written to `journal.jsonl` |
| Mark the task done by editing `index.json` | agents are instructed never to; `task.py doctor` detects inconsistency, and the journal has no matching event |

None of these is airtight against a determined adversary. All of them are airtight
against the default behaviour of an agent taking the cheapest path, which is the
actual threat.

## Choosing a starting tier

| Situation | Start at | First move |
|---|---|---|
| Greenfield | L1 | TDD from the first task; diff coverage is trivially satisfiable |
| Brownfield, good tests | L2 | add diff coverage, then mutation on changed code |
| Brownfield, some tests | L1 | diff coverage at 70%, ratchet to 80% |
| Brownfield, no tests | L0 | characterization gate on; target L1 in a month |
| Legacy, no build automation | L0 | build gate only; getting to a reproducible build *is* the first task |

Set the tier deliberately and let the ratchet do the rest. A team that starts at
L1 and ratchets reaches better real-world quality in a quarter than a team that
declares L3 on day one and turns it off in week two.
