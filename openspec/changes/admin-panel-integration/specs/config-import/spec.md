# SPEC — config-import (D2)

**Capability:** `config-import` — the dedicated `POST /api/config/import` endpoint applying a full Data Server configuration file as a **complete replacement** of settings and index definitions.

**Date:** 2026-10-23

---

## ADDED Requirements

### Requirement: 1. Endpoint Contract

```
POST /api/config/import
Request:  { "path": "<absolute path to a full Data Server configuration JSON file>" }
Response: 200 { "applied": { <full applied configuration> },
                 "requiresRestartFields": [ "<field path>", ... ] }
Errors:   404 file not found · 422 validation failed (nothing applied) · 502 file unreadable/I-O failure
```

The endpoint **SHALL** load the file **server-side** from the given path, validate it, and apply it as a **REPLACE** — never a partial merge (CL-5).

#### Scenario: Request shape

- Given: A valid full configuration file at `D:\configs\server-config.json`
- When: `POST /api/config/import { "path": "D:\\configs\\server-config.json" }`
- Then: The response is 200 with the applied configuration and its restart-required field list

### Requirement: 2. Import File Schema (full — CL-5)

The imported file **SHALL** use the **same schema as `dataserver-admin-config.json`** (and the backend's data-server `appsettings.json` sections): `General` (Port, MinimumLogLevel, DataFolder), `DataStorage` (RetryAttempts, CacheSizeLimit, KeepDataOnRefresh, MaxCacheItemSizeBytes), `DatabaseQuery` (CommandTimeoutEnabled, CommandTimeLimit), `Https` (Enabled, PfxPath, PemCertPath, PemKeyPath, StoreLocation, StoreName, Thumbprint, SubjectName, Protocols), `Security` (Authorization.Enabled, CORS.AllowOrigin), `ReportSharingOptions` (EndpointEnabled, PathToFolder, ReportExpirationTime, MaxSizeOfFolder, AutoDeletingEnabled), and `DataServer.Indexes[]` (full `IndexDefinition` field set). Missing top-level sections **SHALL** be treated as absent (not defaulted-and-replaced) only when the whole section is omitted; **present-but-empty containers are invalid** for `DataServer.Indexes` (at least one index is required to replace an existing non-empty index set — see §4).

#### Scenario: Round-trip with the panel file

- Given: A `dataserver-admin-config.json` exported from a working panel
- When: It is imported on a fresh server
- Then: Settings and indexes match the source configuration exactly

### Requirement: 3. Transactional Validation

The server **SHALL** validate the **entire** file (settings sections via the existing validators + every index definition) **before** applying anything. On any validation error, it **SHALL** return 422 with field-level `errors` and apply **nothing** — the current configuration remains fully intact.

#### Scenario: Invalid index blocks the whole import

- Given: A file whose `General.Port` is valid but one index has a multi-statement query
- When: Import is requested
- Then: 422 lists the offending index/query error
- And: `GET /api/config/*` and `GET /api/indexes` show the previous configuration unchanged

#### Scenario: Invalid settings value blocks the whole import

- Given: A file with `General.Port: 99999`
- When: Import is requested
- Then: 422 with the port-range error; nothing applied

### Requirement: 4. Replace Semantics (settings AND indexes)

On success, the server **SHALL**:

- Apply every settings section as a full replacement of that section's current values (same application paths as `PUT /api/config/{section}` today).
- Replace the **entire index set**: indexes present in the file are created/updated (with their definitions); indexes **not** present are deleted; survivors reload per their `Enabled` semantics (disabled indexes stay unloaded).
- Persist the replaced index set atomically (`DataServer/indexes.json`) and settings per the existing per-section persistence in standalone mode.
- Compute `requiresRestartFields` with the **same rules** as today's PUTs (Port, DataFolder, HTTPS fields, CacheSizeLimit, ReportSharing.PathToFolder, …) — **diffed against the pre-import values** (only changed restart-required fields are listed).

#### Scenario: Index set replacement

- Given: Server has indexes A, B, C; import file defines A (modified) and D
- When: Import succeeds
- Then: `GET /api/indexes` lists exactly **A** (new definition) and **D**
- And: B and C are gone (their cache entries evicted)

#### Scenario: Restart fields diff correctly

- Given: Running on port 5285, HTTPS disabled; import file has port 5285, HTTPS enabled with a valid PEM pair
- When: Import succeeds
- Then: `requiresRestartFields` includes the HTTPS fields but **not** the port
- And: Restart binds TLS

### Requirement: 5. Failure Atomicity

The apply phase **SHALL** be atomic: the pre-import settings + index set are staged; any mid-apply failure **MUST** roll back to the staged state and return 502 with the error, leaving nothing half-applied.

#### Scenario: Mid-apply rollback

- Given: An import that fails while reloading an index (I/O error mid-apply)
- When: The failure occurs
- Then: The pre-import configuration is restored exactly
- And: 502 carries the underlying error message

### Requirement: 6. Secrets Handling

Imported secret values (`Https.PfxPassword`, any credential material) **SHALL** be applied in memory and persisted only via the existing sanitized stores; the response **MUST NOT** echo them; logs **MUST NOT** contain them (D6).

#### Scenario: Password applied but never echoed

- Given: Import file with a PFX password
- When: Import succeeds
- Then: HTTPS uses it after restart; the 200 response omits it; log lines contain no secret

### Requirement: 7. Auth & Guardrails

The endpoint **SHALL** require Basic Auth when enabled (401 otherwise), **SHALL NOT** accept relative/traversing paths (`..` in the resolved path ⇒ 422), and **SHALL** be excluded from CORS wildcard behavior per the existing auth-on origins rule.

#### Scenario: Path traversal rejected

- Given: A path like `../../etc/passwd`
- When: Import is requested
- Then: 422 with a path-validation error; no file access