# DESIGN — Data Server Admin Panel integration (backend deltas D1–D6)

**Date:** 2026-10-23

---

## 1. D1 — Panel-Config Precedence Bootstrap

```
BuildDataServerHost(panelConfigPath)          [new entry point for in-process hosts]
│
├─ ConfigurationBuilder
│   1. JSON source: {panelConfigPath}            ← PRIMARY (added FIRST)
│   2. JSON source: appsettings.json             ← fallback only
│   3. JSON source: appsettings.{env}.json
│   4. env vars / command line                   ← unchanged
│
├─ Load {DataFolder} overlays ONLY in standalone mode
│   (panel mode: panel file + API PUTs are authoritative; overlays skipped)
│
├─ Existing pipeline reused verbatim:
│   validation (ConfigurationValidator) → service binding → Kestrel
│   (Port/HTTPS/DataFolder/CacheSizeLimit/PathToFolder all bind here)
│
└─ Loud startup log naming the active source:
    "Data Server configuration loaded from <path> (panel mode)"
    vs "... from appsettings.json (standalone mode)"
```

- **Panel mode activation:** the caller (MAUI host service) passes `panelConfigPath`. Standalone `dotnet run` keeps today's behavior exactly.
- **While the panel file exists**, `appsettings.json` data-server sections are ignored (locked Q3/CL-1). A missing/unreadable panel file ⇒ clear startup failure surfaced to the caller (panel shows it in the start-failure dialog); the server never silently falls back to `appsettings.json` in panel mode.
- **Overlay rule:** `{DataFolder}/*.json` overlays are loaded **only** in standalone mode (CL-1). In panel mode, persisted changes are written by the panel itself into the panel file (write-through) and the API applies values in memory.

**Files:** `Program.cs` (refactor bootstrap into a shared `BuildDataServerHost`), new `Configuration/Services/PanelConfigBootstrap.cs`.

## 2. D2 — Import Endpoint

```
POST /api/config/import            body: { "path": "<absolute path>" }
│
├─ 404/422  file missing/unreadable/not JSON
├─ Load + validate against the FULL schema:
│     General, DataStorage, DatabaseQuery, Https, Security, ReportSharingOptions,
│     DataServer.Indexes[]  (same schema as dataserver-admin-config.json — CL-5)
│     → per-section validators (existing) + index-definition validators
├─ Validation failure ⇒ 422 { errors: [...] }   NOTHING APPLIED (transactional)
├─ Apply as COMPLETE REPLACEMENT:
│     settings services ← imported values (same code paths as PUT per section)
│     IndexManager     ← replace index set (drop missing, upsert provided,
│                         reload survivors per Enabled semantics)
│     DataServer/indexes.json ← replaced atomically (AtomicJsonStore)
├─ Persist imported settings per existing per-section stores (standalone mode)
├─ Response 200:
│     { applied: { <full config> }, requiresRestartFields: [ "General.Port", ... ] }
└─ requiresRestartFields computed by the SAME rules as PUT /api/config/* today
```

- **Atomicity:** validation of the whole file precedes any application; a mid-apply failure rolls back to the previous configuration (the previous `indexes.json` + settings snapshots are staged before apply).
- **Secrets:** imported `Https.PfxPassword` and any user-password material are applied in memory and stored only via the existing sanitized persistence (never echoed; D6).
- **Import file location:** the path is read **server-side** (the server and panel share the machine in panel mode; this is the contract the user locked — the panel sends the path, the server loads/validates/applies).

**Files:** new `Configuration/Controllers/ConfigImportController.cs`, `Configuration/Services/ConfigImportService.cs`, `Models/ConfigImportRequest.cs`.

## 3. D3 — Multi-User Security

```
users.json  (DataServer/, AtomicJsonStore)
  [ { "username": "admin", "hash": "<pbkdf2>", "salt": "<..>",
      "createdAt": "...", "updatedAt": "..." } ]          ← NEVER plaintext, never returned

API:
  GET    /api/security/users            → [ { username, createdAt, updatedAt } ]   (no hashes)
  POST   /api/security/users            { username, password }        → 201 { username, createdAt }
  PUT    /api/security/users/{username} { newPassword }               → 200 (credential rotates)
  DELETE /api/security/users/{username}                             → 204

BasicAuth (PivotBasicAuthMiddleware, re-pointed):
  Authorization.Enabled == true
    → decode Basic credential → hash against EVERY user's salt (constant-time compare)
    → match ⇒ allow; miss ⇒ 401 + WWW-Authenticate: Basic realm
  Authorization.Enabled == true && user store EMPTY
    → REJECT ENABLE (API: 422; startup: fail-fast)      [CL-4 server-side guard]
CORS rule unchanged (auth on ⇒ specific origins; "*" rejected).
```

- **Hashing:** reuse `Core/Security/PBKDF2PasswordHasher` + `ConstantTimeComparer` (already shipped for this design).
- **Uniqueness:** usernames unique, case-insensitive; immutable after creation (no rename).
- **Deprecation (CL-3):** `Security.Authorization.Username/Password` are removed from the shipped template and ignored at authentication time; migration path for standalone testers = user-secrets/env + `POST /api/security/users` bootstrap (or the panel's Security tab). Startup logs the deprecation once when a stale pair is detected in a custom `appsettings.json`.
- **Fail-fast parity:** `Authorization.Enabled=true` with an empty store at startup behaves exactly like today's missing-credentials fail-fast (spec §5.2.8) — server does not start.

**Files:** new `Security/Models/UserRecord.cs`, `Security/Services/UserStoreService.cs`, `Security/Controllers/SecurityUsersController.cs`; modified `Security/PivotBasicAuthMiddleware.cs`, `Program.cs` (registrations + zero-user startup check), `appsettings.json` (pair removed).

## 4. D4 — Monitoring

```
GET /api/monitoring
→ 200 {
     workingSetBytes: <long>,          // Environment.WorkingSet of the server process (b)
     uptime: "<duration since host start>",
     activeConnections: [
       { requestType: "POST /api/pivot/post",
         initiationTimestamp: "...", requestDuration: "<elapsed>",
         ipAddress: "<remote ip>", index: "<index name | null>" }
     ]
   }
```

- **Registry:** a lightweight `ActiveRequestRegistry` middleware — on request start, push `{ requestId, requestType, initiationTimestamp, ipAddress, index? }`; on completion, pop. `index` is resolved from `?index=` / body where cheaply available (pivot + index endpoints), else null.
- Auth applies (401 when enabled); payload never contains request bodies or secrets (D6).
- `requestDuration` is the running elapsed value for in-flight entries (a snapshot).

**Files:** new `Monitoring/ActiveRequestRegistry.cs`, `Monitoring/Controllers/MonitoringController.cs`; modified `Program.cs` (middleware registration).

## 5. D5 — Diagnostics Logs

```
GET /api/diagnostics/logs?lines=200        (default 200, cap 1000 — c)
→ 200 { lines: [ { timestamp, level, category, message } ], file: "<current jsonl path>" }
```

- Reads the **current** rolling file (`{DataFolder}/logs/dataserver-{yyyyMMdd}.jsonl`), bounded tail (`lines`), newest-last order preserved from the file.
- Honors `MinimumLogLevel` filtering server-side (entries below threshold are omitted — same rule as the console provider) and the existing redaction (connection strings, passwords never logged in the first place; the endpoint adds no new logging).
- Auth applies; no file listing, no arbitrary-path parameter (only the current file is exposed — CL-7 scope).

**Files:** new `Diagnostics/LogsController.cs`.

## 6. D6 — Credential Hygiene (normative)

- Template `appsettings.json`: **no** `Security.Authorization` pair, **no** real passwords; test PKI stays (`Cert/PEM-Cert/` — public test CA, acceptable per existing verify scripts).
- Standalone testing credentials: user-secrets / environment variables (documented in README).
- Global rules (enforced by review + harness gate): secrets never logged (existing redaction audit), never returned by any API (config GETs sanitize; user APIs return no hash), never persisted plaintext by any new endpoint (import applies-in-memory).

**Files:** modified `appsettings.json`, `README.md` (testing credentials section).

## 7. Registration & Startup Order (`Program.cs`, panel mode)

```
BuildDataServerHost(panelConfigPath)
  → config sources (panel file first)
  → DI: existing services + UserStoreService + ConfigImportService
        + ActiveRequestRegistry + new controllers
  → middleware: ActiveRequestRegistry → CORS → BasicAuth (user store)
                 → exception filter → endpoints
  → startup validations: config rules + zero-user rule (fail-fast)
  → Kestrel Listen(Any, General.Port) [+ HTTPS per Https.*]
```

## 8. Compatibility & Testing Strategy

- **Byte-compatible:** all existing endpoints keep shapes; new endpoints are additive; merge-patch semantics unchanged.
- **Gates (harness, before implementation):** D1 precedence matrix (panel/standalone/fallback), D2 transactional import (valid/invalid/partial-rollback), D3 auth matrix (enable/disable, zero-user, rotate/delete, all-endpoint enforcement incl. pivot), D4/D5 payload + auth + redaction checks, D6 secret-scan of templates and logs.
- **Existing verify scripts** (`verify-https.sh`, `verify-cache.sh`) remain authoritative for unchanged behavior; new scenarios extend them per capability specs.

## 9. Risks & Mitigations

| Risk | Mitigation |
|---|---|
| Import mid-apply failure leaves mixed state | Stage previous state; validate-then-apply transactionally; rollback on failure |
| Zero-user lockout (admin deletes last user while auth on) | Allow it server-side but warn via 200 + panel hint; policy = re-add via panel (same as Flexmonster) — documented in spec |
| Panel-file precedence regression in standalone mode | Precedence matrix gate both modes in CI |
| Registry overhead on hot path | Registry is an in-memory concurrent structure with try-add/remove only — no I/O |
| Log tail reads large files | Bounded tail read (`lines` cap 1000); no full-file loads |