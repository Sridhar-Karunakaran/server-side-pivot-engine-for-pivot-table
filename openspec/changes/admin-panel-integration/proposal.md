# PROPOSAL — Data Server Admin Panel integration (backend deltas D1–D6)

**Type:** `feature`

**Date:** 2026-10-23

---

## Why

The Syncfusion Data Server Admin Panel (Phase 2, MAUI — companion repo) embeds this server in-process and administers it over REST. Five backend capabilities it requires do not exist yet — panel-file bootstrap precedence, a full-REPLACE import endpoint, a multi-user security store (the single `Security.Authorization` pair is deprecated per locked decision CL-3), monitoring and diagnostics endpoints — and one defect must be normalized as a normative requirement: the shipped `appsettings.json` contains a plaintext admin password used for testing, which violates credential hygiene (Q10). This change specifies exactly that backend work, additively; no existing REST contract changes.

## What Changes

- **D1 — Panel-config precedence bootstrap:** new host-construction path `BuildDataServerHost(panelConfigPath)` that adds `dataserver-admin-config.json` as the **primary** JSON configuration source ahead of `appsettings.json` for the data-server sections; while the panel file exists, backend `appsettings.json` data-server sections are ignored; `{DataFolder}/*.json` overlays apply only in standalone mode. Exposed for the in-process MAUI host (companion change Step 1).
- **D2 — Import endpoint:** `POST /api/config/import { path }` — server loads the file at `path`, validates it with the existing configuration rules, and applies it as a **complete replacement** of settings **and** index definitions (never a partial merge), responding with the applied config + `requiresRestartFields`. Invalid file ⇒ 422 with nothing applied.
- **D3 — Multi-user security:** `users.json` user store (PBKDF2 hash + salt, reusing `Core/Security` primitives), CRUD API `GET/POST /api/security/users` + `PUT/DELETE /api/security/users/{username}`; Basic-auth middleware validates against the store when enabled; enabling authorization with zero users is rejected (422 / startup fail — CL-4); `Security.Authorization.Username/Password` **deprecated (BREAKING for standalone test setups)** — template sanitized, testing credentials move to user-secrets/env.
- **D4 — Monitoring endpoint:** `GET /api/monitoring` → `{ workingSetBytes, uptime, activeConnections: [ { requestType, initiationTimestamp, requestDuration, ipAddress, index } ] }` from a lightweight in-flight request registry.
- **D5 — Diagnostics endpoint:** `GET /api/diagnostics/logs?lines=200` → bounded tail of the current rolling JSONL log rendered as `{ timestamp, level, category, message }` lines, honoring `MinimumLogLevel` and redaction.
- **D6 — Credential hygiene (normative):** no plaintext secrets in git-tracked files; secrets never logged, never returned by any API (config GETs already sanitize; new endpoints inherit the rule).

## Capabilities

### New Capabilities (each → `specs/<capability>/spec.md`)

| Capability | Covers |
|---|---|
| `panel-config-precedence` | D1 bootstrap precedence, fallback, loud logging, standalone-mode preservation |
| `config-import` | D2 import contract, REPLACE semantics, validation, atomicity, restart fields |
| `security-user-store` | D3 users.json, CRUD API, BasicAuth re-point, zero-user rule, pair deprecation |
| `monitoring` | D4 `/api/monitoring` payload, registry, auth/secrecy rules |
| `diagnostics-logs` | D5 `/api/diagnostics/logs` tail contract, filtering, redaction |
| `credential-hygiene` | D6 normative hygiene across config files, logs, and responses |

### Modified Capabilities

None existing to modify — `custom-data-server/specs/data-server-backend/spec.md` documents today's contracts; those behaviors are **preserved**. This change's status table adds the Phase-2 rows when implemented. (No `openspec/specs/` archive exists yet in this repo.)

## Impact

- **Code (new):** `BuildDataServerHost` bootstrap + config-source ordering; `Controllers/ConfigImportController.cs` + `Services/ConfigImportService.cs`; `Models/UserRecord.cs`, `Services/UserStoreService.cs`, `Controllers/SecurityUsersController.cs`, `PivotBasicAuthMiddleware` re-point; `Monitoring/*` (registry middleware + `MonitoringController`); `Diagnostics/LogsController.cs`; template sanitization + docs.
- **Code (modified):** `Program.cs` (bootstrap path + new registrations), `appsettings.json` (**secret removed**), middleware pipeline (request registry), startup validation (zero-user rule).
- **APIs:** additive only — 1 import endpoint, 4 user endpoints, 2 monitoring/diagnostics endpoints. Existing endpoints byte-compatible.
- **Breaking (operational):** the deprecated `Security.Authorization` pair no longer authenticates once D3 lands; standalone testing must provision a user (panel or user-secrets bootstrap).
- **Companion repos:** `syncfusion-maui-data-server-admin-panel` change `maui-admin-panel` consumes D1–D6 verbatim.

## Approval Checklist

- [x] Scope locked: backend-only, additive, D1–D6 enumerated
- [x] All clarifications resolved (Q1–Q10, CL-1..CL-10, a–g, M-1..M-3) — see EXPLORATION.md
- [x] Capabilities enumerated (6) and mapped to spec files
- [x] Existing REST contracts preserved (no modifications, only additions)
- [x] Defect (plaintext password in git) normalized as normative D6
- [x] Companion change cross-referenced (`maui-admin-panel`)
- [x] Harness setup ordered before implementation