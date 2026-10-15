# Custom Data Server — Phase 1 Technical Specification

## Document Information

| Field | Value |
|---|---|
| Version | 2.0 (replaces draft v1.0) |
| Phase | Phase 1 — Backend Infrastructure & Configuration |
| Repository | `server-side-pivot-engine-for-pivot-table` (Gitea) |
| Scope | Server-side controller configuration, index management, persistence, security, HTTPS, report sharing, logging, client-side sample configuration |
| Out of Scope | Admin Panel UI, Monitoring UI, Diagnostics UI, runtime visual configuration screens |
| Author | Bold Create Agent |
| Status | Ready for development |

---

## Table of Contents

1. [Purpose](#1-purpose)
2. [Background & Current-State Analysis](#2-background--current-state-analysis)
3. [Objectives](#3-objectives)
4. [Functional Requirements](#4-functional-requirements)
5. [Non-Functional Requirements](#5-non-functional-requirements)
6. [High-Level Architecture](#6-high-level-architecture)
7. [Component Design](#7-component-design)
8. [Configuration Models & Settings Hierarchy](#8-configuration-models--settings-hierarchy)
9. [Required Modifications to the Server-Side Controller](#9-required-modifications-to-the-server-side-controller)
10. [API Contracts](#10-api-contracts)
11. [Data Indexing Strategy](#11-data-indexing-strategy)
12. [Data Storage & Caching Mechanisms](#12-data-storage--caching-mechanisms)
13. [Database Query Processing Workflow](#13-database-query-processing-workflow)
14. [HTTPS Configuration](#14-https-configuration)
15. [Security Configuration](#15-security-configuration)
16. [Report Sharing Architecture](#16-report-sharing-architecture)
17. [Logging, Monitoring & Validation](#17-logging-monitoring--validation)
18. [Error Handling & Exception Management](#18-error-handling--exception-management)
19. [Client-Side Pivot Table Sample Configuration](#19-client-side-pivot-table-sample-configuration)
20. [Sequence Diagrams & Workflows](#20-sequence-diagrams--workflows)
21. [Acceptance Criteria](#21-acceptance-criteria)
22. [Implementation Milestones](#22-implementation-milestones)
23. [Future Phase Considerations](#23-future-phase-considerations)

---

# 1. Purpose

This document defines the complete technical specification for implementing a **Custom Data Server** feature in the backend controller of the Syncfusion Pivot Table — conceptually equivalent to the Flexmonster Data Server.

Phase 1 delivers **all backend functionality** required by a future Admin Panel, **without implementing the Admin Panel UI itself**. The server-side controller exposes configuration properties corresponding to every setting available in the Flexmonster Admin Panel, initialized with standard/default values, and persists them in `appsettings.json` (index configuration is stored there, mirroring the Flexmonster approach).

After Phase 1, the data server exposes a single simple endpoint URL (e.g. `http://localhost:5285/api/pivot/post`) that the client-side pivot table consumes, passing an **index name** that identifies which data source to serve. The user can then, in a later phase, use the Admin Panel against the same endpoint to update backend data server settings.

# 2. Background & Current-State Analysis

## 2.1 Existing implementation (Open Spec Explore findings)

The repository currently contains an ASP.NET Core minimal-API style service:

```
server-side-pivot-engine-for-pivot-table/
├── PivotController/
│   ├── Program.cs                       # Host bootstrap, CORS (allow-all), memory cache (100 entries), DI
│   ├── appsettings.json                 # Single "PivotDataSource" section (Type/JsonPath/CsvPath/DatabasePath/Query)
│   ├── Controllers/PivotController.cs    # POST /api/pivot/post — action dispatcher (pivot values, members, raw data, exports)
│   └── DataSource/
│       ├── DynamicDataLoader.cs         # Loads JSON/CSV/SQLite based on config; SourceIdentity for cache keys
│       ├── DataSource.cs                # Legacy strongly-typed POCOs (unused by the current pipeline)
│       └── sales.csv, sales-analysis.json, sales.db, sales.sql
└── Sample/
    ├── pivot.html / pivot.js / require.js  # Static JS (ES5 ej.pivotview.PivotView) sample in server mode
```

## 2.2 Capabilities and gaps

| Area | Current state | Gap vs. Flexmonster Data Server |
|---|---|---|
| Data source binding | One hard-coded `PivotDataSource` section; single active source | Multiple named **indexes**; index name selected per-request from the client |
| Pivot operations | Pivot values, members, raw data, Excel/CSV export via `PivotEngine<ExpandoObject>` | Retained — this is the aggregation core; index routing must be added |
| Caching | `IMemoryCache`, 60-min absolute expiry, `dataSource:{identity}` + `engine:{identity}:{hash}` keys | Cache size limit, keep-data-on-refresh, retry, and eviction policy must become configurable |
| General settings | Port via `applicationUrl`/launchSettings only | Explicit `DataServer:General` settings with validation |
| Security | Allow-any CORS; no authentication | Basic Auth, user store, allowed-origins validation |
| HTTPS | `UseHttpsRedirection()` hard-coded | Configurable enable/disable, certificate selection (PFX/PEM/store), protocols |
| Report sharing | None | Config + storage + expiration + folder limits |
| Logging | Default console logger | Level filtering (INFO/WARN/ERROR/FATAL), categories, file target |
| DB query control | None | Command timeout enable/limit |
| Persistence of settings | None (config is read-only at runtime) | Config Manager service with validation + atomic persistence |
| Admin-facing APIs | None | Config/User/Index/Report management APIs (Phase 2 UI consumers) |

## 2.3 Reuse decisions

- **Keep** `PivotController.Post` as the single pivot endpoint — it is the `http://localhost:5285/api/pivot/post` contract the client already targets.
- **Keep** `DynamicDataLoader` internals (JSON/CSV/SQLite parsing) but refactor it to load by **index definition** rather than from a fixed config section.
- **Keep** `PivotEngine<ExpandoObject>` and export pipeline unchanged.
- **Replace** the `PivotDataSource` config section with the full `DataServer` settings hierarchy (backward-compatible mapping during migration milestone).

# 3. Objectives

The server-side controller shall:

1. Manage server configuration (load, validate, apply, persist, serve via API).
2. Manage **indexes**: create, reload, delete, persist metadata; select per-request by index name.
3. Support JSON, CSV, and database (SQL Server, PostgreSQL, MySQL, Oracle, MariaDB, SQLite) sources.
4. Support HTTPS configuration (PFX, PEM, certificate store, HTTP1/HTTP2).
5. Support Basic Authentication and CORS origin validation.
6. Support Report Sharing (save, retrieve, expire, folder limits).
7. Support structured logging with configurable minimum level.
8. Persist all configuration and runtime state (indexes, users, reports).
9. Expose management APIs ready for a future Admin Panel.
10. Accept the **index name** from the client-side pivot table and route requests to the corresponding indexed data.

# 4. Functional Requirements

Requirements are numbered `FR-x`. Priority: **M** (must, Phase 1), **S** (should), **C** (could).

## 4.1 General Settings

| ID | Requirement | Priority |
|---|---|---|
| FR-1.1 | The server shall expose a configurable **Port Number** (default `5285` for the sample profile; any valid port 1–65535) and bind Kestrel to it. | M |
| FR-1.2 | The server shall expose a configurable **Minimum Log Level** (default `INFO`) that filters all log output. | M |
| FR-1.3 | Changing General Settings via API shall be validated, persisted, and applied (port/HTTPS changes require documented restart; log level applies at runtime). | M |
| FR-1.4 | A `GET` status/health endpoint shall report server version, uptime, configured port, HTTPS state, and active index count. | S |

## 4.2 Data Storage Settings

| ID | Requirement | Priority |
|---|---|---|
| FR-2.1 | Configurable **RetryAttempts** (default `3`, range 0–30) applied to all data loading and database operations. | M |
| FR-2.2 | Configurable **CacheSizeLimit** (default `100` entries, `0` = unlimited) enforced against the data cache. | M |
| FR-2.3 | Configurable **KeepDataOnRefresh** (default `true`): when `true`, a reload keeps serving the previous dataset until the new one is ready; when `false`, data is evicted immediately. | M |
| FR-2.4 | Cache entries expire after a configurable **CacheExpirationMinutes** (default `60`). | S |

## 4.3 Database Query Settings

| ID | Requirement | Priority |
|---|---|---|
| FR-3.1 | Configurable **CommandTimeoutEnabled** (default `false`) and **CommandTimeLimit** seconds (default `150`, `0` = unlimited, min `0`). | M |
| FR-3.2 | The timeout applies to initial index creation, index reload, query execution, and database reconnection. | M |
| FR-3.3 | When timeout is enabled and exceeded, the query fails with a typed error (see §18) and retry policy applies. | M |

## 4.4 HTTPS Settings

| ID | Requirement | Priority |
|---|---|---|
| FR-4.1 | HTTPS can be enabled/disabled (default **disabled**). | M |
| FR-4.2 | PFX certificate with password supported. | M |
| FR-4.3 | PEM certificate + key files supported. | S |
| FR-4.4 | Certificate store lookup by subject supported (Store Location default `CurrentUser`, Store Name default `My`). | S |
| FR-4.5 | HTTP protocols selectable: `HTTP1`, `HTTP2`, `HTTP1_AND_HTTP2` (default `HTTP1_AND_HTTP2`). | M |
| FR-4.6 | `AllowInvalid` (self-signed) certificates flag (default `false`) for development. | S |
| FR-4.7 | When HTTPS is disabled, the server shall not force HTTPS redirection (current `UseHttpsRedirection()` becomes conditional). | M |

## 4.5 Security Settings

| ID | Requirement | Priority |
|---|---|---|
| FR-5.1 | Basic Authentication can be enabled/disabled (default **disabled**). | M |
| FR-5.2 | When enabled, all `/api/**` endpoints (including `/api/pivot/post`) require `Authorization: Basic …` with valid credentials. | M |
| FR-5.3 | Allowed origins list (default `*`). When not `*`, only listed origins receive valid CORS responses; others are rejected. | M |
| FR-5.4 | User management: create, update password, delete, list users via API. | M |
| FR-5.5 | Usernames unique, immutable after creation. Passwords stored only as salted hashes (PBKDF2 default; BCrypt/Argon2 acceptable). | M |
| FR-5.6 | Credential verification uses constant-time comparison. | M |
| FR-5.7 | Pivot requests authenticated **per index** when Basic Auth is enabled (the same credentials gate all indexes in Phase 1; per-index ACL is a future phase). | C |

## 4.6 Report Sharing Settings

| ID | Requirement | Priority |
|---|---|---|
| FR-6.1 | Report sharing endpoint can be enabled/disabled (default **disabled**). | M |
| FR-6.2 | Reports are saved to and retrieved from a configurable **StoragePath** folder (default `DataServer/Reports`). | M |
| FR-6.3 | **ReportExpirationTimeMinutes** (default `0` = permanent). Expired reports are not retrievable. | M |
| FR-6.4 | **MaxFolderSizeMB** (default `0` = unlimited). When exceeded, new report saves are rejected. | M |
| FR-6.5 | **AutoDelete** (default `false`): background job deletes expired report files. | S |
| FR-6.6 | Saved report content is validated as a JSON pivot report before storage. | M |

## 4.7 Index Configuration

| ID | Requirement | Priority |
|---|---|---|
| FR-7.1 | Multiple named indexes may be defined in `appsettings.json` (Flexmonster parity: indexes live in the app settings file). | M |
| FR-7.2 | Each index supports source types: JSON, CSV, SQL Server, PostgreSQL, MySQL, Oracle, MariaDB, SQLite. | M (JSON/CSV/SQLite), S (others) |
| FR-7.3 | The client passes the index name in every pivot request; the server routes to that index's data. Unknown index ⇒ typed error. | M |
| FR-7.4 | Indexes can be created, listed, reloaded, and deleted via management API. | M |
| FR-7.5 | Optional per-index **auto-reload** interval (minutes, `0` = off). | S |
| FR-7.6 | Connection strings / file paths are resolved relative to the content root; connection strings may reference environment variables (`%VAR%` or `${VAR}` form). | S |

## 4.8 Client Integration

| ID | Requirement | Priority |
|---|---|---|
| FR-8.1 | The pivot table sample shall configure the data server URL and **index name** at source level (see §19). | M |
| FR-8.2 | The server shall accept the index name in the request body of `/api/pivot/post` (custom data member, see §10.1) and in the `X-Pivot-Index` header as fallback. | M |

# 5. Non-Functional Requirements

| ID | Category | Requirement |
|---|---|---|
| NFR-1 | Performance | Pivot values for the sample dataset (~2,500 rows) served < 300 ms p95 from warm cache; < 2 s cold index. |
| NFR-2 | Performance | Index creation for 100k-row CSV < 10 s (excluding DB sources). |
| NFR-3 | Scalability | Concurrent requests for different indexes are processed in parallel; index reload is non-blocking for readers (swap-on-ready). |
| NFR-4 | Reliability | Retry with exponential backoff (base 1 s, factor 2, jitter) on transient data-source failures, up to RetryAttempts. |
| NFR-5 | Security | No plain-text secrets at rest in `appsettings.json` without an explicit documented warning; connection strings may be supplied via environment variables. OWASP: constant-time auth compare, no secrets in logs, path-traversal protection for all file operations. |
| NFR-6 | Maintainability | All settings follow one options-pattern model hierarchy (§8) with data annotations + programmatic validation. |
| NFR-7 | Observability | Every request logs: timestamp, level, category, endpoint, index, duration, status. |
| NFR-8 | Portability | Runs on .NET 8+ (keep the project's current TFM); Linux & Windows; no Windows-only APIs in the core path (certificate-store lookup is optional/skippable on Linux). |
| NFR-9 | Compatibility | The existing `/api/pivot/post` payload contract for `FetchData` is preserved (additive `index` field only). |
| NFR-10 | Configuration safety | Configuration persistence is atomic (write temp file + rename) and validated before application. |

# 6. High-Level Architecture

```text
+-----------------------------------------------------------+
| Client (Syncfusion Pivot Table — server mode)              |
|   dataSourceSettings: url = http://localhost:5285/...      |
|                       index = "Sales-Data"                 |
+----------------------------+------------------------------+
                             | POST /api/pivot/post  { ..., index: "Sales-Data" }
                             v
+-----------------------------------------------------------+
|                     Data Server (ASP.NET Core)            |
|                                                           |
|  Middleware pipeline:                                     |
|   RequestLogging -> CORS -> BasicAuth -> ExceptionHandler  |
|                                                           |
|  +-----------------------+   +------------------------+   |
|  | PivotController       |   | Admin/Config APIs       |   |
|  | (existing, enhanced)  |   | (Phase-2 UI consumers) |   |
|  +----------+------------+   +-----------+------------+   |
|             |                            |                 |
|             v                            v                 |
|  +-----------------------------------------------------+  |
|  | IndexManager   (per-index routing, reload, cache)    |  |
|  +------------------------+----------------------------+  |
|             |                                            |
|             v                                            |
|  +---------------------+    +------------------------+   |
|  | DataLoaders         |    | PivotEngine<T> (kept)  |   |
|  | JSON/CSV/Database   |    | + Export engines       |   |
|  +---------------------+    +------------------------+   |
|                                                           |
|  +---------------------+  +-------------+  +------------+  |
|  | ConfigurationManager |  | SecurityMgr |  | HttpsMgr   |  |
|  +---------------------+  +-------------+  +------------+  |
|  +---------------------+  +-------------+                  |
|  | ReportShareManager  |  | LoggingMgr  |                  |
|  +---------------------+  +-------------+                  |
+-----------------------------------------------------------+
        |                |                    |
        v                v                    v
   appsettings.json   DataServer/        users.json /
   (indexes + all     (cache, reports)   environment
    settings)
```

# 7. Component Design

All components live under `PivotController/DataServer/` (new) unless noted. Each is a singleton service registered in DI, following the existing code style (file-scoped namespaces, sealed classes, `ConfigureAwait(false)`).

## 7.1 ConfigurationManager (`Configuration/ConfigurationManager.cs`)

- Loads `DataServer` options tree from `IConfiguration` (appsettings.json + environment overrides) into strongly-typed models (§8).
- Validates via `IOptionsValidate` pattern; on invalid config, startup fails with a listed set of errors.
- `Get<TSection>()`, `Update<TSection>(TSection)` — update re-validates, then atomically persists to `appsettings.json` (preserving unrelated sections), then applies in-memory.
- Applies at runtime: log level, CORS origins, report sharing, cache limits, DB timeout.
- Applies at restart (documented): port, HTTPS certificate, protocols.
- `IOptionsMonitor`-compatible so future hot-reload works.

## 7.2 IndexManager (`Indexing/IndexManager.cs`)

- Holds the registry of `IndexDefinition`s loaded from `DataServer:Indexes` in appsettings.json.
- `GetOrLoad(indexName)` → returns the in-memory dataset (`IReadOnlyList<ExpandoObject>`) with caching (§12).
- `CreateAsync / DeleteAsync / ReloadAsync / ListAsync` operations with validation (e.g., cannot delete an index while requests are in flight — use reader counting or accept last-write semantics; Phase 1 accepts eviction-on-next-request).
- Coordinates auto-reload (background `PeriodicTimer` per index where `AutoReloadMinutes > 0`).
- Emits structured log events: `IndexCreated`, `IndexLoaded`, `IndexReloaded`, `IndexDeleted`, `IndexLoadFailed`.

## 7.3 DataLoaders (`Indexing/Loaders/`)

- `IIndexLoader` interface: `Task<IReadOnlyList<ExpandoObject>> LoadAsync(IndexDefinition, CancellationToken)`.
- `JsonIndexLoader` — from `DynamicDataLoader.LoadJsonAsync` (flat-array validation kept).
- `CsvIndexLoader` — from `DynamicDataLoader.LoadCsv` (configurable delimiter per index).
- `DatabaseIndexLoader` — generic ADO.NET loader (SQLite in Phase 1; provider registration per `Provider` field); applies command timeout (§13).
- Loader selection by `SourceType` via a simple factory.

## 7.4 SecurityManager (`Security/SecurityManager.cs`, `BasicAuthMiddleware.cs`)

- User store (`users.json`, schema §15.3) with PBKDF2 (Rfc2898, 100k iterations, per-user salt).
- `BasicAuthHandler` (ASP.NET Core Authentication) wired when `Security.BasicAuthEnabled = true`.
- Dynamic CORS policy provider evaluating `Security.AllowedOrigins` (with `*` semantics).

## 7.5 HttpsManager (`Configuration/HttpsConfigurer.cs`)

- Reads `HttpsSettings` and configures `KestrelServerOptions` (certificate by type: `Pfx`, `Pem`, `Store`) and `SslProtocols`/`HttpProtocols`.
- `UseHttpsRedirection` becomes conditional.

## 7.6 ReportShareManager (`Reports/ReportShareManager.cs`)

- Save/get/list/delete report JSON files under `ReportSharing.StoragePath`.
- Enforces expiration and folder-size rules; optional auto-delete background sweep.
- Sanitized file names (GUID-based identifiers) — client-supplied names never used as raw paths (path traversal prevention).

## 7.7 LoggingManager (`Logging/`)

- Custom `ILoggerProvider` (file target under `logs/dataserver-.log` with daily rolling) + console provider (existing).
- Minimum-level filter mapping: `INFO→Information`, `WARN→Warning`, `ERROR→Error`, `FATAL→Critical`.
- Categories: `DataServer.Configuration`, `DataServer.Authentication`, `DataServer.Indexing`, `DataServer.Database`, `DataServer.Reports`, `DataServer.Https`, `DataServer.Request`.
- Secrets (passwords, connection strings) are redacted by a logging-enrichment rule.

## 7.8 Modified PivotController (`Controllers/PivotController.cs`)

Changes (detailed in §9): inject `IndexManager`; resolve index from request; replace `dataLoader.LoadAsync()` with `IndexManager.GetOrLoad(indexName)`; per-index cache keys; pass `HttpContext.RequestAborted`.

# 8. Configuration Models & Settings Hierarchy

## 8.1 appsettings.json structure (target state)

```json
{
  "Logging": { "LogLevel": { "Default": "Information" } },
  "AllowedHosts": "*",
  "DataServer": {
    "General": {
      "Port": 5285,
      "MinimumLogLevel": "INFO"
    },
    "DataStorage": {
      "RetryAttempts": 3,
      "CacheSizeLimit": 100,
      "CacheExpirationMinutes": 60,
      "KeepDataOnRefresh": true
    },
    "DatabaseQuery": {
      "CommandTimeoutEnabled": false,
      "CommandTimeLimit": 150
    },
    "Https": {
      "Enabled": false,
      "CertificateType": "Pfx",
      "PfxPath": "",
      "PfxPassword": "",
      "PemCertPath": "",
      "PemKeyPath": "",
      "StoreSubject": "",
      "StoreLocation": "CurrentUser",
      "StoreName": "My",
      "Protocols": "Http1AndHttp2",
      "AllowInvalid": false
    },
    "Security": {
      "BasicAuthEnabled": false,
      "AllowedOrigins": [ "*" ]
    },
    "ReportSharing": {
      "EndpointEnabled": false,
      "StoragePath": "DataServer/Reports",
      "ReportExpirationTimeMinutes": 0,
      "MaxFolderSizeMB": 0,
      "AutoDelete": false
    },
    "Indexes": [
      {
        "Id": "sales-data",
        "Name": "Sales-Data",
        "SourceType": "Csv",
        "SourcePath": "sales.csv",
        "CsvDelimiter": ",",
        "ConnectionString": "",
        "Provider": "SQLite",
        "Query": "",
        "AutoReloadMinutes": 0
      },
      {
        "Id": "sales-json",
        "Name": "Sales-Analysis-JSON",
        "SourceType": "Json",
        "SourcePath": "sales-analysis.json"
      },
      {
        "Id": "sales-db",
        "Name": "Sales-Database",
        "SourceType": "Database",
        "Provider": "SQLite",
        "ConnectionString": "Data Source=DataSource/sales.db",
        "Query": "SELECT Region, Country, ItemType, SalesChannel, OrderDate, UnitsSold, TotalRevenue, TotalCost, TotalProfit FROM Sales"
      }
    ]
  }
}
```

> **Migration note:** the legacy `PivotDataSource` section maps to a single `Sales-Data` index during migration; the loader falls back to it with a deprecation warning if `DataServer.Indexes` is empty.

## 8.2 Model classes (defaults + validation)

All models live in `DataServer/Configuration/Models/`. Validation uses data annotations plus `IValidatableObject` for cross-field rules.

```csharp
public enum LogLevelOption { Info, Warn, Error, Fatal }        // INFO/WARN/ERROR/FATAL
public enum CertificateType { Pfx, Pem, Store }
public enum SslProtocolsOption { Http1, Http2, Http1AndHttp2 }
public enum DataSourceType { Json, Csv, Database }
public enum DbProvider { SqlServer, PostgreSql, MySql, Oracle, MariaDb, SQLite }

public class GeneralSettings : IValidatableObject
{
    [Range(1, 65535)] public int Port { get; set; } = 5285;
    [DefaultValue(LogLevelOption.Info)] public LogLevelOption MinimumLogLevel { get; set; } = LogLevelOption.Info;
    public IEnumerable<ValidationResult> Validate(ValidationContext _) => [];
}

public class DataStorageSettings
{
    [Range(0, 30)] public int RetryAttempts { get; set; } = 3;
    [Range(0, int.MaxValue)] public int CacheSizeLimit { get; set; } = 100;    // 0 = unlimited
    [Range(1, int.MaxValue)] public int CacheExpirationMinutes { get; set; } = 60;
    public bool KeepDataOnRefresh { get; set; } = true;
}

public class DatabaseQuerySettings
{
    public bool CommandTimeoutEnabled { get; set; }            // default false
    [Range(0, int.MaxValue)] public int CommandTimeLimit { get; set; } = 150; // 0 = unlimited (s)
}

public class HttpsSettings : IValidatableObject
{
    public bool Enabled { get; set; }                          // default false
    [DefaultValue(CertificateType.Pfx)] public CertificateType CertificateType { get; set; }
    public string PfxPath { get; set; } = "";
    public string PfxPassword { get; set; } = "";              // prefer env var
    public string PemCertPath { get; set; } = "";
    public string PemKeyPath { get; set; } = "";
    public string StoreSubject { get; set; } = "";
    [DefaultValue(StoreLocation.CurrentUser)] public StoreLocation StoreLocation { get; set; } = StoreLocation.CurrentUser;
    [DefaultValue("My")] public string StoreName { get; set; } = "My";
    [DefaultValue(SslProtocolsOption.Http1AndHttp2)] public SslProtocolsOption Protocols { get; set; } = SslProtocolsOption.Http1AndHttp2;
    public bool AllowInvalid { get; set; }                      // default false
    public IEnumerable<ValidationResult> Validate(ValidationContext _) { /* cert fields required when Enabled */ }
}

public class SecuritySettings
{
    public bool BasicAuthEnabled { get; set; }                 // default false
    public List<string> AllowedOrigins { get; set; } = ["*"];
}

public class ReportSharingSettings
{
    public bool EndpointEnabled { get; set; }                  // default false
    public string StoragePath { get; set; } = "DataServer/Reports";
    [Range(0, int.MaxValue)] public int ReportExpirationTimeMinutes { get; set; }  // 0 = permanent
    [Range(0, int.MaxValue)] public int MaxFolderSizeMB { get; set; }              // 0 = unlimited
    public bool AutoDelete { get; set; }                       // default false
}

public class IndexDefinition : IValidatableObject
{
    public string Id { get; set; } = "";                        // stable identifier
    [Required] public string Name { get; set; } = "";           // what the client sends
    public DataSourceType SourceType { get; set; }
    public string SourcePath { get; set; } = "";                // JSON/CSV file
    [DefaultValue(',')] public char CsvDelimiter { get; set; } = ',';
    public string? ConnectionString { get; set; }
    public DbProvider Provider { get; set; }
    public string? Query { get; set; }                           // Database sources
    [Range(0, int.MaxValue)] public int AutoReloadMinutes { get; set; }  // 0 = off
    public IEnumerable<ValidationResult> Validate(ValidationContext _) { /* per-type required fields, unique Name */ }
}
```

## 8.3 Settings hierarchy & binding

```text
IConfiguration (appsettings.json + env vars + command line)
        │  Bind()
        ▼
DataServerOptions  (root)
 ├── General        → GeneralSettings      → Kestrel port, LoggingManager
 ├── DataStorage    → DataStorageSettings  → CacheOptions, RetryPolicy
 ├── DatabaseQuery  → DatabaseQuerySettings → DatabaseIndexLoader
 ├── Https          → HttpsSettings        → HttpsConfigurer
 ├── Security       → SecuritySettings     → SecurityManager
 ├── ReportSharing  → ReportSharingSettings→ ReportShareManager
 └── Indexes        → IndexDefinition[]    → IndexManager
```

Precedence: environment variables (`DATASERVER__GENERAL__PORT`) > appsettings.json (runtime-updated copy) > model defaults.

# 9. Required Modifications to the Server-Side Controller

## 9.1 `Controllers/PivotController.cs`

| # | Change |
|---|---|
| 1 | Replace `DynamicDataLoader` constructor injection with `IndexManager` (keep `IMemoryCache` for engine cache). |
| 2 | Extract index name: `FetchData.Index` (new field) → fallback `X-Pivot-Index` header → typed `UnknownIndexException` if not found. |
| 3 | `GetData()` becomes `GetDataAsync(string indexName)` calling `IndexManager.GetOrLoad(indexName, RequestAborted)`; honors cache size/expiration from `DataStorageSettings`. |
| 4 | Engine cache key: `engine:{indexName}:{parameter.Hash}` (replaces `SourceIdentity`). |
| 5 | Data cache key: `data:{indexName}` — evicted/replaced by `IndexManager` on reload (respecting `KeepDataOnRefresh`). |
| 6 | `FetchData` model gains `public string Index { get; set; }` — additive, backward compatible. |
| 7 | `GetPivotValues` invalidation logic (lines 111–118 of current file) is preserved but keyed per index. |

## 9.2 `Program.cs`

| # | Change |
|---|---|
| 1 | Register `DataServerOptions` via `builder.Services.Configure<DataServerOptions>(builder.Configuration.GetSection("DataServer"))` + startup validation (`ValidateDataAnnotations` + `ValidateOnStart`). |
| 2 | Register singletons: `ConfigurationManager`, `IndexManager`, `ReportShareManager`, `SecurityManager`, `LoggingManager`. |
| 3 | Conditional `UseHttpsRedirection()` based on `Https.Enabled`. |
| 4 | Kestrel configuration via `HttpsConfigurer` (port + certificate + protocols). |
| 5 | Conditional Basic Auth: `AddAuthentication().AddScheme<BasicAuthOptions, BasicAuthHandler>("Basic")` + `UseAuthentication()` when enabled. |
| 6 | CORS: dynamic policy from `Security.AllowedOrigins` instead of allow-all (keep allow-all semantics when `*`). |
| 7 | Memory cache size from `DataStorage.CacheSizeLimit` (`0` → no `SizeLimit` set). |
| 8 | `DynamicDataLoader` registration removed; loaders live inside `IndexManager`. |

## 9.3 `appsettings.json`

Replaced with the §8.1 structure (with migration fallback for `PivotDataSource`, §8.1 note).

## 9.4 `DataSource/DynamicDataLoader.cs`

Refactored: parsing methods become the `JsonIndexLoader` / `CsvIndexLoader` internals; the config-driven single-source dispatch is deleted. `DataSource.cs` (legacy POCOs) is removed or left unused — recommend removal.

## 9.5 New files

```
PivotController/DataServer/
├── Configuration/
│   ├── ConfigurationManager.cs
│   ├── HttpsConfigurer.cs
│   └── Models/*.cs                     (§8.2)
├── Indexing/
│   ├── IndexManager.cs
│   ├── Loaders/IIndexLoader.cs
│   ├── Loaders/JsonIndexLoader.cs
│   ├── Loaders/CsvIndexLoader.cs
│   └── Loaders/DatabaseIndexLoader.cs
├── Security/
│   ├── SecurityManager.cs
│   ├── BasicAuthHandler.cs
│   └── UserStore.cs
├── Reports/ReportShareManager.cs
├── Logging/LoggingManager.cs + FileLoggerProvider.cs
└── Api/
    ├── ConfigController.cs
    ├── SecurityController.cs
    ├── IndexController.cs
    └── ReportController.cs
```

# 10. API Contracts

All management APIs are versioned under `/api/dataserver/`. Errors use RFC 7807 `ProblemDetails`:

```json
{
  "type": "https://dataserver/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "Port must be between 1 and 65535.",
  "traceId": "..."
}
```

## 10.1 Pivot data endpoint (existing, enhanced)

`POST /api/pivot/post` — body is the Syncfusion `FetchData` JSON **plus**:

```json
{
  "action": "fetchFieldMembers",
  "index": "Sales-Data",
  "memberName": "Country",
  ... (all existing FetchData fields unchanged)
}
```

Header fallback: `X-Pivot-Index: Sales-Data`.
When Basic Auth enabled: `Authorization: Basic base64(user:password)`.
Response payloads are unchanged Syncfusion engine outputs (pivot values / members / raw data / export file).

| Status | Meaning |
|---|---|
| 200 | Success (engine payload or export stream) |
| 400 | Malformed body / missing index |
| 401 | Basic auth enabled, invalid credentials |
| 404 | Unknown index |
| 503 | Index data unavailable after retries |

## 10.2 Configuration APIs (`ConfigController`)

| Method & Path | Request | Response |
|---|---|---|
| `GET /api/dataserver/config` | – | Full settings tree (secrets masked) |
| `GET /api/dataserver/config/{section}` (general, datastorage, databasequery, https, security, reportsharing) | – | Section |
| `PUT /api/dataserver/config/{section}` | Section JSON | `{"applied": true, "restartRequired": bool}` |
| `GET /api/dataserver/status` | – | `{ version, uptimeSeconds, port, httpsEnabled, indexCount }` |

`restartRequired` is `true` for port and HTTPS changes, `false` otherwise.

## 10.3 Security APIs (`SecurityController`)

| Method & Path | Request | Response |
|---|---|---|
| `GET /api/dataserver/security/users` | – | `[{"username","createdAt"}]` |
| `POST /api/dataserver/security/users` | `{"username","password"}` | `201` + user |
| `PUT /api/dataserver/security/users/{username}` | `{"password"}` | `204` |
| `DELETE /api/dataserver/security/users/{username}` | – | `204` |

Errors: `409` duplicate username, `400` weak/short password (min 8 chars), `404` unknown user.

## 10.4 Index APIs (`IndexController`)

| Method & Path | Request | Response |
|---|---|---|
| `GET /api/dataserver/indexes` | – | `[{"id","name","sourceType","status","rowCount","lastLoadedUtc","autoReloadMinutes"}]` |
| `POST /api/dataserver/indexes` | `IndexDefinition` JSON | `201` + definition (validates source, loads data eagerly) |
| `PUT /api/dataserver/indexes/{id}` | `IndexDefinition` | `200` + definition (triggers reload) |
| `DELETE /api/dataserver/indexes/{id}` | – | `204` |
| `POST /api/dataserver/indexes/{id}/reload` | – | `200 {"status":"reloaded","rowCount":n}` |

Errors: `409` duplicate index name, `400` invalid definition, `404` unknown id, `503` load failure.

## 10.5 Report Sharing APIs (`ReportController`)

| Method & Path | Request | Response |
|---|---|---|
| `POST /api/dataserver/reports` | Report JSON body | `201 {"reportId": "guid"}` |
| `GET /api/dataserver/reports/{reportId}` | – | Report JSON |
| `GET /api/dataserver/reports` | – | `[{"reportId","createdAt","expiresAt"}]` |
| `DELETE /api/dataserver/reports/{reportId}` | – | `204` |

Errors: `404` unknown/expired report, `413` folder-size limit exceeded, `503` endpoint disabled.

# 11. Data Indexing Strategy

1. **Definition** — indexes are declarative entries in `appsettings.json` (`DataServer:Indexes`), mirroring Flexmonster's approach.
2. **Identifier vs. Name** — `Id` is the stable internal key (URL-safe); `Name` is what clients send as `index` (e.g. `Sales-Data`). Name is unique and case-insensitive.
3. **Loading** — lazy on first request or eager via management API/reload. Loaders produce `IReadOnlyList<ExpandoObject>` with flat, consistent fields (JSON loader's existing schema validation is retained).
4. **Caching** — loaded datasets are cached in `IMemoryCache` under `data:{indexName}` with configured expiration (§12).
5. **Reload semantics** — `ReloadAsync` loads into a new snapshot; on success the cache entry is swapped atomically. With `KeepDataOnRefresh=true`, readers keep the old snapshot until the swap; with `false`, the entry is evicted first (requests may transiently fail or re-load).
6. **Auto-reload** — optional background `PeriodicTimer` per index.
7. **Deletion** — removes definition from appsettings.json and evicts cache; in-flight engines finish against their loaded snapshot.
8. **Failure handling** — retries per `DataStorage.RetryAttempts` with exponential backoff; final failure produces `503` for pivot requests and a `FATAL` log entry.

# 12. Data Storage & Caching Mechanisms

## 12.1 Cache layers

| Layer | Key | Contents | Eviction |
|---|---|---|---|
| Data cache | `data:{indexName}` | Raw loaded rows (`IReadOnlyList<ExpandoObject>`) | Absolute expiry = `CacheExpirationMinutes`; evicted on reload/delete |
| Engine cache | `engine:{indexName}:{hash}` | Computed `EngineProperties` per report state | Absolute expiry 60 min (existing behavior); removed after each action re-set (existing pattern) |

## 12.2 Size limiting

- `IMemoryCache.SizeLimit = CacheSizeLimit`; each entry counts size 1 (Phase 1; refine to byte-estimated sizes in a later phase). `0` → no limit.
- When the limit is reached, new entries evict the least-recently-used (default `IMemoryCache` behavior).

## 12.3 Persistence files

| File | Purpose | Written by |
|---|---|---|
| `appsettings.json` | All settings + index definitions | ConfigurationManager (atomic) |
| `DataServer/users.json` | User records (hashed) | UserStore (atomic) |
| `DataServer/Reports/{guid}.json` | Shared reports | ReportShareManager |
| `logs/dataserver-yyyyMMdd.log` | File log target | FileLoggerProvider |
| `DataServer/index-snapshots/` (optional, C) | On-disk binary snapshots for fast restart | IndexManager |

Atomic write pattern everywhere: serialize → write `*.tmp` → `File.Move(tmp, target, overwrite: true)`.

# 13. Database Query Processing Workflow

```text
Request / Reload for a Database index
   │
   ▼
Resolve IndexDefinition → DbProvider + ConnectionString + Query
   │
   ▼
RetryPolicy (up to DataStorage.RetryAttempts, exponential backoff)
   │
   ▼
Create DbConnection (provider factory)
   │   Connection string may reference env vars: ${VAR}
   ▼
Create DbCommand
   │   if DatabaseQuery.CommandTimeoutEnabled:
   │       CommandTimeout = CommandTimeLimit == 0 ? infinite : CommandTimeLimit
   ▼
ExecuteReader (CancellationToken + timeout)
   │   on timeout → IndexDataException("timeout", retryable:false) → 503
   │   on transient SqlException → retry
   ▼
Stream rows → ExpandoObjects (type inference: first N rows)
   ▼
Validate flat schema → cache under data:{indexName}
```

Notes:
- Query validation: only `SELECT` statements permitted (reject `INSERT/UPDATE/DELETE/DROP/...` at index-definition validation time).
- Reconnection on transient failures counts as one retry cycle.
- Multi-result sets: only the first result set is consumed.

# 14. HTTPS Configuration

## 14.1 Application flow

```text
Startup
  ▼
Read HttpsSettings
  ├─ Enabled == false → Kestrel binds HTTP only on General.Port; no https redirection
  └─ Enabled == true
        ├─ CertificateType == Pfx  → X509Certificate2(PfxPath, PfxPassword)
        ├─ CertificateType == Pem  → X509Certificate2(PemCertPath, PemKeyPath) [S]
        └─ CertificateType == Store → X509Store(StoreName, StoreLocation).Find(subject) [S]
        ▼
      Kestrel.ListenAnyIP(Port, opts => {
          opts.Protocols = Http1 | Http2 | Http1AndHttp2
          opts.UseHttps(cert, sslOpts => sslOpts.ClientCertificateMode = NoClientCert
                                          (AllowInvalid when dev flag set))
      })
```

## 14.2 Defaults & options

| Property | Default | Values |
|---|---|---|
| Enabled | `false` | `true` / `false` |
| CertificateType | `Pfx` | `Pfx`, `Pem`, `Store` |
| Protocols | `Http1AndHttp2` | `Http1`, `Http2`, `Http1AndHttp2` |
| StoreLocation | `CurrentUser` | `CurrentUser`, `LocalMachine` |
| StoreName | `My` | any store name |
| AllowInvalid | `false` | `true` allows self-signed (dev only) |

## 14.3 Validation & errors

- `Enabled && CertificateType=Pfx` requires non-empty `PfxPath`; password resolved from `PfxPassword` or `DATASERVER__HTTPS__PFXPASSWORD` env var.
- Certificate load failure at startup is fatal with a clear log message (category `DataServer.Https`).
- Port/HTTPS changes via API return `restartRequired: true`.

# 15. Security Configuration

## 15.1 Basic Authentication

- Enabled via `Security.BasicAuthEnabled`; applies to all `/api/**` endpoints including `/api/pivot/post`.
- `BasicAuthHandler` parses `Authorization: Basic` header, verifies via `UserStore.Verify(username, password)` (PBKDF2, constant-time).
- Failure: `401` + `WWW-Authenticate: Basic realm="DataServer"`.
- 5 failed attempts per username within 60 s → 60 s lockout (in-memory).

## 15.2 CORS / origin validation

- `AllowedOrigins = ["*"]` (default) → allow-any (current behavior).
- Otherwise: exact-match origin allow-list; non-listed origins receive no CORS headers (browser blocks).
- Origin list changes via API apply at runtime (dynamic policy provider).

## 15.3 User store (`DataServer/users.json`)

```json
{
  "users": [
    {
      "username": "admin",
      "passwordHash": "PBKDF2:SHA256:100000:<base64salt>:<base64hash>",
      "createdAt": "2025-01-01T00:00:00Z"
    }
  ]
}
```

Rules: unique username (case-insensitive), immutable after creation, password min 8 chars, hashed only (PBKDF2 100k iterations, 16-byte salt, per-user), never logged.

# 16. Report Sharing Architecture

```text
POST /api/dataserver/reports          (client saves current report)
   │  EndpointEnabled? → 503 if disabled
   ▼
Validate JSON report (schema: non-empty object, max 5 MB)
   │
   ▼
Folder size check (MaxFolderSizeMB; 0 = unlimited) → 413 if exceeded
   │
   ▼
Persist {guid}.json under StoragePath (atomic write)
   │  metadata: createdAt, expiresAt (now + ReportExpirationTimeMinutes; 0 = null)
   ▼
Return { reportId }

GET /api/dataserver/reports/{reportId}
   │  exists? not expired? → 200 report JSON; else 404
```

- **Expiration** checked on read; when `AutoDelete=true`, a 1-minute background sweep also deletes expired files.
- **Folder maintenance**: daily log of folder size; optional cleanup of orphaned `.tmp` files.
- **Security**: report IDs are server-generated GUIDs (no user-controlled paths); when Basic Auth is enabled the report endpoints require authentication too.

# 17. Logging, Monitoring & Validation

## 17.1 Log levels & categories

| Level | Applied to |
|---|---|
| INFO | startup, config loaded/applied, index created/loaded/reloaded/deleted, report saved/retrieved |
| WARN | retryable failure, cache eviction under pressure, deprecated config fallback, origin rejected |
| ERROR | failed index load after retries, report save failure, certificate errors |
| FATAL | startup failure, configuration validation failure, user store corruption |

Categories (log namespace): `DataServer.Configuration`, `.Authentication`, `.Indexing`, `.Database`, `.Reports`, `.Https`, `.Request`.

## 17.2 Targets

- **Console** (existing provider retained).
- **File** (`logs/dataserver-yyyyMMdd.log`, daily roll, 30-day retention default).

## 17.3 Minimum log level

`General.MinimumLogLevel` maps `INFO→Information`, `WARN→Warning`, `ERROR→Error`, `FATAL→Critical` and filters both targets. Runtime-updatable via `PUT /api/dataserver/config/general`.

## 17.4 Request logging

One structured line per request: timestamp, method, path, index (if applicable), status code, duration ms.

## 17.5 Validation requirements

| Validation | When | Failure result |
|---|---|---|
| Settings schema & ranges (§8.2) | startup + every API update | startup fatal / API `400` with details |
| Index definition (required fields per type, unique name, SELECT-only queries) | create/update + startup | `400` / startup fatal listing all invalid indexes |
| Port 1–65535 | update | `400` |
| Password policy | user create/update | `400` |
| Report JSON ≤ 5 MB, valid object | save | `400` |
| Origin allow-list | request-time CORS | blocked silently (WARN log) |

# 18. Error Handling & Exception Management

## 18.1 Exception hierarchy

```csharp
DataServerException                       // base, maps to 500 unless overridden
├── ConfigurationValidationException      // 400, carries list of errors
├── UnknownIndexException                 // 404
├── IndexLoadException                    // 503, retryable info attached
├── DataSourceFormatException             // 400 (malformed JSON/CSV)
├── DatabaseQueryException                // 503 (timeout / transient / fatal flags)
├── AuthenticationException               // 401
├── ReportNotFoundException               // 404
├── ReportLimitExceededException          // 413
└── UserConflictException                 // 409
```

## 18.2 Middleware behavior

- Global exception handler converts unhandled exceptions to `ProblemDetails` with `traceId`.
- No stack traces or secrets in production responses; full details at `ERROR` log level only.
- Retry policy (`Polly` or hand-rolled) wraps loader calls: `RetryAttempts` × exponential backoff (1 s base, ×2, jitter).
- Circuit-breaker per index after N consecutive load failures (Phase 1: simple consecutive-failure counter ≥ 5 → `503` immediately for 60 s).

# 19. Client-Side Pivot Table Sample Configuration

The final step of Phase 1: the sample passes the **index name** at source level, mirroring the Flexmonster pattern:

```js
dataSourceSettings: {
    url: 'http://localhost:5285/api/pivot/post',   // simple data server endpoint
    index: 'Sales-Data',                           // index name passed from client
    mode: 'Server',
    ...
}
```

The Syncfusion pivot engine appends the custom `index` member to every `FetchData` POST body. The server routes by it (§10.1). The index name lives in **sample-level/source-level code** (here `Sample/pivot.js`), so users swap datasets by changing one string.

`Sample/pivot.js` is updated in this phase to demonstrate the full configuration (see §22, Milestone 6).

# 20. Sequence Diagrams & Workflows

## 20.1 Pivot request (fetch pivot values)

```text
Client            PivotController        IndexManager         Cache/Loaders        PivotEngine
  │  POST /api/pivot/post  │                 │                     │                  │
  │  {action, index, ...}  │                 │                     │                  │
  │───────────────────────>│                 │                     │                  │
  │                        │ resolve index   │                     │                  │
  │                        │────────────────>│  GetOrLoad(name)   │                  │
  │                        │                 │───cache hit?────────>│                  │
  │                        │                 │<───rows──────────── │ (miss: load+retry)│
  │                        │                 │                     │                  │
  │                        │  engine cache key = engine:{index}:{hash}                  │
  │                        │──────────────────────────────────────────────────────────>│
  │                        │                 │                     │   GetEngine/     │
  │                        │                 │                     │   PerformAction  │
  │<─────pivot values JSON─│                 │                     │                  │
```

## 20.2 Index creation via API

```text
Admin API          ConfigController      IndexManager      Loader      appsettings.json
   │ POST /indexes     │                    │                │             │
   │──────────────────>│  validate def      │                │             │
   │                   │───────────────────>│  eager load    │             │
   │                   │                    │───────────────>│             │
   │                   │                    │<───rows──────── │             │
   │                   │  persist definition │                │             │
   │                   │──────────────────────────────────────────────────> │
   │<──201 + status────│                    │                │             │
```

## 20.3 Index reload (KeepDataOnRefresh = true)

```text
Reload trigger (API or auto-timer)
   │
   ▼
IndexManager: load new snapshot into staging
   │  (readers keep using cached snapshot — no downtime)
   ▼
Load OK? ──yes──> atomically swap cache entry `data:{indexName}`; evict engine keys
   │no
   ▼
Retry up to RetryAttempts → still failing: keep old snapshot, log ERROR/FATAL,
                            mark index status = "stale" in /api/dataserver/indexes
```

## 20.4 Configuration update workflow

```text
PUT /api/dataserver/config/{section}
   ▼
Validate section model ──invalid──> 400 ProblemDetails (all errors listed)
   ▼ valid
Atomically persist to appsettings.json
   ▼
Apply at runtime where possible (log level, CORS, cache, timeout, reports)
   ▼
Respond { applied: true, restartRequired: <port|https ? true : false> }
```

## 20.5 Report sharing workflow — see §16 diagram.

## 20.6 Authenticated pivot request

```text
Client ── POST /api/pivot/post + Authorization: Basic ──> Pipeline
Pipeline: CORS check → BasicAuthHandler → UserStore.Verify (PBKDF2, constant-time)
   ├─ valid → PivotController (normal flow)
   └─ invalid → 401 + WWW-Authenticate
```

# 21. Acceptance Criteria

## 21.1 General Settings
- [ ] Server binds to the configured port; invalid port rejected with 400 / startup error.
- [ ] Log level filtering works: at WARN, no INFO entries appear in console or file targets.
- [ ] `GET /api/dataserver/config` returns the full tree with defaults when nothing customized.

## 21.2 Data Storage
- [ ] Retry logic: failing source retried exactly `RetryAttempts` times with backoff.
- [ ] Cache size limit enforced (LRU eviction observed).
- [ ] `KeepDataOnRefresh=true`: during reload, pivot requests still served with old data.

## 21.3 Database Query Settings
- [ ] Timeout enforced: query exceeding `CommandTimeLimit` aborts within limit + margin and returns 503.
- [ ] `CommandTimeLimit=0` = unlimited works.

## 21.4 HTTPS
- [ ] PFX certificate serves HTTPS on configured port.
- [ ] PEM and Store certificates load (when available on test platform).
- [ ] HTTP2 negotiation works with `Protocols=Http1AndHttp2`.
- [ ] Disabled by default; no forced redirect when disabled.

## 21.5 Security
- [ ] Basic Auth enabled: unauthenticated request → 401; valid credentials → 200.
- [ ] User CRUD works; duplicate username → 409; passwords never stored/returned in plaintext.
- [ ] Origin validation: non-allowed origin receives no CORS headers.

## 21.6 Report Sharing
- [ ] Reports saved & retrieved by ID; invalid JSON rejected.
- [ ] Expiration: expired report returns 404.
- [ ] Folder limit: exceeding MaxFolderSizeMB → 413.
- [ ] AutoDelete removes expired files within ~1 min.

## 21.7 Index Management
- [ ] Create / list / reload / delete index via API works; definitions persist in appsettings.json across restart.
- [ ] Client request with `index: "Sales-Data"` serves the CSV dataset; `index: "Sales-Analysis-JSON"` serves JSON dataset; unknown index → 404.
- [ ] Pivot values, member fetch, raw data, and exports all work per index.

## 21.8 Client Integration
- [ ] `Sample/pivot.js` renders the pivot table against `http://localhost:5285/api/pivot/post` with `index: 'Sales-Data'`.

## 21.9 Non-functional
- [ ] Warm p95 < 300 ms on sample data; cold index < 2 s.
- [ ] Concurrent multi-index requests served in parallel.
- [ ] No secrets in logs; atomic config writes verified (no corruption on kill during write).

# 22. Implementation Milestones

| # | Milestone | Contents | Depends on |
|---|---|---|---|
| 1 | **Configuration foundation** | Models (§8.2), ConfigurationManager, appsettings.json migration, validation, atomic persistence | – |
| 2 | **Indexing core** | IndexManager, loaders (JSON/CSV/SQLite refactor from DynamicDataLoader), per-index routing in PivotController, `FetchData.Index` | 1 |
| 3 | **Data storage & query settings** | Retry policy, cache limits/expiration, KeepDataOnRefresh swap, DB command timeout | 2 |
| 4 | **Security & HTTPS** | UserStore + BasicAuthHandler, dynamic CORS, HttpsConfigurer, conditional redirection | 1 |
| 5 | **Report sharing & logging** | ReportShareManager + endpoints, FileLoggerProvider, level filtering, request logging | 1 |
| 6 | **Client sample & hardening** | Sample/pivot.js index configuration, exception middleware, ProblemDetails, status endpoint, acceptance test pass | 2–5 |

Each milestone ends with: build clean, sample verified, acceptance items ticked for its scope.

# 23. Future Phase Considerations

- **Phase 2**: Admin Panel UI consuming `/api/dataserver/*` (config editor, index manager, user manager, report browser), Monitoring dashboard (request rates, cache hit ratio, index freshness), Diagnostics UI.
- Per-index user ACLs.
- On-disk index snapshots for fast restart; columnar cache for large datasets.
- Streaming/partial aggregates for datasets beyond memory.
- OAuth/JWT authentication alongside Basic Auth.

---

## Conclusion

Phase 1 establishes a backend-first, configuration-driven Data Server: a single simple pivot endpoint (`http://localhost:5285/api/pivot/post`) selected by client-supplied index name, backed by a complete settings hierarchy (General, Data Storage, Database Query, HTTPS, Security, Report Sharing, Indexes) with standard defaults, validation, persistence, and management APIs. The architecture intentionally separates backend functionality from UI concerns so the Phase 2 Admin Panel can be layered on without architectural change.
