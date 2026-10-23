# Tools of Record

The rule this harness exists to enforce:

> **For any claim where a deterministic tool exists, the agent may not assert the
> conclusion. It must run the tool of record and cite the evidence artifact.**

An LLM can say "no memory leaks here" with total fluency and no information. A
profiler cannot. So the harness routes every claim class to a tool, and
`harness.py` refuses to record a verdict that no tool produced.

## Licence policy

Every tool below is **free to use in a commercial closed-source development
environment** unless the Licence column says otherwise. Copyleft licences
(LGPL/MPL/GPL) appear only where the tool is used as a *standalone executable*
that analyses your code — that use does not impose licence obligations on your
source. Nothing here requires linking copyleft code into a product.

**Deliberately excluded, and why** — these get recommended constantly and should
not be, for commercial closed-source work:

| Tool | Why excluded |
|---|---|
| CodeQL | Free only for open-source/research. Commercial closed-source needs GitHub Advanced Security (paid). Use Semgrep OSS instead. |
| SonarQube Developer/Enterprise | Paid. **SonarQube Community Build** and the `SonarAnalyzer.*` NuGet packages are free — use those. |
| dotMemory, dotTrace, ReSharper CLI / InspectCode | Commercial JetBrains licences. Use `dotnet-gcdump` / `dotnet-counters` / `dotnet-trace` — free, from Microsoft, and CI-friendly. |
| NDepend | Commercial. Use `dotnet` project graph + ArchUnitNET for architecture rules. |
| Percy, Chromatic, Applitools | Paid SaaS above small free tiers. Playwright's built-in `toHaveScreenshot()` is free and local. |
| Snyk | Free tier is capped and gated. Use OSV-Scanner or Trivy. |
| Coverity, Veracode, Checkmarx | Commercial. |

`harness.py doctor` prints the `licence:` field of each gate, so an auditor can
read the licence posture straight off the harness.

---

## Claim → tool matrix

Read this as: *nobody may assert the left column without producing the right column.*

### Universal

| Claim (never LLM-asserted) | Tool of record | Licence | Parser | Evidence |
|---|---|---|---|---|
| "it compiles / builds" | the compiler, warnings-as-errors | — | `exitcode`, `msbuild` | build log |
| "tests pass" | the test runner | varies, all free | `junit`, `trx` | `junit.xml`, `*.trx` |
| "coverage is N%" | the coverage tool | varies, all free | `cobertura`, `lcov` | `cobertura.xml`, `lcov.info` |
| "my changes are tested" | coverage, **diff-scoped** | as above | `cobertura-diff`, `lcov-diff` | same artifact + git diff |
| "the tests are meaningful" | mutation testing | Apache-2.0 | `stryker` | `mutation-report.json` |
| "no secrets committed" | Gitleaks | MIT | `sarif` | `gitleaks.sarif` |
| "no known-vulnerable dependencies" | OSV-Scanner / Trivy | Apache-2.0 | `sarif` | `osv.sarif` |
| "no injection/insecure pattern" | Semgrep OSS | LGPL-2.1 (CLI) | `sarif` | `semgrep.sarif` |
| "no dead code" | Knip (JS), `IDE0005` (.NET), Vulture (Py) | MIT / MIT / MIT | `eslint-json`, `msbuild` | report |
| "architecture boundaries hold" | dependency-cruiser, ArchUnitNET | MIT / Apache-2.0 | `exitcode`, `junit` | graph/test report |

### .NET (ASP.NET Core, Blazor, MAUI, WinForms, WPF, document libraries)

| Claim | Tool of record | Licence | Parser |
|---|---|---|---|
| "code quality is acceptable" | **Microsoft.CodeAnalysis.NetAnalyzers** (the FxCop successor, in-SDK) | MIT | `msbuild` |
| "style is consistent" | `dotnet format --verify-no-changes` + StyleCop.Analyzers | MIT | `exitcode`, `msbuild` |
| "no bug-prone patterns" | SonarAnalyzer.CSharp, Roslynator, Meziantou.Analyzer | LGPL-3.0 / Apache-2.0 / MIT | `msbuild` |
| "async is correct" | Microsoft.VisualStudio.Threading.Analyzers (VSTHRD*) | MIT | `msbuild` |
| "disposables are disposed" | CA2000, CA2213, CA1063, CA1816 | MIT | `msbuild` |
| "tests pass" | xUnit / NUnit / MSTest + `--logger trx` | MIT / MIT / MIT | `trx` |
| "changed lines covered" | Coverlet (`--collect:"XPlat Code Coverage"`) | MIT | `cobertura-diff` |
| "tests are meaningful" | **Stryker.NET** (`--since`) | Apache-2.0 | `stryker` |
| "no memory leak" | **dotnet-gcdump + dotnet-counters** | MIT | `gcdump-delta`, `jsonpath` |
| "no perf regression" | BenchmarkDotNet | MIT | `jsonpath` |
| "public API unchanged" | Microsoft.CodeAnalysis.PublicApiAnalyzers (RS0016/RS0017) | MIT | `msbuild` |
| "banned APIs unused" | Microsoft.CodeAnalysis.BannedApiAnalyzers | MIT | `msbuild` |
| "no vulnerable packages" | `dotnet list package --vulnerable --include-transitive` | free (SDK) | `regex` |
| "no EF N+1" | EF Core `ConfigureWarnings(w => w.Throw(...MultipleCollectionIncludeWarning))` in test fixtures | free | `trx` |

**Roslyn setup is a generated artifact, not advice.** The harness writes a
`Directory.Build.props` and `.editorconfig` that turn the relevant rules into
build errors, so they cannot be forgotten. See `references/roslyn-enforcement.md`.

### JavaScript / TypeScript (React, Angular, Vue)

| Claim | Tool of record | Licence | Parser |
|---|---|---|---|
| "lint clean" | ESLint (+ typescript-eslint, angular-eslint, eslint-plugin-react-hooks) | MIT | `eslint-json` |
| "types are sound" | `tsc --noEmit` | Apache-2.0 | `exitcode` |
| "tests pass" | Vitest / Jest (`--reporters=junit`) | MIT | `junit` |
| "changed lines covered" | v8/c8 or Istanbul → lcov | MIT | `lcov-diff` |
| "tests are meaningful" | **StrykerJS** (`--since`) | Apache-2.0 | `stryker` |
| "no memory leak" | **memlab** (Meta) | MIT | `memlab` |
| "no detached DOM nodes" | Playwright + CDP heap snapshot | Apache-2.0 | `jsonpath` |
| "accessible" | axe-core / `@axe-core/playwright` | MPL-2.0 | `jsonpath` |
| "no visual regression" | Playwright `toHaveScreenshot()` | Apache-2.0 | `junit` |
| "bundle within budget" | size-limit, or Angular CLI `budgets` | MIT | `jsonpath`, `exitcode` |
| "no perf regression" | Lighthouse CI | Apache-2.0 | `jsonpath` |
| "no circular imports" | dependency-cruiser / madge | MIT | `exitcode` |
| "no dead exports" | Knip | MIT | `jsonpath` |
| "no vulnerable packages" | `npm audit --json`, OSV-Scanner | free / Apache-2.0 | `jsonpath`, `sarif` |
| "public API unchanged" | API Extractor | MIT | `exitcode` |

### Python

| Claim | Tool | Licence | Parser |
|---|---|---|---|
| "lint clean" | Ruff | MIT | `jsonpath` |
| "types sound" | mypy / pyright | MIT / MIT | `exitcode` |
| "tests pass" | pytest (`--junitxml`) | MIT | `junit` |
| "changed lines covered" | coverage.py (`--cov-report=xml`) | Apache-2.0 | `cobertura-diff` |
| "tests meaningful" | mutmut / cosmic-ray | BSD / MIT | `regex`, `jsonpath` |
| "no insecure pattern" | Bandit | Apache-2.0 | `sarif` |
| "no vulnerable deps" | pip-audit | Apache-2.0 | `jsonpath` |
| "no leak" | `tracemalloc` snapshot delta (stdlib) | PSF | `gcdump-delta` |

---

## The memory-leak case in full

This is the example that motivated the rule, so here is the whole mechanism
rather than a pointer.

**Why an LLM must not answer it.** Leak detection requires observing heap state
across time under load. That information does not exist in source code. A model
reading a file can produce a plausible list of *candidate* leak patterns; it
cannot produce a leak. Accepting its verdict means shipping on a guess.

**What the harness does instead** — three roles, kept separate:

1. **Static candidates (analyzer, blocking).** CA2000/CA2213/CA1063 for .NET;
   `react-hooks/exhaustive-deps` and a custom `no-unsubscribed-observable` rule
   for JS. These catch *shapes* and are cheap. They run inner-lane.
2. **Dynamic proof (profiler, outer-lane).** Grow-and-measure: exercise the
   suspect path N times, sample the heap, assert bounded growth. This is what
   actually decides whether a leak exists.
3. **The agent's role: fix and re-measure.** The agent reads the retaining path
   out of the report, fixes the retainer, and re-runs the gate. It never
   adjudicates.

### Ready-made dynamic gates

**Web (React / Angular / Blazor WASM)** — memlab drives a scenario and reports
retained objects:

```yaml
- id: leak-web
  lane: outer
  claim: "no detached DOM / retained component leaks on the changed route"
  tool: memlab
  license: "MIT (free, commercial use OK)"
  command: npx memlab run --scenario e2e/leak/{SCENARIO}.js --work-dir {EVIDENCE_DIR}/memlab
  parser: memlab
  evidence: "{EVIDENCE_DIR}/memlab/leaks.json"
  threshold: leaks == 0
  installHint: "npm i -D memlab"
  docs: https://facebook.github.io/memlab/
  rationale: "An LLM cannot observe heap retention. This can."
```

**.NET (ASP.NET Core / MAUI / WinForms)** — grow-and-measure with the free
Microsoft diagnostics tools:

```yaml
- id: leak-dotnet
  lane: outer
  claim: "managed heap growth is bounded under repeated exercise"
  tool: dotnet-gcdump
  license: "MIT (free, commercial use OK)"
  command: ./scripts/leak-probe.sh {EVIDENCE_DIR}
  parser: gcdump-delta
  parserArgs:
    baseline: heap-baseline.json
    current: heap-after.json
    field: heapBytes
  threshold: growthRatio <= 1.10
  installHint: "dotnet tool install -g dotnet-gcdump && dotnet tool install -g dotnet-counters"
  docs: https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-gcdump
```

`leak-probe.sh` is generated per stack: start the app or test host, take a
baseline gcdump after warm-up, run the scenario N times, force a collection,
take a second gcdump, and emit both totals as JSON. The threshold is a *growth
ratio*, not an absolute — absolute heap size is not a leak signal, unbounded
growth is.

**WinForms / WPF / MAUI handler leaks** specifically: the highest-yield check is
a unit-level one, and it is fully deterministic — create the control, subscribe,
dispose, force GC, and assert a `WeakReference` is dead. That is an ordinary
xUnit test, so it rides the existing `trx` gate with no new tooling.

---

## Adding a claim class

1. Find a tool that measures it, and check the licence against the policy above.
2. Pick a parser from `parsers.py`, or use `jsonpath`/`regex` if the tool emits
   JSON or a parseable line. Write a new parser only if neither fits.
3. Add the gate to `harness.yaml` with `claim`, `tool`, `license`, `command`,
   `parser`, `evidence`, `threshold`, `installHint`, `docs`, `rationale`.
4. `harness.py validate` then `harness.py lock`.
5. Check `harness.py explain "<the claim>"` now resolves.

If no tool exists, that claim stays **unverifiable**, and the agent must record
it as unverified in the task `## Log` rather than assert it. `harness.py explain`
exits non-zero for exactly this case.
