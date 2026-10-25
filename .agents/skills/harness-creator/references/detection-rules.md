# Detection Rules

Detect with tools and dependency manifests. Never infer a stack from a
recollection of what the code looked like.

## Order of operations

### 1. Find manifests

Walk the repo, skipping `node_modules`, `.git`, `bin`, `obj`, `dist`, `build`,
`vendor`, `.venv`, `target`, `packages`, `TestResults`, `.agent`. Depth-limit to
5 to avoid pathological monorepos.

Collect: `*.csproj`, `*.fsproj`, `*.vbproj`, `*.sln`, `Directory.Packages.props`,
`package.json`, `requirements.txt`, `pyproject.toml`, `Cargo.toml`, `go.mod`,
`pom.xml`, `build.gradle`, `Gemfile`, `composer.json`, `mix.exs`, `pubspec.yaml`.

### 2. Extract dependency names

Names, not versions — a version bump must not look like a stack change. This is
exactly what `harness.py`'s fingerprint hashes, so a newly added framework or
Syncfusion package triggers a drift warning and a regeneration prompt.

| Manifest | Extract from |
|---|---|
| `package.json` | `dependencies`, `devDependencies`, `peerDependencies` keys |
| `*.csproj`, `*.props` | `PackageReference Include=`, `PackageVersion Include=` |
| `requirements.txt` | name before any `=<>![;` |
| `pyproject.toml` | keys before `=`/`>`/`<`/`~` |
| `go.mod` | module paths in the require block |
| `pom.xml` | `<artifactId>` |

### 3. Score profiles

Each profile in `profiles/` declares `detect.requires`, `detect.signals`
(weighted), and `detect.excludes`. Score every profile whose `requires` are all
satisfied and no `excludes` match; highest total wins.

**A tie, or a top score below the profile's `minScore`, is a question for the
user.** Guessing wrong here poisons every gate, catalog, and knowledge file
downstream.

**Monorepos legitimately match several profiles.** Emit one gate set per detected
sub-project, scoped with `cwd`, and say so in PROPOSE. Do not force a single
profile onto a repo that genuinely contains an Angular app and an ASP.NET API.

### 4. Detect overlays

`overlays/*.yaml` declare `detect.anyOf`. Syncfusion matches on the `Syncfusion.`
and `@syncfusion/` package-name prefixes, or a `RegisterLicense` / `registerLicense`
call in source. An overlay composes onto whatever profile won.

### 5. Assess test reality — this sets the tier

Do not assume. Measure:

| Question | How |
|---|---|
| Do test projects exist? | `*.Tests.csproj`, `**/*.test.ts`, `tests/`, `conftest.py` |
| How many tests? | run the suite once with a reporter, or count `[Fact]`/`it(`/`def test_` |
| Current coverage? | run coverage once — this is the number that sets a realistic diff threshold |
| Do the tests pass **now**? | run them. A red suite on arrival is the first task, not a gate failure to design around |
| How long does the suite take? | times the lane split. A 40-minute suite means the outer lane must be async |

That last one is easy to skip and expensive to skip. Suite duration determines
whether an inner lane is possible at all, and therefore whether test impact
selection is required (`references/async-gates.md`).

### 6. Map architecture with a tool

| Stack | Command |
|---|---|
| .NET | `dotnet sln list`, then `dotnet list <proj> reference` per project |
| JS/TS | `npx depcruise --output-type json src`, or `npx madge --json src` |
| Python | `python -m pydeps --show-deps` or import-graph inspection |

Write the resulting **real** graph into `project-context.md`. The previous
generation of this skill read "2–3 representative source files" and inferred the
architecture; that yields a plausible fiction, and every build agent then places
code by that fiction.

### 7. Find change hotspots

```bash
git log --format= --name-only --since="12 months ago" \
  | grep -v '^$' | sort | uniq -c | sort -rn | head -20
```

The cheapest high-value signal in the repository. Defects concentrate in
high-churn files, so the reviewer weights findings there.

### 8. Check tool availability

`which` the first token of every candidate gate's command, and run its
`versionCommand`. Three outcomes, and the difference matters:

- **present** → the gate is emitted
- **absent but installable** → PROPOSE shows it with its `installHint`; the gate
  is **not** emitted until it is installed
- **absent and not applicable** → drop it silently

Never emit a gate whose tool is missing. A gate that errors on every run trains
everyone to ignore gate output, which costs more than the gate was worth.

### 9. Determine the tier

From `references/tiers-and-ratchet.md`:

| Observed | Tier |
|---|---|
| no tests, or the suite does not run | **L0** |
| tests exist and pass; coverage anywhere | **L1** |
| good suite, coverage tooling configured, CI green | **L2** |
| all of L2 plus appetite for mutation and runtime budgets | **L3** |

Propose the tier; let the user raise it. A tier is a claim about what has been
verified, so `harness.py validate` fails when its obligations are unmet.

### 10. Git

Required, not optional. Without it there is no `baseSha`, so no diff-scoped gate,
no scoped review, and no rollback. If `.git` is absent, `git init` plus an initial
commit is task one — never a skipped stage.

## Security and risk signals

Feed `risk.requiresHumanApproval` rather than a prose warning:

| Signal | Effect |
|---|---|
| `auth`, `login`, `identity`, `session` in paths | add those paths to risk globs |
| `Migrations/`, `migrations/` | risk glob — a dropped column is data loss |
| `PublicAPI.*.txt`, `api-extractor.json` | risk glob + emit the public-API gate |
| `*.csproj`, `package.json`, `Directory.Packages.props` | risk glob — dependency changes are never local |
| `.env`, `appsettings*.json`, `secrets.json` | emit the secrets gate; verify `.gitignore` |
| `Dockerfile`, k8s manifests, IaC | risk glob |
| crypto / hashing imports | escalate CA5350/CA5351 to error |

## Greenfield

No source files → interview, one question at a time:

1. What are you building?
2. Who uses it?
3. Which stack? *(selects the profile)*
4. Which architecture? *(sets the placement rules)*
5. Which conventions matter to you? *(attempt rank-1 enforcement for each)*
6. What is the first feature?

Then proceed at **L1** — greenfield has no legacy excuse, so diff coverage is
satisfiable from the first commit, and TDD from task one is the cheapest it will
ever be.
