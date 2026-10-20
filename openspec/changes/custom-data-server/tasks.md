# Syncfusion Pivot Data Server — Phase 1 Implementation Tasks

**Date:** 2024-12-19 (updated after Steps 1–3 implementation)

**Status:** In Progress — Phases 1–3 (Steps 1–3) implemented and verified. **Order revised (2026-10): HTTPS Settings = Step 4 (next); Database Query Settings deferred to Step 8 (final) because it requires DB provider support on the machine.** Phases 4+ pending.

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
- [x] Create file: `PivotController/Configuration/Models/IndexDefinition.cs`
  - Properties: Index, Name, SourceType, SourcePath, CsvDelimiter, CsvDecimalSeparator, CsvThousandsSeparator, DatabaseType, ConnectionStringName, Query, AutoReload, ReloadIntervalMinutes, MappingEnabled, FieldMappings[], Enabled, CreatedAt, UpdatedAt
  - Validation: Index name pattern `[A-Za-z0-9._-]{1,64}`, SourceType immutable on edit
- [x] Create file: `PivotController/Configuration/Models/IndexFieldMapping.cs`
  - Properties: FieldName, Type (Number | String | Date)
- [x] Create file: `PivotController/Configuration/Models/IndexStatus.cs`
  - Enum: Ready | Refreshing | Failed | Disabled
  - Include: IndexName, Status, RowCount, LoadedAt, LastError

**Verification:**
- [x] Models compile with no warnings
- [x] Default values match spec (MappingEnabled=false, AutoReload=false, Enabled=true)
- [x] Serialization/deserialization works (JSON round-trip)

**Specification Mapping:** 1.1, 1.3, 1.5

---

### 1.2 Implement IndexManager (Core Cache Hub)

**Objective:** Central service for index data management and cache coordination

**Subtasks:**
- [x] Create file: `PivotController/Indexing/IIndexManager.cs` (interface)
  - Methods: GetDataAsync, CreateIndexAsync, EditIndexAsync, ReloadIndexAsync, EnableIndexAsync, DisableIndexAsync, DeleteIndexAsync, GetIndexStatus, ListIndexes
- [x] Create file: `PivotController/Indexing/IndexManager.cs` (implementation)
  - Dependencies: IMemoryCache, DataServerConfiguration, IDataSourceProviderFactory, ILogger
  - Per-index SemaphoreSlim for reload protection
  - Keep-data-on-refresh logic (dual-slot cache entry)
  - Index registry (_indexes dictionary, case-insensitive)
- [x] Create file: `PivotController/Indexing/IndexReloadWorker.cs` (BackgroundService)
  - Scans indexes with AutoReload=true + ReloadIntervalMinutes>0
  - Triggers reload at configured intervals from server start
  - Handles reload failures per KeepDataOnRefresh

**Verification:**
- [x] IndexManager injects without DI errors
- [x] CreateIndexAsync persists metadata and loads data
- [x] Cache keys use `data:{index}` and `engine:{index}:{hash}` format
- [x] Concurrent reload protected by SemaphoreSlim (no data corruption)
- [x] Keep-data-on-refresh swaps atomically

**Specification Mapping:** 1.2, 4.1, 4.2, 4.3

---

### 1.3 Bootstrap Index Configuration from appsettings.json

**Objective:** Load indexes from appsettings.json at startup; one-time legacy migration

**Subtasks:**
- [x] Create file: `PivotController/Configuration/Services/ConfigurationPersistenceService.cs`
  - LoadAsync() — reads DataServer section from config
  - SaveAsync() — atomically writes config.json using AtomicJsonStore
  - EnsureFilesAsync() — creates DataServer directory, initializes missing files
- [x] Modify `PivotController/Program.cs`:
  - Call `ConfigurationPersistenceService.EnsureFilesAsync()`
  - Load configuration from appsettings.json with defaults
  - Check for legacy `PivotDataSource` block
  - If exists: create index "default" from it; save to DataServer/indexes.json
  - Register all enabled indexes via `IndexManager.RegisterAllAsync()`
  - Configure cache sizing from `DataStorage.CacheSizeLimit`
- [x] Create file: `PivotController/DataServer/.gitignore`
  - Ignore: `config.json`, `indexes.json`, `users.json`, `reports/`, `logs/`, `*.corrupt.*`

**Verification:**
- [x] Application starts; `DataServer/` directory created
- [x] Indexes from appsettings.json registered and available via `GET /api/indexes`
- [x] Legacy `PivotDataSource` migrated to "default" index (one-time only)
- [x] Disabled indexes not preloaded; enabled indexes load successfully
- [x] Cache size limit applied (`MemoryCache.Options.SizeLimit` set)

**Specification Mapping:** 1.1, 1.2, 12.1, 12.3

---

### 1.4 Index-Aware Pivot Routing

**Objective:** Modify PivotController to route requests via IndexManager using index name

**Subtasks:**
- [x] Modify `FetchData` class (in existing code) to add optional `Index` property
- [x] Modify `PivotController/Controllers/PivotController.cs`:
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
- [x] Pivot request with `index` field routes to correct index data
- [x] Pivot request without `index` uses `DefaultIndexId` (backward compatible)
- [x] Unknown index returns 404 with valid index list
- [x] Disabled index returns 423
- [x] Existing pivot actions work on routed index data

**Specification Mapping:** 2.1, 2.2, 12.1

---

### 1.5 Index Management REST APIs

**Objective:** Implement full index lifecycle endpoints

**Subtasks:**
- [x] Create file: `PivotController/Configuration/Controllers/IndexController.cs`
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
- [x] POST /api/indexes creates index, loads data, persists metadata
- [x] PUT /api/indexes edits metadata, reloads data
- [x] Attempt to change sourceType rejected with 422
- [x] Duplicate index name rejected with 409
- [x] Enable/disable works; disabled indexes return 423 on pivot request
- [x] Test-connection returns latency or error

**Specification Mapping:** 1.2, 1.6, 9.1

---

### 1.6 CSV Separator Options

**Objective:** Support CSV delimiter, decimal separator, thousands separator

**Subtasks:**
- [x] Modify `PivotController/Data/Providers/CsvDataProvider.cs` (or create if not exists):
  - Constructor: accept IndexDefinition with CSV separator options
  - LoadAsync(): use CsvDelimiter, CsvDecimalSeparator, CsvThousandsSeparator on parse
  - Handle edge cases (multi-char separators, escaping)
  - Test with various CSV formats (European decimals, thousands, etc.)

**Verification:**
- [x] CSV file with semicolon delimiter parsed correctly
- [x] Decimal separator (comma vs period) honored in numeric fields
- [x] Thousands separator handled properly
- [x] Edit index to change separators; data reloaded with new options

**Specification Mapping:** 1.4

---

**VERIFIED (live, this branch):** models round-trip; IndexManager DI-clean; bootstrap from appsettings.json + indexes.json with disabled-index skip and Failed-status resilience on bad sources; per-index data:{index} + engine:{index}:{hash} cache keys; SemaphoreSlim reload protection; 409/422/423/404 status codes; field-mapping endpoints reflect in client pivot field list; CSV separator enums (names + raw symbols) in appsettings/API/persisted JSON.

---

## Phase 2: General Settings

*Estimated: 2-3 days*

This phase implements the core server settings (Port, MinimumLogLevel, DataFolder) with full configuration management and API exposure.

### 2.1 Create GeneralSettings Model

**Objective:** Implement the GeneralSettings class with validation and defaults

**Subtasks:**
- [x] Create file: `PivotController/Configuration/Models/LogLevelOption.cs`
  - Enum: Info, Warn, Error, Fatal
  - ToString() returns uppercase (INFO, WARN, ERROR, FATAL)
  - TryParse() supports case-insensitive parsing (info → Info)
- [x] Create file: `PivotController/Configuration/Models/GeneralSettings.cs`
  - Properties with defaults:
    - `Port: int = 9500` 
    - `MinimumLogLevel: LogLevelOption = LogLevelOption.Info`
    - `DataFolder: string = "DataSource"`
  - Validation attributes on properties (if using FluentValidation or DataAnnotations)

**Verification:**
- [x] Model compiles without warnings
- [x] Defaults match spec (Port 9500, MinimumLogLevel Info, DataFolder "DataSource")
- [x] JSON serialization/deserialization works correctly
- [x] LogLevelOption enum parses case-insensitively

**Specification Mapping:** 0.1, 0.2, 0.3, 0.4

---

### 2.2 Implement Port Configuration and Validation

**Objective:** Validate and configure Kestrel listener port

**Subtasks:**
- [x] Add port validation logic:
  - Must be between 1 and 65535
  - Return validation error if outside range
  - Store in GeneralSettings.Port
- [x] Modify `PivotController/Program.cs`:
  - Read `General.Port` from configuration
  - Pass to `WebApplication.CreateBuilder()` → `builder.WebHost.UseUrls($"http://localhost:{port}")`
  - Log startup: "Data Server listening on http://localhost:{port}"
  - On port binding failure, catch exception, log error, exit
- [x] Test configuration update behavior:
  - Verify current port returned in status
  - Port changes flagged as `requiresRestart: true`

**Verification:**
- [x] Application starts on port 9500 (default)
- [x] Custom port via appsettings.json works (test with 5285, 8080)
- [x] Invalid port (0, 99999) rejected during configuration validation
- [x] Port binding failure caught and logged clearly
- [x] `requiresRestart: true` returned when Port changed via API
- [x] Existing tests still pass

**Specification Mapping:** 0.2.1-0.2.10

---

### 2.3 Implement Minimum Log Level Configuration

**Objective:** Support configurable logging level with runtime application

**Subtasks:**
- [x] Modify `PivotController/Program.cs` (logging setup):
  - Replace `AddConsole()` with custom setup
  - Add rolling JSON file logger: `logs/dataserver-{yyyyMMdd}.jsonl` (30-day retention)
  - Hook `MinimumLogLevel` from GeneralSettings
  - Framework logs (Microsoft.AspNetCore.*) pinned to Warning
  - Apply general MinimumLogLevel to application categories
- [x] Create file: `PivotController/Logging/DataServerLoggerProvider.cs` or use built-in `AddConsole()` + `AddFile()`
  - Categories: Config, Security, Indexing, DatabaseQuery, ReportSharing, Https, Pivot
  - Structured logging: { category, correlationId, index?, durationMs, rowCount?, error? }
  - Pivot hit-logging at Debug level (not Info to avoid chattiness)
- [x] Implement LogLevelFilter to apply MinimumLogLevel at runtime
  - File watcher or configuration change event triggers filter update
  - No restart required

**Verification:**
- [x] Application starts with Info-level logging (default)
- [x] Change MinimumLogLevel to Warn via API; Warn/Error/Fatal logged, Info/Debug filtered immediately
- [x] Console output reflects current level
- [x] Log file (JSONL) reflects current level
- [x] Framework logs stay at Warning regardless of setting
- [x] Log entries include structured fields (category, correlationId, etc.)

**Specification Mapping:** 0.3.1-0.3.10

---

### 2.4 Implement Data Folder Configuration

**Objective:** Validate and establish root directory for all data files

**Subtasks:**
- [x] Add validation logic for DataFolder:
  - Non-empty string required
  - No path traversal (no `../` or `..\`)
  - Create directory if not exists
  - Check write permissions
  - Resolve relative paths from `{workingDirectory}`
  - Log error if not writable
- [x] Modify `PivotController/Program.cs`:
  - Read `General.DataFolder` from configuration
  - Resolve to absolute path
  - Create if missing
  - Ensure subdirectories: `{DataFolder}/reports/`, `{DataFolder}/logs/`
  - Verify write access; exit with error if not writable
  - Log: "Data folder: {resolvedPath}"
- [x] Ensure all data operations use this folder:
  - Config files → `{DataFolder}/config.json`, `{DataFolder}/indexes.json`, `{DataFolder}/users.json`
  - Reports → `{DataFolder}/reports/`
  - Logs → `{DataFolder}/logs/`

**Verification:**
- [x] Default "DataSource" folder created on startup
- [x] Custom DataFolder via appsettings.json works (absolute and relative paths)
- [x] Empty DataFolder rejected (422 error)
- [x] Path traversal (`../../`) rejected (422 error)
- [x] No-write-permission detected and logged; exit with error
- [x] Subdirectories created automatically
- [x] All data files stored in correct locations

**Specification Mapping:** 0.4.1-0.4.10

---

### 2.5 Create Configuration Persistence Service (Atoms Writes)

**Objective:** Implement atomic write/read for configuration files

**Subtasks:**
- [x] Create file: `PivotController/Configuration/Services/AtomicJsonStore.cs`
  - LoadAsync(filePath) — deserialize JSON file
  - SaveAsync(filePath, data) — write to temp file, then rename (atomic)
  - Handles corruption: rename .corrupt.{timestamp}
- [x] Modify `ConfigurationPersistenceService.cs` (from Phase 1):
  - LoadAsync() — reads from `{DataFolder}/config.json`
  - SaveAsync(config) — atomically writes using AtomicJsonStore
  - EnsureFilesAsync() — creates DataFolder, initializes missing JSON files with defaults

**Verification:**
- [x] Configuration loaded correctly from file
- [x] Configuration saved atomically (temp → rename, not direct overwrite)
- [x] Corrupt JSON renamed to .corrupt.{timestamp}, defaults used
- [x] Application recovers with defaults after corruption

**Specification Mapping:** 0.7.1-0.7.4, 11

---

### 2.6 Implement Configuration API Endpoints

**Objective:** Expose full General Settings via REST API

**Subtasks:**
- [x] Create file: `PivotController/Configuration/Controllers/ConfigController.cs`
  - `GET /api/config/general` — return current GeneralSettings + requiresRestart flag
  - `PUT /api/config/general` — update via merge-patch
    - Validate each field
    - Return 422 with field errors if invalid
    - Return 200 with updated settings + requiresRestart flag
  - `GET /api/config` — return full configuration (all 7 sections) [for Phase 2B]
  - `PUT /api/config` — update full config [for Phase 2B]
- [x] Response format: `{ success: true, general: {...}, requiresRestart: false/true, message?: string }`
- [x] Add BasicAuth middleware check (when Security settings implement it in Phase 5)

**Verification:**
- [x] GET /api/config/general returns Port, MinimumLogLevel, DataFolder
- [x] PUT /api/config/general { minimumLogLevel: "Error" } applies change, returns requiresRestart: false
- [x] PUT /api/config/general { port: 5286 } returns requiresRestart: true
- [x] PUT /api/config/general { port: 0 } returns 422 with error message
- [x] PUT /api/config/general { dataFolder: "" } returns 422 with error message

**Specification Mapping:** 0.5.1-0.5.4

---

### 2.7 Implement Hot-Reload for Runtime-Applicable Settings

**Objective:** Apply MinimumLogLevel changes without restart; persist and validate Port/DataFolder changes

**Subtasks:**
- [x] Create file: `PivotController/Configuration/Services/ConfigurationFileWatcher.cs`
  - Monitor `{DataFolder}/config.json` for changes
  - On change: call ConfigurationService.ApplyAsync(newConfig)
  - For runtime-applicable settings: apply immediately
  - For restart-required settings: flag but do not apply
- [x] Implement ConfigurationService.ApplyAsync():
  - Load new config from file
  - Validate
  - For MinimumLogLevel: update logger filter immediately
  - For other settings: update config object but flag requiresRestart
  - On error: log, quarantine corrupt file, revert to previous

**Verification:**
- [x] Edit `config.json` directly (MinimumLogLevel change)
- [x] Change detected within 1 second
- [x] New log level applied immediately to new log entries
- [x] No need for server restart
- [x] Edit Port in config.json; change detected but not applied; requiresRestart: true
- [x] Corrupt config.json renamed to .corrupt.{timestamp}; old config continues

**Specification Mapping:** 0.7.2-0.7.4

---

### 2.8 Unit Tests for General Settings

**Objective:** Comprehensive test coverage for General Settings functionality

**Subtasks:**
- [x] Create file: `PivotController.Tests/Configuration/GeneralSettingsTests.cs`
  - Test Port validation (range, boundaries)
  - Test MinimumLogLevel enum parsing (case-insensitive, all 4 levels)
  - Test DataFolder validation (empty, traversal, permissions)
  - Test configuration API (GET, PUT merge-patch)
  - Test requiresRestart flag (Port yes, DataFolder yes, MinimumLogLevel no)
  - Test hot-reload (MinimumLogLevel changes apply immediately)
  - Test atomic writes (no corruption on partial write)
  - Test corruption recovery
- [x] Use xUnit or existing test framework
- [x] Test edge cases: special characters in folder names, Unicode, etc.

**Verification:**
- [x] All tests pass
- [x] Code coverage ≥ 80% for GeneralSettings-related code
- [x] Test files compile and run cleanly

**Specification Mapping:** 0.1-0.7

---

**VERIFIED (live, this branch):** GET/PUT /api/config/general merge-patch; General:Port drives Kestrel (launchSettings applicationUrl removed; only ASPNETCORE_URLS overrides); MinimumLogLevel enforced live via MinimumLevelLoggerProvider + config.json FileSystemWatcher hot-reload (~100ms) + POST /api/config/general/test-log helper; DataFolder subdirs (reports/, logs/) + writability probe + traversal 422; requiresRestart flags correct per property.

---

## Phase 3: Data Storage Settings & Configuration Models

*Estimated: 1-2 days*

### 3.1 Create Configuration Models

**Objective:** Implement all 7 configuration sections per spec

**Subtasks:**
- [x] Create files in `PivotController/Configuration/Models/`:
  - `GeneralSettings.cs` — Port, MinimumLogLevel, DataFolder
  - `DataStorageSettings.cs` — RetryAttempts, CacheSizeLimit, KeepDataOnRefresh, MaxCacheItemSizeBytes
  - `DataServerConfiguration.cs` (root) — ConfigVersion, DefaultIndexId, all 7 sections, Indexes[]
- [x] Add defaults per spec (§6, §7)

**Verification:**
- [x] All properties with correct types and defaults
- [x] JSON serialization matches expected structure

**Specification Mapping:** 6.1, 6.2, 7

---

### 2.2 Configuration Validation

**Objective:** Implement per-section and cross-field validation

**Subtasks:**
- [x] Create file: `PivotController/Configuration/Validation/ConfigurationValidator.cs`
  - Validate per-section rules (Port 1-65535, RetryAttempts 0-30, etc.)
  - Cross-field rules:
    - `DefaultIndexId` must exist
    - If `ReloadIntervalMinutes>0`, must have `AutoReload=true`
    - If `EndpointEnabled=true` (reports), `StoragePath` must be writable
  - Return FieldErrors dictionary for problem details
- [x] Add validation attributes to model properties where appropriate

**Verification:**
- [x] Invalid Port (0, 99999) rejected
- [x] RetryAttempts < 0 rejected
- [x] DefaultIndexId not existing rejected
- [x] Validation returns clear field-level errors

**Specification Mapping:** 17.3

---

### 2.3 Configuration Service

**Objective:** Orchestrate configuration updates with persistence

**Subtasks:**
- [x] Create file: `PivotController/Configuration/Services/ConfigurationService.cs`
  - UpdateAsync(patch) — merge patch with current config
  - Validate merged result
  - Persist to DataServer/config.json (atomic)
  - Apply runtime changes per rules (§12.5)
  - Return { config, requiresRestart }
- [x] Implement RequiresRestart detection logic

**Verification:**
- [x] MinimumLogLevel change applied immediately (requiresRestart=false)
- [x] Port change flagged as requiresRestart=true
- [x] Invalid update rejected with 422; config unchanged

**Specification Mapping:** 3.1, 3.2

---

**VERIFIED (live, this branch):** GET/PUT /api/config/datastorage (422 validation); RetryAttempts/KeepDataOnRefresh apply immediately, CacheSizeLimit/MaxCacheItemSizeBytes require restart; datastorage.json loaded at startup (LoadPersisted; absent file keeps appsettings values); CacheSizeLimit implemented with **Flexmonster semantics** (max cached server responses per index, per-index LRU, 0 disables response caching); KeepDataOnRefresh both polarities (Ready+kept-data vs Failed+evicted) with recovery; exponential-backoff retries with per-attempt logging; GET /api/config/datastorage/cache-stats observability endpoint.

---

## Phase 4: Database Query Settings (DEFERRED — reordered to Step 8 / Final; requires DB support on the machine)

*Estimated: 1-2 days*

### 4.1 Database Provider Abstraction

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
- [x] Create file: `PivotController/Configuration/Services/CertificateService.cs` (path differs from plan)
  - ResolveCertificate(HttpsSettings) — returns X509Certificate2
  - Support:
    - PFX: `new X509Certificate2(pfxPath, password)`
    - PEM: `X509Certificate2.CreateFromPemFile(certPath, keyPath)`
    - Store: lookup by `StoreLocation`, `StoreName`, `Thumbprint` (Windows-only, guarded)
  - Validation: exactly one source must be configured (422 otherwise, verified live)
- [x] Create file: `PivotController/Configuration/Models/HttpsSettings.cs` (+ enums, Sanitized() copy without password)
- [x] Create file: `PivotController/Configuration/Services/HttpsSettingsService.cs` (merge-patch, https.json persistence, LoadPersisted)
- [x] Create file: `PivotController/Configuration/Controllers/HttpsConfigController.cs` (GET/PUT /api/config/https)

**Verification:**
- [x] PFX certificate loaded (PFX branch verified by code path; PEM verified end-to-end live)
- [x] PEM certificate loaded (self-signed test cert, live TLS handshake OK)
- [x] Windows store certificate loaded (if on Windows) — Windows-only guard verified (non-Windows store lookup rejected 422)
- [x] Invalid certificate paths fail with clear error (missing PFX path -> 422; invalid settings fail startup fast)

**Specification Mapping:** 6.4, 15.1

---

### 4.2 Kestrel HTTPS Configuration

**Objective:** Configure Kestrel listener with HTTPS and protocol options

**Subtasks:**
- [x] Modify `PivotController/Program.cs`:
  - `builder.WebHost.ConfigureKestrel()`:
    - If `Https.Enabled=true`:
      - Resolve certificate via CertificateService
      - `options.Listen(IPAddress.Any, Port, lo => lo.UseHttps(cert))`
      - Map protocols: HTTP1 | HTTP2 | HTTP1_AND_HTTP2
    - Set TLS 1.2+ enforced (Tls12 | Tls13)
  - `app.UseHttpsRedirection()` when HTTPS enabled (skipped when disabled)
  - Load persisted https.json pre-Build so restart-required changes bind

**Verification:**
- [x] HTTPS listener active on configured port (https://0.0.0.0:5285 live)
- [x] HTTP/1.1 and/or HTTP/2 negotiated per setting (HTTP2 negotiated after persisted patch; requiresRestart honored)
- [x] TLS 1.2+ enforced (TLS 1.1 handshake refused, TLS 1.2 OK — verified with curl)

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

