# Syncfusion Pivot Table Data Server — Phase 1 Detailed Technical Specification

## Document Information

- **Version:** 2.1
- **Phase:** Phase 1 – Backend Infrastructure + Client Integration
- **Scope:** Server-Side Controller, Configuration Management, Index Configuration (full Indexes-tab parity: multiple JSON/CSV/DB sources, edit, reload, enable/disable, mapping, refresh time), JSON File-Driven Configuration Testing, Client-Side Sample Configuration
- **Note:** This feature is a Syncfusion Pivot Table data server, functionally similar to the Flexmonster Data Server (which serves Flexmonster Pivot Table); the Flexmonster design is referenced where its behavior is adopted.
- **Out of Scope:** Admin Panel UI, Monitoring UI, Diagnostics UI
- **Repository:** `server-side-pivot-engine-for-pivot-table` → `PivotController/`

---

# 1. Purpose

This document defines the detailed technical specification for a backend data server for the **Syncfusion Pivot Table** that supports configuration-driven indexing, security, HTTPS, report sharing, logging, and data connectivity — used by the Syncfusion Pivot Table in server mode. The feature is functionally similar to the Flexmonster Data Server, which serves the Flexmonster Pivot Table; its design is used as a reference where noted.

Phase 1 delivers all backend functionality required by the future Syncfusion Admin Panel **without implementing the Admin Panel itself**. The implementation order within Phase 1 is:

1. **Step 1 — Index Configuration (backend).** Indexes are named, registered in `appsettings.json`, and managed by the server. This is the first implementation step because every other feature (pivot routing, caching, database query settings, report sharing) depends on a named-index model.
2. **Steps 2–7 — Remaining settings sections.** General, Data Storage, Database Query, HTTPS, Security, and Report Sharing settings are implemented as configurable, persisted models exposing APIs for the future Admin Panel.
3. **Step 8 (final) — Client-side pivot table sample configuration.** The sample pivot table is updated to pass the index name, similar to how Flexmonster passes `index` in its `api` data source. After this step the data server exposes a simple endpoint URL (e.g., `http://localhost:5285/api/pivot/post`); the Admin Panel (Phase 2) will point to this same backend to read/update all data server settings.

---

# 2. Objectives

The server-side controller shall:

- Register and serve **named indexes** configured in `appsettings.json` (similar to the Flexmonster Data Server's approach).
- Accept an **index name from the client** on every pivot request and route to the correct dataset.
- Manage index lifecycle (create, reload, delete, persist metadata).
- Support JSON, CSV, and Database sources.
- Support HTTPS configuration (PFX/PEM/certificate store).
- Support Basic Authentication and CORS origin validation.
- Support Report Sharing.
- Support structured Logging with configurable minimum level.
- Persist configurations and expose REST APIs for the future Admin Panel.
- Expose a simple, stable endpoint URL (e.g., `http://localhost:5285/api/pivot/post`) that the Admin Panel will target in Phase 2 to update server settings at runtime.
- Support full Indexes-tab parity (similar to the Flexmonster Data Server's Indexes tab): add indexes from multiple JSON/CSV files and multiple databases; edit indexes (everything except the data source type); reload indexes manually and on a configured refresh time; enable/disable indexes; per-field Mapping (Number/String/Date) with autodetect as default; CSV delimiter/decimal/thousands separator options; database connection-string Test.
- Support JSON file-driven configuration testing: every Data Server option is editable in a runtime JSON settings file whose changes are detected and applied to the running backend without code changes (hot-applied where possible, flagged `requiresRestart` otherwise), so each Phase 1 setting can be verified by editing the file alone.

---

# 3. Key Design Decisions

## 3.1 Index Configuration is the first step

Index configuration is the foundation of the data server. Named indexes are resolved at startup from **`appsettings.json`** — this follows the Flexmonster Data Server approach, which updates `appsettings.json` for its indexing option. Precedence for index metadata:

```
appsettings.json (initial source of truth, edited by the developer)
        ↓  (one-time bootstrap / explicit save)
DataServer/indexes.json  (runtime-managed copy, editable via /api/indexes)
```

## 3.2 Client passes the index name (similar to the Flexmonster `api` contract)

For reference, Flexmonster connects to its Data Server with:

```js
dataSource: {
  type: "api",
  url: "http://localhost:9500",
  index: "Sales-Data"
},
```

The data server must accept the same `index` concept: every pivot request carries the index name and the server routes to the registered index with that name. The Syncfusion sample uses the equivalent contract (see §10).

## 3.3 Client-side sample configuration is the last step

The index name is passed from the client side of the pivot table, either:

- **Sample-level:** defined as a constant in the sample page/script (e.g., `PIVOT_INDEX` in `Sample/pivot.js`), or
- **Source-level:** defined in a shared client configuration module used by all samples.

## 3.4 Single endpoint for the Admin Panel

After Phase 1, the running data server exposes one simple endpoint URL, e.g. `http://localhost:5285/api/pivot/post`. The Admin Panel (Phase 2) uses this URL plus the configuration/index APIs to read and update all backend data server settings — no backend architectural change is required between the phases.

---

# 4. High-Level Architecture

```text
+------------------------------+
| Syncfusion Pivot Table       |      Syncfusion PivotView (Sample)
| dataSourceSettings: { url,   |      (index sent on every request)
|   index: "Sales-Data" }      |      
+------------------------------+
            |
            |  POST /api/pivot/post  { ..., index: "Sales-Data" }
            v
+--------------------------------+
|         Data Server            |
|  PivotController (index-aware) |
|  Configuration Manager         |
|  Index Manager  (Step 1)       |
|  Security Manager              |
|  HTTPS Manager                 |
|  Report Share Manager          |
|  Database Query Manager        |
|  Logging Manager               |
+--------------------------------+
            |
    +-------+-------+
    |               |
    v               v
 JSON/CSV       Databases
```

## 4.1 Layer Diagram

```text
┌────────────────────────────────────────────────────────────────────┐
│                       HTTP Pipeline (Kestrel)                       │
│  Port | HTTPS (Kestrel) | HttpsRedirection | CORS | BasicAuth       │
└──────────────┬─────────────────────────────────────────────────────┘
               │
┌──────────────▼─────────────────────────────────────────────────────┐
│                        API Controllers (MVC)                       │
│  PivotController   ConfigController   IndexController              │
│  SecurityController   ReportsController                            │
└──────────────┬─────────────────────────────────────────────────────┘
               │
┌──────────────▼─────────────────────────────────────────────────────┐
│                         Application Services                       │
│  ConfigService  IndexService  UserService  ReportSharingService    │
│  CertificateService  OriginValidator                              │
└───────┬──────────────────┬──────────────────┬──────────────────────┘
        │                  │                  │
┌───────▼───────┐  ┌───────▼────────┐  ┌───────▼─────────────────────┐
│ Configuration │  │  Index Manager │  │      Data Access Layer       │
│ Persistence   │  │  (cache hub)   │  │  IDataSourceProvider         │
│ config.json   │  │  MemoryCache   │  │   ├ JsonDataProvider          │
│ users.json    │  │  IIndexStore   │  │   ├ CsvDataProvider           │
│ indexes.json  │  │                │  │   └ DatabaseDataProvider      │
└───────────────┘  └────────────────┘  │       └ IDatabaseQueryProvider│
                                       │          (Sqlite, SqlServer,  │
                                       │           PostgreSql, MySql,  │
                                       │           Oracle, MariaDb)    │
                                       └────────────┬────────────────┘
                                                      │
                              ┌───────────────────────▼────────┐
                              │   Logging (manager categories)  │
                              │   Console   +    File (JSON)    │
                              └────────────────────────────────┘
```

## 4.2 Component Responsibilities

| Component | Responsibility |
|-----------|----------------|
| `DataServerConfiguration` | Root config document (all 7 sections) with versioning |
| `ConfigurationPersistenceService` | Atomic load/save of `config.json`; migration hooks; quarantine of corrupt files |
| `ConfigurationService` | Validation + update orchestration, delta merge |
| `IndexManager` (`IIndexManager`) | Index lifecycle; data cache hub; retry policy; keep-data-on-refresh |
| `DataSourceProviderFactory` | Resolves `IDataSourceProvider` by `DataSourceType` |
| `IDataSourceProvider` | Loads `IReadOnlyList<ExpandoObject>` from a source |
| `IDatabaseQueryProvider` | Provider-specific `DbConnection`/`DbCommand` + timeout + read-only guard |
| `CertificateService` | Resolves `X509Certificate2` from PFX / PEM / store |
| `OriginValidator` | Populates the effective CORS policy from `SecuritySettings.AllowedOrigins` |
| `UserService` | User CRUD + PBKDF2 hashing + constant-time verify |
| `ReportSharingService` | Report save/get/delete, expiry, quota sweep |
| `DataServerLog` | Category constants + structured log helper |

---

# 5. Controller Responsibilities

## Configuration Manager

- Loading configurations.
- Applying configurations.
- Validating configurations.
- Persisting configurations.
- Serving configuration APIs (`/api/config*`) for the future Admin Panel.

## Index Manager (implementation Step 1)

- Loading index definitions from `appsettings.json` at startup.
- Creating, editing, reloading, deleting, enabling, and disabling indexes.
- Resolving the requested index by name from pivot requests.
- Managing cache per index, retry processing, keep-data-on-refresh.

## Security Manager

- User management.
- Basic Authentication.
- Origin validation.

## HTTPS Manager

- Certificate loading (PFX/PEM/store).
- SSL protocol configuration.

## Report Sharing Manager

- Saving reports.
- Retrieving reports.
- Expiring reports.
- Folder/quota maintenance.

---

# 6. Supported Configuration Sections — Data Server Settings

All seven Data Server settings sections are configurable through the server-side controller, initialized with standard/default values.

## 6.1 General Settings

### Configuration Model

```csharp
public enum LogLevelOption { Info, Warn, Error, Fatal }

public class GeneralSettings
{
    public int Port { get; set; } = 9500;
    public LogLevelOption MinimumLogLevel { get; set; } = LogLevelOption.Info;
    public string DataFolder { get; set; } = "DataSource";
}
```

### Properties

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| Port | 9500 | int 1–65535 | Yes (listener) |
| MinimumLogLevel | Info | Info / Warn / Error / Fatal | No (applied on save) |
| DataFolder | DataSource | non-empty string | Yes |

### Valid Log Levels

- INFO
- WARN
- ERROR
- FATAL

## 6.2 Data Storage Settings

### Configuration Model

```csharp
public class DataStorageSettings
{
    public int RetryAttempts { get; set; } = 3;       // 0–30 (0 = no retry)
    public int CacheSizeLimit { get; set; } = 100;    // >= 0 (0 disables caching)
    public bool KeepDataOnRefresh { get; set; } = true;
    public long MaxCacheItemSizeBytes { get; set; }   // 0 = unlimited
}
```

### Properties

| Property | Default | Validation |
|----------|---------|------------|
| RetryAttempts | 3 | 0–30 |
| CacheSizeLimit | 100 | >= 0 |
| KeepDataOnRefresh | true | bool |
| MaxCacheItemSizeBytes | 0 | >= 0 (0 = unlimited) |

### Retry Behavior

- Retries transient failures (IO, provider timeout, transient `DbException`) with exponential backoff (`250ms * 2^attempt` + jitter), bounded by `RetryAttempts`.
- Validation / data-shape errors (`InvalidDataException`) are **not** retried (fail fast).

## 6.3 Database Query Settings

### Configuration Model

```csharp
public class DatabaseQuerySettings
{
    public bool CommandTimeoutEnabled { get; set; } // default false
    public int CommandTimeLimit { get; set; } = 150; // seconds; 0 = unlimited
}
```

### Defaults

- Command Timeout: Disabled
- Command Time Limit: 150 seconds

### Validation

- 0 = Unlimited
- Minimum value = 0

### Applies To

- Initial index creation
- Index reload
- Database query execution
- Database reconnection

## 6.4 HTTPS Settings

### Configuration Model

```csharp
public enum HttpProtocolsOption { HTTP1, HTTP2, HTTP1_AND_HTTP2 }
public enum CertificateStoreLocationOption { CurrentUser, LocalMachine }

public class HttpsSettings
{
    public bool Enabled { get; set; }                            // default false
    public string? PfxPath { get; set; }
    public string? PfxPassword { get; set; }                     // secret — never persisted in config.json
    public string? PemCertPath { get; set; }
    public string? PemKeyPath { get; set; }
    public CertificateStoreLocationOption StoreLocation { get; set; } = CertificateStoreLocationOption.CurrentUser;
    public string StoreName { get; set; } = "My";
    public string? Thumbprint { get; set; }
    public HttpProtocolsOption Protocols { get; set; } = HttpProtocolsOption.HTTP1_AND_HTTP2;
    public bool AllowInvalidCertificates { get; set; }           // rejected in Production
}
```

### Supported Features

- HTTPS Enable/Disable
- PFX Certificates
- PEM Certificates
- Store (Subject) Certificates
- HTTP1
- HTTP2

### Default Values

| Property | Default |
|----------|---------|
| HTTPS | Disabled |
| Protocols | HTTP1_AND_HTTP2 |
| AllowInvalid | false |
| Store Location | CurrentUser |
| Store Name | My |

## 6.5 Security Settings

### Configuration Model

```csharp
public class SecuritySettings
{
    public bool BasicAuthEnabled { get; set; }                 // default false
    public List<string> AllowedOrigins { get; set; } = ["*"];  // default allow all
}
```

### Defaults

- BasicAuthEnabled = false
- AllowedOrigins = ["*"]

### Supported Features

- Basic Authentication
- User Management (see §8)
- CORS Origin Validation

## 6.6 Report Sharing Settings

### Configuration Model

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

### Defaults

| Property | Default |
|----------|---------|
| EndpointEnabled | false |
| StoragePath | DataServer/reports |
| Expiration | 0 (permanent) |
| MaxFolderSize | 0 (unlimited) |
| AutoDelete | false |

### Notes

- Expiration = 0 means permanent storage.
- MaxFolderSize = 0 means unlimited storage.
- When `EndpointEnabled=false`, report endpoints return `404 ReportSharingDisabled`.

## 6.7 Index Configuration (Step 1 — the first implementation step)

### Registration in `appsettings.json` (similar to the Flexmonster approach)

Indexes are declared in `appsettings.json`. The existing `PivotDataSource` section is extended so the index **name** and its data source **type/path/query** live together:

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "DataServer": {
    "DefaultIndexId": "default",
    "General": { "Port": 5285, "MinimumLogLevel": "Info" },
    "DataStorage": { "RetryAttempts": 3, "CacheSizeLimit": 100, "KeepDataOnRefresh": true },
    "DatabaseQuery": { "CommandTimeoutEnabled": false, "CommandTimeLimit": 150 },
    "Https": { "Enabled": false, "Protocols": "HTTP1_AND_HTTP2" },
    "Security": { "BasicAuthEnabled": false, "AllowedOrigins": ["*"] },
    "ReportSharing": { "EndpointEnabled": false },
    "Indexes": [
      {
        "Index": "Sales-Data",
        "Name": "Sales Data",
        "SourceType": "Csv",
        "SourcePath": "sales.csv",
        "CsvDelimiter": ",",
        "CsvDecimalSeparator": ".",
        "CsvThousandsSeparator": ",",
        "MappingEnabled": false,
        "AutoReload": false,
        "ReloadIntervalMinutes": 0,
        "Enabled": true
      },
      {
        "Index": "Sales-Json",
        "Name": "Sales JSON",
        "SourceType": "Json",
        "SourcePath": "sales-analysis.json",
        "MappingEnabled": true,
        "FieldMappings": [
          { "FieldName": "TotalRevenue", "Type": "Number" },
          { "FieldName": "OrderDate", "Type": "Date" }
        ],
        "Enabled": true
      },
      {
        "Index": "Sales-Db",
        "Name": "Sales Database",
        "SourceType": "Database",
        "DatabaseType": "MSSQL",
        "ConnectionStringName": "SalesDb",
        "Query": "SELECT Region, Country, ItemType, SalesChannel, OrderDate, UnitsSold, TotalRevenue, TotalCost, TotalProfit FROM Sales",
        "Enabled": true
      }
    ]
  },
  "ConnectionStrings": {
    "SalesDb": "Data Source=DataSource/sales.db"
  }
}
```

> **Backward compatibility:** a one-time bootstrap migrates the legacy `PivotDataSource` block (Type/JsonPath/CsvPath/DatabasePath/Query) into the index named `default`, so existing clients (and the current `Sample/pivot.js`) keep working.

### Configuration Model

```csharp
public enum DataSourceType { Json, Csv, SqlServer, PostgreSQL, MySql, Oracle, MariaDb }
public enum DatabaseType { MySql, MariaDB, MSSQL, PostgreSQL, Oracle, Sqlite }
public enum MappingDataType { Number, String, Date }

public class IndexDefinition
{
    public required string Index { get; set; }   // index name, e.g. "Sales-Data" (unique, immutable)
    public required string Name { get; set; }    // display name
    public DataSourceType SourceType { get; set; } // immutable after creation (Flexmonster-like edit rule)
    public string? SourcePath { get; set; }      // JSON/CSV path under DataFolder
    // CSV-specific (Flexmonster-like options)
    public char CsvDelimiter { get; set; } = ',';
    public char CsvDecimalSeparator { get; set; } = '.';
    public char CsvThousandsSeparator { get; set; } = ',';
    // Database-specific (Flexmonster-like options)
    public DatabaseType? DatabaseType { get; set; }
    public string? ConnectionStringName { get; set; } // references ConnectionStrings (never inline secrets)
    public string? Query { get; set; }
    // Refresh time (minutes from server start; 0 = never reload)
    public bool AutoReload { get; set; } = false;
    public int ReloadIntervalMinutes { get; set; } // >0 requires AutoReload=true
    // Mapping (disabled by default = autodetect)
    public bool MappingEnabled { get; set; } = false;
    public List<IndexFieldMapping>? FieldMappings { get; set; } // cleared when MappingEnabled=false
    public bool Enabled { get; set; } = true;   // disabled indexes are not preloaded/served
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class IndexFieldMapping
{
    public required string FieldName { get; set; }
    public MappingDataType Type { get; set; } // Number | String | Date
}
```

### Supported Sources

- JSON
- CSV
- SQL Server
- PostgreSQL
- MySQL
- Oracle
- MariaDB

### Index Naming Contract

- The `Index` value is the public identifier exchanged between client and server (e.g., `"Sales-Data"`).
- Case-insensitive uniqueness; immutable after creation; pattern `[A-Za-z0-9._-]{1,64}`.
- Pivot requests without an index use `DataServer:DefaultIndexId`.

### Indexes-Tab Parity (new in v2.1, similar to the Flexmonster Indexes tab)

**Add Index.** An index may point at any JSON file, CSV file, or supported database. Multiple indexes — including several JSON/CSV files and several databases — coexist in the `Indexes` array; each is loaded and cached independently under `data:{index}`.

**Edit Index.** Every property is editable except `SourceType` (the data source type cannot be changed after creation, matching the Flexmonster rule). Editing is applied via `PUT /api/indexes/{index}` (or by editing the settings JSON file, see §12.5); a successful edit persists metadata and reloads the index data.

**Delete Index.** `DELETE /api/indexes/{index}` removes metadata and evicts `data:{index}` + `engine:{index}:*`. Consumers are prompted to confirm (Phase 2 UI parity; Phase 1 exposes the endpoint).

**Reload Index.** Manual reload: `POST /api/indexes/{index}/reload`. Automatic reload: `Refresh time` = `ReloadIntervalMinutes`, measured from Data Server startup; `0` (default) means never reload. Semantics governed by `AutoReload`, `KeepDataOnRefresh`, retry policy (§12, §13).

**Enable / Disable Index.** `POST /api/indexes/{index}/disable` / `enable`. Disabled indexes are **not preloaded at startup**, their `data:{index}` cache slot is evicted, and pivot requests against them return `423 Disabled` (status reported via `GET /api/indexes/{index}` → `status: "Disabled"`). Enabled indexes reload immediately.

**Mapping (JSON/CSV only).** Disabled by default — all field types are autodetected. When enabled, each field's type can be set to **Number**, **String**, or **Date**. Disabling Mapping deletes all adjustments and reverts to autodetection. Applies on next load/edit-save.

**CSV separators.** `CsvDelimiter` (default `,`), `CsvDecimalSeparator` (default `.`), `CsvThousandsSeparator` (default `,`) mirror the Flexmonster Data Server's CSV index options.

**Database Test.** `POST /api/indexes/{index}/test-connection` executes `SELECT 1` against the configured connection and returns `200 { success: true, latencyMs }` or `502 { success: false, error }`.

**Connection-string password rule (Flexmonster-like).** `;` and `"` are treated as delimiters. Passwords containing them must be enclosed in single quotes, e.g. `Pwd='123;"45'`. The connection-string parser must implement this rule for inline test strings; persisted production indexes continue to reference `ConnectionStrings:Name` (no inline secrets).

### Index Lifecycle

```text
POST   /api/indexes                      → create (bootstrap-verified), immediately load if Enabled
PUT    /api/indexes/{index}               → edit (all except SourceType), save + reload
POST   /api/indexes/{index}/reload       → reload data (KeepDataOnRefresh semantics, retry policy)
POST   /api/indexes/{index}/enable       → enable + load
POST   /api/indexes/{index}/disable      → disable + evict data
POST   /api/indexes/{index}/test-connection → test DB connection (Database indexes)
DELETE /api/indexes/{index}              → remove metadata + evict cache
GET    /api/indexes                      → list index metadata + status
GET    /api/indexes/{index}              → index detail + status
Cache keys:  data:{index}  engine:{index}:{parameter.Hash}
```

---

# 7. Settings Hierarchy (Root Configuration Document)

```text
DataServerConfiguration
├── ConfigVersion                (string, "1.0")
├── DefaultIndexId               (string, "default")
├── GeneralSettings              (§6.1)
├── DataStorageSettings          (§6.2)
├── DatabaseQuerySettings        (§6.3)
├── HttpsSettings                (§6.4)
├── SecuritySettings             (§6.5)
├── ReportSharingSettings        (§6.6)
└── Indexes                      (List<IndexDefinition>, §6.7)
```

```csharp
public sealed class DataServerConfiguration
{
    public const string SectionName = "DataServer";
    public const string CurrentVersion = "1.0";
    public string ConfigVersion { get; set; } = CurrentVersion;
    public string DefaultIndexId { get; set; } = "default";
    public GeneralSettings General { get; set; } = new();
    public DataStorageSettings DataStorage { get; set; } = new();
    public DatabaseQuerySettings DatabaseQuery { get; set; } = new();
    public HttpsSettings Https { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public ReportSharingSettings ReportSharing { get; set; } = new();
    public List<IndexDefinition> Indexes { get; set; } = [];
}
```

## Default Value Matrix (single source of truth: model defaults + appsettings.json)

| Setting | Default | Validation | Customization Surface |
|---------|---------|------------|-----------------------|
| Port | 9500 | 1–65535 int | appsettings / PUT /api/config / env |
| MinimumLogLevel | Info | enum | appsettings / PUT /api/config |
| DataFolder | DataSource | non-empty | appsettings / API |
| RetryAttempts | 3 | 0–30 | appsettings / API |
| CacheSizeLimit | 100 | >= 0 (0=off) | appsettings / API (restart) |
| KeepDataOnRefresh | true | bool | appsettings / API |
| CommandTimeoutEnabled | false | bool | appsettings / API |
| CommandTimeLimit | 150 s | >= 0 (0=unlimited) | appsettings / API |
| HTTPS Enabled | false | bool | appsettings / API (restart) |
| Protocols | HTTP1_AND_HTTP2 | enum | appsettings / API (restart) |
| StoreLocation | CurrentUser | enum (Windows) | appsettings / API (restart) |
| AllowInvalidCertificates | false | bool (forbidden in Production) | appsettings / API |
| BasicAuthEnabled | false | bool | appsettings / API |
| AllowedOrigins | ["*"] | list of origins | appsettings / API |
| ReportShare Enabled | false | bool | appsettings / API |
| Expiration | 0 (=permanent) | >= 0 min | appsettings / API |
| MaxFolderSizeMB | 0 (=unlimited) | >= 0 | appsettings / API |
| AutoDelete | false | bool | appsettings / API |
| Index AutoReload | false | bool | appsettings / POST /api/indexes |
| ReloadInterval | 0 (no timer) | >= 0 min | appsettings / POST /api/indexes |
| DefaultIndexId | "default" | must match an index | appsettings / API |

---

# 8. User Management

### User Model

```csharp
public class User
{
    public string Username { get; set; }
    public string PasswordHash { get; set; } // PBKDF2 $iterations$salt$hash
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### Persisted JSON Shape

```json
{
  "username": "admin",
  "passwordHash": "PBKDF2$100000$SHA256$<saltB64>$<hashB64>",
  "createdAt": "2025-01-01T00:00:00Z",
  "updatedAt": "2025-01-01T00:00:00Z"
}
```

### Rules

- Username must be unique (case-insensitive).
- Username cannot be modified.
- Passwords must be hashed (PBKDF2, 100,000 iterations, SHA-256 — no new package required; BCrypt/Argon2 optional).
- Plain-text passwords must never be stored.
- Verification uses constant-time comparison of the derived key.

---

# 9. REST API Specification

## 9.1 Conventions

- Base path: `/api`
- Content type: `application/json`
- Correlation: honor/echo `X-Correlation-ID`; echo in logs.
- Error envelope (RFC 7807 via `AddProblemDetails`) with `traceId`.

```json
{
  "type": "https://dataserver.local/errors/config-validation",
  "title": "Configuration validation failed",
  "status": 422,
  "detail": "Port must be between 1 and 65535.",
  "traceId": "00-…",
  "errors": { "General.Port": ["Port must be between 1 and 65535."] }
}
```

## 9.2 Pivot Data Endpoint (client-facing, the Admin Panel target URL)

```http
POST /api/pivot/post
```

Request body (existing `FetchData` action contract **+ `index`**):

```json
{
  "index": "Sales-Data",
  "action": "fetchFieldMembers",
  "memberName": "Country",
  "...existingFields": "..."
}
```

Response: unchanged (serialized pivot values / members / raw data / export file).

Behavior:
- `index` is the client-supplied index name. Missing → `DefaultIndexId`.
- Unknown index → `404 IndexNotFound` with a problem-details body naming the valid indexes.
- Disabled index → `423 IndexDisabled`.
- All actions (`fetchFieldMembers`, `fetchRawData`, exports, pivot values) are routed to the resolved index's dataset.

After Phase 1, this endpoint is available at a simple URL such as `http://localhost:5285/api/pivot/post`. The future Admin Panel points to this URL and to the management APIs below to inspect/update server settings.

## 9.3 Endpoint Table

| Method | Route | Purpose | Success | Errors |
|--------|-------|---------|---------|--------|
| GET | `/api/config` | Full configuration | 200 | — |
| GET | `/api/config/{section}` | One section (general/dataStorage/databaseQuery/https/security/reportSharing) | 200 | 404 |
| PUT | `/api/config` | Partial (merge-patch) update | 200 `{config, requiresRestart}` | 400/422 |
| GET | `/api/security/users` | List users (no password hash) | 200 | 401 |
| POST | `/api/security/users` | Create user | 201 | 409 / 422 |
| PUT | `/api/security/users/{username}` | Update password/roles | 200 | 404 / 422 |
| DELETE | `/api/security/users/{username}` | Delete user | 204 | 404 |
| GET | `/api/indexes` | List index metadata + status | 200 | — |
| GET | `/api/indexes/{index}` | Index detail + status | 200 | 404 |
| POST | `/api/indexes` | Create + load index | 201 | 409 / 422 |
| PUT | `/api/indexes/{index}` | Edit index (all except `sourceType`) → save + reload | 200 | 404 / 422 |
| POST | `/api/indexes/{index}/reload` | Reload data | 200 | 404 / 502 / 504 |
| POST | `/api/indexes/{index}/test-connection` | Test DB connection string | 200 | 404 / 502 |
| POST | `/api/indexes/{index}/enable` | Enable index (loads data) | 200 | 404 |
| POST | `/api/indexes/{index}/disable` | Disable index (evicts data) | 200 | 404 |
| DELETE | `/api/indexes/{index}` | Delete + evict cache | 204 | 404 |
| POST | `/api/pivot/post` | Existing endpoint (+ optional `index`) | 200 | 400/404/423/401 |
| POST | `/api/reportsharing/reports` | Save report | 201 `{reportId}` | 404 / 507 |
| GET | `/api/reportsharing/reports/{reportId}` | Retrieve report | 200 | 404 / 410 |
| DELETE | `/api/reportsharing/reports/{reportId}` | Delete report | 204 | 404 |
| GET | `/api/reportsharing/reports` | List report metadata | 200 | 404 |

## 9.4 Request/Response Schemas

**PUT `/api/config`** — partial (merge-patch):

```json
{
  "General": { "Port": 9501, "MinimumLogLevel": "Warn" },
  "Security": { "AllowedOrigins": ["https://app.example.com"] }
}
```

Response:

```json
{
  "config": { "...full config...", "General": { "Port": 9501, "MinimumLogLevel": "Warn" } },
  "requiresRestart": true
}
```

**POST `/api/indexes`**:

```json
{
  "index": "sales-csv",
  "name": "Sales CSV",
  "sourceType": "Csv",
  "sourcePath": "sales.csv",
  "autoReload": true,
  "reloadIntervalMinutes": 60,
  "fields": [ { "name": "UnitsSold", "typeHint": "integer" } ]
}
```

Response `201`:

```json
{
  "index": "sales-csv",
  "status": "Ready",
  "rowCount": 8948,
  "loadedAt": "2025-01-01T10:00:00Z",
  "lastError": null
}
```

**PUT `/api/indexes/{index}`** — edit (all except `sourceType`):

```json
{
  "index": "sales-csv",
  "name": "Sales CSV (renamed)",
  "sourceType": "Csv",          // must match existing type; change → 422
  "sourcePath": "sales-2025.csv",
  "csvDelimiter": ";",
  "csvDecimalSeparator": ",",
  "csvThousandsSeparator": ".",
  "mappingEnabled": true,
  "fieldMappings": [ { "fieldName": "UnitsSold", "type": "Number" },
                     { "fieldName": "OrderDate", "type": "Date" } ],
  "reloadIntervalMinutes": 30
}
```

Response `200`:

```json
{
  "index": "sales-csv",
  "status": "Ready",
  "rowCount": 10240,
  "loadedAt": "2025-01-01T11:30:00Z",
  "lastError": null
}
```

**POST `/api/indexes/{index}/test-connection`** — Database indexes only:

Response `200`:

```json
{ "success": true, "latencyMs": 42 }
```

Response `502`:

```json
{ "success": false, "error": "Unable to connect: login failed for user 'sa'." }
```

**POST `/api/reportsharing/reports`**:

```json
{
  "name": "Q4-Review",
  "owner": "analyst@corp",
  "payload": { "rows": [], "columns": [], "values": [] }
}
```

Response `201`:

```json
{ "reportId": "3f2b…", "createdAt": "...", "expiresAt": null, "sizeBytes": 41230 }
```

---

# 10. Client-Side Integration (final Phase 1 step)

## 10.1 Flexmonster reference contract (for similarity only)

For reference, the Flexmonster client connects to its Data Server by index name (the Syncfusion equivalent is shown in §10.2):

```js
dataSource: {
  type: "api",
  url: "http://localhost:9500",   // Data Server endpoint
  index: "Sales-Data"             // index registered in appsettings.json
},
```

## 10.2 Syncfusion PivotView sample (this repository)

The sample (`Sample/pivot.js`) uses server mode with URL pointing at the data server. The index name is passed per request.

**Sample-level (preferred).** Define the index in the sample script:

```js
// sample-level client configuration
var PIVOT_INDEX = 'Sales-Data';                          // ← index name
var PIVOT_URL = 'http://localhost:5285/api/pivot/post';  // ← data server endpoint

var pivotGridObj = new ej.pivotview.PivotView({
  dataSourceSettings: {
    url: PIVOT_URL,
    mode: 'Server',
    type: 'API',                 // type: API — data server mode
    index: PIVOT_INDEX,          // ← sent with every request to the backend
    expandAll: false,
    rows: [{ name: 'ProductID', caption: 'Product ID' }],
    columns: [{ name: 'Year', caption: 'Production Year' }],
    values: [{ name: 'Sold', caption: 'Units Sold' }, { name: 'Price', caption: 'Sold Amount' }],
    allowMemberFilter: true
  },
  enableVirtualization: true,
  enableValueSorting: true,
  allowCalculatedField: true,
  showGroupingBar: true,
  showFieldList: true,
  height: 500,
  width: 1000
});
pivotGridObj.appendTo('#PivotView');
```

**Source-level alternative.** Share the same constant module-wide:

```js
// e.g. Sample/config.js — source-level client configuration
window.PivotDataServer = {
  url: 'http://localhost:5285/api/pivot/post',
  index: 'Sales-Data'
};
```

The backend reads `index` from each `POST /api/pivot/post` payload (`FetchData.Index`) and serves the matching dataset. If the property is absent, the server falls back to `DefaultIndexId` (legacy `default` index from the `PivotDataSource` migration) — preserving backward compatibility.

## 10.3 Endpoint-based Admin Panel integration (post-Phase 1)

The data server is now reachable at a stable URL, e.g. `http://localhost:5285/api/pivot/post` (HTTP) or the configured `General.Port`/HTTPS listener. In Phase 2 the Admin Panel connects to this URL plus `/api/config`, `/api/security/users`, `/api/indexes`, `/api/reportsharing/*` to manage all server settings at runtime. No additional backend endpoints are required — Phase 1 already exposes the full management surface.

---

# 11. Persistence Requirements

| Artifact | Location | Initial source | Runtime management |
|----------|----------|----------------|--------------------|
| Configuration | `DataServer/config.json` | defaults + `appsettings.json` overrides | PUT /api/config, file hot-reload (§12.5) |
| Index definitions | `DataServer/indexes.json` | **`appsettings.json` bootstrap (Step 1)** | POST/PUT/GET/RELOAD/DELETE/ENABLE/DISABLE /api/indexes |
| Users | `DataServer/users.json` | empty (admin created via API) | /api/security/users |
| Reports | `DataServer/reports/` | — | /api/reportsharing/* |

- Atomic writes: serialize → temp file in same directory → rename (`File.Replace`).
- Corrupt JSON files are quarantined (`*.corrupt.{timestamp}`) and logged; the host starts with defaults.
- Secrets (connection-string passwords, PFX password) are **never** persisted in `config.json` / `indexes.json` — stored in user-secrets/env and referenced by name.

---

# 12. Data Indexing Strategy

## 12.1 Lifecycle

```text
CreateIndex(definition)
  → persist metadata (atomic)
  → create provider by SourceType
  → LoadAsync(def, timeoutCfg)
      ├─ success → Set(dataKey, rows) → 201 { status: "Ready", rowCount }
      ├─ transient failure → retry (backoff, ≤ RetryAttempts)
      └─ persistent failure → 502 { status: "Failed", lastError }

Edit(index, patch)
  → reject SourceType change → 422
  → persist metadata (atomic) → Reload(index) semantics → 200

Reload(index)
  → if KeepDataOnRefresh: pin old entry
  → LoadAsync(...)
      ├─ success → replace data entry
      └─ failure && KeepDataOnRefresh → 502 (old data still served)

Disable(index)
  → Evict(data:{index}), Evict(engine:{index}:*) → status "Disabled"
  → pivot requests → 423; not preloaded at startup

Enable(index)
  → LoadAsync(...) → status "Ready" (or "Failed" per retry policy)

Delete(index)
  → Evict(data:{index}), Evict(engine:{index}:*)
  → remove metadata → 204
```

Status values: `Ready | Refreshing | Failed | Disabled`.

## 12.2 Cache Key Scheme

- Data: `data:{index}` (one slot per index)
- Engine: `engine:{index}:{parameter.Hash}` (existing pattern, prefixed by index)
- Sizing: every entry `SetSize(1)`; `MemoryCacheOptions.SizeLimit` bound to `DataStorage.CacheSizeLimit` at startup.

## 12.3 Registration & Backward Compatibility

```csharp
// Program.cs bootstrap
var boot = new DataServerBootstrapper(host);
await boot.EnsureIndexesAsync();      // reads appsettings.json:DataServer:Indexes
await boot.EnsureDefaultIndexAsync(); // one-time legacy PivotDataSource → "default"
```

## 12.4 Auto-Reload

- `AutoReload=true` + `ReloadIntervalMinutes>0` → `PeriodicTimer` per index.
- Refresh time is measured from Data Server startup (Flexmonster-like semantics); `0` = never reload.
- Reload failures are logged, never crash the host; `KeepDataOnRefresh` governs continuity.

## 12.5 JSON File-Driven Configuration Testing (new in v2.1)

Phase 1 is configuration-heavy; verifying each option by editing code is impractical. Instead, the running backend watches a single human-editable JSON settings file — the runtime configuration document persisted by `ConfigurationPersistenceService` — and applies changes at runtime.

### File

- Location: `PivotController/DataServer/config.json` (runtime data; gitignored; §13.4 `AtomicJsonStore`).
- Contents: the full §7 hierarchy **plus** the `Indexes` array — every Data Server option and value in one place.
- Structure mirrors `GET /api/config` output (§9.3), so the file, the API, and the future Admin Panel share one schema.

### Hot-Reload Pipeline

1. `FileSystemWatcher` on `DataServer/*.json` (rename/replace-safe) + 500 ms debounce + atomic-rename detection (editors save via temp-file rename).
2. Parse + validate the whole document (`ConfigurationValidator`). On failure: keep last-known-good config, log `Error ConfigReloadFailed {reason}`, continue serving.
3. Compute delta vs. current config; apply per-option:

| Option | Applies at runtime? |
|---|---|
| `Indexes` (add/edit/delete/enable/disable/mapping/reload time) | Yes — index reload via §12 semantics |
| `Security.BasicAuthEnabled`, `AllowedOrigins`, users | Yes |
| `ReportSharing.EndpointEnabled` | Yes |
| `General.MinimumLogLevel` | Yes |
| `DatabaseQuery.*` | Yes (next query) |
| `General.Port`, `General.DataFolder` | **No — requiresRestart** (log warning) |
| `Https.*` (cert, protocols) | **No — requiresRestart** |
| `DataStorage.CacheSizeLimit`, `MaxCacheItemSizeBytes` | **No — requiresRestart** (§26.2) |

4. Result logged: `Info ConfigReloaded {appliedSections, requiresRestartSections}`.

### Example test file (excerpt)

```json
{
  "ConfigVersion": "1.0",
  "DefaultIndexId": "Sales-Data",
  "General": { "Port": 5285, "MinimumLogLevel": "Info" },
  "DataStorage": { "RetryAttempts": 3, "CacheSizeLimit": 100, "KeepDataOnRefresh": true },
  "DatabaseQuery": { "CommandTimeoutEnabled": true, "CommandTimeLimit": 150 },
  "Https": { "Enabled": false, "Protocols": "HTTP1_AND_HTTP2" },
  "Security": { "BasicAuthEnabled": false, "AllowedOrigins": ["*"] },
  "ReportSharing": { "EndpointEnabled": false },
  "Indexes": [
    { "Index": "Sales-Data", "SourceType": "Csv", "SourcePath": "sales.csv",
      "CsvDelimiter": ",", "MappingEnabled": false, "ReloadIntervalMinutes": 0, "Enabled": true },
    { "Index": "Sales-Db", "SourceType": "Database", "DatabaseType": "PostgreSQL",
      "ConnectionStringName": "PgSales", "Query": "SELECT * FROM Sales", "Enabled": true }
  ]
}
```

Editing any value in this file is reflected in the backend (watcher latency ≤ 1 s) — no code change, no rebuild, restart only for the `requiresRestart` options above. This is the primary Phase 1 manual test loop.

---

# 13. Data Storage & Caching Mechanisms

## 13.1 Data Providers

- `JsonDataProvider` — flat-array validation, field-set consistency, type conversion (reuse of existing `LoadJsonAsync` logic).
- `CsvDataProvider` — header validation, column-count checks, per-column type inference honoring `CsvDelimiter`, `CsvDecimalSeparator`, `CsvThousandsSeparator`, and Mapping overrides (reuse of existing `LoadCsv` logic).
- `DatabaseDataProvider` — see §14.
- `DynamicDataLoader` is kept as a thin facade implementing `IDataSourceProvider` for minimal controller changes.

## 13.2 Retry Policy

```csharp
// delays = [250ms, 500ms, 1s, ...] up to RetryAttempts, + jitter ±20%
await RetryPolicy.ExecuteAsync(attempts, operation, logger, ct);
```

Transient-only: `IOException`, provider `TimeoutException`, transient `DbException`. Validation failures are not retried.

## 13.3 Keep-Data-on-Refresh

- Two-slot cache entry: on refresh, new data written to `new` slot; on success the entry atomically switches; on failure the `current` slot remains.

## 13.4 Persistence Store

- `AtomicJsonStore` for all JSON artifacts; quarantine of corrupt files; same-directory temp file writes.

---

# 14. Database Query Processing Workflow

## 14.1 Provider Abstraction

```csharp
public interface IDatabaseQueryProvider
{
    DataSourceType SourceType { get; }
    DbConnection CreateConnection(string connectionString);
    // Sqlite → SqliteConnection | SqlServer → SqlConnection | PostgreSQL → NpgsqlConnection
    // MySql/MariaDb → MySqlConnector | Oracle → Oracle.ManagedDataAccess.Core
}
```

- Phase 1 mandatory provider: **SQLite** (package already referenced).
- Other providers: optional packages, reflection-registered only when present (keeps build footprint small).

## 14.2 Query Execution Pipeline

```text
Validate Query (single SELECT, no ';')
 → CreateConnection(connectionString, ReadOnly)
 → OpenAsync (timeout budget starts; reconnection retried per policy)
 → CommandTimeout = CommandTimeLimit (when CommandTimeoutEnabled; 0 = infinite)
 → ExecuteReaderAsync(cancellationToken)
     ├─ timeout → cancel command → DatabaseTimeoutException → retry logic
     └─ rows → map to ExpandoObject (validated headers)
 → IReadOnlyList<ExpandoObject>
```

## 14.3 Connection Strings

- Stored by name in `appsettings.json` `ConnectionStrings` or user-secrets; referenced via `ConnectionStringName`.
- Never persisted in `config.json` or `indexes.json`.
- Inline connection strings supplied to the Test-connection endpoint are parsed with the Flexmonster-like password rule (§6.7): `;` and `"` are delimiters; passwords containing them must be single-quoted (e.g., `Pwd='123;"45'`).

---

# 15. HTTPS & Security Configuration Detail

## 15.1 HTTPS Bootstrapping (Program.cs)

- `HttpsSettings.Enabled=true` → resolve certificate (`CertificateService`):
  - PFX: `new X509Certificate2(PfxPath, PfxPassword)`
  - PEM: `X509Certificate2.CreateFromPemFile(PemCertPath, PemKeyPath)` + PKCS12 export (Linux-safe)
  - Store: by `StoreLocation` + `StoreName` + `Thumbprint` (Windows-only, guarded by `OperatingSystem.IsWindows()`)
- Kestrel: `Listen(IPAddress.Any, Port, lo => lo.Protocols = Map(Protocols))` + `ConfigureHttpsDefaults` (`Tls12 | Tls13`).
- `AllowInvalidCertificates=true` rejected at validation time in Production.
- `UseHttpsRedirection()` enabled when HTTPS on.

## 15.2 Basic Auth Middleware

```
UseCors → PivotBasicAuthMiddleware → UseAuthentication/UseAuthorization
```

- `[AllowAnonymous]` on `/api/security/users` when `BasicAuthEnabled`; everything else gated.
- Verification: decode Base64, split on first `:`, PBKDF2 + constant-time compare.
- Bad credentials → `401` with `WWW-Authenticate: Basic realm`.

## 15.3 CORS Origin Validation

- `AllowedOrigins=["*"]` → `AllowAnyOrigin`. Otherwise exact-match `WithOrigins(...)`.
- Restricted origins + no-Origin browser requests → rejected (CSRF defense-in-depth).

---

# 16. Report Sharing Architecture

## 16.1 Storage Layout

```text
DataServer/reports/
├── 3f2b9c1e-...json          # report payload
├── 3f2b9c1e-....meta.json    # { reportId, name, owner, createdAt, expiresAt, sizeBytes }
└── .quota                    # { currentBytes }
```

- `reportId` = GUID (no user input in paths → no traversal).
- All reads/writes confined to `StoragePath` via an `IsPathWithinRoot` guard.

## 16.2 Expiry & Quota Sweep (BackgroundService)

- Runs every 5 minutes when `AutoDelete=true`.
- Removes `expiresAt < now`; when quota set and exceeded, evicts oldest first.
- Each save checks quota → `507 Insufficient Storage` when exceeded.

## 16.3 Expired Read Semantics

- `GET` an expired report → `410 Gone`; file left in place if `AutoDelete=false`.

---

# 17. Logging, Monitoring & Validation

## 17.1 Logging

- `ClearProviders()` → `AddConsole()` + rolling JSON file (`logs/dataserver-{yyyyMMdd}.jsonl`, 30-day retention).
- Minimum level filter driven by `General.MinimumLogLevel`; framework logs pinned to Warning.
- Categories (`SourceContext`): `Config`, `Security`, `Indexing`, `DatabaseQuery`, `ReportSharing`, `Https`, `Pivot`.
- Structured fields: `{ category, correlationId, index?, durationMs, rowCount?, error? }`.
- Pivot hit-logging at `Debug` by default (avoids chatty Info logs).

## 17.2 Monitoring (Phase 1 minimal)

- `/health` liveness endpoint → `200 OK` when responsive.
- Index statuses (`Ready | Refreshing | Failed | Disabled`) surfaced via `GET /api/indexes`.

## 17.3 Validation Rules

- Per-section rule table from §7 (Default Value Matrix).
- Cross-field rules:
  - `Https.Enabled` requires exactly one certificate source configured.
  - `AllowInvalidCertificates=true` forbidden in Production.
  - `ReloadIntervalMinutes>0` requires `AutoReload=true`.
  - `EndpointEnabled=true` requires writable `StoragePath`.
  - `DefaultIndexId` must exist.
- Update semantics: merge-patch per section; unknown section/setting keys → `400 UnknownSetting` (prevents silent typos).

---

# 18. Error Handling & Exception Management

| Condition | HTTP | Log Level | Notes |
|-----------|------|-----------|-------|
| Configuration validation failure | 422 | Warn | field-level errors; previous value retained; no persistence |
| Unknown config section / setting key | 400/404 | Info | prevents silent typos |
| Index not found | 404 | Info | body lists valid indexes |
| Duplicate index | 409 | Info | — |
| Index disabled | 423 | Info | `IndexDisabled` |
| Index load transient failure | 502 | Warn | retried per RetryAttempts; `lastError` on status |
| Database timeout | 504 | Error | command canceled; status `Failed` |
| Unsupported provider (package missing) | 422 | Error | names the missing `PackageReference` |
| Invalid data source shape (JSON/CSV) | 422 | Warn | no retry |
| Path traversal | 403 | Error | blocked + logged |
| Bad credentials | 401 | Info | `WWW-Authenticate: Basic realm` |
| Origin not allowed | 403 | Info | log origin |
| Report not found | 404 | Info | — |
| Report expired | 410 | Info | — |
| Storage quota exceeded | 507 | Warn | currentBytes vs quota |
| Unhandled exception | 500 | Fatal | problem details; no stack traces outside Development |

Global filter: `DataServerExceptionFilter : IExceptionFilter` maps exceptions → problem details with category + `correlationId`.

---

# 19. Sequence Diagrams

## 19.1 Startup: Index bootstrap from appsettings.json (Step 1)

```text
Program.cs → ConfigurationPersistenceService.EnsureFiles()
        → IndexBootstrap.EnsureIndexesAsync()
              read appsettings.json:DataServer:Indexes
              (one-time) migrate legacy PivotDataSource → index "default"
              persist to DataServer/indexes.json
        → IndexManager.RegisterAll()
        → Kestrel configured from General.Port (e.g. 5285)
        → Data server ready: {url}/api/pivot/post
```

## 19.2 Pivot request with client-supplied index

```text
PivotView (Sample/pivot.js, index: "Sales-Data")
 → POST {url}/api/pivot/post { index: "Sales-Data", action, ... }
 → PivotController extracts FetchData.Index
 → IndexManager.Resolve("Sales-Data")
      ├─ unknown → 404 { title: "IndexNotFound", validIndexes: [...] }
      ├─ disabled → 423 IndexDisabled
      └─ known → GetDataAsync("Sales-Data")  [cache data:Sales-Data]
                   (miss → provider load with retry policy)
 → PivotEngine.GetEngine({ index prefix in cache key })
 → PerformAction(action)
 → serialized pivot values → PivotView
```

## 19.3 Index Create / Edit / Reload / Enable / Disable

```text
CreateIndex(definition)
  → persist metadata (atomic)
  → create provider by SourceType
  → LoadAsync(def, timeoutCfg)
      ├─ success → Set(dataKey, rows) → 201 { status: "Ready", rowCount }
      ├─ transient failure → retry (backoff, ≤ RetryAttempts)
      └─ persistent failure → 502 { status: "Failed", lastError }

EditIndex(index, patch)
  → SourceType changed? → 422
  → persist metadata → reload → 200 { status, rowCount }

Reload(index)
  → if KeepDataOnRefresh: pin old entry
  → LoadAsync(...)
      ├─ success → replace data entry
      └─ failure && KeepDataOnRefresh → 502 (old data still served)

Disable(index)  → Evict(data, engine) → 200 { status: "Disabled" }
Enable(index)   → LoadAsync(...)       → 200 { status: "Ready" }
```

---

# 20. File-by-File Change List

## Modified

| File | Change |
|------|--------|
| `PivotController/Program.cs` | Bootstrap index registration, config persistence, logging filter, cache sizing, CORS from config, HTTPS/Kestrel, Basic Auth, DI registration of all managers, health endpoint |
| `PivotController/Controllers/PivotController.cs` | Route through `IIndexManager` by `FetchData.Index` (fallback `DefaultIndexId`); prefix cache keys with index |
| `PivotController/DataSource/DynamicDataLoader.cs` | Refactor into providers; keep as facade `IDataSourceProvider`; add per-index configuration and timeout support |
| `PivotController/DataSource/DataSource.cs` | (Optional) mark `[Obsolete]` — unused legacy sample classes |
| `PivotController/appsettings.json` | Add `DataServer` section with `Indexes` array; keep legacy `PivotDataSource` for one-time migration |
| `PivotController/PivotController.csproj` | Document optional DB provider packages (SqlClient, Npgsql, MySqlConnector, Oracle) |
| `Sample/pivot.js` | **Final Phase 1 step** — pass `index` on every request (sample-level or source-level constant) |

## Created

```text
PivotController/Configuration/
├── Models/DataServerConfiguration.cs        (all §6/§7 models)
├── Validation/ConfigurationValidator.cs
├── Services/ConfigurationService.cs
├── Services/ConfigurationPersistenceService.cs
├── Services/ConfigurationFileWatcher.cs     (§12.5 hot-reload watcher)
└── Options/DataServerOptions.cs
PivotController/Core/
├── Persistence/AtomicJsonStore.cs
├── Persistence/JsonFileRepository.cs        (users, indexes)
├── Retry/RetryPolicy.cs
├── Security/PBKDF2PasswordHasher.cs
├── Security/ConstantTimeComparer.cs
├── Security/OriginValidator.cs
├── Security/PivotBasicAuthMiddleware.cs
├── Http/DataServerExceptionFilter.cs
└── Logging/RollingFileLoggerProvider.cs
PivotController/Indexing/
├── IIndexManager.cs / IndexManager.cs
├── IndexStatus.cs
├── IndexReloadWorker.cs (BackgroundService)
└── PivotEngineCache.cs
PivotController/Data/
├── Providers/IDataSourceProvider.cs
├── Providers/JsonDataProvider.cs
├── Providers/CsvDataProvider.cs
├── Providers/DatabaseDataProvider.cs
├── Providers/IDatabaseQueryProvider.cs
└── Providers/SqliteQueryProvider.cs (+ optional provider stubs)
PivotController/Security/
├── Controllers/SecurityController.cs
└── Services/UserService.cs
PivotController/Reports/
├── Services/ReportSharingService.cs
├── Services/ReportSweepWorker.cs
└── Controllers/ReportsController.cs
PivotController/Configuration/Controllers/
├── ConfigController.cs
└── IndexController.cs
PivotController/DataServer/                    (runtime data; gitignored)
├── config.json  ├── users.json  ├── indexes.json  └── reports/
Tests/PivotController.Tests/                   (xUnit)
```

---

# 21. Phase 1 Execution Order and Milestones

| # | Milestone | Scope |
|---|-----------|-------|
| **1** | **Index Configuration (Step 1)** | `IndexDefinition` model; `appsettings.json` index registration; bootstrap/migration of legacy `PivotDataSource`; `IndexManager` (create/edit/reload/delete/list/enable/disable); per-index cache keys; index-aware pivot routing with `FetchData.Index`; `DefaultIndexId` fallback; edit/enable/disable/test-connection endpoints; CSV separators; Mapping; settings-file hot-reload of indexes |
| 2 | General & Data Storage Settings | Port, MinimumLogLevel, DataFolder; RetryAttempts, CacheSizeLimit, KeepDataOnRefresh, MaxCacheItemSizeBytes |
| 3 | Database Query Settings | CommandTimeoutEnabled, CommandTimeLimit; `IDatabaseQueryProvider` abstraction (SQLite first); timeout enforcement |
| 4 | HTTPS Settings | PFX/PEM/store certificate resolution; Kestrel binding; protocol selection |
| 5 | Security Settings | User model + PBKDF2; Basic Auth middleware; CORS OriginValidator; user CRUD APIs |
| 6 | Report Sharing Settings | ReportSharingService; expiry/quota sweep worker; report APIs |
| 7 | Persistence + Logging + Config APIs + Hot-Reload | AtomicJsonStore; config.json/users.json persistence; rolling file logger; `ConfigController`; health endpoint; error-handling filter; `ConfigurationFileWatcher` hot-reload (§12.5) |
| **8** | **Client-side sample configuration (final)** | Update `Sample/pivot.js` to send `index` (sample-level or source-level constant); verify `http://localhost:5285/api/pivot/post` serves the named index; document URL for Admin Panel integration |

---

# 22. Acceptance Criteria

## Index Configuration (Step 1)

- [ ] An index declared in `appsettings.json` (e.g., `Sales-Data`) is available after startup without any manual file creation.
- [ ] `GET /api/indexes` lists it; `GET /api/indexes/Sales-Data` returns metadata + status `Ready`.
- [ ] A pivot request containing `index: "Sales-Data"` returns data from that index's dataset.
- [ ] A request with an unknown index returns `404` naming the valid indexes.
- [ ] A request without `index` still works (legacy `default` index) — `Sample/pivot.html` behavior unchanged.
- [ ] Multiple indexes across JSON files, CSV files, and databases coexist and serve independently.
- [ ] Editing an index (name, path, query, separators, mapping, refresh time) via API or settings file applies without restart; changing `sourceType` is rejected with 422.
- [ ] Deleting an index evicts its data; pivot requests to it return 404.
- [ ] Manual reload returns 200 and refreshes data; `Refresh time` auto-reloads from server start (0 = never).
- [ ] Disabled indexes are not preloaded and return 423; enabling reloads and serves again.
- [ ] Mapping toggle works: enabled → per-field Number/String/Date honored; disabled → adjustments deleted, autodetect restored.
- [ ] CSV delimiter / decimal / thousands separators are honored on load.
- [ ] Test-connection endpoint returns success/failure with latency.
- [ ] Editing any runtime-applicable option in `DataServer/config.json` is reflected in the backend within 1 s (watcher); invalid edits keep last-known-good config and log an error; restart-requiring options log `requiresRestart`.

## General Settings

- [ ] Port is configurable in `appsettings.json` and honored; `PUT /api/config` reports `requiresRestart: true`.
- [ ] Log level filtering works (Debug records absent at `MinimumLogLevel=Info`; Warn records present).

## Data Storage

- [ ] Retry logic works (3 retries on a transient failure).
- [ ] Cache size is enforced (`CacheSizeLimit`).
- [ ] KeepDataOnRefresh is supported (old data served on reload failure; empty on false).

## Database Query Settings

- [ ] Timeout is enforced (`CommandTimeLimit`).
- [ ] Unlimited mode is supported (`0`).
- [ ] Non-SELECT / multi-statement queries rejected (422).

## HTTPS

- [ ] PFX certificates supported.
- [ ] PEM certificates supported.
- [ ] Store (Subject) certificates supported (Windows).
- [ ] TLS 1.2+ enforced; HTTP/2 negotiated for `HTTP1_AND_HTTP2`.

## Security

- [ ] Basic Authentication supported (401 without credentials; 200 with valid).
- [ ] User CRUD supported (create/update/delete/list).
- [ ] Origin validation supported (403 for disallowed origin).
- [ ] Passwords stored hashed only.

## Report Sharing

- [ ] Reports can be stored and retrieved.
- [ ] Expiration rules work (410 after expiry).
- [ ] Folder limits are enforced (507).
- [ ] Auto-delete sweep works.

## Client-Side (final step)

- [ ] `Sample/pivot.js` passes the index name (sample-level or source-level constant) on every request.
- [ ] `http://localhost:5285/api/pivot/post` is the single, documented endpoint of the Phase 1 data server.

---

# 23. Testing Strategy

- **Unit:** model defaults; validator rules; `RetryPolicy`; `PBKDF2PasswordHasher`; `OriginValidator`; `AtomicJsonStore` (corrupt-file quarantine); quota math.
- **Integration (WebApplicationFactory):** config CRUD (422/404/merge-patch); index lifecycle against fixture JSON/CSV/SQLite (create/edit/reload/enable/disable); Basic Auth (401/200); CORS (403/200); report create/read/expire/delete; timeout stub provider (504).
- **Regression:** existing `PivotController` actions (fetchFieldMembers / fetchRawData / exports) against the migrated `default` index.
- **E2E (manual):** run `Sample/pivot.html` against the server with `index: "Sales-Data"` — pivot table renders from the named index.
- **Manual settings-file testing loop (§12.5):** for each Phase 1 option, edit its value in `DataServer/config.json` and verify the behavior change in the running backend (see §22 acceptance criteria).

---

# 24. Phase 1 Deliverables

## Included

- Index Configuration (appsettings.json registration + Index Manager + APIs, incl. edit/enable/disable/test-connection)
- Configuration Models + Configuration APIs
- JSON File-Driven Configuration Testing (settings-file hot-reload)
- User Management APIs + Basic Auth
- Index Management APIs
- HTTPS, Security, Report Sharing configurations
- Persistence layer (config.json, users.json, indexes.json)
- Logging infrastructure
- **Client-side pivot table sample configuration (index passing)** — final step

## Excluded

- Admin Panel UI
- Monitoring UI
- Diagnostics UI
- Runtime Visual Configuration Screen

---

# 25. Future Phase Considerations (Phase 2)

- Admin Panel UI connecting to the Phase 1 endpoint URL (e.g., `http://localhost:5285/api/pivot/post`) to read/update configuration, indexes, users, HTTPS, and report sharing.
- Monitoring / Diagnostics dashboards.
- Runtime configuration editor.
- Performance metrics viewer.
- Health monitoring.

---

# 26. Risks & Considerations

1. **Port/HTTPS changes require Kestrel restart** — cannot hot-apply; mitigated by `requiresRestart` in API responses.
2. **Memory cache sizing fixed at startup** — `IMemoryCache.Options.SizeLimit` immutable after first use; `CacheSizeLimit` changes require restart (flag `requiresRestart`).
3. **DB provider package footprint** — Phase 1 delivers SQLite fully; other providers documented behind optional packages (reflection-registered).
4. **Windows certificate store** — not available on Linux; guard with `OperatingSystem.IsWindows()` and validate at configuration time.
5. **Existing client compatibility** — keep `/api/pivot/post` payload/response identical; only additive `index` field.
6. **Secrets hygiene** — connection strings and `PfxPassword` must never land in `config.json`/`indexes.json` (gitleaks CI enforces).
7. **Concurrent index reload vs pivot requests** — protect per-index data swap with a lock/`SemaphoreSlim`; `KeepDataOnRefresh` prevents blank windows.
8. **Atomic write on Windows** — temp file must be created in the same directory as the target (`File.Replace` fails cross-volume).
9. **Log volume** — keep pivot hit-logging at `Debug`.
10. **Legacy `DataSource.cs`** (`WebClient` sample classes) — mark `[Obsolete]` rather than delete.
11. **Settings-file watcher reliability** — editors write via temp-file rename; the watcher must debounce and validate before applying; invalid JSON keeps last-known-good (§12.5).
12. **Hot-reload during in-flight pivot requests** — index reload on file change follows the same `SemaphoreSlim`/`KeepDataOnRefresh` protection as item 7.

---

# 27. Open Questions

1. Should `PivotDataSource` migration be automatic (recommended) or manual?
2. For DB providers beyond SQLite, are additional NuGet packages in the csproj acceptable, or must they be runtime-swappable plugins?
3. Should the `General.DataFolder` default remain `DataSource`, or move to `DataServer/data`?
4. Confirm the log file location convention and retention period (proposed `logs/`, 30 days).
5. Confirm the primary sample port — `5285` (current `launchSettings.json` http profile) vs. `9500` (Flexmonster default `General.Port`, for reference).

---

# Conclusion

Phase 1 delivers a backend-first data server where **index configuration is implemented first**, indexes are declared in `appsettings.json` with full **Indexes-tab parity** (similar to the Flexmonster Data Server) — multiple JSON/CSV/DB sources, editable indexes, manual and refresh-time reloads, enable/disable, per-field Mapping, CSV separators, and connection testing — **the client passes the index name on every pivot request**, and the **final step wires the client-side pivot table sample** to the data server's simple endpoint URL. Every Data Server option is additionally verifiable through the **JSON settings file hot-reload loop (§12.5)**, so each Phase 1 setting can be tested by editing a value in `DataServer/config.json` without code changes. All seven Data Server settings sections — General, Data Storage, Database Query, HTTPS, Security, Report Sharing, and Index Configuration — are configurable server-side with documented defaults, validation, persistence, and management APIs. After Phase 1, the data server exposes `{url}/api/pivot/post` plus a complete management API surface, so the Phase 2 Admin Panel can update all backend settings without architectural changes.