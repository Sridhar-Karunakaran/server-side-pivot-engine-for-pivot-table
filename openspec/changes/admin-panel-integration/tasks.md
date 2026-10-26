# TASKS — Data Server Admin Panel integration (backend deltas D1–D6)

**Date:** 2026-10-23

> Planning artifact, checked off as milestones land (harness locked). This change
> is the backend half of the Admin Panel program; the MAUI half lives in the
> companion repo's `maui-admin-panel` change. Milestones M1–M6 map to the
> companion change's Steps 1, 2 (D1), 6 (D2), 5/7 (D3), and 8 (D4/D5); D6 spans
> all of them. M0, M1 (D1) and M2 (D6a) are complete; the companion MAUI panel
> is live against this backend (in-process, start/stop/restart working).

---

## M0 — Harness & Baseline

- [x] **0.1 Harness setup** — run harness-creator against the backend stack (ASP.NET Core net10.0, existing verify scripts as reference gates). Gate ladder must include the D1–D6 verification scenarios below before implementation. *(Done: harness in `.agent/`, verify scripts wired as live gates.)*
- [x] **0.2 Baseline regression** — confirm `verify-https.sh` (33 checks) and `verify-cache.sh` pass on HEAD before any change. *(Done — note: baseline https-live initially failed 16/33 due to the pre-existing plaintext test credential (auth on, scripted calls 401); resolved by M2 (D6a) sanitization, now 33/33.)*

## M1 — D1: Panel-Config Precedence (companion Step 1)

- [x] **1.1** Refactor `Program.cs` bootstrap into `BuildDataServerHost(string? panelConfigPath = null)`; add panel file as the first JSON source when provided; suppress `{DataFolder}/*.json` overlays in panel mode; standalone path untouched. *(Done: `DataServerHostBuilder.BuildDataServerHost` + thin `Program.cs` shim; panel file first config source; https/security/reportsharing overlays and the persisted `indexes.json` index store gated on !panelMode — panel file is the sole index authority; standalone verified unchanged.)*
- [x] **1.2** Loud startup source log (panel mode vs standalone mode) + deprecation note for ignored appsettings data-server sections in panel mode. *(Done: "Data Server configuration loaded from <path> (panel mode)…" vs "…appsettings.json (standalone mode)"; index registration log names the active configuration source.)*
- [x] **1.3** Missing/invalid panel file ⇒ clear construction failure (path + reason), no listener. *(Done: FileNotFoundException naming the path; verified live, non-zero exit, no listener.)*
- [x] **1.4 Verify:** `specs/panel-config-precedence/spec.md` §1–§6 — precedence matrix (panel vs appsettings per section; standalone unchanged; overlay suppression; restart re-read; failure modes). Gate: D1 matrix green. *(Done: live-verified precedence (panel indexes win, appsettings Sales-Db ignored), overlay suppression both directions, restart re-read (port 5285→5601 rebind), 7/7 harness gates incl. https-live 33/33 + cache-live. Also added `GET /health` liveness (custom-data-server spec §7.6, planned but previously unimplemented) for the panel reachability probe; pivot POST verified 200 E2E from the panel-configured index.)*

## M2 — D6a: Credential Hygiene — Template Sanitization

- [x] **2.1** Remove `Security.Authorization.Username/Password` from shipped `appsettings.json`; add README "standalone testing credentials via user-secrets/env" section; note history-rotation guidance. *(Done: Authorization disabled + empty Username/Password in the shipped template — startup validation rejects Enabled with an empty username, so activation goes through user-secrets/env; README section with rotation note; DR-1 answered.)*
- [x] **2.2 Verify:** `specs/credential-hygiene/spec.md` §1 — template scan clean; README section present. Gate: hygiene scan (templates) green. *(Done: no-secrets gate green; https-live 33/33 after sanitization.)*

## M3 — D3: Multi-User Security (companion Steps 5 & 7)

- [ ] **3.1** `Security/Models/UserRecord.cs` + `Security/Services/UserStoreService.cs` (users.json via AtomicJsonStore; PBKDF2 + salt; case-insensitive unique immutable usernames).
- [ ] **3.2** `Security/Controllers/SecurityUsersController.cs`: GET/POST/PUT/DELETE per `specs/security-user-store/spec.md` §2 (no hash/salt ever returned).
- [ ] **3.3** Re-point `PivotBasicAuthMiddleware` to validate against the store (any-user match, constant-time); legacy pair ignored + one-time deprecation log; zero-user enable ⇒ 422 (API) and fail-fast (startup); last-user delete warns.
- [ ] **3.4 Verify:** auth matrix — enable/disable, zero-user both paths, create/rotate/delete flows, 401 on every endpoint family (incl. pivot & preflight exemption), §5–§6 of the spec. Gate: D3 matrix green.

## M4 — D2: Import Endpoint (companion Step 6)

- [ ] **4.1** `Models/ConfigImportRequest.cs` + `Configuration/Services/ConfigImportService.cs`: load server-side, full-schema validate (settings validators + every index), stage pre-import state, transactional full REPLACE (settings sections via existing apply paths; index set replaced atomically; survivors reload per Enabled), rollback on mid-apply failure.
- [ ] **4.2** `Configuration/Controllers/ConfigImportController.cs`: 200 `{ applied, requiresRestartFields }` (diffed), 404/422/502 paths, path-traversal rejection, auth applied.
- [ ] **4.3 Verify:** `specs/config-import/spec.md` §1–§7 — round-trip vs panel file schema, invalid-settings/invalid-index atomicity, index-set replacement, restart-field diffing, secret non-echo, traversal. Gate: D2 transactional checks green.

## M5 — D4 + D5: Monitoring & Diagnostics (companion Step 8)

- [ ] **5.1** `Monitoring/ActiveRequestRegistry.cs` middleware (in-memory, O(1) add/remove, no bodies/headers/credentials, index attribution where cheap) + `Monitoring/Controllers/MonitoringController.cs` (`GET /api/monitoring`: workingSetBytes (b), uptime, activeConnections).
- [ ] **5.2** `Diagnostics/LogsController.cs` (`GET /api/diagnostics/logs?lines=` default 200, cap 1000): current-file tail in file order, `MinimumLogLevel` filter, credential-pattern scan before response, empty/missing-file ⇒ 200 `[]`.
- [ ] **5.3** Register middleware + controllers; extend redaction audit to new endpoints.
- [ ] **5.4 Verify:** `specs/monitoring/spec.md` §1–§5 and `specs/diagnostics-logs/spec.md` §1–§5 — payload shapes, auth, no-secrets, overhead, empty states. Gate: D4/D5 checks green.

## MF — Cross-Cutting Verification & Closure

- [ ] **F.1** Full regression: `verify-https.sh`, `verify-cache.sh`, existing spec §0–§6 scenarios spot-checked (merge-patch, requiresRestart, CORS rules) — **byte-compatible** existing contracts.
- [ ] **F.2** D6 full gate: credential-pattern scan across templates, a representative log run (incl. import + user ops), and all new API responses — zero findings.
- [ ] **F.3** Integration with the companion panel change: run the panel's Step 1 (in-process start/stop/restart via D1), Step 6 (import), Steps 5/7 (security), Step 8 (monitoring/diagnostics) against this backend — all companion spec scenarios pass.
- [ ] **F.4** Update `custom-data-server` status table with the Phase-2 rows; keep `README.md` (testing credentials) and this change's artifacts in sync with any drift.
- [ ] **F.5** Archive `admin-panel-integration` (this repo) and `maui-admin-panel` (companion) after both verifications complete.

---

## Traceability

| Milestone | Delta | Companion panel step | Spec file |
|---|---|---|---|
| M0 | harness + baseline | Step 0 | gate ladder |
| M1 | D1 | Steps 1–2 | `panel-config-precedence` |
| M2 | D6a (template) | — | `credential-hygiene` §1 |
| M3 | D3 | Steps 5, 7 | `security-user-store` |
| M4 | D2 | Step 6 | `config-import` |
| M5 | D4, D5 | Step 8 | `monitoring`, `diagnostics-logs` |
| MF | D6 (full), regression, closure | Final | `credential-hygiene` §2–§5 |