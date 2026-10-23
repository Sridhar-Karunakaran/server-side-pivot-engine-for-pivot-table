# Async Gates: Slow and Cloud-Based Test Systems

The scenario that breaks a naive harness: the test suite runs on a build farm and
takes three hours. A loop that blocks on VERIFY stalls; a loop with a 3-retry cap
burns its retries on a queue wait and blocks the task for no reason.

## Two lanes

Split verification by feedback latency, not by kind.

| Lane | Budget | Contents | When |
|---|---|---|---|
| **inner** | **< 2 min** | build, lint, analyzers, typecheck, *impacted* unit tests | after every subtask |
| **outer** | minutes to hours | full suite, E2E, integration, mutation, leak/perf, cloud runs | once per task |

The inner-lane budget is a hard design constraint, not a target. `harness.py gate
run` prints a warning when a lane exceeds its budget:

```
BUDGET: inner lane took 340s, budget is 120s. Move slow gates to the outer lane
        or make them async - a slow inner lane is how agents learn to skip
        verification.
```

That warning is the point. A slow inner lane trains agents (and people) to batch
work and skip checks, which loses more quality than the extra gates gain.

## The `verifying` state

The loop needs a state for "built, outer gates in flight, not done":

```
active --> verifying --> review --> done
             |
             +--> (gates fail) --> active
```

`verifying` is the only non-terminal state that does **not** block picking new
work — that is what keeps the loop moving during a three-hour run. `maxVerifying`
(default 3) caps how many tasks may be in flight, so failures stay attributable
and the agent does not build fifteen tasks on top of unverified foundations.

Critically: **`verifying` is not `done`.** `task.py done` refuses without a
passing `gate-summary.json` whose commit matches HEAD. A cloud suite that never
returns means the task never completes — which is correct, and visible.

## Protocol

```bash
# 1. inner lane, synchronous, after each subtask
python3 .agent/tools/harness.py gate run --lane inner --scope diff

# 2. submit the outer lane and keep moving
python3 .agent/tools/harness.py gate submit
python3 .agent/tools/task.py verify-submit --job "build-48213"

# 3. pick independent work (dependency graph guarantees independence)
python3 .agent/tools/task.py next

# 4. reconcile when results land
python3 .agent/tools/harness.py gate poll
```

`gate poll` merges async results into the same `gate-summary.json` the
synchronous path writes, so `task.py` has one contract regardless of lane.

## Configuring an async gate

```yaml
lanes:
  inner:
    budgetSeconds: 120
  outer:
    budgetSeconds: 10800
    mode: async

gates:
  - id: full-suite-cloud
    lane: outer
    mode: async
    claim: "the full suite passes on the build farm"
    tool: internal-ci
    submitCommand: ./scripts/ci-submit.sh {TASK} {BASE}
    pollCommand: ./scripts/ci-poll.sh {JOB} {EVIDENCE_DIR}
    command: ./scripts/ci-poll.sh {JOB} {EVIDENCE_DIR}
    parser: junit
    evidence: "{EVIDENCE_DIR}/cloud-junit.xml"
    threshold: failed == 0
    timeoutSeconds: 10800
```

Contract for the two scripts — thin adapters you write once per CI system:

- **`ci-submit.sh`** queues the run for the current commit and prints a job id.
  The harness extracts it by regex, or you pass `--job` explicitly.
- **`ci-poll.sh`** must **exit 75 (`EX_TEMPFAIL`) while the job is still
  running**, and on completion download the result artifact to `{EVIDENCE_DIR}`
  and exit 0. Any output matching `pending|running|queued|in_progress` is also
  treated as not-yet-finished.

Exit 75 is the whole integration surface. Everything else — Azure DevOps, Jenkins,
GitHub Actions, TeamCity, an internal farm — is a detail of those two scripts.

## Test impact selection

The highest-leverage optimisation for teams with hours-long suites: run only the
tests that could be affected by the diff. This is what makes an inner lane
possible at all on a large repo.

Ordered by effort:

1. **Project/package granularity (start here).** Map changed files to their
   owning project or package and run only those test projects. For .NET, derive
   the map from the solution's project references; for a JS monorepo, use the
   workspace graph (`nx affected`, `turbo run --filter=...[HEAD^]`, or
   `pnpm --filter ...`). Cheap, and typically an 80% cut.

2. **Test-name conventions.** `src/Grid/Sorting.cs` → run `*Grid*Sorting*` tests.
   Crude but nearly free, and effective in codebases with disciplined naming.

3. **Coverage-derived mapping.** Run the full suite once with per-test coverage,
   invert it into a line → tests map, cache it, and select from the diff. Most
   precise, needs periodic regeneration. `dotnet-coverage` and Istanbul both
   support per-test attribution.

4. **Built-in impact analysis.** Some runners provide it directly (e.g. Jest
   `--changedSince`, `dotnet test` with a filter driven by option 1).

Record the selection in evidence. A task whose inner lane ran 12 of 4,000 tests
must say so, otherwise `passRate=1.0` reads as far stronger evidence than it is.
The generated gate does this via `--scope`: `diff` selects impacted tests and
labels the evidence accordingly; `full` runs everything.

## Flaky tests

Without explicit handling, flakes block real tasks and burn the retry budget —
and worse, they teach the agent that failures are noise to be retried rather than
signals.

1. **Quarantine list** in `harness.yaml`:
   ```yaml
   flaky:
     quarantine:
       - "Grid.Tests.VirtualScroll_LoadsNextPage"
     retries: 2
     expiresAfterDays: 30
   ```
2. Quarantined tests are **excluded from the blocking threshold** but still run,
   and their results are recorded. They never silently disappear.
3. A quarantine entry older than `expiresAfterDays` becomes a `validate`
   warning, then an error. Quarantine is a holding pen, not a graveyard.
4. Every quarantine addition is a task. The list has an owner and shrinks.
5. **Retries are recorded in evidence.** A test that passed on attempt 2 is
   reported as `retried`, not as `passed`, so flakiness stays visible rather than
   being laundered into a green check.

The distinction that matters: a retry that is *recorded* is data about test
health. A retry that is *hidden* is how a suite rots.
