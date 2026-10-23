# EXPLORATION — Data Server Admin Panel integration (backend deltas D1–D6)

**Change Type:** `feature`

**Date:** 2026-10-23

**Companion change:** `syncfusion-maui-data-server-admin-panel` → `openspec/changes/maui-admin-panel` (the panel; consumes everything specified here)

---

## Request

Specify the backend work the MAUI Admin Panel requires: (D1) loading the panel's `dataserver-admin-config.json` as the **primary** Data Server configuration source, (D2) a dedicated full-REPLACE import endpoint, (D3) a multi-user security store with CRUD + Basic Auth against it (deprecating `Security.Authorization`), (D4) a monitoring endpoint, (D5) a diagnostics log-tail endpoint, and (D6) credential hygiene as a normative requirement. The panel's own spec (`maui-admin-panel`) consumes these; the backend's existing endpoints and contracts are **not** altered.

---

## Current Behavior (verified by inspection)

### What exists and is verified (branch `bc/3a965654-implement-custom`, change `custom-data-server`)

From `openspec/changes/custom-data-server/specs/data-server-backend/spec.md` (v2.2) and the `PivotController/` tree:

- **Config precedence today:** `appsettings.json` sections (`General`, `DataStorage`, `DatabaseQuery`, `Https`, `Security`, `ReportSharingOptions`, `DataServer.Indexes`) **plus** `{DataFolder}/*.json` persisted overlays (`config.json`, `https.json`, `datastorage.json`, `databasequery.json`, `reportsharing.json`, `security.json`) with non-null-wins precedence, loaded before `Listen` and logged loudly at startup.
- **Settings APIs:** `GET/PUT /api/config/{general,datastorage,databasequery,https,security,reportsharing}` (merge-patch; explicit null clears; omitted unchanged), `GET /api/config/datastorage/cache-stats`. Responses carry `requiresRestart` / `requiresRestartFields` (Port, DataFolder, HTTPS fields, CacheSizeLimit, ReportSharing.PathToFolder require restart; log level, retry, keep-on-refresh, DB-query options, security flags are runtime).
- **Security today:** single credential pair `Security.Authorization.{Username,Password}` in `appsettings.json` (spec §5.1, “Admin Panel later”); `PivotBasicAuthMiddleware` (`PivotController/Security/`) validates it when `Authorization.Enabled`; PBKDF2 + constant-time comparison exist in `Core/Security` (used by the user-management design in `design.md` but **no user store or user API is implemented**); CORS `AllowOrigin` with the auth-on ⇒ specific-origins-mandatory rule (`*` rejected; startup fail-fast; 422 via API).
- **Logging:** `RollingFileLoggerProvider` → `{DataFolder}/logs/dataserver-{yyyyMMdd}.jsonl`, 30-day retention, categories `DataServer.Config|Security|Indexing|DatabaseQuery|ReportSharing|Https|Pivot`; `MinimumLevelLoggerProvider` with runtime threshold flip + `GeneralSettingsFileWatcher` hot-reload; `POST /api/config/general/test-log` helper.
- **Index APIs:** full lifecycle incl. `POST /api/indexes/test-connection` (probe + per-index), fields API for mapping. Pivot: `POST /api/pivot/post` index-aware (`?index=` query added).
- **Persistence:** `AtomicJsonStore` (temp+rename), `JsonFileRepository`, `DataServerExceptionFilter` → RFC 7807 problem details with `errors` extension; `/health`.
- **Not implemented:** monitoring, diagnostics API, import endpoint, user store/user API, panel-file bootstrap.

### Defect found during inspection (normative to fix here — D6)

`PivotController/appsettings.json` currently ships a **plaintext admin password** (`Security.Authorization.Password: "<literal value redacted from this spec; see the appsettings file on the working branch>"`) — used by the user for backend testing (stated in the clarification rounds). A git-tracked plaintext credential violates the credential-hygiene requirement and is normalized by this change (template sanitized; standalone testing credentials move to user-secrets/env). No spec file repeats the literal value.

### Panel-side context (from the companion repo)

`Syncfusion_Data_Server_Admin_Panel_MAUI_controls.md` (control inventory), `Syncfusion_Data_Server_Admin_Panel_MAUI_Specification.md` (new master structure doc), and the `maui-admin-panel` change's EXPLORATION/proposal/design/specs/tasks — the panel embeds this server **in-process** (Q1a), starts/stops/restarts it exclusively (Q1), reads its base URL from the panel file, and calls the endpoints specified below.

---

## Options Considered

### D1 — panel-file bootstrap
- **A.** Runtime reload from the panel file after host build: rejected — Port/HTTPS must be known **before** `Listen`; a post-build patch would require a restart anyway.
- **B. (Recommended)** A `BuildDataServerHost(string panelConfigPath)` bootstrap path that adds a JSON provider for the panel file **ahead of** `appsettings.json`, then reuses the existing overlay + validation pipeline unchanged. Standalone mode keeps today's behavior (no panel file → `appsettings.json` stands).

### D2 — import
- **A.** Client-side merge in the panel: rejected — violates REPLACE semantics and bypasses server validation (CL-5).
- **B. (Recommended)** Server-side endpoint: load → validate (existing `ConfigurationValidator` rules) → atomically apply as full replacement (settings + indexes) → respond with `requiresRestartFields`. Reuses the existing apply/reload machinery per section.

### D3 — users
- **A.** Keep the single pair and manage it via config API: rejected — locked decision Q4/CL-3 is multi-user with the pair deprecated.
- **B. (Recommended)** `users.json` store (PBKDF2 hash+salt per user, reusing `Core/Security` primitives), CRUD API `/api/security/users`, BasicAuth middleware re-pointed to the store, `Authorization.Enabled=true` requires ≥ 1 user (422 / startup fail per CL-4), CORS rule unchanged.

### D4/D5 — monitoring & diagnostics
- **A.** Expose process metrics via `/health` extensions and logs via static file serving: rejected — conflates health semantics; static log files bypass level filtering and secret redaction.
- **B. (Recommended)** Dedicated `GET /api/monitoring` (working set + active-connection snapshot from a lightweight in-flight request registry) and `GET /api/diagnostics/logs?lines=N` (bounded tail of the current JSONL file, rendered server-side so level filtering and redaction apply).

### D6 — credential hygiene
- **A.** Keep the testing pair in git with a warning: rejected — plaintext secrets in git are normalized out; testing moves to user-secrets.
- **B. (Recommended)** Normative requirement: no plaintext secrets in git-tracked files; never logged; never returned; template sanitized + documentation for user-secrets/env override in standalone mode.

---

## Recommended Approach

**B in every case**, as specified above. Existing endpoints, response shapes, and the merge-patch semantics stay byte-compatible; all new behavior is additive. The `custom-data-server` change's status table gains a Phase-2 row set once these land.

**Locked decisions applied:** Q3/Q1 panel file primary at app root · CL-1 sole authority + overlays standalone-only · CL-3 deprecate pair, admin via panel · CL-4 zero-user block (client + server) · CL-5 import = full REPLACE, same schema · Q6 endpoints named `/api/monitoring` and `/api/diagnostics/logs` · Q7 auth = users from the Security tab across all endpoints · Q10 hygiene normative · (b) working set metric · (c) 200-line default tail.

---

## Out of Scope (this change)

- Any panel-side (MAUI) code — companion change owns it.
- Changes to existing settings/index/pivot/report REST contracts (only *additive* endpoints here).
- Auto-polling monitoring, log filters beyond `?lines=`, Open Logs Folder server-side features (panel renders API data only — CL-6/CL-7).
- Client-side sample (Step 8 / §7 of `custom-data-server`) — stays in that change.
- Report CRUD UI (Q8) — settings section only; backend report APIs already exist.

## Open Questions

None blocking. Endpoint response shapes below are the minimum the panel binds to; the backend may extend them additively without breaking the panel.