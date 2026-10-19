# Syncfusion Pivot Data Server — Phase 1 Technical Specification

**Date:** 2024-12-19

**Version:** 2.1

---

## 0. General Settings Configuration

### 0.1 Configuration Model

The `GeneralSettings` class encapsulates core server behavior:

```csharp
public enum LogLevelOption { Info, Warn, Error, Fatal }

public class GeneralSettings
{
    public int Port { get; set; } = 9500;
    public LogLevelOption MinimumLogLevel { get; set; } = LogLevelOption.Info;
    public string DataFolder { get; set; } = "DataSource";
}
```

### 0.2 Port Configuration

**SHALL** accept valid port numbers (1–65535) and bind the Kestrel listener to that port.

| Property | Default | Validation | Requires Restart | Description |
|----------|---------|------------|:---:|---|
| **Port** | 9500 | int 1–65535 | **Yes** | HTTP/HTTPS listener port; changes require server restart |

**Scenario 0.2.1: Default Port**
- Given: Port not specified in `appsettings.json`
- When: Application starts
- Then: Server binds to port 9500

**Scenario 0.2.2: Custom Port via appsettings.json**
- Given: `appsettings.json` contains `DataServer: { General: { Port: 5285 } }`
- When: Application starts
- Then: Server binds to port 5285

**Scenario 0.2.3: Port Update via API (Requires Restart)**
- Given: Server running on port 5285
- When: `PUT /api/config/general { Port: 5286 }`
- Then: Returns 200 with response `{ success: true, requiresRestart: true, message: "Port change requires server restart" }`
- And: Old port still active until restart

**Scenario 0.2.4: Restart Applies Port Change**
- Given: Configuration updated to port 5286, `requiresRestart: true` response received
- When: Server restarts
- Then: Server binds to port 5286; clients redirect/reconnect to new port

**Scenario 0.2.5: Invalid Port Number (Too Low)**
- Given: Port configuration update with value 0
- When: `PUT /api/config/general { Port: 0 }`
- Then: Returns 422 Validation Error with message "Port must be between 1 and 65535"
- And: Configuration unchanged

**Scenario 0.2.6: Invalid Port Number (Too High)**
- Given: Port configuration update with value 99999
- When: `PUT /api/config/general { Port: 99999 }`
- Then: Returns 422 Validation Error with message "Port must be between 1 and 65535"
- And: Configuration unchanged

**Scenario 0.2.7: Port Already in Use (Startup)**
- Given: Another service already uses port 5285
- When: Application attempts to start with `Port: 5285`
- Then: Startup fails with error log "Address already in use (port 5285)"
- And: Application exits with non-zero exit code

### 0.3 Minimum Log Level Configuration

**SHALL** accept one of four valid log levels and apply to all subsequent log output without requiring restart.

| Property | Default | Validation | Requires Restart | Description |
|----------|---------|------------|:---:|---|
| **MinimumLogLevel** | Info | Info / Warn / Error / Fatal | **No** | Minimum severity for all logged messages; applied immediately |

#### Valid Log Levels

| Level | Severity | Description | Typical Usage |
|-------|----------|-------------|---|
| **INFO** | Low | Informational messages | Startup, shutdown, index load success |
| **WARN** | Medium | Warning conditions | Deprecated API usage, fallback behaviors, recoverable errors |
| **ERROR** | High | Error conditions | Failed operations, data validation failures, provider errors |
| **FATAL** | Critical | System-threatening failures | Uncaught exceptions, data corruption, unrecoverable state |

**Scenario 0.3.1: Default Log Level (Info)**
- Given: `MinimumLogLevel` not specified in `appsettings.json`
- When: Application starts
- Then: All INFO, WARN, ERROR, and FATAL messages logged; DEBUG messages filtered
- And: Console shows messages at all these levels
- And: Log file includes all these levels

**Scenario 0.3.2: Increase Log Level to Warn**
- Given: Currently logging at Info level
- When: `PUT /api/config/general { MinimumLogLevel: "Warn" }`
- Then: Returns 200 with response `{ success: true, requiresRestart: false, message: "Log level changed to Warn" }`
- And: Change applied immediately
- And: INFO messages now filtered out; only WARN, ERROR, FATAL logged

**Scenario 0.3.3: Decrease Log Level to Error**
- Given: Currently logging at Warn level
- When: `PUT /api/config/general { MinimumLogLevel: "Error" }`
- Then: Returns 200 with response `{ success: true, requiresRestart: false }`
- And: Only ERROR and FATAL messages logged; WARN and INFO filtered

**Scenario 0.3.4: Set to Fatal Only**
- Given: Currently at Error level
- When: `PUT /api/config/general { MinimumLogLevel: "Fatal" }`
- Then: Returns 200; Only system-critical FATAL-level messages logged
- And: DEBUG, INFO, WARN, ERROR all filtered

**Scenario 0.3.5: Invalid Log Level**
- Given: Current config at Info level
- When: `PUT /api/config/general { MinimumLogLevel: "Debug" }`
- Then: Returns 422 Validation Error with message "MinimumLogLevel must be one of: Info, Warn, Error, Fatal"
- And: Configuration unchanged, logging continues at Info level

**Scenario 0.3.6: Case Insensitivity**
- Given: Current config at Info level
- When: `PUT /api/config/general { MinimumLogLevel: "warn" }` (lowercase)
- Then: Returns 200; MinimumLogLevel set to "Warn"
- And: WARN level messages begin logging immediately

**Scenario 0.3.7: Log Level Applied to Console Output**
- Given: MinimumLogLevel set to "Error"
- When: Index loading succeeds (INFO-level event)
- Then: No message appears in console
- When: Index loading fails (ERROR-level event)
- Then: Error message appears in console immediately

**Scenario 0.3.8: Log Level Applied to File Output**
- Given: MinimumLogLevel set to "Error"
- When: Various operations occur (INFO, WARN, ERROR, FATAL)
- Then: Inspect `logs/dataserver-{yyyyMMdd}.jsonl` file
- And: Only ERROR and FATAL entries present; INFO and WARN entries absent

**Scenario 0.3.9: Log Level Change at Runtime (No Message Duplication)**
- Given: Server logging at Warn level with 100 messages per second
- When: `PUT /api/config/general { MinimumLogLevel: "Error" }`
- Then: Returns 200 immediately
- And: Warn messages stop appearing in output
- And: No duplicate or missed messages during transition
- And: All existing Warn messages already logged remain in file

**Scenario 0.3.10: Log Level per Category**
- Given: Framework ASP.NET Core logs are pinned to Warning (per specification)
- When: `PUT /api/config/general { MinimumLogLevel: "Info" }`
- Then: Returns 200
- And: Custom application logs (Category: Config, Security, Indexing, etc.) appear at Info level
- And: Framework logs (Microsoft.AspNetCore.*) remain pinned to Warning
- And: User sees application activity without framework noise

### 0.4 Data Folder Configuration

**SHALL** define the root directory for all data files (indexes, reports, configuration) and validate before use.

| Property | Default | Validation | Requires Restart | Description |
|----------|---------|------------|:---:|---|
| **DataFolder** | `DataSource` | Non-empty string; must be writable | **Yes** | Root directory for all persistent data (indexes, reports, config) |

**Scenario 0.4.1: Default Data Folder**
- Given: `DataFolder` not specified in `appsettings.json`
- When: Application starts
- Then: All data files stored in `{workingDirectory}/DataSource/`
- And: Subdirectories created: `DataSource/reports/`, `DataSource/logs/`

**Scenario 0.4.2: Custom Data Folder via appsettings.json**
- Given: `appsettings.json` contains `DataServer: { General: { DataFolder: "/var/data/pivot" } }`
- When: Application starts
- Then: All data files stored under `/var/data/pivot/`
- And: Subdirectories created as needed

**Scenario 0.4.3: Relative Path**
- Given: `DataFolder: "data"`
- When: Application starts in `/app/` directory
- Then: Data folder resolves to `/app/data/`
- And: Subdirectories: `/app/data/reports/`, `/app/data/logs/`

**Scenario 0.4.4: Absolute Path**
- Given: `DataFolder: "C:\\DataServer"` (Windows) or `/data/pivot` (Linux)
- When: Application starts
- Then: Data folder is exactly that path
- And: Must have write permissions to that directory

**Scenario 0.4.5: Empty DataFolder (Validation Failure)**
- Given: Configuration update with `DataFolder: ""`
- When: `PUT /api/config/general { DataFolder: "" }`
- Then: Returns 422 Validation Error with message "DataFolder cannot be empty"
- And: Configuration unchanged

**Scenario 0.4.6: DataFolder Does Not Exist (Created at Startup)**
- Given: `DataFolder: "/new/path/dataserver"` does not exist
- When: Application starts
- Then: Directory `/new/path/dataserver/` is created
- And: All required subdirectories created

**Scenario 0.4.7: DataFolder Exists (No Override)**
- Given: `DataFolder: "/existing/path/"` already contains files
- When: Application starts
- Then: Existing directory and files preserved
- And: New data stored in same directory (merged)

**Scenario 0.4.8: No Write Permission to DataFolder (Startup Failure)**
- Given: `DataFolder: "/readonly/"` exists but user lacks write permission
- When: Application attempts to start
- Then: Startup fails with error "Access denied to DataFolder: /readonly/"
- And: Application exits with non-zero code
- And: Error logged to console

**Scenario 0.4.9: DataFolder Update Requires Restart**
- Given: Currently using `DataFolder: "DataSource"`
- When: `PUT /api/config/general { DataFolder: "NewDataFolder" }`
- Then: Returns 200 with response `{ success: true, requiresRestart: true, message: "DataFolder change requires server restart" }`
- And: New folder is **not** used until restart

**Scenario 0.4.10: Path Traversal Protection**
- Given: Configuration update attempted
- When: `PUT /api/config/general { DataFolder: "../../" }`
- Then: Returns 422 Validation Error with message "DataFolder path traversal not allowed"
- And: Configuration unchanged

### 0.5 General Settings API Endpoints

**SHALL** expose General Settings via dedicated REST endpoints with full CRUD semantics.

**Scenario 0.5.1: Get General Settings**
- Given: Server running
- When: `GET /api/config/general`
- Then: Returns 200 with JSON:
```json
{
  "port": 5285,
  "minimumLogLevel": "Info",
  "dataFolder": "DataSource",
  "requiresRestart": false
}
```

**Scenario 0.5.2: Update General Settings (Single Property)**
- Given: Current settings with Port: 5285
- When: `PUT /api/config/general { minimumLogLevel: "Error" }`
- Then: Returns 200 with updated settings
- And: Port remains 5285 (not overwritten)

**Scenario 0.5.3: Update General Settings (Multiple Properties)**
- Given: Current settings
- When: `PUT /api/config/general { port: 5286, minimumLogLevel: "Warn", dataFolder: "data" }`
- Then: Returns 200 with all properties updated
- And: Response indicates `requiresRestart: true` (because Port and DataFolder require restart)

**Scenario 0.5.4: Patch vs Replace Semantics**
- Given: Current settings with 3 properties
- When: `PUT /api/config/general { minimumLogLevel: "Error" }`
- Then: Returns 200 with **merge patch** semantics (other properties preserved)
- And: Not treated as full replacement (which would null other properties)

### 0.6 Validation Rules Summary

| Rule | Check | Error Code | Message |
|------|-------|-----------|---------|
| Port range | 1–65535 | 422 | "Port must be between 1 and 65535" |
| Port in use | Address not bound | Fatal | "Address already in use (port XXXX)" |
| Log level enum | Info \| Warn \| Error \| Fatal | 422 | "MinimumLogLevel must be one of: Info, Warn, Error, Fatal" |
| DataFolder not empty | string.IsNullOrWhiteSpace | 422 | "DataFolder cannot be empty" |
| DataFolder writable | Has write permission | Fatal | "Access denied to DataFolder: {path}" |
| DataFolder no traversal | No `../` or `..\\` | 422 | "DataFolder path traversal not allowed" |

### 0.7 Persistence and Hot-Reload

**SHALL** persist General Settings to `config.json` and support hot-reload (no restart for runtime-applicable settings).

**Scenario 0.7.1: Persistence to config.json**
- Given: `PUT /api/config/general { minimumLogLevel: "Error" }`
- When: Returns 200
- Then: Value persisted to `DataServer/config.json`
- And: Format is valid JSON
- And: Survives application restart

**Scenario 0.7.2: Atomic File Write**
- Given: Server running normally
- When: Configuration update received
- Then: Configuration written to temporary file first
- And: Then atomically renamed to `config.json` (no corruption on partial write)

**Scenario 0.7.3: Hot-Reload for RuntimeApplicable Settings**
- Given: MinimumLogLevel at Info
- When: External process updates `DataServer/config.json` to `"Error"`
- Then: File watcher detects change
- And: MinimumLogLevel applied immediately (within 100ms)
- And: No restart required

**Scenario 0.7.4: Corrupt config.json Recovery**
- Given: `config.json` is malformed JSON
- When: Application starts
- Then: Corrupt file renamed to `config.corrupt.{timestamp}`
- And: Application starts with defaults
- And: Error logged: "config.json was corrupt; moved to config.corrupt.{timestamp}"

---

## 1. Data Storage Settings Configuration

### 1.1 Configuration Model

The `DataStorageSettings` class manages caching, retry policy, and data refresh behavior:

```csharp
public class DataStorageSettings
{
    public int RetryAttempts { get; set; } = 3;       // 0–30 (0 = no retry)
    public int CacheSizeLimit { get; set; } = 100;    // >= 0 (0 disables caching)
    public bool KeepDataOnRefresh { get; set; } = true;
    public long MaxCacheItemSizeBytes { get; set; }   // 0 = unlimited
}
```

### 1.2 Retry Attempts Configuration

**SHALL** retry transient failures with exponential backoff (250ms × 2^attempt + jitter) up to `RetryAttempts`; fail fast on validation errors.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| RetryAttempts | 3 | 0–30 | No |

**Scenario 1.2.1: Retry on Transient IO Failure**
- Given: File temporarily locked, `RetryAttempts: 3`
- When: Index load attempted
- Then: Retries at delays: 250ms, 500ms, 1000ms (±20% jitter)
- And: Succeeds when file becomes available

**Scenario 1.2.2: No Retry on Validation Error**
- Given: CSV with mismatched column counts
- When: Index load attempted with `RetryAttempts: 3`
- Then: Fails immediately (no retry)
- And: Returns 422 with error

**Scenario 1.2.3: Retry Policy Exhausted**
- Given: Network connection unavailable, `RetryAttempts: 3`
- When: Index reload attempted
- Then: Retries 3 times, then fails
- And: Returns 502 with "Unable to load after 3 attempts"

**Scenario 1.2.4: Update Retry Attempts at Runtime**
- Given: Current `RetryAttempts: 3`
- When: `PUT /api/config/datastorage { retryAttempts: 5 }`
- Then: Returns 200, change applied immediately (no restart required)
- And: Next load attempt uses value 5

### 1.3 Cache Size Limit Configuration

**SHALL** enforce maximum number of cached index datasets per `CacheSizeLimit`.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| CacheSizeLimit | 100 | >= 0 | **Yes** |

**Scenario 1.3.1: Default Cache Limit**
- Given: `CacheSizeLimit: 100` (default)
- When: 100 indexes loaded
- Then: All data cached successfully

**Scenario 1.3.2: Cache Size Exceeded (LRU Eviction)**
- Given: `CacheSizeLimit: 5`, 5 indexes loaded and active
- When: 6th index loads
- Then: Least-recently-used index evicted from cache
- And: 6th index cached; subsequent requests to evicted index reload

**Scenario 1.3.3: Cache Disabled**
- Given: `CacheSizeLimit: 0` (disables caching)
- When: Index loads
- Then: Data not cached; every pivot request reloads from source
- And: Performance degraded; recommended for development only

**Scenario 1.3.4: Cache Size Limit Update Requires Restart**
- Given: Current `CacheSizeLimit: 100`
- When: `PUT /api/config/datastorage { cacheSizeLimit: 50 }`
- Then: Returns 200 with `requiresRestart: true`
- And: New limit applied after server restart

### 1.4 Keep Data on Refresh Configuration

**SHALL** retain old data if reload fails (when enabled); discard if disabled.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|No |
| KeepDataOnRefresh | true | bool |

**Scenario 1.4.1: Reload Failure with KeepDataOnRefresh=true**
- Given: Index "Sales-Data" enabled with `keepDataOnRefresh: true`, data loaded
- When: `POST /api/indexes/Sales-Data/reload` and source becomes unavailable
- Then: Returns 502 "Unable to reload"
- And: Old data continues serving subsequent pivot requests

**Scenario 1.4.2: Reload Failure with KeepDataOnRefresh=false**
- Given: Index with `keepDataOnRefresh: false`, data loaded
- When: Reload fails
- Then: Returns 502; cache evicted
- And: Subsequent requests fail with 502 until reload succeeds

**Scenario 1.4.3: Auto-Reload Failure with Keep**
- Given: Index with `autoReload: true`, `keepDataOnRefresh: true`
- When: Automatic reload triggered at scheduled time, source unavailable
- Then: Reload logged as failed (category: Indexing)
- And: Index continues serving old data
- And: Next scheduled reload retried

**Scenario 1.4.4: Update Keep Data on Refresh**
- Given: Current `keepDataOnRefresh: true`
- When: `PUT /api/config/datastorage { keepDataOnRefresh: false }`
- Then: Returns 200, applied immediately (no restart)
- And: Affects next reload attempt

### 1.5 Max Cache Item Size Configuration

**SHALL** enforce per-item size limit in cache; items exceeding limit not cached.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| MaxCacheItemSizeBytes | 0 | >= 0 (0 = unlimited) | **Yes** |

**Scenario 1.5.1: Unlimited Cache Item Size**
- Given: `MaxCacheItemSizeBytes: 0` (unlimited)
- When: Large dataset (100 MB) loads
- Then: Entire dataset cached

**Scenario 1.5.2: Item Size Limit Enforced**
- Given: `MaxCacheItemSizeBytes: 52428800` (50 MB)
- When: Index data calculated as 75 MB
- Then: Item not cached; every request loads from source
- And: Log warning: "Index exceeds max cache item size"

**Scenario 1.5.3: Update Max Cache Item Size Requires Restart**
- Given: Current `MaxCacheItemSizeBytes: 0`
- When: `PUT /api/config/datastorage { maxCacheItemSizeBytes: 52428800 }`
- Then: Returns 200 with `requiresRestart: true`
- And: Applied after restart

---

## 2. Database Query Settings Configuration

### 2.1 Configuration Model

The `DatabaseQuerySettings` class manages database command execution:

```csharp
public class DatabaseQuerySettings
{
    public bool CommandTimeoutEnabled { get; set; }  // default false
    public int CommandTimeLimit { get; set; } = 150; // seconds; 0 = unlimited
}
```

### 2.2 Command Timeout Enable/Disable

**SHALL** apply `CommandTimeLimit` to all database operations when enabled; unlimited when disabled.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| CommandTimeoutEnabled | false | bool | No |

**Scenario 2.2.1: Timeout Disabled (Default)**
- Given: `CommandTimeoutEnabled: false` (default)
- When: Slow query executes (takes 5 minutes)
- Then: Query completes successfully (no timeout)

**Scenario 2.2.2: Timeout Enabled**
- Given: `CommandTimeoutEnabled: true`, `CommandTimeLimit: 30`
- When: Query takes longer than 30 seconds
- Then: Command cancelled after 30 seconds
- And: Returns 504 Gateway Timeout

**Scenario 2.2.3: Enable Timeout at Runtime**
- Given: `CommandTimeoutEnabled: false`
- When: `PUT /api/config/databasequery { commandTimeoutEnabled: true, commandTimeLimit: 60 }`
- Then: Returns 200, applied immediately (no restart)
- And: Next query uses 60-second timeout

### 2.3 Command Time Limit Configuration

**SHALL** define maximum execution time for database queries; 0 means unlimited.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| CommandTimeLimit | 150 | >= 0 (0 = unlimited) | No |

**Scenario 2.3.1: Default Timeout**
- Given: `CommandTimeLimit: 150` (default, seconds)
- When: Query takes 120 seconds and `CommandTimeoutEnabled: true`
- Then: Query completes successfully

**Scenario 2.3.2: Query Exceeds Timeout**
- Given: `CommandTimeLimit: 30` and `CommandTimeoutEnabled: true`
- When: Query takes 45 seconds
- Then: Cancelled after 30 seconds
- And: Returns 504 with error "Command execution exceeded 30 seconds"

**Scenario 2.3.3: Unlimited Timeout (0)**
- Given: `CommandTimeLimit: 0` (unlimited)
- When: Long-running query executes
- Then: Query runs to completion (no timeout)

**Scenario 2.3.4: Update Command Time Limit**
- Given: Current `CommandTimeLimit: 150`
- When: `PUT /api/config/databasequery { commandTimeLimit: 300 }`
- Then: Returns 200, applied immediately
- And: Next query uses 300-second timeout

### 2.4 Applies To

Command timeout applies to:
- Initial index creation (first data load)
- Index reload (manual via `/reload` or automatic)
- Database query execution (index serving pivot requests)
- Database reconnection (after connection drop)

**Scenario 2.4.1: Timeout on Initial Creation**
- Given: Creating database index with `CommandTimeLimit: 30`, `CommandTimeoutEnabled: true`
- When: First query takes 40 seconds
- Then: Returns 504; index creation fails
- And: User must retry after diagnosing slow query

**Scenario 2.4.2: Timeout on Auto-Reload**
- Given: Index with `autoReload: true`, query normally takes 5 seconds
- When: Database slow, auto-reload takes 40 seconds, limit is 30 seconds
- Then: Reload fails (504); old data continues serving (if `keepDataOnRefresh: true`)

### 2.5 Database Query Settings API

**SHALL** expose Database Query settings via endpoints.

**Scenario 2.5.1: Get Database Query Settings**
- Given: Server running
- When: `GET /api/config/databasequery`
- Then: Returns 200 with JSON: `{ "commandTimeoutEnabled": false, "commandTimeLimit": 150 }`

**Scenario 2.5.2: Update Database Query Settings**
- Given: Current settings
- When: `PUT /api/config/databasequery { commandTimeoutEnabled: true, commandTimeLimit: 60 }`
- Then: Returns 200 with updated settings
- And: Applied immediately (no restart required)

---

## 3. HTTPS Settings Configuration

### 3.1 Configuration Model

The `HttpsSettings` class manages SSL/TLS certificate and protocol configuration:

```csharp
public enum HttpProtocolsOption { HTTP1, HTTP2, HTTP1_AND_HTTP2 }
public enum CertificateStoreLocationOption { CurrentUser, LocalMachine }

public class HttpsSettings
{
    public bool Enabled { get; set; }                            // default false
    public string? PfxPath { get; set; }
    public string? PfxPassword { get; set; }                     // secret — never persisted
    public string? PemCertPath { get; set; }
    public string? PemKeyPath { get; set; }
    public CertificateStoreLocationOption StoreLocation { get; set; } = CertificateStoreLocationOption.CurrentUser;
    public string StoreName { get; set; } = "My";
    public string? Thumbprint { get; set; }
    public HttpProtocolsOption Protocols { get; set; } = HttpProtocolsOption.HTTP1_AND_HTTP2;
    public bool AllowInvalidCertificates { get; set; }           // rejected in Production
}
```

### 3.2 HTTPS Enable/Disable

**SHALL** enable/disable HTTPS with certificate validation; requires exactly one certificate source when enabled.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| Enabled | false | bool | **Yes** |

**Scenario 3.2.1: HTTPS Disabled (Default)**
- Given: `enabled: false`
- When: Application starts
- Then: Server listens on `http://localhost:{port}` only
- And: CORS uses `http://localhost:{port}` base URL

**Scenario 3.2.2: Enable HTTPS with PFX Certificate**
- Given: `enabled: true`, `pfxPath: "certs/server.pfx"`, `pfxPassword: "secret"` (from user-secrets)
- When: Application starts
- Then: Server listens on `https://localhost:{port}`
- And: Certificate loaded from PFX file

**Scenario 3.2.3: Enable HTTPS with PEM Certificates**
- Given: `enabled: true`, `pemCertPath: "certs/server.crt"`, `pemKeyPath: "certs/server.key"`
- When: Application starts
- Then: Server listens on `https://localhost:{port}`
- And: Certificate/key loaded from PEM files

**Scenario 3.2.4: Enable HTTPS with Certificate Store**
- Given: `enabled: true`, `storeLocation: "LocalMachine"`, `storeName: "My"`, `thumbprint: "ABC123"`
- When: Application starts (on Windows)
- Then: Certificate loaded from Windows certificate store by thumbprint

**Scenario 3.2.5: Attempt Enable HTTPS without Certificate Source**
- Given: `enabled: true`, but no PFX, PEM, or Store configured
- When: `PUT /api/config/https { enabled: true }`
- Then: Returns 422 "Https.Enabled requires exactly one certificate source (PFX, PEM, or Store)"

**Scenario 3.2.6: Enable HTTPS Requires Restart**
- Given: Server running on HTTP
- When: `PUT /api/config/https { enabled: true, pfxPath: "certs/server.pfx" }`
- Then: Returns 200 with `requiresRestart: true`
- And: HTTPS not active until restart

### 3.3 Certificate Configuration

**SHALL** support multiple certificate sources: PFX, PEM, and Windows Certificate Store.

**Scenario 3.3.1: PFX Certificate Resolution**
- Given: `pfxPath: "certs/server.pfx"`, password from user-secrets
- When: Application starts with `https.enabled: true`
- Then: Certificate loaded via `new X509Certificate2(path, password)`

**Scenario 3.3.2: PEM Certificate Resolution**
- Given: `pemCertPath: "certs/server.crt"`, `pemKeyPath: "certs/server.key"`
- When: Application starts
- Then: Certificate loaded via `X509Certificate2.CreateFromPemFile(certPath, keyPath)`
- And: Exported to PKCS12 format (Linux-safe)

**Scenario 3.3.3: Certificate Store Resolution (Windows Only)**
- Given: `storeLocation: "LocalMachine"`, `storeName: "My"`, `thumbprint: "ABC123DEF"`
- When: Application starts on Windows
- Then: Certificate retrieved from store by thumbprint
- And: On non-Windows OS: returns 422 "Certificate store not supported on this OS"

**Scenario 3.3.4: Invalid Certificate File**
- Given: `pfxPath: "/nonexistent/cert.pfx"`
- When: Application starts with `https.enabled: true`
- Then: Startup fails with error "Certificate file not found: /nonexistent/cert.pfx"

**Scenario 3.3.5: AllowInvalidCertificates Forbidden in Production**
- Given: `allowInvalidCertificates: true` in Production environment
- When: Configuration validated at startup
- Then: Returns error "AllowInvalidCertificates forbidden in Production environment"
- And: Application does not start

**Scenario 3.3.6: AllowInvalidCertificates Allowed in Development**
- Given: `allowInvalidCertificates: true` in Development environment
- When: Configuration validated
- Then: Accepted with warning log: "Invalid certificates allowed (Development only)"

### 3.4 HTTP Protocol Configuration

**SHALL** support HTTP/1, HTTP/2, or both.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| Protocols | HTTP1_AND_HTTP2 | HTTP1 \| HTTP2 \| HTTP1_AND_HTTP2 | **Yes** |

**Scenario 3.4.1: HTTP/2 Only**
- Given: `protocols: "HTTP2"`, HTTPS enabled
- When: Application starts
- Then: Server accepts only HTTP/2 connections
- And: HTTP/1 clients receive protocol error

**Scenario 3.4.2: HTTP/1 Only**
- Given: `protocols: "HTTP1"`, HTTPS enabled
- When: Application starts
- Then: Server accepts only HTTP/1 connections
- And: HTTP/2 clients fall back or error

**Scenario 3.4.3: Both HTTP/1 and HTTP/2**
- Given: `protocols: "HTTP1_AND_HTTP2"` (default)
- When: Application starts
- Then: Server accepts both HTTP/1 and HTTP/2
- And: Client chooses version (typically HTTP/2 for modern clients)

### 3.5 HTTPS Configuration API

**Scenario 3.5.1: Get HTTPS Settings**
- Given: Server running
- When: `GET /api/config/https`
- Then: Returns 200 with JSON (passwords omitted)

**Scenario 3.5.2: Update HTTPS Settings**
- Given: Current HTTPS disabled
- When: `PUT /api/config/https { enabled: true, protocols: "HTTP2" }`
- Then: Returns 200 with `requiresRestart: true`

---

## 4. Security Settings Configuration

### 4.1 Configuration Model

The `SecuritySettings` class manages authentication and CORS:

```csharp
public class SecuritySettings
{
    public bool BasicAuthEnabled { get; set; }                 // default false
    public List<string> AllowedOrigins { get; set; } = ["*"];  // default allow all
}
```

### 4.2 Basic Authentication Enable/Disable

**SHALL** enable/disable Basic Auth; when enabled, all endpoints require `Authorization: Basic` header.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|No |
| BasicAuthEnabled | false | bool |

**Scenario 4.2.1: Basic Auth Disabled (Default)**
- Given: `basicAuthEnabled: false`
- When: Pivot request sent without credentials
- Then: Accepted; no authentication required

**Scenario 4.2.2: Enable Basic Auth**
- Given: `basicAuthEnabled: false`
- When: `PUT /api/security { basicAuthEnabled: true }`
- Then: Returns 200, applied immediately
- And: Subsequent requests without credentials return 401

**Scenario 4.2.3: Basic Auth with Valid Credentials**
- Given: `basicAuthEnabled: true`, user "admin" with password "secret"
- When: Request sent with `Authorization: Basic YWRtaW46c2VjcmV0` (base64)
- Then: Request accepted (200)

**Scenario 4.2.4: Basic Auth with Invalid Credentials**
- Given: `basicAuthEnabled: true`
- When: Request sent with `Authorization: Basic aW52YWxpZDppbnZhbGlk` (base64)
- Then: Returns 401 with `WWW-Authenticate: Basic realm="DataServer"`

**Scenario 4.2.5: User Management Endpoint Anonymous**
- Given: `basicAuthEnabled: true`
- When: `POST /api/security/users` (create user, no credentials)
- Then: Accepted (creates first admin user)
- And: Subsequent admin user creation requires authentication

**Scenario 4.2.6: Disable Basic Auth**
- Given: `basicAuthEnabled: true`
- When: `PUT /api/security { basicAuthEnabled: false }`
- Then: Returns 200, applied immediately
- And: Credentials no longer required

### 4.3 CORS Origin Validation

**SHALL** validate `Origin` header against `AllowedOrigins`; reject mismatches.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|No |
| AllowedOrigins | ["*"] | List of origins or ["*"] |

**Scenario 4.3.1: Allow All Origins (Wildcard)**
- Given: `allowedOrigins: ["*"]`
- When: Browser request from any origin, with `Origin` header
- Then: Accepted; `Access-Control-Allow-Origin: *` in response

**Scenario 4.3.2: Restrict to Specific Origins**
- Given: `allowedOrigins: ["https://app.example.com", "https://admin.example.com"]`
- When: Request from `https://app.example.com` with `Origin` header
- Then: Accepted; `Access-Control-Allow-Origin: https://app.example.com` in response

**Scenario 4.3.3: Origin Not in Allowed List**
- Given: `allowedOrigins: ["https://app.example.com"]`
- When: Request from `https://attacker.com` with `Origin` header
- Then: Returns 403 Forbidden
- And: Logged: "Origin not allowed: https://attacker.com"

**Scenario 4.3.4: No Origin Header (Non-Browser Request)**
- Given: `allowedOrigins: ["https://app.example.com"]` (restricted)
- When: Request without `Origin` header (e.g., curl, server-to-server)
- Then: Returns 403 (defense-in-depth for CSRF)

**Scenario 4.3.5: Update Allowed Origins**
- Given: Current `allowedOrigins: ["*"]`
- When: `PUT /api/security { allowedOrigins: ["https://app.example.com"] }`
- Then: Returns 200, applied immediately (no restart)
- And: Next request from other origins rejected

### 4.4 User Management APIs

**Scenario 4.4.1: Create User**
- Given: `basicAuthEnabled: true`
- When: `POST /api/security/users { username: "analyst", password: "p@ssw0rd" }`
- Then: Returns 201 with user metadata
- And: Password hashed with PBKDF2, not stored plaintext

**Scenario 4.4.2: List Users**
- Given: Server with 3 users
- When: `GET /api/security/users` (with credentials)
- Then: Returns 200 with username list (no passwords)

**Scenario 4.4.3: Delete User**
- Given: User "analyst" exists
- When: `DELETE /api/security/users/analyst` (with admin credentials)
- Then: Returns 204
- And: User cannot authenticate

---

## 5. Report Sharing Settings Configuration

### 5.1 Configuration Model

The `ReportSharingSettings` class manages report storage and lifecycle:

```csharp
public class ReportSharingSettings
{
    public bool EndpointEnabled { get; set; }                  // default false
    public string StoragePath { get; set; } = "DataServer/reports";
    public int ReportExpirationTimeMinutes { get; set; }       // 0 = permanent
    public int MaxFolderSizeMB { get; set; }                   // 0 = unlimited
    public bool AutoDelete { get; set; }                       // default false
}
```

### 5.2 Endpoint Enable/Disable

**SHALL** enable/disable report sharing endpoints; when disabled, all report endpoints return 404.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|No |
| EndpointEnabled | false | bool |

**Scenario 5.2.1: Report Sharing Disabled (Default)**
- Given: `endpointEnabled: false`
- When: `POST /api/reportsharing/reports`
- Then: Returns 404 "Report sharing is disabled"

**Scenario 5.2.2: Enable Report Sharing**
- Given: `endpointEnabled: false`
- When: `PUT /api/config/reportsharing { endpointEnabled: true }`
- Then: Returns 200, applied immediately
- And: `/api/reportsharing/*` endpoints now available

**Scenario 5.2.3: Disable Report Sharing**
- Given: `endpointEnabled: true`, reports stored
- When: `PUT /api/config/reportsharing { endpointEnabled: false }`
- Then: Returns 200, applied immediately
- And: Existing reports not deleted (remain on disk)
- And: All endpoints return 404

### 5.3 Storage Path Configuration

**SHALL** define directory for report storage; must be writable and within `DataFolder`.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|**Yes** |
| StoragePath | DataServer/reports | Non-empty, writable, within DataFolder | **Yes** |

**Scenario 5.3.1: Default Storage Path**
- Given: `storagePath: "DataServer/reports"` (default)
- When: Application starts with reports enabled
- Then: Directory `DataServer/reports` created if missing
- And: Subdirectories: `DataServer/reports/`.quota

**Scenario 5.3.2: Custom Storage Path**
- Given: `storagePath: "/custom/reports"`
- When: Application starts with reports enabled
- Then: Directory `/custom/reports` created/validated
- And: Reports stored there

**Scenario 5.3.3: Storage Path Not Writable**
- Given: `storagePath: "/readonly/reports"` (read-only permission)
- When: Application starts with `endpointEnabled: true`
- Then: Startup fails with error "Storage path not writable: /readonly/reports"

**Scenario 5.3.4: Storage Path Update Requires Restart**
- Given: Current `storagePath: "DataServer/reports"`
- When: `PUT /api/config/reportsharing { storagePath: "/new/reports" }`
- Then: Returns 200 with `requiresRestart: true`
- And: New path used after restart

### 5.4 Report Expiration Configuration

**SHALL** delete expired reports; 0 means permanent storage.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|No |
| ReportExpirationTimeMinutes | 0 | >= 0 (0 = permanent) |

**Scenario 5.4.1: Permanent Reports (Default)**
- Given: `reportExpirationTimeMinutes: 0` (default)
- When: Report saved
- Then: `expiresAt: null` in metadata
- And: Report never expires automatically

**Scenario 5.4.2: Report Expiration Set**
- Given: `reportExpirationTimeMinutes: 1440` (24 hours)
- When: Report saved at 10:00 AM
- Then: `expiresAt: 2025-01-02T10:00:00Z` in metadata
- And: `GET /api/reportsharing/reports/{id}` at 10:01 AM next day returns 410 Gone

**Scenario 5.4.3: Read Expired Report**
- Given: Report expired at 2025-01-02T10:00:00Z, `autoDelete: false`
- When: `GET /api/reportsharing/reports/{id}` after expiration
- Then: Returns 410 Gone
- And: File remains on disk (not deleted)

**Scenario 5.4.4: Update Expiration Time**
- Given: Current `reportExpirationTimeMinutes: 1440`
- When: `PUT /api/config/reportsharing { reportExpirationTimeMinutes: 4320 }` (72 hours)
- Then: Returns 200, applied immediately
- And: Existing reports' expiration not recalculated (only new reports use new setting)

### 5.5 Storage Quota Configuration

**SHALL** enforce maximum folder size; oldest reports deleted when quota exceeded.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|No |
| MaxFolderSizeMB | 0 | >= 0 (0 = unlimited) |

**Scenario 5.5.1: Unlimited Quota (Default)**
- Given: `maxFolderSizeMB: 0` (unlimited)
- When: Reports saved
- Then: No quota enforcement; reports grow unbounded

**Scenario 5.5.2: Quota Limit Set**
- Given: `maxFolderSizeMB: 1000` (1 GB)
- When: Reports total 950 MB, new 100 MB report saved
- Then: Oldest reports deleted to stay within 1 GB
- And: New report saved successfully

**Scenario 5.5.3: Save Exceeds Quota**
- Given: `maxFolderSizeMB: 1000`, folder currently 1000 MB
- When: `POST /api/reportsharing/reports` with 10 MB report
- Then: Returns 507 Insufficient Storage
- And: Report not saved

**Scenario 5.5.4: Quota Tracking**
- Given: Reports stored
- When: `GET /api/reportsharing/reports` (metadata list)
- Then: Response includes `{ currentSizeBytes, quotaMB, percentUsed }`

### 5.6 Auto-Delete Configuration

**SHALL** run cleanup sweep every 5 minutes when enabled; removes expired reports and enforces quota.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|No |
| AutoDelete | false | bool |

**Scenario 5.6.1: Auto-Delete Disabled (Default)**
- Given: `autoDelete: false`
- When: Report expires
- Then: File remains on disk
- And: Manual cleanup required or `GET` returns 410

**Scenario 5.6.2: Enable Auto-Delete**
- Given: `autoDelete: false`
- When: `PUT /api/config/reportsharing { autoDelete: true }`
- Then: Returns 200, applied immediately
- And: Background service starts 5-minute sweep cycle

**Scenario 5.6.3: Auto-Delete Removes Expired Reports**
- Given: `autoDelete: true`, 5 expired reports
- When: Cleanup sweep runs
- Then: Expired reports deleted from disk
- And: Log entry: "ReportSharing: Deleted 5 expired reports"

**Scenario 5.6.4: Auto-Delete Enforces Quota**
- Given: `autoDelete: true`, `maxFolderSizeMB: 1000`, folder at 1100 MB
- When: Cleanup sweep runs
- Then: Oldest reports deleted until folder < 1000 MB
- And: Log entry: "ReportSharing: Evicted 150 MB to stay within quota"

### 5.7 Report Sharing APIs

**Scenario 5.7.1: Save Report**
- Given: `endpointEnabled: true`
- When: `POST /api/reportsharing/reports { name: "Q4-Review", owner: "analyst@corp", payload: {...} }`
- Then: Returns 201 with `{ reportId, createdAt, expiresAt, sizeBytes }`

**Scenario 5.7.2: Retrieve Report**
- Given: Report "abc-123" saved
- When: `GET /api/reportsharing/reports/abc-123`
- Then: Returns 200 with full report payload

**Scenario 5.7.3: List Reports**
- Given: 5 reports saved
- When: `GET /api/reportsharing/reports`
- Then: Returns 200 with metadata list (no payloads)

**Scenario 5.7.4: Delete Report**
- Given: Report "abc-123" saved
- When: `DELETE /api/reportsharing/reports/abc-123`
- Then: Returns 204; file deleted

---

## 6. Index Configuration

### 6.1 Index Registration from appsettings.json

**SHALL** load index definitions from `DataServer:Indexes` array in `appsettings.json` at startup and make them available immediately without manual file creation.

**Scenario 6.1.1: Index Declared in appsettings.json**
- Given: `appsettings.json` contains `DataServer:Indexes[0]` with `Index: "Sales-Data"`, `SourceType: "Csv"`, `SourcePath: "sales.csv"`
- When: Application starts
- Then: `GET /api/indexes/Sales-Data` returns 200 with `status: "Ready"` and `rowCount > 0`

**Scenario 6.1.2: Multiple Indexes Coexist**
- Given: appsettings defines 3 indexes (JSON, CSV, Database)
- When: `GET /api/indexes`
- Then: Returns 200 with all 3 indexes listed

### 6.2 Index Lifecycle APIs

**SHALL** support full index lifecycle via REST API: create, edit, reload, enable/disable, delete.

**Scenario 6.2.1: Create Index**
- Given: No index named "test-index"
- When: `POST /api/indexes { index: "test-index", name: "Test", sourceType: "Csv", sourcePath: "test.csv" }`
- Then: Returns 201 with `status: "Ready"` and loads data

**Scenario 6.2.2: Edit Index**
- Given: Index "Sales-Data" exists
- When: `PUT /api/indexes/Sales-Data { name: "Sales Data Updated", csvDelimiter: ";" }`
- Then: Returns 200, metadata updated, data reloaded

**Scenario 6.2.3: Attempt to Change SourceType**
- Given: Index "Sales-Data" with `sourceType: "Csv"`
- When: `PUT /api/indexes/Sales-Data { sourceType: "Json" }`
- Then: Returns 422 with error message "SourceType is immutable"

**Scenario 6.2.4: Reload Index**
- Given: Index "Sales-Data" with stale data
- When: `POST /api/indexes/Sales-Data/reload`
- Then: Returns 200 with updated `rowCount`

**Scenario 6.2.5: Enable/Disable Index**
- Given: Index "Sales-Data"
- When: `POST /api/indexes/Sales-Data/disable`
- Then: Returns 200; pivot requests to it return 423
- When: `POST /api/indexes/Sales-Data/enable`
- Then: Returns 200; index loads and serves again

**Scenario 6.2.6: Delete Index**
- Given: Index "Sales-Data"
- When: `DELETE /api/indexes/Sales-Data`
- Then: Returns 204; subsequent pivot requests return 404

### 6.3 Index Naming & Uniqueness

**SHALL** enforce case-insensitive unique index names matching pattern `[A-Za-z0-9._-]{1,64}`.

**Scenario 6.3.1: Duplicate Index Creation**
- Given: Index "Sales-Data" exists
- When: `POST /api/indexes { index: "sales-data" ... }` (lowercase variant)
- Then: Returns 409 "Index already exists"

**Scenario 6.3.2: Invalid Index Name**
- Given: No index
- When: `POST /api/indexes { index: "sales@data!" ... }`
- Then: Returns 422 "Invalid index name"

### 6.4 CSV Separator Configuration

**SHALL** honor CSV delimiter, decimal separator, and thousands separator options on load.

**Scenario 6.4.1: Custom CSV Delimiters**
- Given: CSV file with semicolon-delimited columns and comma as decimal separator
- When: Index created with `csvDelimiter: ";"`, `csvDecimalSeparator: ","`
- Then: Data loads correctly with proper field count and numeric precision

**Scenario 6.4.2: Edit Separators**
- Given: Index using default separators
- When: `PUT /api/indexes/sales-csv { csvDelimiter: "|" }`
- Then: Data reloaded with new delimiter honored

### 6.5 Field Mapping (Type Inference)

**SHALL** support optional per-field type mapping (Number, String, Date); disabled by default (autodetect).

**Scenario 6.5.1: Mapping Disabled (Default)**
- Given: Index with `mappingEnabled: false`
- When: Data loads
- Then: Field types autodetected; all fields accessible as discovered

**Scenario 6.5.2: Mapping Enabled**
- Given: Index with `mappingEnabled: true`, `fieldMappings: [{ fieldName: "Price", type: "Number" }]`
- When: Data loads
- Then: "Price" field enforced as Number type

**Scenario 6.5.3: Disable Mapping**
- Given: Index with `mappingEnabled: true`, custom mappings applied
- When: `PUT /api/indexes/{index} { mappingEnabled: false }`
- Then: Mappings deleted, autodetect restored

### 6.6 Database Test Connection

**SHALL** test database connection and return success/failure with latency.

**Scenario 6.6.1: Test Connection Success**
- Given: Index with valid `connectionStringName`
- When: `POST /api/indexes/{index}/test-connection`
- Then: Returns 200 `{ success: true, latencyMs: 42 }`

**Scenario 6.6.2: Test Connection Failure**
- Given: Index with invalid connection string
- When: `POST /api/indexes/{index}/test-connection`
- Then: Returns 502 `{ success: false, error: "Unable to connect: ..." }`

---

## 7. Pivot Request Routing

### 7.1 Index-Aware Pivot Endpoint

**SHALL** accept `index` field in pivot request and route to the named index; default to `DefaultIndexId` if absent.

**Scenario 7.1.1: Pivot Request with Index**
- Given: Indexes "Sales-Data" and "Sales-Db" registered
- When: `POST /api/pivot/post { index: "Sales-Data", action: "fetchFieldMembers", memberName: "Country" }`
- Then: Returns 200 with members from "Sales-Data" index

**Scenario 7.1.2: Pivot Request without Index (Legacy)**
- Given: `DefaultIndexId: "default"`
- When: `POST /api/pivot/post { action: "fetchFieldMembers", memberName: "Country" }` (no index field)
- Then: Returns 200 using "default" index (backward compatible)

**Scenario 7.1.3: Unknown Index**
- Given: No index named "unknown"
- When: `POST /api/pivot/post { index: "unknown", action: "..." }`
- Then: Returns 404 with valid index names listed

**Scenario 7.1.4: Disabled Index**
- Given: Index "Sales-Data" with `enabled: false`
- When: `POST /api/pivot/post { index: "Sales-Data", action: "..." }`
- Then: Returns 423 "Index is disabled"

### 7.2 Cache Key Prefixing

**SHALL** prefix all per-index cache entries with index name to prevent data collision.

**Scenario 7.2.1: Separate Cache per Index**
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

