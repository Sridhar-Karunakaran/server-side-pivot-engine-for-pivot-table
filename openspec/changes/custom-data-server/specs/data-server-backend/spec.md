# Syncfusion Pivot Data Server — Phase 1 Technical Specification

**Date:** 2024-12-19

**Version:** 2.1

---

## 1. Index Configuration

### 1.1 Index Registration from appsettings.json

**SHALL** load index definitions from `DataServer:Indexes` array in `appsettings.json` at startup and make them available immediately without manual file creation.

**Scenario 1.1.1: Index Declared in appsettings.json**
- Given: `appsettings.json` contains `DataServer:Indexes[0]` with `Index: "Sales-Data"`, `SourceType: "Csv"`, `SourcePath: "sales.csv"`
- When: Application starts
- Then: `GET /api/indexes/Sales-Data` returns 200 with `status: "Ready"` and `rowCount > 0`

**Scenario 1.1.2: Multiple Indexes Coexist**
- Given: appsettings defines 3 indexes (JSON, CSV, Database)
- When: `GET /api/indexes`
- Then: Returns 200 with all 3 indexes listed

### 1.2 Index Lifecycle APIs

**SHALL** support full index lifecycle via REST API: create, edit, reload, enable/disable, delete.

**Scenario 1.2.1: Create Index**
- Given: No index named "test-index"
- When: `POST /api/indexes { index: "test-index", name: "Test", sourceType: "Csv", sourcePath: "test.csv" }`
- Then: Returns 201 with `status: "Ready"` and loads data

**Scenario 1.2.2: Edit Index**
- Given: Index "Sales-Data" exists
- When: `PUT /api/indexes/Sales-Data { name: "Sales Data Updated", csvDelimiter: ";" }`
- Then: Returns 200, metadata updated, data reloaded

**Scenario 1.2.3: Attempt to Change SourceType**
- Given: Index "Sales-Data" with `sourceType: "Csv"`
- When: `PUT /api/indexes/Sales-Data { sourceType: "Json" }`
- Then: Returns 422 with error message "SourceType is immutable"

**Scenario 1.2.4: Reload Index**
- Given: Index "Sales-Data" with stale data
- When: `POST /api/indexes/Sales-Data/reload`
- Then: Returns 200 with updated `rowCount`

**Scenario 1.2.5: Enable/Disable Index**
- Given: Index "Sales-Data"
- When: `POST /api/indexes/Sales-Data/disable`
- Then: Returns 200; pivot requests to it return 423
- When: `POST /api/indexes/Sales-Data/enable`
- Then: Returns 200; index loads and serves again

**Scenario 1.2.6: Delete Index**
- Given: Index "Sales-Data"
- When: `DELETE /api/indexes/Sales-Data`
- Then: Returns 204; subsequent pivot requests return 404

### 1.3 Index Naming & Uniqueness

**SHALL** enforce case-insensitive unique index names matching pattern `[A-Za-z0-9._-]{1,64}`.

**Scenario 1.3.1: Duplicate Index Creation**
- Given: Index "Sales-Data" exists
- When: `POST /api/indexes { index: "sales-data" ... }` (lowercase variant)
- Then: Returns 409 "Index already exists"

**Scenario 1.3.2: Invalid Index Name**
- Given: No index
- When: `POST /api/indexes { index: "sales@data!" ... }`
- Then: Returns 422 "Invalid index name"

### 1.4 CSV Separator Configuration

**SHALL** honor CSV delimiter, decimal separator, and thousands separator options on load.

**Scenario 1.4.1: Custom CSV Delimiters**
- Given: CSV file with semicolon-delimited columns and comma as decimal separator
- When: Index created with `csvDelimiter: ";"`, `csvDecimalSeparator: ","`
- Then: Data loads correctly with proper field count and numeric precision

**Scenario 1.4.2: Edit Separators**
- Given: Index using default separators
- When: `PUT /api/indexes/sales-csv { csvDelimiter: "|" }`
- Then: Data reloaded with new delimiter honored

### 1.5 Field Mapping (Type Inference)

**SHALL** support optional per-field type mapping (Number, String, Date); disabled by default (autodetect).

**Scenario 1.5.1: Mapping Disabled (Default)**
- Given: Index with `mappingEnabled: false`
- When: Data loads
- Then: Field types autodetected; all fields accessible as discovered

**Scenario 1.5.2: Mapping Enabled**
- Given: Index with `mappingEnabled: true`, `fieldMappings: [{ fieldName: "Price", type: "Number" }]`
- When: Data loads
- Then: "Price" field enforced as Number type

**Scenario 1.5.3: Disable Mapping**
- Given: Index with `mappingEnabled: true`, custom mappings applied
- When: `PUT /api/indexes/{index} { mappingEnabled: false }`
- Then: Mappings deleted, autodetect restored

### 1.6 Database Test Connection

**SHALL** test database connection and return success/failure with latency.

**Scenario 1.6.1: Test Connection Success**
- Given: Index with valid `connectionStringName`
- When: `POST /api/indexes/{index}/test-connection`
- Then: Returns 200 `{ success: true, latencyMs: 42 }`

**Scenario 1.6.2: Test Connection Failure**
- Given: Index with invalid connection string
- When: `POST /api/indexes/{index}/test-connection`
- Then: Returns 502 `{ success: false, error: "Unable to connect: ..." }`

---

## 2. Pivot Request Routing

### 2.1 Index-Aware Pivot Endpoint

**SHALL** accept `index` field in pivot request and route to the named index; default to `DefaultIndexId` if absent.

**Scenario 2.1.1: Pivot Request with Index**
- Given: Indexes "Sales-Data" and "Sales-Db" registered
- When: `POST /api/pivot/post { index: "Sales-Data", action: "fetchFieldMembers", memberName: "Country" }`
- Then: Returns 200 with members from "Sales-Data" index

**Scenario 2.1.2: Pivot Request without Index (Legacy)**
- Given: `DefaultIndexId: "default"`
- When: `POST /api/pivot/post { action: "fetchFieldMembers", memberName: "Country" }` (no index field)
- Then: Returns 200 using "default" index (backward compatible)

**Scenario 2.1.3: Unknown Index**
- Given: No index named "unknown"
- When: `POST /api/pivot/post { index: "unknown", action: "..." }`
- Then: Returns 404 with valid index names listed

**Scenario 2.1.4: Disabled Index**
- Given: Index "Sales-Data" with `enabled: false`
- When: `POST /api/pivot/post { index: "Sales-Data", action: "..." }`
- Then: Returns 423 "Index is disabled"

### 2.2 Cache Key Prefixing

**SHALL** prefix all per-index cache entries with index name to prevent data collision.

**Scenario 2.2.1: Separate Cache per Index**
- Given: Two indexes "Sales-Data" and "Sales-Json" with different data
- When: Pivot requests to both
- Then: Cache keys are `data:Sales-Data` and `data:Sales-Json`; each serves correct data

---

## 3. Configuration Management

### 3.1 Configuration API

**SHALL** expose full configuration via REST API with merge-patch semantics.

**Scenario 3.1.1: Get Full Configuration**
- Given: Server running
- When: `GET /api/config`
- Then: Returns 200 with all 7 sections + indexes array

**Scenario 3.1.2: Get Single Section**
- Given: Server running
- When: `GET /api/config/general`
- Then: Returns 200 with only General settings

**Scenario 3.1.3: Update Configuration (Merge Patch)**
- Given: Current config
- When: `PUT /api/config { General: { MinimumLogLevel: "Warn" } }`
- Then: Returns 200 with updated config; Other settings unchanged; response includes `requiresRestart` flag

**Scenario 3.1.4: Invalid Configuration Update**
- Given: Current config
- When: `PUT /api/config { General: { Port: "invalid" } }`
- Then: Returns 422 with field-level errors; previous config unchanged

### 3.2 Requires Restart Flag

**SHALL** indicate which configuration changes require server restart.

**Scenario 3.2.1: Runtime-Applicable Change**
- Given: Port is 5285
- When: `PUT /api/config { General: { MinimumLogLevel: "Debug" } }`
- Then: Returns 200 with `requiresRestart: false`; change applies immediately

**Scenario 3.2.2: Requires-Restart Change**
- Given: Port is 5285
- When: `PUT /api/config { General: { Port: 5286 } }`
- Then: Returns 200 with `requiresRestart: true`; change does NOT apply until restart

---

## 4. Data Storage & Caching

### 4.1 Retry Policy

**SHALL** retry transient failures (IO, timeouts, transient DB errors) with exponential backoff up to `RetryAttempts`; non-transient failures fail fast.

**Scenario 4.1.1: Transient Failure with Retry Success**
- Given: File temporarily locked, then available
- When: Index loads with `retryAttempts: 3`
- Then: Retries (250ms, 500ms, 1s + jitter) until success or max attempts

**Scenario 4.1.2: Validation Failure (No Retry)**
- Given: CSV file with mismatched column counts
- When: Index loads
- Then: Fails immediately (422) without retry

### 4.2 Keep-Data-on-Refresh

**SHALL** retain old data if reload fails (when `keepDataOnRefresh: true`); discard on false.

**Scenario 4.2.1: Reload Failure with Keep**
- Given: Index enabled with `keepDataOnRefresh: true`, data loaded
- When: Reload fails (DB unavailable)
- Then: Returns 502 but old data continues serving

**Scenario 4.2.2: Reload Failure without Keep**
- Given: Index with `keepDataOnRefresh: false`, data loaded
- When: Reload fails
- Then: Returns 502; cache cleared, subsequent requests fail until reload succeeds

### 4.3 Cache Size Limits

**SHALL** enforce cache size limits per `CacheSizeLimit` and `MaxCacheItemSizeBytes`.

**Scenario 4.3.1: Cache Size Enforcement**
- Given: `CacheSizeLimit: 5`, 5 indexes loaded
- When: 6th index loaded
- Then: Least-recently-used index evicted to stay within limit

---

## 5. Database Query Settings

### 5.1 Command Timeout

**SHALL** enforce `CommandTimeLimit` on all database queries when `CommandTimeoutEnabled: true`.

**Scenario 5.1.1: Query Within Timeout**
- Given: `CommandTimeLimit: 30`, query executes in 5s
- When: Query runs
- Then: Completes normally

**Scenario 5.1.2: Query Exceeds Timeout**
- Given: `CommandTimeLimit: 30`, query hangs
- When: 30 seconds elapse
- Then: Command canceled, returns 504 "Gateway Timeout"

**Scenario 5.1.3: Timeout Disabled**
- Given: `CommandTimeoutEnabled: false`
- When: Query runs
- Then: Completes regardless of duration

### 5.2 Query Validation

**SHALL** validate queries: single SELECT statement only, no multi-statement, no DML.

**Scenario 5.2.1: Valid SELECT Query**
- Given: Query: "SELECT * FROM Sales"
- When: Index with query created
- Then: Returns 201, data loads

**Scenario 5.2.2: Multi-Statement Query Rejected**
- Given: Query: "SELECT * FROM Sales; DELETE FROM Audit"
- When: Index creation attempted
- Then: Returns 422 "Invalid query format"

### 5.3 Read-Only Guard

**SHALL** reject queries that appear to modify data.

**Scenario 5.3.1: INSERT Rejected**
- Given: Query: "INSERT INTO Sales VALUES (...)"
- When: Index creation attempted
- Then: Returns 422 "Only SELECT statements allowed"

---

## 6. HTTPS Settings

### 6.1 Certificate Resolution

**SHALL** resolve X509Certificate2 from PFX, PEM, or Windows certificate store.

**Scenario 6.1.1: PFX Certificate**
- Given: `Https.Enabled: true`, `PfxPath: "cert.pfx"`, `PfxPassword: "***"`
- When: Server starts
- Then: HTTPS listens on configured port with PFX certificate

**Scenario 6.1.2: PEM Certificate**
- Given: `Https.Enabled: true`, `PemCertPath: "cert.pem"`, `PemKeyPath: "key.pem"`
- When: Server starts
- Then: HTTPS listens with PEM certificate

**Scenario 6.1.3: Windows Store Certificate**
- Given: `Https.Enabled: true`, `Thumbprint: "ABC123..."` on Windows
- When: Server starts
- Then: HTTPS listens with store certificate (guarded by `OperatingSystem.IsWindows()`)

### 6.2 Protocol Configuration

**SHALL** support HTTP/1, HTTP/2, or both per `Protocols` setting.

**Scenario 6.2.1: HTTP1_AND_HTTP2**
- Given: `Protocols: "HTTP1_AND_HTTP2"`
- When: Client connects
- Then: HTTP/2 negotiated if supported; HTTP/1.1 fallback available

**Scenario 6.2.2: HTTP1 Only**
- Given: `Protocols: "HTTP1"`
- When: Client attempts HTTP/2
- Then: HTTP/1.1 served

---

## 7. Security & Authentication

### 7.1 Basic Authentication

**SHALL** enforce Basic Authentication when `BasicAuthEnabled: true`; allow all when false.

**Scenario 7.1.1: Auth Enabled, Valid Credentials**
- Given: `BasicAuthEnabled: true`, user "admin" created
- When: Request with `Authorization: Basic base64(admin:password)`
- Then: Returns 200 (allowed)

**Scenario 7.1.2: Auth Enabled, Missing Credentials**
- Given: `BasicAuthEnabled: true`
- When: Request without Authorization header
- Then: Returns 401 with `WWW-Authenticate: Basic realm`

**Scenario 7.1.3: Auth Disabled**
- Given: `BasicAuthEnabled: false`
- When: Request without Authorization
- Then: Returns 200 (allowed)

### 7.2 User Management

**SHALL** support user CRUD with PBKDF2 hashing and constant-time verification.

**Scenario 7.2.1: Create User**
- Given: No user "analyst"
- When: `POST /api/security/users { username: "analyst", password: "secret" }`
- Then: Returns 201; password stored as hash only

**Scenario 7.2.2: Update Password**
- Given: User "analyst" exists
- When: `PUT /api/security/users/analyst { password: "newsecret" }`
- Then: Returns 200; new password hash persisted

**Scenario 7.2.3: Delete User**
- Given: User "analyst" exists
- When: `DELETE /api/security/users/analyst`
- Then: Returns 204; user no longer authenticates

**Scenario 7.2.4: List Users (No Password)**
- Given: Users exist
- When: `GET /api/security/users`
- Then: Returns 200 with usernames and timestamps; no password hashes

### 7.3 CORS Origin Validation

**SHALL** validate request origin against `AllowedOrigins` list.

**Scenario 7.3.1: Wildcard Allow**
- Given: `AllowedOrigins: ["*"]`
- When: Request from any origin
- Then: CORS allowed

**Scenario 7.3.2: Specific Origins**
- Given: `AllowedOrigins: ["https://app.example.com", "https://admin.example.com"]`
- When: Request from https://app.example.com
- Then: CORS allowed
- When: Request from https://other.com
- Then: CORS rejected (403)

**Scenario 7.3.3: No Origin Header**
- Given: `AllowedOrigins: ["https://app.example.com"]`
- When: Browser request without Origin header
- Then: CORS rejected (403) — CSRF defense

---

## 8. Report Sharing

### 8.1 Save Report

**SHALL** save report payload with metadata and return reportId.

**Scenario 8.1.1: Save Report**
- Given: `EndpointEnabled: true`
- When: `POST /api/reportsharing/reports { name: "Q4-Review", owner: "analyst@corp", payload: {...} }`
- Then: Returns 201 `{ reportId: "3f2b...", createdAt: "...", expiresAt: null }`

**Scenario 8.1.2: Endpoint Disabled**
- Given: `EndpointEnabled: false`
- When: `POST /api/reportsharing/reports { ... }`
- Then: Returns 404 "Endpoint not available"

### 8.2 Retrieve Report

**SHALL** retrieve report by ID; return 410 if expired.

**Scenario 8.2.1: Get Unexpired Report**
- Given: Report created, not expired
- When: `GET /api/reportsharing/reports/{reportId}`
- Then: Returns 200 with payload

**Scenario 8.2.2: Get Expired Report**
- Given: Report with `expiresAt: <now>`
- When: `GET /api/reportsharing/reports/{reportId}`
- Then: Returns 410 "Gone"

### 8.3 Expiry & Quota

**SHALL** enforce expiration time and folder size quota; auto-delete when `AutoDelete: true`.

**Scenario 8.3.1: Expiration**
- Given: `ReportExpirationTimeMinutes: 60`
- When: Report saved at T, accessed at T+61
- Then: Returns 410

**Scenario 8.3.2: Quota Exceeded**
- Given: `MaxFolderSizeMB: 100`, reports total 105 MB
- When: Sweep runs (every 5 minutes)
- Then: Oldest reports deleted until under 100 MB

**Scenario 8.3.3: Save with Quota Full**
- Given: `MaxFolderSizeMB: 100`, reports total 100 MB
- When: `POST /api/reportsharing/reports { ... }` (50 MB)
- Then: Returns 507 "Insufficient Storage"

---

## 9. Configuration Persistence & Hot-Reload

### 9.1 Atomic JSON Writes

**SHALL** write JSON files atomically (temp-file-rename); quarantine corrupt files.

**Scenario 9.1.1: Atomic Write Success**
- Given: Server running, PUT /api/config called
- When: Configuration updated
- Then: Temp file created, renamed to config.json (atomic)

**Scenario 9.1.2: Corrupt File Recovery**
- Given: config.json corrupted mid-write by crash
- When: Server restarts
- Then: Corrupt file quarantined as `config.json.corrupt.{timestamp}`; defaults loaded; log warning

### 9.2 Hot-Reload Configuration File Watching

**SHALL** watch config.json for changes; apply runtime-applicable options immediately; flag restart-required options.

**Scenario 9.2.1: MinimumLogLevel Change**
- Given: config.json has `MinimumLogLevel: "Info"`, file watcher running
- When: User edits config.json to `MinimumLogLevel: "Debug"`, saves
- Then: Within 1 s, log level changes; existing logs show Debug entries

**Scenario 9.2.2: Port Change**
- Given: config.json edited to `Port: 9501`
- When: File watcher detects change
- Then: Logs warning "Port change requires restart"; port does NOT change

**Scenario 9.2.3: Invalid JSON**
- Given: config.json corrupted
- When: File watcher attempts reload
- Then: Logs error "Config validation failed"; keeps last-known-good config; continues serving

---

## 10. Logging

### 10.1 Structured Logging

**SHALL** log to console and rolling JSON file with structured fields (category, correlationId, timestamp, etc.).

**Scenario 10.1.1: Structured Log Output**
- Given: Config update via API
- When: `PUT /api/config { ... }`
- Then: Log file contains `{ timestamp, level: "Info", category: "Config", message: "Configuration updated", correlationId: "...", appliedSections: [...] }`

### 10.2 Log Level Filtering

**SHALL** filter logs per `MinimumLogLevel`.

**Scenario 10.2.1: Info Minimum**
- Given: `MinimumLogLevel: "Info"`
- When: Debug log written
- Then: Not logged; Info/Warn/Error logged

**Scenario 10.2.2: Warn Minimum**
- Given: `MinimumLogLevel: "Warn"`
- When: Info log written
- Then: Not logged; Warn/Error logged

---

## 11. Error Handling

### 11.1 Problem Details (RFC 7807)

**SHALL** return RFC 7807 problem details with status code, type, title, detail, and traceId.

**Scenario 11.1.1: Configuration Validation Error**
- Given: Invalid config update
- When: `PUT /api/config { General: { Port: 99999 } }`
- Then: Returns 422 with problem details:
  ```json
  {
    "type": "https://dataserver.local/errors/config-validation",
    "title": "Configuration validation failed",
    "status": 422,
    "detail": "Port must be between 1 and 65535.",
    "traceId": "...",
    "errors": { "General.Port": ["Port must be between 1 and 65535."] }
  }
  ```

### 11.2 Error Status Codes

**SHALL** return appropriate HTTP status codes:

| Condition | HTTP |
|-----------|------|
| Configuration validation | 422 |
| Unknown config section | 404 |
| Index not found | 404 |
| Duplicate index | 409 |
| Index disabled | 423 |
| Database timeout | 504 |
| Report expired | 410 |
| Quota exceeded | 507 |
| Bad credentials | 401 |
| Origin not allowed | 403 |
| Path traversal | 403 |

---

## 12. Client-Side Integration

### 12.1 Index Passing in Pivot Requests

**SHALL** accept and handle `index` field in pivot request.

**Scenario 12.1.1: Sample Passes Index**
- Given: Sample/pivot.js with `PIVOT_INDEX = "Sales-Data"`
- When: Pivot table renders and requests data
- Then: Every request includes `index: "Sales-Data"`

**Scenario 12.1.2: Backward Compatibility**
- Given: Existing client code without index field
- When: Request sent
- Then: Uses `DefaultIndexId`; pivot table works unchanged

---

## 13. Backward Compatibility

### 13.1 Legacy PivotDataSource Migration

**SHALL** auto-migrate legacy `PivotDataSource` block to index "default" on first startup.

**Scenario 13.1.1: One-Time Migration**
- Given: `appsettings.json` contains legacy `PivotDataSource` (Type, JsonPath, CsvPath, etc.)
- When: Application starts for first time
- Then: Creates index "default" from legacy config; persists to indexes.json; subsequent starts use indexes.json

**Scenario 13.1.2: Existing DataServer Configuration**
- Given: `appsettings.json` contains both legacy `PivotDataSource` and new `DataServer:Indexes`
- When: Application starts
- Then: Uses `DataServer:Indexes`; legacy block ignored (no migration)

---

## Acceptance Criteria Summary

All acceptance criteria from the specification document (§22) SHALL be satisfied:

- [x] Index declared in appsettings.json available after startup
- [x] Multiple indexes (JSON/CSV/DB) coexist independently
- [x] Pivot request with index routes correctly
- [x] Pivot request without index uses DefaultIndexId (legacy)
- [x] Unknown index returns 404 with valid list
- [x] Disabled index returns 423
- [x] Edit index (all except sourceType) works
- [x] SourceType change rejected (422)
- [x] Manual reload works
- [x] Auto-reload (by interval) works
- [x] Enable/disable works
- [x] Mapping (Number/String/Date) works
- [x] CSV separators honored
- [x] Test-connection works
- [x] All configuration options editable and persistent
- [x] Hot-reload applies runtime changes (§12.5)
- [x] User CRUD works
- [x] Basic Auth works (401 without, 200 with)
- [x] CORS validation works (403 for disallowed)
- [x] Reports stored/retrieved/expired/deleted
- [x] Retry policy works (transient + backoff)
- [x] Keep-data-on-refresh works
- [x] Cache size limits enforced
- [x] Database timeout enforced
- [x] HTTPS certificates (PFX/PEM/store) work
- [x] Logging structured and filterable
- [x] Error handling (problem details, status codes)
- [x] Sample passes index
- [x] Backward compatibility maintained

