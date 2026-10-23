# Syncfusion Onboarding Integration

Syncfusion publishes an official agent-onboarding system at **ai.syncfusion.com**:
an onboarding skill, 775 component skills across 18 platforms, and per-platform
MCP servers. This supersedes any attempt to derive component knowledge from
documentation, and it is what the harness must wire up.

All content below was retrieved 2026-09-01. Re-verify before relying on exact
strings — `harness.py lock` records the retrieval date so drift is visible.

Source: https://ai.syncfusion.com/llms.txt (retrieved 2026-09-01)
Source: https://ai.syncfusion.com/agent-setup/prompt.md (retrieved 2026-09-01)
Source: https://ai.syncfusion.com/mcp/llms.txt (retrieved 2026-09-01)
Source: https://ai.syncfusion.com/mcp/claude-code.md (retrieved 2026-09-01)
Source: https://ai.syncfusion.com/skills/catalog.md (retrieved 2026-09-01)

## Why this matters to the harness

Syncfusion's own first rule is **"Do not write Syncfusion code from memory."**
That is precisely the harness's rank-1/rank-2 principle
(`references/knowledge-acquisition.md`), stated by the vendor. So the overlay's
skill slots resolve to real installed skills — rank 2 — rather than to generated
documentation summaries, which was the earlier fallback.

Their other mandatory rules map directly onto gates the harness already has, or
should add:

| Syncfusion rule | Harness mechanism |
|---|---|
| "Registration must happen before the first Syncfusion component is created" | `syncfusion-license` gate — ordering check |
| "Never generate, guess, echo, log, or commit a license key. Read it from configuration or the environment" | `syncfusion-license` gate — literal-key and WASM checks |
| "Never suppress, hide, catch or CSS-hide a licensing banner or warning" | `syncfusion-banner-suppression` gate |
| "License keys are version-specific" | `syncfusion-version-consistency` gate |
| "Verify runtime behavior — compilation alone is insufficient" | the whole gate ladder; component-tests and leak gates |
| "Do not load every skill or reference into context" | install narrow component skills, detected from actual usage |
| "Emit complete files without ellipsis truncation" | BUILD prompt rule |
| "One platform per answer, no framework mixing" | profile resolution; monorepos get per-project gate sets |

## Platform slug mapping (authoritative)

Use Syncfusion's slugs, not the harness's internal profile ids. The two differ,
and the overlay maps between them.

| Manifest signal | Syncfusion slug | Harness profile |
|---|---|---|
| `package.json` + `react` + `@syncfusion/ej2-react-*` | `react` | `js-react` |
| `package.json` + `@angular/core` | `angular` | `js-angular` |
| `package.json` + `vue` | `vue` | `js-common` |
| `package.json`, no framework, `@syncfusion/ej2` | `javascript` | `js-common` |
| `.csproj` + BlazorWebAssembly packages | `blazor` | `dotnet-blazor` |
| `.csproj` + `Microsoft.NET.Sdk.Web` (.NET 6+) | `aspnet-core` | `dotnet-aspnetcore` |
| `.csproj` + `net48` + `System.Web.Mvc` | `aspnet-mvc` | `dotnet-aspnetcore` |
| `pubspec.yaml` + `flutter` | `flutter` | *(no profile yet)* |
| `.csproj` + `UseMaui` + `Syncfusion.Maui.*` | `maui` | `dotnet-maui` |
| `.csproj` + `Syncfusion.Maui.Toolkit` | `maui-toolkit` | `dotnet-maui` |
| `.csproj` + `UseWindowsForms` | `winforms` | `dotnet-winforms` |
| `.csproj` + `UseWPF` | `wpf` | `dotnet-winforms` |
| `.csproj` + `WindowsAppSDK` | `winui` | *(no profile yet)* |
| PDF/Word/Excel/PowerPoint programmatic work | `document-sdk` | any .NET profile |

**Detect from the manifest, not from what the user said.** That is Syncfusion's
rule and it is the right one — a user saying "our React app" in an Angular
monorepo is common.

## Onboarding sequence

Run during Phase 3 GENERATE, after the profile is confirmed:

```bash
# 1. the onboarding skill (idempotent; skip if already present)
npx skills add syncfusion/agent-onboarding-skill --agent <agent> -y

# 2. the platform pack, or better, narrow component skills
npx skills add syncfusion/<pack-name> -y
npx skills add syncfusion/<pack-name> --skill <skill_name> --agent <agent> -y
```

`<agent>` is the host: `claude-code`, `codex`, `cursor`, `windsurf`, `copilot`,
`gemini`, `cline`, `code-studio`, `jetbrains`.

Install directories: Claude Code, Windsurf and Code Studio use vendor-specific
directories (`.claude/skills/` for Claude Code); most others use `.agents/skills/`.
**Check whether the skill is already installed before adding it.**

### Pack names

| Platform | Pack | Skills |
|---|---|---|
| React | `syncfusion/react-ui-components-skills` | |
| Angular | `syncfusion/angular-ui-components-skills` | |
| Blazor | `syncfusion/blazor-ui-components-skills` | 72 |
| .NET MAUI | `syncfusion/maui-ui-components-skills` | |
| WinForms | `syncfusion/winforms-ui-components-skills` | |
| WPF | `syncfusion/wpf-ui-components-skills` | |
| ASP.NET Core | `syncfusion/aspnetcore-ui-components-skills` | |
| Document SDK | `syncfusion/document-sdk-skills` | |

Individual skill identifiers follow `syncfusion-<platform>-<component>`
(e.g. `syncfusion-blazor-datagrid`, `syncfusion-blazor-pdf-viewer`,
`syncfusion-react-grid`). **Read the exact identifier from the platform
inventory** — `https://ai.syncfusion.com/<slug>/inventory.txt`, `skill_name`
column — rather than constructing it.

### Install narrow, not wide

Syncfusion is explicit: *"Do not load every skill or reference into context"* and
*"Install narrow skills, not full packs, when possible."* A 72-skill Blazor pack
loaded wholesale is context the build agent pays for on every delegation and
mostly cannot use.

So the harness generates `scripts/detect-syncfusion-components.sh`, which greps
the repo for actually-used components and emits the matching `skill_name` list.
Install those. Re-run it when `harness.py doctor` reports a fingerprint change.

## MCP servers

Skills and MCP are complementary: skills work anonymously, MCP confirms current
APIs and recent features and needs an API key.

| Family | Packages |
|---|---|
| npm | `@syncfusion/react-mcp`, `@syncfusion/angular-mcp`, `@syncfusion/vue-mcp`, `@syncfusion/javascript-mcp`, `@syncfusion/typescript-mcp` |
| NuGet | `Syncfusion.Blazor.MCP`, `Syncfusion.AspNetCore.MCP`, `Syncfusion.AspNetMvc.MCP`, `Syncfusion.Maui.MCP`, `Syncfusion.WPF.MCP`, `Syncfusion.WinForms.MCP`, `Syncfusion.WinUI.MCP`, `Syncfusion.UWP.MCP`, `Syncfusion.DocumentSdk.MCP` |

### Environment variables — exact casing is required

| Variable | Use |
|---|---|
| `Syncfusion_API_Key_Path` | absolute path to a `.txt`/`.key` file holding the key — **recommended** |
| `Syncfusion_API_Key` | inline key; only when a path is impossible |

`SYNCFUSION_API_KEY` (all caps) is **invalid**. Store the key at
`~/.syncfusion/api-key.txt` and reference it by absolute path.

An MCP API key is **not** a product license key; both are needed. Generate one at
`https://www.syncfusion.com/account/api-key`.

### Claude Code

```bash
claude mcp add sf-blazor-mcp \
  --env Syncfusion_API_Key_Path=$HOME/.syncfusion/api-key.txt \
  -- dnx Syncfusion.Blazor.MCP --yes
```

npm platforms use `npx -y @syncfusion/<platform>-mcp@latest`. On .NET 8/9,
substitute `dnx` with `dotnet tool run syncfusion-<platform>-mcp`.

**The harness never writes the key.** It writes the config referencing the path,
verifies the file exists, and tells the user to create it if not. Committing or
echoing a key violates Syncfusion's rules and the harness's own secrets gate.

## What the harness generates

1. **Onboarding + narrow component skills installed**, recorded in `harness.lock`
   with pack name, skill identifiers, and install directory.
2. **MCP config** for the detected platform, keyed on `Syncfusion_API_Key_Path`.
   If no key file exists, the harness records MCP as `configured-but-unauthenticated`
   and says so — it does not pretend the server is live.
3. **A knowledge pointer, not a knowledge dump.** Because real skills exist, the
   overlay writes `.agent/knowledge/syncfusion.md` as a short file that names the
   installed skills, the platform index URL, and the mandatory rules — then
   delegates API detail to the skills. No hand-summarised component APIs.
4. **Gates** enforcing the licensing rules (see the overlay).

## Reporting requirement

Syncfusion's step 6 is *"Report findings to the user: detected platform, installed
pack, MCP status."* The harness folds this into Phase 2 PROPOSE and the Phase 4
completion report, so the user sees it before and after generation.
