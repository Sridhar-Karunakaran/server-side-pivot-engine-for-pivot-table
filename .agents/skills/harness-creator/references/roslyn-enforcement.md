# Roslyn Code Quality Enforcement (.NET)

FxCop's successor is **Microsoft.CodeAnalysis.NetAnalyzers**, shipped inside the
.NET SDK. The legacy `Microsoft.CodeAnalysis.FxCopAnalyzers` NuGet package is
deprecated and must not be added — it forwards to the in-SDK analyzers, so
referencing it only creates version confusion.

The harness generates enforcement files rather than telling the agent to "follow
best practices". A rule an analyzer enforces at build time cannot be forgotten by
an agent, skipped under time pressure, or argued with. A rule written only in a
markdown file will be all three.

## What the harness generates

Two files at the repo root, plus one gate:

- `Directory.Build.props` — turns analysis on for every project, at the right level
- `.editorconfig` — sets per-rule severity (this is where the opinions live)
- gate `analyzers` in `harness.yaml` — parses the build output and thresholds on it

Both files are **additive and idempotent**: on a brownfield repo the generator
merges into existing files, never overwrites them, and reports every rule whose
severity it did not change because the repo had already set it.

## Directory.Build.props

```xml
<Project>
  <PropertyGroup>
    <!-- In-SDK analyzers (the FxCop successor). 'latest-Recommended' enables the
         curated set; AnalysisMode=All is available but produces far too much
         noise to land on an existing codebase. -->
    <EnableNETAnalyzers>true</EnableNETAnalyzers>
    <AnalysisLevel>latest-Recommended</AnalysisLevel>

    <!-- Without this, IDE* rules (unused usings, etc.) are IDE-only and never
         fail a build or a CI run. -->
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>

    <!-- Nullable reference types: the single highest-yield correctness setting
         for AI-generated C#, because it turns "forgot a null check" into a
         compile error rather than a runtime NRE. -->
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Escalate warnings the harness treats as gates. Prefer this over a blanket
         TreatWarningsAsErrors on a brownfield repo: a blanket setting produces
         thousands of failures on day one, the team disables it, and you end up
         with less enforcement than you started with. -->
    <WarningsAsErrors>$(WarningsAsErrors);nullable</WarningsAsErrors>

    <!-- Deterministic, parseable diagnostics for the msbuild parser. -->
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <!-- All free and usable in commercial closed-source work. SonarAnalyzer is
         LGPL-3.0, consumed as an analyzer that inspects your code at build time;
         it does not link into your product. -->
    <PackageReference Include="StyleCop.Analyzers" Version="1.2.0-beta.556"
                      PrivateAssets="all" IncludeAssets="runtime;build;native;contentfiles;analyzers" />
    <PackageReference Include="SonarAnalyzer.CSharp" Version="9.32.0.97167"
                      PrivateAssets="all" IncludeAssets="runtime;build;native;contentfiles;analyzers" />
    <PackageReference Include="Roslynator.Analyzers" Version="4.12.4"
                      PrivateAssets="all" IncludeAssets="runtime;build;native;contentfiles;analyzers" />
    <PackageReference Include="Meziantou.Analyzer" Version="2.0.163"
                      PrivateAssets="all" IncludeAssets="runtime;build;native;contentfiles;analyzers" />
    <PackageReference Include="Microsoft.VisualStudio.Threading.Analyzers" Version="17.11.20"
                      PrivateAssets="all" IncludeAssets="runtime;build;native;contentfiles;analyzers" />
  </ItemGroup>
</Project>
```

`PrivateAssets="all"` matters: without it, analyzer packages leak into your NuGet
package's dependency graph and become your consumers' problem. Pin versions and
let the lock record them.

## .editorconfig — the escalation set

Severities below are **the harness's opinionated set for AI-generated code**:
each one catches a defect class that models produce repeatedly and that reviewers
miss. Everything else stays at its default severity.

```ini
root = true

[*.{cs,vb}]
dotnet_analyzer_diagnostic.category-Security.severity = error

# -- Resource lifetime. The #1 source of leaks in WinForms/WPF/MAUI, and the
#    thing an LLM cannot verify by reading code.
dotnet_diagnostic.CA2000.severity = error   # dispose before losing scope
dotnet_diagnostic.CA2213.severity = error   # disposable fields must be disposed
dotnet_diagnostic.CA1001.severity = error   # types owning disposables implement IDisposable
dotnet_diagnostic.CA1063.severity = error   # implement IDisposable correctly
dotnet_diagnostic.CA1816.severity = error   # GC.SuppressFinalize(this) in Dispose

# -- Async correctness. Models produce async void and sync-over-async constantly;
#    both deadlock in UI and ASP.NET contexts.
dotnet_diagnostic.VSTHRD100.severity = error  # avoid async void
dotnet_diagnostic.VSTHRD002.severity = error  # no synchronous wait on a task
dotnet_diagnostic.VSTHRD110.severity = error  # observe the result of async calls
dotnet_diagnostic.VSTHRD200.severity = suggestion
dotnet_diagnostic.CA1849.severity = error     # call async methods in async context
dotnet_diagnostic.CA2016.severity = error     # forward the CancellationToken
dotnet_diagnostic.CA2007.severity = none      # ConfigureAwait: see per-folder override below

# -- Security. Non-negotiable.
dotnet_diagnostic.CA2100.severity = error   # SQL built from user input
dotnet_diagnostic.CA3001.severity = error   # SQL injection
dotnet_diagnostic.CA3003.severity = error   # file path injection
dotnet_diagnostic.CA5350.severity = error   # weak crypto
dotnet_diagnostic.CA5351.severity = error   # broken crypto
dotnet_diagnostic.CA5359.severity = error   # do not disable cert validation
dotnet_diagnostic.CA5364.severity = error   # no deprecated protocols

# -- Correctness traps
dotnet_diagnostic.CA1062.severity = warning # validate public arguments
dotnet_diagnostic.CA2201.severity = error   # do not throw reserved exception types
dotnet_diagnostic.CA1305.severity = warning # specify IFormatProvider
dotnet_diagnostic.CA1307.severity = warning # specify StringComparison
dotnet_diagnostic.CA1310.severity = error   # StringComparison for correctness
dotnet_diagnostic.CA1852.severity = warning # seal internal types

# -- Dead code. Cheap to fix, and AI edits accumulate it fast.
dotnet_diagnostic.IDE0005.severity = warning  # unnecessary using
dotnet_diagnostic.IDE0051.severity = warning  # unused private member
dotnet_diagnostic.IDE0052.severity = warning  # unread private member

# -- Library-only rules. ConfigureAwait(false) matters in libraries and is noise
#    in apps, so scope it rather than setting it globally.
[src/**/*.cs]
dotnet_diagnostic.CA2007.severity = warning

# -- Public API surface: breaking changes must be deliberate.
[src/**/*.cs]
dotnet_diagnostic.RS0016.severity = error   # symbol not in PublicAPI.Shipped.txt
dotnet_diagnostic.RS0017.severity = error   # symbol removed from public API

# -- Tests: relax noise, keep the rules that catch real test bugs.
[**/*{Test,Tests,Spec,Specs}/**/*.cs]
dotnet_diagnostic.CA1707.severity = none    # underscores in test names are idiomatic
dotnet_diagnostic.CA1062.severity = none
dotnet_diagnostic.CA2007.severity = none
dotnet_diagnostic.VSTHRD200.severity = none

# -- Generated code is not yours to fix.
[**/{obj,bin,Migrations,Generated}/**]
generated_code = true
```

## The gate

```yaml
- id: analyzers
  lane: inner
  claim: "no Roslyn analyzer violations at or above warning"
  tool: dotnet-analyzers
  license: "MIT (in-SDK); StyleCop MIT; SonarAnalyzer LGPL-3.0; Roslynator Apache-2.0"
  command: dotnet build --nologo -v:n /p:EnforceCodeStyleInBuild=true
  parser: msbuild
  threshold: errors == 0 and warnings <= 0
  blocking: true
  installHint: "included in the .NET SDK; the harness adds Directory.Build.props"
  docs: https://learn.microsoft.com/dotnet/fundamentals/code-analysis/overview
  rationale: >
    Replaces "the agent reviewed the code for quality issues" with a build that
    fails. The msbuild parser also reports per-rule counts, so the reviewer agent
    can target specific rule ids instead of eyeballing.
```

## Landing this on a brownfield repo

A repo with 4,000 existing warnings cannot go to `warnings <= 0` on day one. The
sequence that actually works:

1. **Measure.** `dotnet build -v:n` → the `msbuild` parser gives per-rule counts.
2. **Add the props/editorconfig with the escalation set, but gate on the diff.**
   The gate initially runs `dotnet build` on *changed projects only*
   (`--filter`/explicit project list), so new code is held to the bar and legacy
   code is not. Same principle as diff coverage.
3. **Baseline the rest.** Generate a suppression baseline
   (`dotnet build /p:GenerateBaseline=true` where supported, or a
   `GlobalSuppressions.cs` with a dated `Justification: harness baseline`). A
   baseline is a debt register, not a fix — it must be dated and shrinking.
4. **Ratchet.** Mark the gate `ratchet: true` on a `warnings <= N` threshold and
   let `harness.py ratchet` walk N down as the team burns the baseline. The
   ratchet floor in `harness.lock` stops N drifting back up.

Step 2 is the important one. Every attempt to enforce analyzers globally on a
large legacy codebase in one step ends with the team turning them off.

## Other .NET quality gates worth generating

| Gate | Command | Catches |
|---|---|---|
| `format` | `dotnet format --verify-no-changes` | whitespace/using churn in AI diffs |
| `public-api` | build with PublicApiAnalyzers | accidental breaking API change |
| `banned-api` | build with BannedApiAnalyzers + `BannedSymbols.txt` | `DateTime.Now`, `Task.Result`, internal legacy APIs |
| `vulnerable-packages` | `dotnet list package --vulnerable --include-transitive` | known CVEs |
| `deprecated-packages` | `dotnet list package --deprecated` | packages on the way out |
| `arch` | ArchUnitNET tests | layer violations (UI referencing data access) |

`BannedApiAnalyzers` deserves specific mention for Syncfusion-scale codebases: it
is the only mechanism that reliably stops an agent reaching for a deprecated
internal API, because it fails the build with a message you author.
