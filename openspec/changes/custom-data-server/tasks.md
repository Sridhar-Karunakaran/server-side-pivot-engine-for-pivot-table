# Syncfusion Pivot Data Server — Phase 1 Implementation Tasks

**Date:** 2024-12-19

**Status:** Ready for Implementation

---

## Overview

This document outlines the 40+ implementation tasks organized into 8 phases corresponding to the specification sections. Each task includes acceptance criteria mapped to testable requirements (spec.md) and verification steps.

**Prerequisites:**
- All specification documents approved (EXPLORATION, proposal, design, spec.md)
- OpenSpec status updated to "approved"
- No implementation begins until this document is reviewed and signed off

---

## Phase 1: Index Configuration (Step 1 — Foundational)

*Estimated: 3-4 days*

This phase is critical; all subsequent features depend on named indexes and IndexManager.

### 1.1 Create IndexDefinition Model

**Objective:** Implement data model for index metadata with validation

**Subtasks:**
- [ ] Create file: `PivotController/Configuration/Models/IndexDefinition.cs`
  - Properties: Index, Name, SourceType, SourcePath, CsvDelimiter, CsvDecimalSeparator, CsvThousandsSeparator, DatabaseType, ConnectionStringName, Query, AutoReload, ReloadIntervalMinutes, MappingEnabled, FieldMappings[], Enabled, CreatedAt, UpdatedAt
  - Validation: Index name pattern `[A-Za-z0-9._-]{1,64}`, SourceType immutable on edit
- [ ] Create file: `PivotController/Configuration/Models/IndexFieldMapping.cs`
  - Properties: FieldName, Type (Number | String | Date)
- [ ] Create file: `PivotController/Configuration/Models/IndexStatus.cs`
  - Enum: Ready | Refreshing | Failed | Disabled
  - Include: IndexName, Status, RowCount, LoadedAt, LastError

**Verification:**
- [ ] Models compile with no warnings
- [ ] Default values match spec (MappingEnabled=false, AutoReload=false, Enabled=true)
- [ ] Serialization/deserialization works (JSON round-trip)

**Specification Mapping:** 1.1, 1.3, 1.5

---

### 1.2 Implement IndexManager (Core Cache Hub)

**Objective:** Central service for index data management and cache coordination

**Subtasks:**
- [ ] Create file: `PivotController/Indexing/IIndexManager.cs` (interface)
  - Methods: GetDataAsync, CreateIndexAsync, EditIndexAsync, ReloadIndexAsync, EnableIndexAsync, DisableIndexAsync, DeleteIndexAsync, GetIndexStatus, ListIndexes
- [ ] Create file: `PivotController/Indexing/IndexManager.cs` (implementation)
  - Dependencies: IMemoryCache, DataServerConfiguration, IDataSourceProviderFactory, ILogger
  - Per-index SemaphoreSlim for reload protection
  - Keep-data-on-refresh logic (dual-slot cache entry)
  - Index registry (_indexes dictionary, case-insensitive)
- [ ] Create file: `PivotController/Indexing/IndexReloadWorker.cs` (BackgroundService)
  - Scans indexes with AutoReload=true + ReloadIntervalMinutes>0
  - Triggers reload at configured intervals from server start
  - Handles reload failures per KeepDataOnRefresh

**Verification:**
- [ ] IndexManager injects without DI errors
- [ ] CreateIndexAsync persists metadata and loads data
- [ ] Cache keys use `data:{index}` and `engine:{index}:{hash}` format
- [ ] Concurrent reload protected by SemaphoreSlim (no data corruption)
- [ ] Keep-data-on-refresh swaps atomically

**Specification Mapping:** 1.2, 4.1, 4.2, 4.3

---

### 1.3 Bootstrap Index Configuration from appsettings.json

**Objective:** Load indexes from appsettings.json at startup; one-time legacy migration

**Subtasks:**
- [ ] Create file: `PivotController/Configuration/Services/ConfigurationPersistenceService.cs`
  - LoadAsync() — reads DataServer section from config
  - SaveAsync() — atomically writes config.json using AtomicJsonStore
  - EnsureFilesAsync() — creates DataServer directory, initializes missing files
- [ ] Modify `PivotController/Program.cs`:
  - Call `ConfigurationPersistenceService.EnsureFilesAsync()`
  - Load configuration from appsettings.json with defaults
  - Check for legacy `PivotDataSource` block
  - If exists: create index "default" from it; save to DataServer/indexes.json
  - Register all enabled indexes via `IndexManager.RegisterAllAsync()`
  - Configure cache sizing from `DataStorage.CacheSizeLimit`
- [ ] Create file: `PivotController/DataServer/.gitignore`
  - Ignore: `config.json`, `indexes.json`, `users.json`, `reports/`, `logs/`, `*.corrupt.*`

**Verification:**
- [ ] Application starts; `DataServer/` directory created
- [ ] Indexes from appsettings.json registered and available via `GET /api/indexes`
- [ ] Legacy `PivotDataSource` migrated to "default" index (one-time only)
- [ ] Disabled indexes not preloaded; enabled indexes load successfully
- [ ] Cache size limit applied (`MemoryCache.Options.SizeLimit` set)

**Specification Mapping:** 1.1, 1.2, 12.1, 12.3

---

### 1.4 Index-Aware Pivot Routing

**Objective:** Modify PivotController to route requests via IndexManager using index name

**Subtasks:**
- [ ] Modify `FetchData` class (in existing code) to add optional `Index` property
- [ ] Modify `PivotController/Controllers/PivotController.cs`:
  - Inject `IIndexManager`
  - In `Post()` method:
    - Extract `FetchData.Index` (fallback to `_config.DefaultIndexId`)
    - Call `_indexManager.GetIndexStatus(indexName)` to validate
    - Return 404 if not found (list valid indexes)
    - Return 423 if disabled
    - Call `_indexManager.GetDataAsync(indexName)` instead of `GetData()`
  - Prefix cache keys: `engine:{indexName}:{hash}` (use helper method)
  - All existing pivot actions (fetchFieldMembers, fetchRawData, exports) continue working with index-prefixed data

**Verification:**
- [ ] Pivot request with `index` field routes to correct index data
- [ ] Pivot request without `index` uses `DefaultIndexId` (backward compatible)
- [ ] Unknown index returns 404 with valid index list
- [ ] Disabled index returns 423
- [ ] Existing pivot actions work on routed index data

**Specification Mapping:** 2.1, 2.2, 12.1

---

### 1.5 Index Management REST APIs

**Objective:** Implement full index lifecycle endpoints

**Subtasks:**
- [ ] Create file: `PivotController/Configuration/Controllers/IndexController.cs`
  - `GET /api/indexes` — list all indexes with status
  - `GET /api/indexes/{index}` — index detail + status (404 if not found)
  - `POST /api/indexes` — create index (409 if duplicate)
    - Request: IndexDefinition
    - Response: 201 `{ index, status, rowCount, loadedAt, lastError }`
  - `PUT /api/indexes/{index}` — edit index (all except sourceType)
    - Reject if sourceType changed (422)
    - Reload data after metadata saved
    - Response: 200 `{ index, status, rowCount, loadedAt, lastError }`
  - `POST /api/indexes/{index}/reload` — manual reload
    - Response: 200 `{ status, rowCount, lastError }` or 502 on failure
    - Honor KeepDataOnRefresh
  - `POST /api/indexes/{index}/enable` — enable + load
    - Response: 200 `{ status, rowCount }`
  - `POST /api/indexes/{index}/disable` — disable + evict
    - Response: 200 `{ status: "Disabled" }`
  - `DELETE /api/indexes/{index}` — delete metadata + evict cache
    - Response: 204
  - `POST /api/indexes/{index}/test-connection` — test DB connection (DB indexes only)
    - Response: 200 `{ success: true, latencyMs }` or 502 `{ success: false, error }`

**Verification:**
- [ ] POST /api/indexes creates index, loads data, persists metadata
- [ ] PUT /api/indexes edits metadata, reloads data
- [ ] Attempt to change sourceType rejected with 422
- [ ] Duplicate index name rejected with 409
- [ ] Enable/disable works; disabled indexes return 423 on pivot request
- [ ] Test-connection returns latency or error

**Specification Mapping:** 1.2, 1.6, 9.1

---

### 1.6 CSV Separator Options

**Objective:** Support CSV delimiter, decimal separator, thousands separator

**Subtasks:**
- [ ] Modify `PivotController/Data/Providers/CsvDataProvider.cs` (or create if not exists):
  - Constructor: accept IndexDefinition with CSV separator options
  - LoadAsync(): use CsvDelimiter, CsvDecimalSeparator, CsvThousandsSeparator on parse
  - Handle edge cases (multi-char separators, escaping)
  - Test with various CSV formats (European decimals, thousands, etc.)

**Verification:**
- [ ] CSV file with semicolon delimiter parsed correctly
- [ ] Decimal separator (comma vs period) honored in numeric fields
- [ ] Thousands separator handled properly
- [ ] Edit index to change separators; data reloaded with new options

**Specification Mapping:** 1.4

---

## Phase 2: General & Data Storage Settings

*Estimated: 1-2 days*

### 2.1 Create Configuration Models

**Objective:** Implement all 7 configuration sections per spec

**Subtasks:**
- [ ] Create files in `PivotController/Configuration/Models/`:
  - `GeneralSettings.cs` — Port, MinimumLogLevel, DataFolder
  - `DataStorageSettings.cs` — RetryAttempts, CacheSizeLimit, KeepDataOnRefresh, MaxCacheItemSizeBytes
  - `DataServerConfiguration.cs` (root) — ConfigVersion, DefaultIndexId, all 7 sections, Indexes[]
- [ ] Add defaults per spec (§6, §7)

**Verification:**
- [ ] All properties with correct types and defaults
- [ ] JSON serialization matches expected structure

**Specification Mapping:** 6.1, 6.2, 7

---

### 2.2 Configuration Validation

**Objective:** Implement per-section and cross-field validation

**Subtasks:**
- [ ] Create file: `PivotController/Configuration/Validation/ConfigurationValidator.cs`
  - Validate per-section rules (Port 1-65535, RetryAttempts 0-30, etc.)
  - Cross-field rules:
    - `DefaultIndexId` must exist
    - If `ReloadIntervalMinutes>0`, must have `AutoReload=true`
    - If `EndpointEnabled=true` (reports), `StoragePath` must be writable
  - Return FieldErrors dictionary for problem details
- [ ] Add validation attributes to model properties where appropriate

**Verification:**
- [ ] Invalid Port (0, 99999) rejected
- [ ] RetryAttempts < 0 rejected
- [ ] DefaultIndexId not existing rejected
- [ ] Validation returns clear field-level errors

**Specification Mapping:** 17.3

---

### 2.3 Configuration Service

**Objective:** Orchestrate configuration updates with persistence

**Subtasks:**
- [ ] Create file: `PivotController/Configuration/Services/ConfigurationService.cs`
  - UpdateAsync(patch) — merge patch with current config
  - Validate merged result
  - Persist to DataServer/config.json (atomic)
  - Apply runtime changes per rules (§12.5)
  - Return { config, requiresRestart }
- [ ] Implement RequiresRestart detection logic

**Verification:**
- [ ] MinimumLogLevel change applied immediately (requiresRestart=false)
- [ ] Port change flagged as requiresRestart=true
- [ ] Invalid update rejected with 422; config unchanged

**Specification Mapping:** 3.1, 3.2

---

## Phase 3: Database Query Settings

*Estimated: 1-2 days*

### 3.1 Database Provider Abstraction

**Objective:** Implement pluggable database provider pattern

**Subtasks:**
- [ ] Create file: `PivotController/Data/Providers/IDatabaseQueryProvider.cs`
  - Properties: SourceType
  - Methods: CreateConnection(connectionString)
- [ ] Create file: `PivotController/Data/Providers/SqliteQueryProvider.cs` (mandatory)
  - Implement for SQLite connections
- [ ] Create stub files (optional, documented):
  - `SqlServerQueryProvider.cs` (requires System.Data.SqlClient package)
  - `PostgresqlQueryProvider.cs` (requires Npgsql package)
  - `MySqlQueryProvider.cs` (requires MySqlConnector package)
  - etc.
- [ ] Create file: `PivotController/Data/Providers/DatabaseDataProvider.cs`
  - Implement IDataSourceProvider
  - Use IDatabaseQueryProvider factory to create connection
  - Execute query with timeout
  - Map results to ExpandoObject[]

**Verification:**
- [ ] SQLite provider works (mandatory)
- [ ] Optional providers gracefully missing (reflection-registered only if package present)
- [ ] Query execution returns data or timeout error

**Specification Mapping:** 5.1, 5.2, 5.3, 14.1

---

### 3.2 Query Timeout Enforcement

**Objective:** Enforce CommandTimeLimit on queries

**Subtasks:**
- [ ] Modify `DatabaseDataProvider`:
  - If `CommandTimeoutEnabled=true`, set `command.CommandTimeout = CommandTimeLimit`
  - If `CommandTimeLimit=0`, set `CommandTimeout = 0` (infinite)
  - On timeout: catch timeout exception, log, return 504

**Verification:**
- [ ] Query within timeout completes normally
- [ ] Long-running query canceled at timeout threshold; returns 504
- [ ] Timeout disabled (0): query runs indefinitely

**Specification Mapping:** 5.1

---

## Phase 4: HTTPS Settings

*Estimated: 1 day*

### 4.1 Certificate Service

**Objective:** Resolve X509Certificate2 from PFX, PEM, or Windows store

**Subtasks:**
- [ ] Create file: `PivotController/Core/Security/CertificateService.cs`
  - ResolveCertificate(HttpsSettings) — returns X509Certificate2
  - Support:
    - PFX: `new X509Certificate2(pfxPath, password)`
    - PEM: `X509Certificate2.CreateFromPemFile(certPath, keyPath)`
    - Store: lookup by `StoreLocation`, `StoreName`, `Thumbprint` (Windows-only, guarded)
  - Validation: exactly one source must be configured

**Verification:**
- [ ] PFX certificate loaded
- [ ] PEM certificate loaded
- [ ] Windows store certificate loaded (if on Windows)
- [ ] Invalid certificate paths fail with clear error

**Specification Mapping:** 6.4, 15.1

---

### 4.2 Kestrel HTTPS Configuration

**Objective:** Configure Kestrel listener with HTTPS and protocol options

**Subtasks:**
- [ ] Modify `PivotController/Program.cs`:
  - `builder.WebHost.ConfigureKestrel()`:
    - If `Https.Enabled=true`:
      - Resolve certificate via CertificateService
      - `options.Listen(IPAddress.Any, Port, lo => lo.UseHttps(cert))`
      - Map protocols: HTTP1 | HTTP2 | HTTP1_AND_HTTP2
    - Set TLS 1.2+ enforced
  - `app.UseHttpsRedirection()` when HTTPS enabled

**Verification:**
- [ ] HTTPS listener active on configured port
- [ ] HTTP/1.1 and/or HTTP/2 negotiated per setting
- [ ] TLS 1.2+ enforced

**Specification Mapping:** 15.1, 6.4

---

## Phase 5: Security Settings

*Estimated: 1-2 days*

### 5.1 User Model & Password Hashing

**Objective:** Implement PBKDF2 user authentication

**Subtasks:**
- [ ] Create file: `PivotController/Core/Security/PBKDF2PasswordHasher.cs`
  - Hash(password) — PBKDF2 with 100,000 iterations, SHA-256
  - Verify(password, hash) — using constant-time comparison
- [ ] Create file: `PivotController/Core/Security/ConstantTimeComparer.cs`
  - Compare two byte arrays in constant time (no early exit on mismatch)
- [ ] Create file: `PivotController/Security/Models/User.cs`
  - Properties: Username, PasswordHash, CreatedAt, UpdatedAt

**Verification:**
- [ ] Hashed password matches spec format `PBKDF2$100000$SHA256$saltB64$hashB64`
- [ ] Verification succeeds for correct password
- [ ] Verification fails for wrong password
- [ ] Constant-time comparison prevents timing attacks

**Specification Mapping:** 7.1, 8

---

### 5.2 User Service & CRUD APIs

**Objective:** Manage users (create, update, delete, list)

**Subtasks:**
- [ ] Create file: `PivotController/Security/Services/UserService.cs`
  - Dependencies: IIndexStore (for users.json persistence), ILogger
  - CreateAsync(username, password) — hash password, save user
  - UpdatePasswordAsync(username, newPassword) — update hash
  - DeleteAsync(username)
  - GetAsync(username) — load user
  - ListAsync() — all users (no password hashes)
  - VerifyAsync(username, password) — for auth
- [ ] Create file: `PivotController/Security/Controllers/SecurityController.cs`
  - `GET /api/security/users` — list users (401 if auth required)
  - `POST /api/security/users` — create user
  - `PUT /api/security/users/{username}` — update password
  - `DELETE /api/security/users/{username}` — delete user

**Verification:**
- [ ] User created; password stored as hash
- [ ] User verified via correct password
- [ ] User not verified via wrong password
- [ ] User deleted; cannot authenticate
- [ ] List endpoint returns no password hashes

**Specification Mapping:** 7.1, 7.2

---

### 5.3 Basic Authentication Middleware

**Objective:** Implement Basic Auth middleware for securing endpoints

**Subtasks:**
- [ ] Create file: `PivotController/Core/Security/PivotBasicAuthMiddleware.cs`
  - Extract Authorization header
  - Decode Base64
  - Split on first ':'
  - Verify username/password via UserService
  - Set ClaimsPrincipal or return 401 with WWW-Authenticate header
- [ ] Modify `PivotController/Program.cs`:
  - Register middleware in pipeline (before UseAuthorization)
  - Gate protected endpoints with `[Authorize]` or check in controller

**Verification:**
- [ ] Request without Authorization returns 401
- [ ] Request with valid credentials succeeds (200)
- [ ] Request with invalid credentials returns 401
- [ ] Disabled BasicAuth allows all requests

**Specification Mapping:** 7.1, 15.2

---

### 5.4 CORS Origin Validation

**Objective:** Enforce AllowedOrigins configuration

**Subtasks:**
- [ ] Create file: `PivotController/Core/Security/OriginValidator.cs`
  - Build CORS policy from Security.AllowedOrigins
  - AllowedOrigins=["*"] → AllowAnyOrigin
  - Otherwise: exact match against list
- [ ] Modify `PivotController/Program.cs`:
  - Register CORS middleware with OriginValidator policy
  - Reject no-Origin header when origins restricted (CSRF defense)

**Verification:**
- [ ] Wildcard AllowedOrigins allows any origin
- [ ] Specific origins: allowed origin succeeds, disallowed rejected (403)
- [ ] No-Origin header rejected when restricted

**Specification Mapping:** 7.3, 15.3

---

## Phase 6: Report Sharing Settings

*Estimated: 1 day*

### 6.1 Report Sharing Service

**Objective:** Manage report storage with expiration and quota

**Subtasks:**
- [ ] Create file: `PivotController/Reports/Models/ReportMetadata.cs`
  - Properties: ReportId, Name, Owner, CreatedAt, ExpiresAt, SizeBytes
- [ ] Create file: `PivotController/Reports/Services/ReportSharingService.cs`
  - SaveAsync(name, owner, payload) — store report, return reportId
  - GetAsync(reportId) — retrieve report; return 410 if expired
  - DeleteAsync(reportId) — delete report
  - ListAsync() — list all report metadata
  - CheckQuotaAsync() — verify folder size limit

**Verification:**
- [ ] Report saved and retrieved successfully
- [ ] Expired report returns 410
- [ ] Quota check prevents save when full; returns 507
- [ ] Delete removes report

**Specification Mapping:** 8.1, 8.2, 8.3, 16.1

---

### 6.2 Report Sweep Worker

**Objective:** Auto-delete expired reports and maintain quota

**Subtasks:**
- [ ] Create file: `PivotController/Reports/Services/ReportSweepWorker.cs` (BackgroundService)
  - Runs every 5 minutes (when AutoDelete=true)
  - Remove reports where `ExpiresAt < now`
  - If quota exceeded, evict oldest until under limit
  - Log sweep results

**Verification:**
- [ ] Expired reports deleted after sweep
- [ ] Quota maintained (no folder size exceed after sweep)

**Specification Mapping:** 16.2, 8.3

---

### 6.3 Report APIs

**Objective:** Expose report endpoints

**Subtasks:**
- [ ] Create file: `PivotController/Reports/Controllers/ReportsController.cs`
  - `POST /api/reportsharing/reports` — save report
    - Request: { name, owner, payload }
    - Response: 201 `{ reportId, createdAt, expiresAt, sizeBytes }`
  - `GET /api/reportsharing/reports/{reportId}` — retrieve report
    - Response: 200 or 410 if expired
  - `GET /api/reportsharing/reports` — list report metadata
  - `DELETE /api/reportsharing/reports/{reportId}` — delete report

**Verification:**
- [ ] Report saved via API
- [ ] Report retrieved via API
- [ ] Expired report returns 410
- [ ] List shows all reports

**Specification Mapping:** 8.1, 8.2, 9.1

---

## Phase 7: Persistence, Logging, APIs, Hot-Reload

*Estimated: 2-3 days*

### 7.1 Atomic JSON Store

**Objective:** Persist JSON files safely with corruption recovery

**Subtasks:**
- [ ] Create file: `PivotController/Core/Persistence/AtomicJsonStore.cs`
  - Save<T>(filePath, obj) — temp-file-rename pattern
  - Load<T>(filePath, defaultValue) — load with corruption handling
  - On corruption: quarantine as `{file}.corrupt.{timestamp}`; return default; log warning

**Verification:**
- [ ] Save creates temp file, renames to target atomically
- [ ] Corrupt JSON quarantined; application continues with defaults

**Specification Mapping:** 11, 12.5

---

### 7.2 Configuration File Watcher

**Objective:** Hot-reload configuration changes from config.json

**Subtasks:**
- [ ] Modify/create `PivotController/Configuration/Services/ConfigurationFileWatcher.cs`
  - FileSystemWatcher on `DataServer/*.json`
  - 500 ms debounce for editor writes
  - Load and validate new config
  - Compute delta vs. current
  - Apply runtime-applicable options (§12.5 table)
  - Log requiresRestart options as warnings
  - On validation error: keep last-known-good; log error

**Verification:**
- [ ] MinimumLogLevel changed in config.json; reflected in logs within 1 s
- [ ] Port changed in config.json; logged as requiresRestart
- [ ] Invalid JSON in config.json; error logged; previous config retained

**Specification Mapping:** 12.5

---

### 7.3 Logging Infrastructure

**Objective:** Structured logging to console and rolling file

**Subtasks:**
- [ ] Create file: `PivotController/Core/Logging/RollingFileLoggerProvider.cs`
  - Logs to `logs/dataserver-{yyyyMMdd}.jsonl`
  - JSON format: { timestamp, level, category, message, correlationId, ... }
  - 30-day retention (archive/delete old files)
  - Structured fields per category
- [ ] Modify `PivotController/Program.cs`:
  - ClearProviders()
  - AddConsole()
  - Add RollingFileLoggerProvider
  - Filter minimum level per config

**Verification:**
- [ ] Logs written to console
- [ ] Logs written to rolling JSON file
- [ ] MinimumLogLevel filter applied (Debug records absent at Info)

**Specification Mapping:** 17.1

---

### 7.4 Exception Handling & Problem Details

**Objective:** Standardized error responses per RFC 7807

**Subtasks:**
- [ ] Create file: `PivotController/Core/Http/DataServerExceptionFilter.cs` (IExceptionFilter)
  - Map exceptions to HTTP status codes
  - Generate ProblemDetails with type, title, detail, status, traceId
  - Include field-level errors for validation exceptions
  - Add category and correlationId to extensions
- [ ] Register filter in Program.cs

**Verification:**
- [ ] 422 validation error returns problem details with field-level errors
- [ ] 404 not found returns problem details
- [ ] Unhandled exception returns 500 with no stack trace outside Development

**Specification Mapping:** 11, 9.1

---

### 7.5 Configuration Controller

**Objective:** Expose configuration APIs

**Subtasks:**
- [ ] Create file: `PivotController/Configuration/Controllers/ConfigController.cs`
  - `GET /api/config` — return full configuration
  - `GET /api/config/{section}` — return single section (general/dataStorage/databaseQuery/https/security/reportSharing)
  - `PUT /api/config` — merge-patch update
    - Response: 200 `{ config, requiresRestart }`
    - On error: 422 with field errors

**Verification:**
- [ ] GET /api/config returns all 7 sections + indexes
- [ ] GET /api/config/general returns only General section
- [ ] PUT /api/config merges partial update; flags requiresRestart

**Specification Mapping:** 3.1, 3.2

---

### 7.6 Health Endpoint

**Objective:** Liveness endpoint for monitoring

**Subtasks:**
- [ ] Add `GET /health` endpoint (or use ASP.NET Core HealthChecks)
  - Response: 200 OK if responsive
  - Can include index statuses in response

**Verification:**
- [ ] GET /health returns 200

**Specification Mapping:** 17.2

---

## Phase 8: Client-Side Sample Configuration (Final Step)

*Estimated: 0.5 day*

### 8.1 Update Sample Pivot Table

**Objective:** Wire client-side sample to send index name

**Subtasks:**
- [ ] Modify `Sample/pivot.js`:
  - Define `PIVOT_INDEX = "Sales-Data"` (or preferred index)
  - Define `PIVOT_URL = "http://localhost:5285/api/pivot/post"`
  - Pass `index: PIVOT_INDEX` in every pivot request (dataSourceSettings.index)
- [ ] (Optional) Create `Sample/config.js` for shared configuration
  - Export window.PivotDataServer = { url, index }
- [ ] Verify `Sample/pivot.html` loads without modification
- [ ] Test: pivot table renders data from named index

**Verification:**
- [ ] Sample/pivot.js includes index field in requests
- [ ] Pivot table loads data from specified index
- [ ] Multiple indexes can be tested by changing PIVOT_INDEX

**Specification Mapping:** 10.2, 10.3, 12.1

---

## Cross-Cutting Tasks

### Data Provider Factory

**Objective:** Implement factory for creating data providers by source type

**Subtasks:**
- [ ] Create file: `PivotController/Data/Providers/DataSourceProviderFactory.cs`
  - CreateProvider(DataSourceType) — returns IDataSourceProvider
  - Support: Json, Csv, Database (via IDatabaseQueryProvider)
- [ ] Register in DI

---

### Index Store Abstraction

**Objective:** Persist and load index metadata

**Subtasks:**
- [ ] Create file: `PivotController/Core/Persistence/IIndexStore.cs`
  - Methods: LoadAsync(), SaveAsync()
- [ ] Implement using AtomicJsonStore
- [ ] Use for indexes.json persistence

---

### Retry Policy

**Objective:** Implement exponential backoff retry

**Subtasks:**
- [ ] Create file: `PivotController/Core/Retry/RetryPolicy.cs`
  - ExecuteAsync(attempts, operation, logger, ct) — transient-only retry
  - Delays: 250ms * 2^attempt + jitter ±20%
  - Catch: IOException, TimeoutException, transient DbException
  - Fail fast: ValidationException, non-transient errors

**Verification:**
- [ ] Transient failure retried up to RetryAttempts
- [ ] Validation failure fails immediately
- [ ] Exponential backoff delays applied

**Specification Mapping:** 4.1, 13.2

---

## Verification & Testing

### Unit Tests

**Objective:** Core business logic tested in isolation

**Checklist:**
- [ ] ConfigurationValidator rules
- [ ] PBKDF2 hashing and verification
- [ ] ConstantTimeComparer
- [ ] AtomicJsonStore (corrupt file handling)
- [ ] RetryPolicy (backoff, transient detection)
- [ ] OriginValidator (CORS rules)
- [ ] Quota math (report folder limits)

### Integration Tests (WebApplicationFactory)

**Objective:** API contracts and workflows tested end-to-end

**Checklist:**
- [ ] Index creation, listing, editing, deletion
- [ ] Pivot request routing by index name
- [ ] Configuration CRUD (merge-patch, validation)
- [ ] Basic Auth (401 without, 200 with credentials)
- [ ] CORS (403 for disallowed, 200 for allowed)
- [ ] Report save/retrieve/expire/delete
- [ ] Database timeout stub provider (504)

### Regression Tests

**Objective:** Existing pivot functionality unaffected

**Checklist:**
- [ ] fetchFieldMembers on migrated "default" index
- [ ] fetchRawData on migrated index
- [ ] Export (Excel/CSV) on routed index
- [ ] Sample/pivot.html renders without errors

### E2E Test

**Objective:** Full flow from sample to server to pivot display

**Checklist:**
- [ ] Run Sample/pivot.html
- [ ] Select index "Sales-Data" (or configure in pivot.js)
- [ ] Pivot table renders data from that index
- [ ] Grouping/sorting works
- [ ] Field list shows correct fields

### Manual Settings-File Testing (§12.5)

**Objective:** Verify each Phase 1 option via JSON file editing

**Checklist:**
- [ ] Edit MinimumLogLevel in config.json; verify log output changes within 1 s
- [ ] Edit Index ReloadIntervalMinutes; verify auto-reload triggers at interval
- [ ] Edit CSV delimiter; verify data reloads with new separator
- [ ] Edit Mapping enabled/disabled; verify field types change
- [ ] Edit BasicAuthEnabled; verify auth required/bypassed
- [ ] Edit AllowedOrigins; verify CORS policy changes
- [ ] Edit ReportExpirationTimeMinutes; verify old reports expire

---

## Task Dependencies & Execution Order

```
Phase 1 (Foundational)
├─ 1.1 IndexDefinition
├─ 1.2 IndexManager → depends on 1.1
├─ 1.3 Bootstrap & Migration → depends on 1.1, 1.2
├─ 1.4 Pivot Routing → depends on 1.2, 1.3
├─ 1.5 Index APIs → depends on 1.2, 1.3
└─ 1.6 CSV Separators → depends on 1.5

Phase 2 → depends on Phase 1
├─ 2.1 Config Models
├─ 2.2 Validation → depends on 2.1
└─ 2.3 ConfigService → depends on 2.1, 2.2

Phase 3 → depends on Phase 1, 2
├─ 3.1 DB Providers
└─ 3.2 Timeout → depends on 3.1

Phase 4 → depends on Phase 2
├─ 4.1 CertificateService
└─ 4.2 Kestrel Config → depends on 4.1

Phase 5 → depends on Phase 2
├─ 5.1 User Model & Hashing
├─ 5.2 UserService & CRUD → depends on 5.1
├─ 5.3 BasicAuth → depends on 5.2
└─ 5.4 CORS → depends on 2

Phase 6 → depends on Phase 2
├─ 6.1 ReportService
├─ 6.2 ReportSweepWorker → depends on 6.1
└─ 6.3 Report APIs → depends on 6.1, 6.2

Phase 7 → depends on all phases
├─ 7.1 AtomicJsonStore
├─ 7.2 FileWatcher → depends on 7.1, 2.3
├─ 7.3 Logging
├─ 7.4 Exception Filter
├─ 7.5 ConfigController
└─ 7.6 Health Endpoint

Phase 8 (Final) → depends on Phase 1, 7
└─ 8.1 Update Sample → all phases complete
```

---

## Sign-Off & Approval

**Status:** Ready for Implementation

Before proceeding with Phase 1, confirm:

- [ ] All specification documents approved (EXPLORATION, proposal, design, spec.md)
- [ ] All OpenSpec artifacts reviewed and signed off
- [ ] Resources allocated (developer, reviewer, QA)
- [ ] Timeline (10-15 business days) acceptable
- [ ] This tasks.md document reviewed and acknowledged

**Implementation Begins:** Upon approval above

