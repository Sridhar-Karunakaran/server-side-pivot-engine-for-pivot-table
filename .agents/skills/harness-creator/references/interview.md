# The Interview

Detection gets most of the way. The interview covers what detection cannot see: what
the team *wants*, which tool they use where nothing is configured yet, and the things
they need that no profile anticipated.

Runs **after SCAN, before PROPOSE**. Its output feeds the proposal.

## The three rules

1. **Never ask what you detected.** If `playwright.config.ts` exists, do not ask about
   E2E. Say what you found and move on. Asking questions the repo already answers is
   the fastest way to lose someone's patience.
2. **Every question is skippable, and states its default.** The user should be able to
   press enter through the whole thing and get a sane harness. A question with no
   stated default is a question that stalls.
3. **One question at a time.** Each answer changes what to ask next — a wall of
   questions gets skimmed and half-answered.

## Report detection first

Open by showing what you already know, so the user can correct it and can see why the
remaining questions are the remaining questions:

```
DETECTED — no questions needed for these
  test runner        xUnit                  3 projects, 412 tests
  coverage           coverlet               currently 34%
  linting            none configured        <-- will ask
  E2E                Playwright             e2e/ · 18 specs
  memory profiling   none configured        <-- will ask
  perf profiling     none configured        <-- will ask
  security scanning  none configured        <-- will ask
  a11y               none configured        <-- will ask
  design system      Tailwind + tokens.css  <-- will extract, not ask
  CI                 Azure DevOps           azure-pipelines.yml

5 questions. All skippable — press enter to take the default.
```

## The scenario matrix

One question per unconfigured concern. Ask **only** the rows that came back
undetected.

| Concern | Detection signals | If undetected, ask | Default on skip |
|---|---|---|---|
| **Unit testing** | test project, `*.test.*`, `conftest.py` | which runner? | stack idiomatic (xUnit / Vitest / pytest) |
| **Coverage** | coverlet, c8, coverage.py | measure coverage? | yes, diff-scoped at 80% |
| **Linting** | eslint config, `.editorconfig`, analyzers | which linter, and how strict? | stack default at `error`, ratcheting |
| **E2E** | playwright, cypress, selenium | which E2E tool, and does it run in CI? | Playwright, outer lane |
| **Memory profiling** | gcdump usage, memlab, leak tests | do leaks matter here, and which tool? | offer at L3; `dotnet-gcdump` / `memlab` |
| **Perf profiling** | BenchmarkDotNet, k6, Lighthouse | which perf concern binds — latency, memory, bundle, throughput? | bundle + Lighthouse for UI; BenchmarkDotNet for libraries |
| **Security scanning** | semgrep, gitleaks, CodeQL config | SAST and secret scanning? | Semgrep OSS + Gitleaks, blocking |
| **Dependency CVEs** | dependabot, renovate, audit in CI | which scanner? | OSV-Scanner |
| **Accessibility** | axe, pa11y, Accessibility Insights | is a11y in scope, and to which standard? | axe-core at WCAG AA, non-blocking |
| **Visual regression** | percy, chromatic, screenshot tests | visual regression testing? | Playwright screenshots (free, local) |
| **Mutation testing** | stryker config, mutmut | *explain it*, then ask | offer at L3, diff-scoped |
| **API surface** | PublicAPI.*.txt, api-extractor | is this a library others consume? | yes if a package is published |
| **Architecture rules** | ArchUnit, dependency-cruiser config | enforce layer boundaries? | dependency graph only, no rules yet |
| **CI system** | pipeline files | where do gates run, and how long does the suite take? | local only; ask before assuming async |
| **Design system** | see `references/design-system.md` | **never ask — extract it** | — |

### Ask about the concern, not the tool

Someone who has not set up memory profiling does not know whether they want
`dotnet-gcdump` or `dotnet-counters`. They do know whether their app leaks.

| Bad | Good |
|---|---|
| "Do you want to use Stryker.NET?" | "Should the harness check that your tests actually catch bugs, not just run the code? (mutation testing — adds ~25 min to the outer lane)" |
| "Configure memlab?" | "Does this app run long enough in one session for a memory leak to matter?" |
| "Which SAST tool?" | "Should the harness block a commit that introduces an injection risk?" |

Then pick the tool yourself, from `references/tools-of-record.md`, and name it in the
proposal with its licence.

### Teach in one line, or not at all

When asking about something a user is unlikely to know, one sentence on what it buys
them and what it costs. Then move on — the interview is not a tutorial.

> **Mutation testing** — deliberately breaks your source and checks whether any test
> notices. It is the only thing that distinguishes a real test from
> `Assert.NotNull(result)`. Costs ~25 min on the outer lane. Add it? [y/N]

## The three questions that change the harness most

Ask these even when partially detected, because detection cannot see intent.

**1. Suite duration and where it runs.**
> "How long does your full test suite take, and where does it run?"

This decides the inner/outer lane split and whether outer gates go async. Getting it
wrong makes the harness feel slow, which is how it ends up disabled.

**2. What must never break.**
> "What is the one thing in this codebase that must never break?"

This becomes the risk-gated path list and the first candidate for characterization
tests. The answers are always specific and never guessable — *"the licence key
validation"*, *"the Excel export"*, *"the tenant isolation query"*.

**3. What has bitten you before.**
> "What kind of bug has reached your customers from this codebase?"

Every answer maps to a gate. "Memory leaks in the grid" → leak gate at L3. "We shipped
an unlicensed build" → licence gate. "A breaking API change" → API baseline gate.

This is the highest-yield question in the interview, because it turns lived pain into
enforcement.

## The open-ended question — always ask it

> "Anything else you want the harness to do? It can generate custom skills, gates, or
> workflows — for example a log extractor for your error tracker, an internal API
> client, a deployment check, or a report generator."

Give examples. "Any other requirements?" gets "no"; the version with examples gets
real answers, because it shows the shape of an answerable request.

### Generating a custom skill

Worked example — the user asks for a Sentry log extractor.

**Step 1 — clarify, in at most three questions.** Never assume an API shape.

> - Which Sentry: sentry.io or self-hosted? (changes the base URL)
> - What should it fetch: unresolved issues, a specific issue's events, or a release's
>   regressions?
> - Should it write findings into the harness as tasks, or just report?

**Step 2 — classify it.** This determines where it goes:

| If it… | It is a… | Lives in |
|---|---|---|
| runs a command and returns output | **skill** | `harness.yaml` `skills:` |
| runs a command with a pass/fail threshold | **gate** | `harness.yaml` `gates:` |
| is a multi-step procedure | **workflow** | `.agent/workflows/` |
| is a binding rule | **policy** | `.agent/policies/` |
| needs live API access at agent runtime | **MCP server** | `apm.yml` `dependencies.mcp` |

A Sentry extractor that fetches and reports is a **skill**. One that fails the build
when a new unresolved error appears for the current release is a **gate**.

**Step 3 — check for an existing MCP server first.** If the vendor publishes one, wire
that instead of writing a script. A maintained server beats a bespoke curl wrapper.

**Step 4 — generate it complete.** For Sentry:

```yaml
# .agent/harness.yaml
skills:
  - id: sentry-issues
    claim: "unresolved Sentry issues for the current release"
    command: ./scripts/sentry-issues.sh
    usedBy: [incident, escalation-analyst, release-manager]

gates:
  - id: sentry-clean-release
    tier: L2
    lane: outer
    claim: "no new unresolved errors for the release under test"
    tool: sentry
    license: "n/a (generated script; Sentry's own plan applies)"
    command: ./scripts/sentry-issues.sh --release {RELEASE} --json {EVIDENCE_DIR}/sentry.json
    parser: jsonpath
    evidence: "{EVIDENCE_DIR}/sentry.json"
    parserArgs:
      paths:
        unresolved: unresolved_count
    threshold: unresolved == 0
    blocking: false
    installHint: "set SENTRY_AUTH_TOKEN and SENTRY_ORG in the environment"
    rationale: >
      Production errors are evidence the test suite did not produce. Wiring them into
      the release gate closes the loop between what shipped and what broke.
```

Plus `scripts/sentry-issues.sh` — reading the token **from the environment, never a
literal**, failing with an actionable message when it is unset, and emitting JSON the
parser can read.

**Step 5 — wire it in.** Add `usedBy`, reference the skill from the workflows that need
it (`incident`, `release`), and re-run `cp-lock`. A skill nothing references is an
orphan and `harness.py verify` will say so.

**Step 6 — say what it does not do.** If the script cannot verify something, state it.
A generated Sentry gate proves what Sentry reported; it does not prove the absence of
errors Sentry never received.

## Interview output

Feed straight into PROPOSE:

```
FROM THE INTERVIEW
  suite duration       40 min on Azure DevOps  -> outer lane goes ASYNC
  must never break     licence key validation  -> risk-gated + first backfill target
  past customer bugs   memory leaks in grids   -> leak gate proposed at L3 (usually
                                                  deferred; promoting it, this is
                                                  their actual failure mode)
                       shipped unlicensed build-> licence gate, blocking, inner lane
  skipped (defaults)   linting, a11y, visual regression
  custom request       Sentry extractor        -> skill + optional release gate
```

Recording what was **skipped** matters as much as what was answered — it shows the
user which defaults they accepted without deciding, and it is where the next
conversation starts.
