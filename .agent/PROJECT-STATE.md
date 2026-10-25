# PROJECT STATE

**Repo:** server-side-pivot-engine-for-pivot-table (Syncfusion Pivot Data Server)
**Branch:** `bc/4792425e-detailed-functional`
**Phase:** Harness generated (L0). Implementation NOT started.

## Program context

Two-repo program:

- **This repo** — Data Server backend. Phase 1 (`custom-data-server`) complete &
  verified. Phase 2 backend deltas specified in `openspec/changes/admin-panel-integration`
  (D1–D6): panel-config precedence, `/api/config/import`, multi-user security,
  `/api/monitoring`, `/api/diagnostics/logs`, credential hygiene. Planning complete;
  PR #29 open against `bc/3a965654-implement-custom`.
- **Companion repo** (`syncfusion-maui-data-server-admin-panel`) — the MAUI Admin
  Panel (PR #3 against `maui-app`). Embeds this server in-process (locked Q1a);
  option (B) standalone-service connection is future scope, not to be built now.

## Active decisions

- In-process hosting (Q1a): panel builds/stops/rebuilds the host via new
  `BuildDataServerHost(panelConfigPath)` — D1.
- Panel file `dataserver-admin-config.json` (panel app root) is sole authority
  for data-server config once present (Q3/CL-1); overlays standalone-only.
- D3 multi-user store deprecates `Security.Authorization`; enabling auth with
  zero users ⇒ startup failure / 422 (Q7/CL-4).
- Import = full REPLACE of settings + indexes (Q5/CL-5).
- Monitoring = manual refresh, working set metric (CL-6/b). Diagnostics = API
  log viewer, last 200 lines (CL-7/c).
- Harness before implementation (user instruction): done — this folder.
- **HARNESS-GENERATED THRESHOLDS ARE L0 FLOORS.** Raising tiers (tests, coverage
  floors, mutation) requires regenerated lock + a human-approved decision entry.

## Open decisions (parking lot)

See `DECISIONS-REQUIRED.md`.

## Blockers

- D6-1: `appsettings.json` test credential rotation — template sanitization task
  is gated on the user rotating the real credential first.