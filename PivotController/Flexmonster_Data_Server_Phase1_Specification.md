# Custom Data Server — Phase 1 Technical Specification

## Document control

| Field | Value |
| --- | --- |
| Document | Custom Data Server Phase 1 Specification |
| Version | 2.0 |
| Status | Specification only — implementation is out of scope for this document revision |
| Method | Open Spec Explore of the existing PivotController repository, then specify the delta |
| Repository | `server-side-pivot-engine-for-pivot-table` |
| Working branch baseline | `Controller` |
| Target runtime | ASP.NET Core, `net10.0`, `Syncfusion.Pivot.Engine` 34.2.6 |
| Phase | Phase 1 — server-side controller configuration, index resolution, and client index binding |
| Explicitly out of scope | Admin Panel UI, Monitoring UI, Diagnostics UI, visual configuration editor |

This document replaces the high-level draft previously stored in this file. It is the source of truth for Phase 1. Later phases must extend this contract rather than invent a parallel one.

---

## 1. Purpose

Phase 1 turns the existing Syncfusion server-side pivot controller into a configuration-driven Custom Data Server whose settings model is conceptually equivalent to the Flexmonster Data Server Admin Panel.

The controller must:

1. Own every Admin Panel setting as a typed, validated, persisted property, initialized to documented defaults even though no Admin Panel is built in this phase.
2. Resolve a named **index** supplied by the Pivot Table client and bind that index to a JSON, CSV, or database source.
3. Continue to perform pivot aggregation on the server through `Syncfusion.Pivot.Engine` and return only the viewport payload.
4. Expose a stable pivot endpoint, `POST /api/pivot/post`, plus configuration endpoints that a future Admin Panel can call without changing the pivot protocol.
5. Ship a sample Pivot Table configuration that passes the index name at sample level (and documents the equivalent source-level hook).

After Phase 1, an operator can change data-server settings by editing `appsettings.json` (the same place Flexmonster persists indexes) or by calling the configuration API. The Admin Panel itself is a later consumer of those APIs, not a Phase 1 deliverable.

### 1.1 What “Flexmonster-like” means here

Flexmonster’s client connects to its data server with an index name:

```javascript
dataSource: {
  type: "api",
  url: "http://localhost:9500",
  index: "Sales-Data"
}
```

Flexmonster persists that index, and the other Admin Panel sections, in `appsettings.json`. This product is **not** a Flexmonster protocol clone. The Syncfusion Pivot Table already posts engine actions to `POST /api/pivot/post`. Phase 1 keeps that protocol and adds the missing index dimension:

```javascript
dataSourceSettings: {
  url: "http://localhost:5285/api/pivot/post",
  mode: "Server",
  index: "Sales-Data"
}
```

The index name is the only new required client field. All other Admin Panel settings stay on the server.

---

## 2. Open Spec Explore — current controller

Exploration is limited to the files that Phase 1 must change or deliberately leave alone.

### 2.1 Runtime topology today

```text
Sample/pivot.html
    └── Sample/pivot.js
            dataSourceSettings.url = https://localhost:44350/api/pivot/post
            dataSourceSettings.mode = Server
            no index property
                    │
                    ▼
PivotController/Program.cs
    CORS: AllowAnyOrigin / Header / Method ("_customOrigins")
    MemoryCache SizeLimit = 100, no sliding policy
    DynamicDataLoader registered as Scoped
    Swagger only in Development
    UseHttpsRedirection + UseAuthorization (no authentication scheme)
                    │
                    ▼
Controllers/PivotController.cs
    POST /api/pivot/post
    actions: fetchFieldMembers | fetchRawData | export* | default pivot values
    engine cache key: engine:{SourceIdentity}:{parameter.Hash}
    data cache key:   dataSource:{SourceIdentity}
    both entries size 1, absolute expiration 60 minutes
                    │
                    ▼
DataSource/DynamicDataLoader.cs
    reads a single IConfiguration section "PivotDataSource"
    Type = Json | Csv | Database
    files must resolve inside ContentRoot/DataSource
    Database = local SQLite only, one SELECT, optional sales.sql bootstrap
```

Sample data already present under `PivotController/DataSource/`:

| File | Role |
| --- | --- |
| `sales.csv` | CSV source used when `PivotDataSource:Type` is `Csv` |
| `sales-analysis.json` | JSON source |
| `sales.db` / `sales.sql` | SQLite database plus bootstrap script |

`launchSettings.json` http profile listens on `http://localhost:5285`. The https profile also binds `https://localhost:7264`. IIS Express SSL port `44350` is what `Sample/pivot.js` currently hard-codes. Those three URLs must not be treated as equivalent in the new sample.

### 2.2 Current configuration (`appsettings.json`)

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "PivotDataSource": {
    "Type": "Csv",
    "JsonPath": "sales-analysis.json",
    "CsvPath": "sales.csv",
    "CsvDelimiter": ",",
    "DatabasePath": "sales.db",
    "InitializationScript": "sales.sql",
    "Query": "SELECT Region, Country, ItemType, SalesChannel, OrderDate, UnitsSold, TotalRevenue, TotalCost, TotalProfit FROM Sales"
  }
}
```

Gaps relative to a data server:

- One global source. The client cannot choose `Sales-Data` versus another index.
- No port, HTTPS certificate, basic-auth, report-sharing, retry, command-timeout, or cache-policy section.
- `Program.cs` hard-codes cache size `100` and an allow-all CORS policy. Those values are not the settings model.
- `DynamicDataLoader` is scoped and closes over `IConfiguration` at construction. It cannot switch source per request.
- Cache keys ignore any client-supplied source identity, so two indexes would collide or, worse, share one engine.
- Database access is SQLite file only. Connection strings, providers, and command timeout are absent.
- HTTPS is the ASP.NET development certificate via launch profile, not a configurable PFX/PEM/store certificate.
- Authorization middleware is registered without an authentication handler.
- `DataSource/DataSource.cs` (`PivotCSVData`, `PivotJSONData`, `PivotViewData`, `BusinessObjectsDataView`) is legacy sample code. It uses `WebClient`, is not referenced by `PivotController`, and is not the data-server loader. Phase 1 must not extend it.

### 2.3 Pivot protocol that must be preserved

`PivotController.Post` deserializes the body with Newtonsoft into `Syncfusion.Pivot.Engine.FetchData` and switches on `Action`:

| `Action` | Behavior to preserve |
| --- | --- |
| `fetchFieldMembers` | Ensure members, return `{ memberName, members }` |
| `fetchRawData` | `pivotEngine.GetRawData` |
| `onExcelExport`, `onCsvExport`, `onPivotExcelExport`, `onPivotCsvExport` | Existing export engines |
| anything else | `GetEngine` + `PerformAction` + `GetSerializedPivotValues` |

`FetchData` is an engine type. Phase 1 must not fork it. The index name arrives as an additional JSON property on the same POST body (see §8.1). Grouping refresh still reloads rows into `engine.Data`. Engine cache removal/re-set after `PerformAction` stays, but the cache key gains the index name.

### 2.4 Constraints discovered during explore

| ID | Constraint | Implication |
| --- | --- | --- |
| C1 | `FetchData` is owned by `Syncfusion.Pivot.Engine` | Do not subclass it solely to add `Index`. Read `index` from the raw JSON body. |
| C2 | Engine and export types are not thread-safe shared singletons | Keep a pivot engine instance per request, as today. Do not register `PivotEngine<T>` as a singleton. |
| C3 | `IMemoryCache` size is in arbitrary units (`SetSize(1)`) | `CacheSizeLimit` counts entries, not megabytes, unless a later phase measures payloads. Document this explicitly so Admin Panel labels do not promise bytes. |
| C4 | File paths are confined to `ContentRoot/DataSource` | Index file paths stay inside that root. Connection strings are the exception and must not be used as file paths. |
| C5 | SQLite query guard rejects non-SELECT and any `;` | Keep that guard for SQLite. External providers need a stricter read-only policy, not string concatenation of client input. |
| C6 | Port cannot be changed by rewriting `IConfiguration` after `WebApplication` has started | Persist the new port and surface `restartRequired`. Do not pretend `IServer` rebinding is in Phase 1. |
| C7 | Sample currently points at IIS Express `:44350` | Phase 1 sample must point at the data-server URL operators will actually run, default `http://localhost:5285/api/pivot/post`. |
| C8 | No test project exists | Phase 1 implementation must add tests; this document specifies them, it does not add the project. |

---

## 3. Phase 1 goals and non-goals

### 3.1 Goals

| ID | Goal |
| --- | --- |
| G1 | Represent every Flexmonster Admin Panel setting group as server configuration with defaults. |
| G2 | Persist settings in `appsettings.json` under a `DataServer` section, including the index catalog, matching the Flexmonster operational model. |
| G3 | Accept `index` from the Pivot Table sample and from any client that posts to `/api/pivot/post`. |
| G4 | Load JSON, CSV, and database rows for the requested index, with retry, cache, and command-timeout settings applied. |
| G5 | Enforce basic authentication, CORS allow-list, and HTTPS certificate selection from configuration. |
| G6 | Provide report-sharing storage settings and the minimal read/write endpoints those settings govern. |
| G7 | Expose configuration read/update APIs so a future Admin Panel can edit the same model. |
| G8 | Leave the existing pivot calculation contract intact for clients that omit `index` only when a single default index is configured. |

### 3.2 Non-goals (this phase)

- Admin Panel, monitoring dashboard, diagnostics UI, or any new browser application.
- Flexmonster binary protocol, Flexmonster `type: "api"` response shapes, or Flexmonster license checks.
- Implementing SQL Server, PostgreSQL, MySQL, Oracle, or MariaDB providers beyond the configuration model and a provider-resolution seam. Phase 1 **executes** JSON, CSV, and SQLite. Other providers are validated and stored, and return a controlled `501` / index status `ProviderNotAvailable` until their driver package is added. The settings and connection-string shape must already match what those providers will need.
- Changing Kestrel’s listening port or certificate live, without process restart.
- Multi-node cache coherence, distributed locks, or a shared database for report files.
- Replacing `Syncfusion.Pivot.Engine`.
- Deleting legacy `DataSource.cs` (allowed as a follow-up, not required to satisfy acceptance).

---

## 4. Requirements

### 4.1 Functional requirements

| ID | Requirement |
| --- | --- |
| FR-1 | The server shall load `DataServer` from `appsettings.json`, then overlay environment variables and command-line configuration using the standard ASP.NET Core configuration order. |
| FR-2 | Missing `DataServer` shall be equivalent to the default object graph in §6. Startup shall not fail solely because the section is absent. |
| FR-3 | Invalid settings shall fail startup when they make the process unsafe to run (bad certificate path while HTTPS is enabled, port out of range, unreadable security store). Invalid index definitions shall not crash startup; they shall be marked `Error` and excluded from serving. |
| FR-4 | `POST /api/pivot/post` shall select the index named by the request `index` property, case-sensitive, and shall never fall through to another index’s rows. |
| FR-5 | If the request omits `index` and exactly one index has `IsDefault: true`, that index shall be used. If none or more than one default exists, the server shall return 400 `IndexRequired`. |
| FR-6 | Unknown, disabled, or failed indexes shall return 404 `IndexNotFound` or 409 `IndexUnavailable` and shall not populate the pivot cache. |
| FR-7 | JSON and CSV indexes shall load through the existing flat-record rules in `DynamicDataLoader` (unique headers, consistent fields, no nested JSON, typed CSV columns, non-empty result). |
| FR-8 | Database indexes shall execute only the configured query. The query is server configuration, never request input. |
| FR-9 | Cache size, refresh retention, and load retries shall follow `DataStorage`. |
| FR-10 | Command timeout shall follow `DatabaseQuery` for every database open, initialize, and reader execution. |
| FR-11 | When `Security.BasicAuthEnabled` is true, every endpoint except `GET /api/dataserver/health` shall require HTTP Basic credentials verified against the configured user store. |
| FR-12 | CORS shall allow only `Security.AllowedOrigins`. The value `*` retains today’s allow-any behavior. Any other value is an explicit origin list and must not be combined with `AllowAnyOrigin`. |
| FR-13 | HTTPS settings shall be persisted and applied on startup. Certificate material shall not be returned by any GET API. |
| FR-14 | Report sharing, when enabled, shall store and retrieve report JSON under the configured folder, enforce expiration and folder quota, and refuse paths that escape the folder. |
| FR-15 | Configuration updates shall be validated, written atomically, and reflected in `IOptionsMonitor<DataServerOptions>` without requiring a restart, except properties flagged `restartRequired`. |
| FR-16 | The sample Pivot Table shall set `index` to a seeded index name and `url` to `http://localhost:5285/api/pivot/post`. |
| FR-17 | Logs shall honor `General.MinimumLogLevel` and shall never write passwords, certificate passwords, or full connection strings. |
| FR-18 | User passwords shall be stored only as hashes. User names shall be immutable. |
| FR-19 | Index reload shall be an explicit operation. File-change watchers are out of scope. |
| FR-20 | A future Admin Panel shall be able to round-trip every setting in §6 through the APIs in §8 without knowing the pivot engine. |

### 4.2 Non-functional requirements

| ID | Category | Requirement |
| --- | --- | --- |
| NFR-1 | Compatibility | Existing `FetchData` actions and response shapes remain unchanged when the selected index returns the same rows the old `PivotDataSource` returned. |
| NFR-2 | Security | No secret in API responses, logs, or exception payloads. Path traversal rejected for data files, certificates, and report files. Queries are not built from request strings. |
| NFR-3 | Atomicity | Configuration writes use a temp file in the same directory, then `File.Replace`, so a crash cannot leave a truncated `appsettings.json`. |
| NFR-4 | Concurrency | Index catalog mutations and report-folder quota checks are serialized per store. Pivot reads of an already loaded index do not take that write lock. |
| NFR-5 | Performance | Index metadata lookup is in-memory. Row materialization hits disk or database only on cache miss, reload, or first use. Cache hits do not re-parse CSV/JSON. |
| NFR-6 | Capacity | Default cache limit is 100 entries (current `SizeLimit`). A single index’s row cache counts as one entry; each distinct `parameter.Hash` engine state counts as one entry. |
| NFR-7 | Observability | Each pivot request logs index name, action, cache hit/miss, and elapsed milliseconds at Information. Failures log exception type and index name at Error, without row payloads. |
| NFR-8 | Operability | Defaults must boot with the current sample files and no extra secrets. HTTPS and basic auth stay off until configured. |
| NFR-9 | Maintainability | Settings types live outside the MVC controller. `PivotController` depends on an index resolver, not on `IConfiguration` string keys. |
| NFR-10 | Testability | Resolver, validator, cache policy, query guard, and configuration writer are unit-testable without Kestrel. |
| NFR-11 | Localization | Error `code` values are stable English identifiers. `message` may be English prose. Clients branch on `code`. |
| NFR-12 | Time | Expirations use UTC. Report `createdUtc` is ISO-8601. |

---

## 5. Architecture

### 5.1 Component view

```text
Pivot Table sample                         Future Admin Panel (not in Phase 1)
  index: "Sales-Data"                        GET/PUT /api/dataserver/config/*
  POST /api/pivot/post                       POST/DELETE /api/dataserver/indexes
        │                                              │
        └──────────────────┬───────────────────────────┘
                           ▼
                 ASP.NET Core pipeline
                 CORS  →  Basic auth (optional)  →  Controllers
                           │
        ┌──────────────────┼───────────────────────────┐
        ▼                  ▼                           ▼
 PivotController     DataServerConfigController    ReportShareController
        │                  │                           │
        ▼                  ▼                           ▼
 IndexResolver      DataServerConfigStore        ReportShareStore
        │                  │
        ▼                  ▼
 IndexCatalog  ←──  appsettings.json  DataServer section
        │
        ▼
 IndexDataLoader  (json / csv / sqlite / provider seam)
        │
        ▼
 IMemoryCache  →  PivotEngine<ExpandoObject>  →  serialized pivot values
```

### 5.2 Proposed project layout

Add files under `PivotController/`. Do not create a second web project.

```text
PivotController/
  Program.cs                              # bind options, CORS, auth, cache, HTTPS
  appsettings.json                        # DataServer section + legacy bridge
  Controllers/
    PivotController.cs                    # resolve index, then existing engine flow
    DataServerConfigController.cs         # new
    DataServerIndexController.cs          # new
    ReportShareController.cs              # new
  DataServer/
    Options/
      DataServerOptions.cs                # root + all setting groups
      IndexDefinition.cs
    Validation/
      DataServerOptionsValidator.cs
    Configuration/
      DataServerConfigStore.cs            # atomic appsettings.json updates
    Indexing/
      IndexCatalog.cs
      IndexResolver.cs
      IndexDataLoader.cs                  # extracts DynamicDataLoader per index
      IndexLoadRetryPolicy.cs
    Security/
      BasicAuthHandler.cs
      PasswordHasher.cs
    Https/
      KestrelHttpsConfigurator.cs
    Reports/
      ReportShareStore.cs
    Logging/
      DataServerLogger.cs                 # category names only; uses ILogger
  Sample/                                 # repository root, not under PivotController
    pivot.js                              # url + index
```

`DynamicDataLoader` remains the parsing implementation. Phase 1 changes its public surface from “read `PivotDataSource` from `IConfiguration`” to “load this `IndexDefinition`”. The old constructor can stay as a thin adapter for one release, but `PivotController` must stop calling it.

### 5.3 Dependency registration

| Service | Lifetime | Notes |
| --- | --- | --- |
| `IOptions<DataServerOptions>` | singleton options | `services.Configure<DataServerOptions>(configuration.GetSection("DataServer"))` |
| `IValidateOptions<DataServerOptions>` | singleton | startup validation |
| `DataServerConfigStore` | singleton | file lock + `IOptionsMonitor` cache |
| `IndexCatalog` | singleton | projects `DataServer:Indexes` into a case-sensitive dictionary |
| `IndexDataLoader` | singleton | stateless aside from data directory path |
| `IMemoryCache` | singleton | `SizeLimit` from options; see restart note in §6.3 |
| `ReportShareStore` | singleton | path from options |
| `PasswordHasher` | singleton | PBKDF2 |
| `PivotController` dependencies | scoped controller | engine objects created per request, not DI singletons |

`DynamicDataLoader` scoped registration in `Program.cs` is removed when `PivotController` no longer injects it.

### 5.4 Request pipeline order

```text
UseExceptionHandler
UseCors(DataServerCors)          # policy rebuilt when AllowedOrigins changes
UseAuthentication                # no-op when basic auth disabled
UseAuthorization
MapControllers
```

`UseHttpsRedirection` runs only when `Https.Enabled` is true. Today it always runs, which breaks the pure HTTP sample profile (`http://localhost:5285`) by redirecting to a development HTTPS port. Phase 1 must not redirect when HTTPS is disabled.

Swagger remains Development-only and is not an Admin Panel.

### 5.5 Sequence — pivot request with index

```text
Client                PivotController        IndexResolver         Cache           IndexDataLoader      PivotEngine
  │ POST /api/pivot/post   │                     │                  │                    │                  │
  │ index=Sales-Data       │                     │                  │                    │                  │
  │ action=...             │                     │                  │                    │                  │
  │───────────────────────>│                     │                  │                    │                  │
  │                        │ resolve(index)      │                  │                    │                  │
  │                        │────────────────────>│                  │                    │                  │
  │                        │ IndexDefinition     │                  │                    │                  │
  │                        │<────────────────────│                  │                    │                  │
  │                        │ get dataSource:{index}                 │                    │                  │
  │                        │───────────────────────────────────────>│                    │                  │
  │                        │ miss                │                  │                    │                  │
  │                        │ load(definition)    │                  │                    │                  │
  │                        │───────────────────────────────────────────────────────────>│                  │
  │                        │ rows                │                  │                    │                  │
  │                        │<───────────────────────────────────────────────────────────│                  │
  │                        │ get engine:{index}:{hash}              │                    │                  │
  │                        │───────────────────────────────────────>│                    │                  │
  │                        │ GetEngine / PerformAction              │                    │                  │
  │                        │──────────────────────────────────────────────────────────────────────────────>│
  │ serialized values      │                    │                   │                    │                  │
  │<───────────────────────│                    │                   │                    │                  │
```

Reload and configuration update sequences are in §9.

---

## 6. Configuration model

### 6.1 Root

Section name: `DataServer`.

```csharp
public sealed class DataServerOptions
{
    public GeneralSettings General { get; set; } = new();
    public DataStorageSettings DataStorage { get; set; } = new();
    public DatabaseQuerySettings DatabaseQuery { get; set; } = new();
    public HttpsSettings Https { get; set; } = new();
    public SecuritySettings Security { get; set; } = new();
    public ReportSharingSettings ReportSharing { get; set; } = new();
    public List<IndexDefinition> Indexes { get; set; } = [];
}
```

JSON names use PascalCase to match the current `appsettings.json` style and Flexmonster’s file. API JSON uses camelCase. The store is responsible for mapping between them so the file and the API do not drift into two schemas.

### 6.2 General settings

```csharp
public sealed class GeneralSettings
{
    public int Port { get; set; } = 5285;
    public DataServerLogLevel MinimumLogLevel { get; set; } = DataServerLogLevel.Info;
}

public enum DataServerLogLevel
{
    Info = 0,
    Warn = 1,
    Error = 2,
    Fatal = 3
}
```

| Property | Default | Validation | Apply |
| --- | --- | --- | --- |
| `Port` | `5285` | Integer 1–65535. Must not be a port already documented as conflicting in the error payload. | Persist immediately. Kestrel does not rebind. Response includes `restartRequired: true`. Startup reads this value only when no `ASPNETCORE_URLS` and no launch-profile URL is set. When either is set, the host URL wins and a warning is logged. |
| `MinimumLogLevel` | `Info` | One of `Info`, `Warn`, `Error`, `Fatal`. | Applied immediately to the `PivotController.DataServer` filter. Microsoft categories stay at Warning. |

Log level mapping:

| Data server | Microsoft.Extensions.Logging |
| --- | --- |
| Info | Information |
| Warn | Warning |
| Error | Error |
| Fatal | Critical |

`Debug` and `Trace` are intentionally absent, matching the Flexmonster minimum-level set in the draft. They must be rejected, not coerced.

Default port is **5285**, not 9500. 9500 is Flexmonster’s convention. This repository’s http profile is already `http://localhost:5285`, and that is the URL Phase 1 tells Pivot Table users to call. A comment in `appsettings.json` is not possible in JSON; the specification and README sample must state the choice.

### 6.3 Data storage settings

```csharp
public sealed class DataStorageSettings
{
    public int RetryAttempts { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 200;
    public int CacheSizeLimit { get; set; } = 100;
    public int CacheEntryTtlMinutes { get; set; } = 60;
    public bool KeepDataOnRefresh { get; set; } = true;
}
```

| Property | Default | Validation | Behavior |
| --- | --- | --- | --- |
| `RetryAttempts` | 3 | 0–30 | Extra attempts after the first failure. 0 means no retry. Counts file IO and database open/execute failures. Does not retry validation errors (`InvalidDataException`, bad SQL guard). |
| `RetryDelayMilliseconds` | 200 | 0–60000 | Fixed delay between attempts. Not in the Flexmonster panel; included so retries are deterministic. Admin Panel may hide it. |
| `CacheSizeLimit` | 100 | 0–100000 | Maps to `MemoryCacheOptions.SizeLimit`. Each `SetSize(1)` entry consumes one unit. 0 disables caching (limit 0 is treated as “do not cache”; the host still constructs a cache with limit 1 internally only if ASP.NET rejects 0 — implementation must verify and, if 0 is illegal, skip cache writes instead). Changing the limit is `restartRequired` because `MemoryCacheOptions` is read at registration. |
| `CacheEntryTtlMinutes` | 60 | 1–1440 | Replaces the hard-coded 60-minute absolute expiration. Applied on the next cache write. |
| `KeepDataOnRefresh` | true | bool | When true, reload builds a new row list and swaps the cache entry only after success. When false, the data-cache entry is removed before reload, so concurrent pivot calls fail with `IndexUnavailable` until reload finishes. |

Eviction order is the default `IMemoryCache` compaction (least recently used once the size limit is exceeded). Phase 1 does not implement a custom eviction policy.

### 6.4 Database query settings

```csharp
public sealed class DatabaseQuerySettings
{
    public bool CommandTimeoutEnabled { get; set; } = false;
    public int CommandTimeLimitSeconds { get; set; } = 150;
}
```

| Property | Default | Validation | Behavior |
| --- | --- | --- | --- |
| `CommandTimeoutEnabled` | false | bool | When false, `CommandTimeout` is 0 (ADO.NET unlimited). |
| `CommandTimeLimitSeconds` | 150 | 0–86400 | Used only when timeout is enabled. 0 also means unlimited. |

Applies to connection open is not an ADO.NET command timeout. Phase 1 therefore applies:

- `DbCommand.CommandTimeout` to initialize-script execution and the index query.
- A linked `CancellationTokenSource` of the same duration around `OpenAsync` and `ExecuteReaderAsync`, so a dead server cannot ignore command timeout. The request-aborted token is always linked as well.

Timeout failure becomes `IndexLoadException` with code `CommandTimeout` after retries are exhausted.

### 6.5 HTTPS settings

```csharp
public sealed class HttpsSettings
{
    public bool Enabled { get; set; } = false;
    public HttpsCertificateSource Source { get; set; } = HttpsCertificateSource.None;
    public string? PfxPath { get; set; }
    public string? PfxPassword { get; set; }
    public string? PemCertificatePath { get; set; }
    public string? PemPrivateKeyPath { get; set; }
    public string? SubjectName { get; set; }
    public CertificateStoreLocation StoreLocation { get; set; } = CertificateStoreLocation.CurrentUser;
    public string StoreName { get; set; } = "My";
    public bool AllowInvalid { get; set; } = false;
    public HttpProtocols Protocols { get; set; } = HttpProtocols.Http1AndHttp2;
}

public enum HttpsCertificateSource
{
    None = 0,
    Pfx = 1,
    Pem = 2,
    SubjectStore = 3
}
```

| Property | Default |
| --- | --- |
| Enabled | false |
| Source | None |
| Protocols | HTTP/1.1 and HTTP/2 |
| AllowInvalid | false |
| StoreLocation | CurrentUser |
| StoreName | My |

Rules:

- `Enabled: false` means do not call `ListenAnyIP` with HTTPS options and do not enable HTTPS redirection. HTTP remains available on `General.Port` or the host URL.
- `Enabled: true` requires `Source != None` and the fields for that source.
- PFX path and PEM paths must resolve inside `ContentRoot/certificates` or be absolute paths that exist. Relative paths are rooted at content root, then checked so they cannot escape via `..`.
- `PfxPassword` is write-only. GET configuration returns `"pfxPasswordSet": true/false` and never the password. The stored file may contain the password because that is how the process must restart; file permissions are an operator concern. Logs must not print it.
- Subject-store lookup uses `X509Store`, finds by subject distinguished name, and requires a private key. Multiple matches are an error, not “first certificate”.
- `AllowInvalid: true` skips chain and expiry checks. It does not skip “certificate has a private key”.
- Protocol changes and certificate changes are `restartRequired`.
- Phase 1 does not generate certificates and does not install them into the OS store.

### 6.6 Security settings

```csharp
public sealed class SecuritySettings
{
    public bool BasicAuthEnabled { get; set; } = false;
    public List<string> AllowedOrigins { get; set; } = ["*"];
    public List<DataServerUser> Users { get; set; } = [];
}

public sealed class DataServerUser
{
    public string Username { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PasswordSalt { get; set; } = "";
    public int PasswordIterations { get; set; } = 100_000;
    public DateTime CreatedUtc { get; set; }
    public bool Enabled { get; set; } = true;
}
```

| Property | Default | Rules |
| --- | --- | --- |
| `BasicAuthEnabled` | false | When false, authentication middleware does not challenge. When true, at least one enabled user must exist or startup fails. |
| `AllowedOrigins` | `["*"]` | `*` must be the only entry if present. Other entries must be absolute `http` or `https` origins without a path. Empty list means deny all browser origins (server-to-server calls with no `Origin` header still proceed). |
| `Users` | empty | Usernames unique, 1–64 chars, `[A-Za-z0-9._@-]+`. Username immutable. Password never stored. Disabled users fail authentication. |

Password hashing:

- Algorithm: PBKDF2-HMAC-SHA256 via `Rfc2898DeriveBytes`.
- Salt: 16 random bytes, stored as base64.
- Iterations: 100000 default, minimum accepted on verify is 100000.
- Hash: 32 bytes, stored as base64.
- Verification uses `CryptographicOperations.FixedTimeEquals`.
- BCrypt and Argon2 are acceptable later; Phase 1 standardizes on PBKDF2 so no extra package is required. The hash record includes algorithm id `PBKDF2-SHA256` in `PasswordHash` prefix: `PBKDF2-SHA256$<iterations>$<salt>$<hash>` as a single string if a one-field store is preferred. The split fields above are the canonical model; the config store may serialize them as one string in JSON to keep `appsettings.json` shorter. Spec requirement is: no reversible password, constant-time compare, unique salt per user.

Basic authentication:

- Scheme name: `DataServerBasic`.
- Header: `Authorization: Basic base64(username:password)`.
- Username comparison is ordinal case-sensitive.
- Failure: 401, `WWW-Authenticate: Basic realm="DataServer"`, body code `Unauthorized`.
- Do not disclose whether the user exists.
- Lockout and rate limiting are out of scope. Log warning on failure with username only.

CORS:

- Policy name: `DataServerCors`.
- Methods and headers remain any, matching current pivot clients that POST JSON.
- `AllowCredentials` stays false. Basic auth does not require credentialed CORS if the client sends `Authorization` as a non-wildcard header, which `AllowAnyHeader` already permits.
- Changing origins refreshes the policy on the next request via `IOptionsMonitor`. If the ASP.NET CORS policy is snapshotted at startup, the implementation must register a custom `ICorsPolicyProvider` that reads options per request. That is part of the design, not an optional refinement.

### 6.7 Report sharing settings

```csharp
public sealed class ReportSharingSettings
{
    public bool EndpointEnabled { get; set; } = false;
    public string StoragePath { get; set; } = "App_Data/reports";
    public int ReportExpirationMinutes { get; set; } = 0;
    public int MaxFolderSizeMb { get; set; } = 0;
    public bool AutoDelete { get; set; } = false;
    public int MaxReportBytes { get; set; } = 1_048_576;
}
```

| Property | Default | Meaning |
| --- | --- | --- |
| `EndpointEnabled` | false | When false, report routes return 404 `ReportSharingDisabled` and the cleanup timer does not run. |
| `StoragePath` | `App_Data/reports` | Relative to content root unless absolute. Must resolve inside content root. |
| `ReportExpirationMinutes` | 0 | 0 means do not expire. Otherwise delete or reject reads after `createdUtc + expiration`. |
| `MaxFolderSizeMb` | 0 | 0 means unlimited. Otherwise measured as sum of report file lengths. |
| `AutoDelete` | false | When true, a 60-second background sweep deletes expired reports and, if still over quota, oldest reports until under quota. When false, expired reports are hidden from GET and new writes fail with `ReportStorageFull` instead of deleting. |
| `MaxReportBytes` | 1048576 | Per-report cap. Not an Admin Panel field in the draft; required to keep the endpoint safe. |

Report identity is a server-generated 128-bit hex string. Clients do not choose paths.

### 6.8 Index configuration

```csharp
public sealed class IndexDefinition
{
    public string Name { get; set; } = "";
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; } = true;
    public bool IsDefault { get; set; }
    public IndexSourceType SourceType { get; set; }
    public JsonIndexSource? Json { get; set; }
    public CsvIndexSource? Csv { get; set; }
    public DatabaseIndexSource? Database { get; set; }
}

public enum IndexSourceType
{
    Json = 0,
    Csv = 1,
    Database = 2
}

public sealed class JsonIndexSource
{
    public string Path { get; set; } = "";
}

public sealed class CsvIndexSource
{
    public string Path { get; set; } = "";
    public string Delimiter { get; set; } = ",";
}

public sealed class DatabaseIndexSource
{
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.Sqlite;
    public string? ConnectionString { get; set; }
    public string? DatabasePath { get; set; }
    public string? InitializationScript { get; set; }
    public string Query { get; set; } = "";
}

public enum DatabaseProvider
{
    Sqlite = 0,
    SqlServer = 1,
    PostgreSql = 2,
    MySql = 3,
    MariaDb = 4,
    Oracle = 5
}
```

Index name rules, aligned with the client example `Sales-Data`:

- Required, unique, case-sensitive.
- Length 1–64.
- Pattern: `^[A-Za-z][A-Za-z0-9_-]*$`.
- `Name` is the client `index` value. It is the dictionary key. There is no separate numeric id.
- `DisplayName` is optional UI text for a future panel. It is not used for lookup.

Source rules:

| Type | Required members | Loader |
| --- | --- | --- |
| Json | `Json.Path` | Existing JSON parser. Path relative to `DataSource/`. |
| Csv | `Csv.Path`; delimiter defaults to `,` | Existing CSV parser and delimiter guard. |
| Database + Sqlite | `Query`, and `DatabasePath` or `ConnectionString` | Existing SQLite loader. `InitializationScript` retained for the sample database bootstrap. |
| Database + other provider | `ConnectionString` and `Query` | Validated and stored. Load returns `ProviderNotAvailable` until a later phase registers a driver. |

Query guard for every provider:

- Trimmed text must start with `SELECT` or `WITH` (allowing a leading parenthesis).
- No `;` anywhere, which also rejects stacked statements.
- No SQL comments (`--`, `/*`).
- Maximum length 8 KB.
- The query is taken only from the index definition, never from `FetchData`.

Connection strings:

- Accepted only on database indexes.
- GET index API returns `connectionStringSet: true` and redacts the value to the provider plus a hash prefix, not the raw string.
- PUT/POST may send the raw string. Omitted connection string on update means “keep existing”, not “clear”, when the JSON property is absent. JSON `null` clears it.

Runtime status is not persisted:

```csharp
public sealed class IndexRuntimeStatus
{
    public string Name { get; set; } = "";
    public IndexState State { get; set; } = IndexState.NotLoaded;
    public DateTime? LastLoadedUtc { get; set; }
    public int? RowCount { get; set; }
    public string? LastErrorCode { get; set; }
    public string? LastErrorMessage { get; set; }
}

public enum IndexState
{
    NotLoaded,
    Loading,
    Ready,
    Error,
    Disabled,
    ProviderNotAvailable
}
```

### 6.9 Seeded configuration

Phase 1 `appsettings.json` must contain this seed so the sample works without an Admin Panel. `PivotDataSource` remains as a deprecated mirror of the default index for one release; the loader must not read it if `DataServer:Indexes` is non-empty.

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  "DataServer": {
    "General": {
      "Port": 5285,
      "MinimumLogLevel": "Info"
    },
    "DataStorage": {
      "RetryAttempts": 3,
      "RetryDelayMilliseconds": 200,
      "CacheSizeLimit": 100,
      "CacheEntryTtlMinutes": 60,
      "KeepDataOnRefresh": true
    },
    "DatabaseQuery": {
      "CommandTimeoutEnabled": false,
      "CommandTimeLimitSeconds": 150
    },
    "Https": {
      "Enabled": false,
      "Source": "None",
      "StoreLocation": "CurrentUser",
      "StoreName": "My",
      "AllowInvalid": false,
      "Protocols": "Http1AndHttp2"
    },
    "Security": {
      "BasicAuthEnabled": false,
      "AllowedOrigins": [ "*" ],
      "Users": []
    },
    "ReportSharing": {
      "EndpointEnabled": false,
      "StoragePath": "App_Data/reports",
      "ReportExpirationMinutes": 0,
      "MaxFolderSizeMb": 0,
      "AutoDelete": false,
      "MaxReportBytes": 1048576
    },
    "Indexes": [
      {
        "Name": "Sales-Data",
        "DisplayName": "Sales CSV",
        "Enabled": true,
        "IsDefault": true,
        "SourceType": "Csv",
        "Csv": { "Path": "sales.csv", "Delimiter": "," }
      },
      {
        "Name": "Sales-Json",
        "DisplayName": "Sales analysis JSON",
        "Enabled": true,
        "IsDefault": false,
        "SourceType": "Json",
        "Json": { "Path": "sales-analysis.json" }
      },
      {
        "Name": "Sales-Db",
        "DisplayName": "Sales SQLite",
        "Enabled": true,
        "IsDefault": false,
        "SourceType": "Database",
        "Database": {
          "Provider": "Sqlite",
          "DatabasePath": "sales.db",
          "InitializationScript": "sales.sql",
          "Query": "SELECT Region, Country, ItemType, SalesChannel, OrderDate, UnitsSold, TotalRevenue, TotalCost, TotalProfit FROM Sales"
        }
      }
    ]
  },
  "PivotDataSource": {
    "Type": "Csv",
    "JsonPath": "sales-analysis.json",
    "CsvPath": "sales.csv",
    "CsvDelimiter": ",",
    "DatabasePath": "sales.db",
    "InitializationScript": "sales.sql",
    "Query": "SELECT Region, Country, ItemType, SalesChannel, OrderDate, UnitsSold, TotalRevenue, TotalCost, TotalProfit FROM Sales"
  }
}
```

`Sales-Data` is the sample index name. It must match `Sample/pivot.js`.

### 6.10 Customization matrix

| Setting | appsettings.json | Environment variable | Config API | Live effect |
| --- | --- | --- | --- | --- |
| Port | yes | `DataServer__General__Port` | yes | restart |
| Minimum log level | yes | yes | yes | next log call |
| Retry / TTL / KeepDataOnRefresh | yes | yes | yes | next load |
| Cache size limit | yes | yes | yes | restart |
| Command timeout | yes | yes | yes | next query |
| HTTPS | yes | yes, but do not put PFX password in process lists if avoidable | yes | restart |
| Basic auth and users | yes | users should be file-managed, not env | yes | next request |
| CORS origins | yes | yes | yes | next request |
| Report sharing | yes | yes | yes | next request |
| Indexes | yes | impractical for lists | yes | next request; cache invalidated for that index |

Environment variables follow ASP.NET Core `__` hierarchy. They override the JSON file for reads. API updates write the JSON file and call `IConfigurationRoot.Reload()` if a reload token is available. An environment variable that overrides a key will mask the file value on next read; the API response must include `overriddenByEnvironment: ["DataServer:General:Port"]` when a key is non-file. Phase 1 may implement that list only for the General and Security sections if a full walk is expensive, but it must not silently claim the file value is active when an environment variable wins.

---

## 7. Data indexing, storage, and query workflow

### 7.1 What an index is

An index is a named binding from a client token to a server-side tabular source. It is not a search index and not a pre-aggregated cube. Pivot aggregation remains inside `Syncfusion.Pivot.Engine` after rows are loaded.

Phase 1 indexing steps:

1. Parse and validate `IndexDefinition` at startup and on every write.
2. Publish it in `IndexCatalog`.
3. On first pivot request, load rows, infer field names from the first row / CSV header / reader schema, cache the row list.
4. Build the pivot engine from those rows and the `FetchData` report.
5. On reload, repeat step 3 under the `KeepDataOnRefresh` rule and drop engine cache keys for that index only.

Field schema is implicit. Phase 1 does not persist column types in `appsettings.json`. A future panel can call `GET /api/dataserver/indexes/{name}/fields` to preview names after a load.

### 7.2 Cache keys

| Key | Value | Size | TTL |
| --- | --- | --- | --- |
| `dataserver:data:{indexName}` | `IReadOnlyList<ExpandoObject>` | 1 | `CacheEntryTtlMinutes` |
| `dataserver:engine:{indexName}:{fetchData.Hash}` | `EngineProperties` | 1 | same TTL |

The current keys `dataSource:{SourceIdentity}` and `engine:{SourceIdentity}:{Hash}` are retired. Including only `SourceIdentity` would still collide if two indexes pointed at the same file with different queries, and it would ignore the client-visible name.

Invalidation:

- Index update or delete removes both prefixes for that name.
- Reload removes engine keys immediately and replaces or removes the data key per `KeepDataOnRefresh`.
- Global config changes other than data storage do not flush pivot data.
- `CacheSizeLimit` eviction may drop either key. A missing data key forces reload. A missing engine key recomputes from cached rows.

### 7.3 Load workflow

```text
Resolve index
  → disabled? 409 IndexDisabled
  → provider unimplemented? 409 ProviderNotAvailable
  → cache hit? return rows
  → state = Loading
  → for attempt in 0..RetryAttempts
        try load
        on success: cache rows, state = Ready, return
        on InvalidData / query-guard: state = Error, do not retry, throw
        on IO / timeout / db connection: delay, retry
  → state = Error, throw IndexLoadException
```

Concurrent loads for the same index share one in-flight `Task` (`SemaphoreSlim` per index, or `Lazy<Task>` slot). Callers await the same task. This prevents a thundering herd from opening SQLite write initialization many times.

### 7.4 JSON and CSV

Behavior is the current `DynamicDataLoader` behavior, moved behind `IndexDataLoader` and pointed at the index path rather than `PivotDataSource:*` keys:

- JSON root must be an array of flat objects with identical field order.
- CSV first row is the header. Delimiter is one character, not quote or newline.
- Empty files fail with `EmptyData`.
- Paths must stay inside `ContentRoot/DataSource`.

### 7.5 Database workflow

```text
Read DatabaseIndexSource
  → validate query guard
  → if provider != Sqlite: status ProviderNotAvailable, stop
  → resolve DatabasePath inside DataSource/ if path is set
  → if file missing and InitializationScript set: create and run script
       (script execution is the only write; connection then reopened read-only)
  → if ConnectionString set: use it only for Sqlite in Phase 1
       and reject keys that enable write modes other than the bootstrap path
  → set CommandTimeout from DatabaseQuery settings
  → ExecuteReader
  → map columns to ExpandoObject using reader.GetName
  → reject duplicate or empty column names
  → reject zero rows with EmptyData
```

External provider seam:

```csharp
public interface IDatabaseProvider
{
    DatabaseProvider Provider { get; }
    Task<IReadOnlyList<ExpandoObject>> ExecuteAsync(
        DatabaseIndexSource source,
        DatabaseQuerySettings querySettings,
        CancellationToken cancellationToken);
}
```

Phase 1 registers only `SqliteDatabaseProvider`. The catalog checks registration before load. Adding Npgsql or `Microsoft.Data.SqlClient` is a later phase and must not require an API change.

Connection-string examples to document, not to embed as secrets:

| Provider | Shape the later phase will accept |
| --- | --- |
| SqlServer | `Server=...;Database=...;User Id=...;Password=...;Encrypt=true` |
| PostgreSql | `Host=...;Database=...;Username=...;Password=...` |
| MySql / MariaDb | `Server=...;Database=...;User=...;Password=...` |
| Oracle | provider-specific data source string |

Phase 1 stores these strings and does not open them.

### 7.6 Client index binding

The Pivot Table does not have a first-class `index` property on `dataSourceSettings` in the current sample, and `FetchData` may ignore unknown members depending on Newtonsoft settings. Phase 1 therefore specifies two client injection points. Both are required in the specification; the sample implements the sample-level one.

**Sample-level (required for acceptance):** `Sample/pivot.js` sets `index` and injects it into every engine request.

```javascript
var dataServer = {
    url: "http://localhost:5285/api/pivot/post",
    index: "Sales-Data"
};

var pivotGridObj = new ej.pivotview.PivotView({
    dataSourceSettings: {
        url: dataServer.url,
        mode: "Server",
        index: dataServer.index,
        expandAll: false,
        rows: [{ name: "Country" }],
        columns: [{ name: "ItemType" }],
        values: [{ name: "UnitsSold" }, { name: "TotalProfit" }],
        filters: [{ name: "Region" }]
    },
    enableVirtualization: true,
    beforeServiceInvoke: function (args) {
        if (args.request && typeof args.request === "string") {
            var body = JSON.parse(args.request);
            body.index = dataServer.index;
            args.request = JSON.stringify(body);
        }
    }
});
```

Notes for implementers:

- Field names above match `sales.csv` (`Region`, `Country`, `ItemType`, `UnitsSold`, `TotalProfit`). The current sample’s `ProductID` / `Year` / `Sold` / `Price` fields belong to the unused virtual `PivotViewData` generator, not to the CSV loader. Leaving them would make a successful index load render an empty pivot. The sample change is part of Phase 1 acceptance.
- `beforeServiceInvoke` is the supported Syncfusion hook to amend the POST body. If the installed script version does not raise it, the source-level hook below is the alternative, and the sample must use whichever the referenced CDN build actually fires. The specification requires the index to be present on the wire either way.
- `dataSourceSettings.index` is still set so a future Pivot Table build can forward it without the event. The server must not depend on that property existing inside `FetchData`.

**Source-level (document in sample comments, implement only if the event is absent):** a one-line adapter beside the sample, not inside `ej2.min.js`:

```javascript
function withIndex(url, index) {
    var original = ej.base.Ajax.prototype.send;
    ej.base.Ajax.prototype.send = function () {
        if (this.url === url && this.data) {
            var body = JSON.parse(this.data);
            body.index = index;
            this.data = JSON.stringify(body);
        }
        return original.apply(this, arguments);
    };
}
```

Patching the CDN bundle is forbidden. The adapter lives in `Sample/pivot.js`.

### 7.7 Server extraction of `index`

```csharp
private static string? ReadIndex(object args)
{
    JObject body = JObject.Parse(args.ToString() ?? "{}");
    return body.Value<string>("index");
}
```

Then deserialize `FetchData` exactly as today. Do not fail if `FetchData` has no `Index` property.

---

## 8. API contracts

Base path for management APIs: `/api/dataserver`. Pivot path stays `/api/pivot/post` so existing clients and the user’s example URL keep working.

Success JSON uses camelCase. Error JSON:

```json
{
  "code": "IndexNotFound",
  "message": "No index named 'Sales-Data' is configured.",
  "details": { "index": "Sales-Data" }
}
```

| HTTP | When |
| --- | --- |
| 400 | Validation, missing index on a multi-index server, malformed JSON |
| 401 | Basic auth failed or missing |
| 404 | Unknown index, unknown user, report sharing disabled, unknown report |
| 409 | Index disabled, index loading failed, storage full, name conflict |
| 413 | Report larger than `MaxReportBytes` |
| 500 | Unhandled exception, code `Unhandled` |
| 501 | Database provider not registered |

### 8.1 Pivot endpoint

`POST /api/pivot/post`

Request content type: `application/json`.

Required existing body: a `FetchData` payload (`action`, `hash`, engine properties). The server already accepts this as `object` and deserializes it.

Added property:

| Field | Type | Required | Description |
| --- | --- | --- | --- |
| `index` | string | conditional | Index `Name`. Required unless exactly one index is `IsDefault`. |

Example:

```json
{
  "action": "onPivotUpdate",
  "hash": "<engine hash>",
  "index": "Sales-Data"
}
```

Response: unchanged engine payload (`GetSerializedPivotValues`, member dictionary, raw data, or export file result).

Errors use the error envelope only when the failure happens before the engine returns. Engine exceptions map to 500 `PivotEngineFailed` with the exception message sanitized (no connection strings).

### 8.2 Health

`GET /api/dataserver/health`

Anonymous even when basic auth is on, so a process manager can probe it.

```json
{
  "status": "ok",
  "version": "1.0.0",
  "indexCount": 3,
  "readyIndexCount": 1,
  "restartRequired": false
}
```

`status` is `ok` or `degraded`. Degraded means at least one enabled index is in `Error`. It does not mean “fail the probe”; process managers should use HTTP 200 unless the host cannot read configuration, in which case HTTP 500.

### 8.3 Configuration

`GET /api/dataserver/config`

Returns the full options graph with secrets redacted:

- `https.pfxPassword` omitted; `https.pfxPasswordSet` boolean.
- `security.users[].passwordHash` omitted; `hasPassword: true`.
- `indexes[].database.connectionString` omitted; `connectionStringSet` boolean.
- Adds `restartRequired` boolean and `restartReasons` string array.

`GET /api/dataserver/config/{section}`

`section` is one of: `general`, `dataStorage`, `databaseQuery`, `https`, `security`, `reportSharing`, `indexes`.

`PUT /api/dataserver/config/{section}`

Body is that section’s object. Replaces the section. For `indexes`, prefer the index routes; PUT of the whole array is allowed and is atomic.

Response:

```json
{
  "saved": true,
  "restartRequired": true,
  "restartReasons": ["General.Port", "Https.Enabled"]
}
```

Validation failure is 400 with `details.errors[]` of `{ "path": "general.port", "message": "Port must be between 1 and 65535." }`. The previous file is unchanged.

`PUT /api/dataserver/config` replaces all sections. Omitted sections reset to defaults. This is dangerous; the Admin Panel should use section PUT. Phase 1 still implements full replace because the draft required `PUT /api/config`.

### 8.4 Users

| Method | Path | Body | Result |
| --- | --- | --- | --- |
| GET | `/api/dataserver/security/users` | — | `{ "users": [ { "username", "createdUtc", "enabled" } ] }` |
| POST | `/api/dataserver/security/users` | `{ "username", "password" }` | 201 user without password |
| PUT | `/api/dataserver/security/users/{username}` | `{ "password"?, "enabled"? }` | 200. Username in the URL is not changed. Body `username` if present must match or 400. |
| DELETE | `/api/dataserver/security/users/{username}` | — | 204. Refuses to delete the last enabled user while basic auth is on (409 `LastUser`). |

Password rules on write: 8–128 characters, no leading or trailing whitespace requirement beyond trimming being forbidden (password is taken exactly). At least one letter and one digit. This is a server policy so an empty Admin Panel cannot store blank passwords later.

### 8.5 Indexes

| Method | Path | Purpose |
| --- | --- | --- |
| GET | `/api/dataserver/indexes` | List definitions (redacted) plus runtime status |
| GET | `/api/dataserver/indexes/{name}` | One definition plus status |
| POST | `/api/dataserver/indexes` | Create. 409 if name exists |
| PUT | `/api/dataserver/indexes/{name}` | Replace definition. Name in body must match URL |
| DELETE | `/api/dataserver/indexes/{name}` | Remove definition and cache keys |
| POST | `/api/dataserver/indexes/{name}/reload` | Reload rows now. 202 with status object |
| GET | `/api/dataserver/indexes/{name}/fields` | Field names and CLR type names after a load |

Create body example:

```json
{
  "name": "Sales-Data",
  "displayName": "Sales CSV",
  "enabled": true,
  "isDefault": true,
  "sourceType": "Csv",
  "csv": { "path": "sales.csv", "delimiter": "," }
}
```

Setting `isDefault: true` clears `isDefault` on every other index in the same atomic write.

Reload response:

```json
{
  "name": "Sales-Data",
  "state": "Ready",
  "rowCount": 1000,
  "lastLoadedUtc": "2026-04-15T12:00:00Z"
}
```

### 8.6 Report sharing

Active only when `ReportSharing.EndpointEnabled` is true.

| Method | Path | Behavior |
| --- | --- | --- |
| POST | `/api/dataserver/reports` | Body is raw JSON report, max `MaxReportBytes`. Returns `{ "id", "createdUtc", "expiresUtc" }`. `expiresUtc` is null when expiration is 0. |
| GET | `/api/dataserver/reports/{id}` | Returns the stored JSON. 404 if missing, expired, or id fails the hex pattern. |
| DELETE | `/api/dataserver/reports/{id}` | 204 or 404 |

`id` must match `^[a-f0-9]{32}$`. Any other id is 404 and is not interpolated into a path.

Stored file: `{StoragePath}/{id}.json` plus `{id}.meta.json` containing `createdUtc` and `byteLength`. Writes use the same temp-file-and-replace pattern.

Quota check and write hold a process-local lock. Over quota with `AutoDelete: false` returns 409 `ReportStorageFull`. Over quota with `AutoDelete: true` deletes oldest until the new report fits, then writes. If one report cannot fit even in an empty folder, return 413.

### 8.7 Compatibility aliases

The draft used `/api/config` and `/api/indexes`. Phase 1 also maps these aliases to the canonical routes so a panel built from the draft still works:

| Alias | Canonical |
| --- | --- |
| `GET /api/config` | `GET /api/dataserver/config` |
| `GET /api/config/{section}` | `GET /api/dataserver/config/{section}` |
| `PUT /api/config` | `PUT /api/dataserver/config` |
| `/api/security/users` | `/api/dataserver/security/users` |
| `/api/indexes` | `/api/dataserver/indexes` |

Aliases are thin route attributes on the same actions, not a second controller. They are deprecated in the OpenAPI description.

### 8.8 OpenAPI

Existing Swashbuckle registration stays. New controllers must appear in the Development swagger document. Pivot `POST` remains loosely typed (`object`) because `FetchData` is an external model; document the `index` property in an XML comment / operation description rather than replacing the engine schema.

---

## 9. Workflows

### 9.1 Startup

```text
Build configuration
  → bind DataServerOptions (defaults if section missing)
  → validate options
       fatal: HTTPS enabled without readable certificate, port out of range,
              basic auth on with zero enabled users, report path escapes content root
       non-fatal: bad index definitions marked Error
  → configure Kestrel HTTPS only if enabled and host URLs do not already bind TLS
  → configure memory cache size
  → configure CORS provider and optional basic auth
  → build IndexCatalog
  → do not preload indexes (first request loads, so a down database cannot block boot)
  → if report auto-delete: start PeriodicTimer 60s
  → run
```

### 9.2 Configuration update

```text
Admin or operator
  → PUT section
  → authenticate if basic auth on
  → validate candidate graph (current graph with section replaced)
  → if invalid: 400, file untouched
  → write appsettings.json via temp file + File.Replace
  → update IOptionsMonitor snapshot / reload configuration
  → if section is indexes: rebuild catalog, invalidate caches for changed names
  → if CORS: next request sees new origins
  → if port or HTTPS: set restartRequired flag consumed by health and PUT response
  → log Information "Configuration section {Section} updated"
```

### 9.3 Index reload

```text
POST /indexes/{name}/reload
  → unknown: 404
  → disabled: 409
  → if KeepDataOnRefresh: load into a side buffer
       success: swap data cache, remove engine keys, state Ready
       failure: keep old data cache, state remains Ready if old data exists else Error
  → if not KeepDataOnRefresh: remove data cache, state Loading, load, state Ready or Error
  → concurrent pivot requests during swap either see old or new rows, never a torn list
     (swap is reference replacement of an immutable list)
```

### 9.4 Report cleanup

```text
every 60s if EndpointEnabled and (AutoDelete or expiration > 0)
  → if not AutoDelete and expiration is 0: timer not started
  → if AutoDelete is false: timer only deletes expired when expiration > 0
  → if AutoDelete is true: delete expired, then oldest, until folder <= MaxFolderSizeMb
  → failures logged at Error, timer continues
```

### 9.5 Authentication

```text
request
  → health path: skip
  → BasicAuthEnabled false: skip
  → no Authorization header: 401
  → scheme not Basic: 401
  → decode fails: 401
  → user missing, disabled, or hash mismatch: 401
  → success: HttpContext.User name set to username
```

Phase 1 does not issue cookies or JWTs. The future Admin Panel uses the same Basic scheme or sits behind a reverse proxy. Session login is out of scope.

---

## 10. HTTPS and security configuration details

### 10.1 Kestrel application

Implementation target in `Program.cs`:

- Read `DataServer:Https` and `DataServer:General:Port` before `builder.Build()` only for the certificate callback registration.
- Prefer `builder.WebHost.ConfigureKestrel` when `Https.Enabled` is true **and** `ASPNETCORE_URLS` is empty. If launchSettings already supplies URLs, do not call `Listen` again (double bind). Log Warning that launch profile URLs override `General.Port`.
- Certificate selection:
  - PFX: `new X509Certificate2(path, password, X509KeyStorageFlags.EphemeralKeySet)`.
  - PEM: `X509Certificate2.CreateFromPemFile(certPath, keyPath)`.
  - Store: open `StoreLocation` / `StoreName`, find subject, require exactly one with private key.
- `AllowInvalid` maps to skipping `X509Chain.Build` failure. Expired certificates are rejected when `AllowInvalid` is false.
- Protocols map to `KestrelServerOptions.ConfigureEndpointDefaults` `listenOptions.Protocols`.

Changing certificates at runtime is not attempted. The PUT API saves the paths and returns `restartRequired`.

### 10.2 Threats Phase 1 must close

| Threat | Control |
| --- | --- |
| Client selects another tenant’s file by sending a path | Client sends an index name only. Paths exist only in server configuration. |
| Index path `..\secrets.json` | `Path.GetFullPath` + relative-path escape check, same as current `ResolveDataPath`. |
| Client-supplied SQL | Query is not read from the request. Guard rejects stacked statements. |
| Report id `../../web.config` | Id allow-list before path join. |
| Password in GET `/config` | Redaction rules in §8.3. |
| Password in logs | Logger helper that strips known secret property names. |
| CORS reflection of any origin | Explicit list unless the single configured value is `*`. |
| Basic auth disabled by accident in production | Not auto-detected. Health payload includes `basicAuthEnabled` so operators can see it. A warning is logged at startup if it is false. |
| CSRF on config POST | Phase 1 APIs are header-authenticated, not cookie-authenticated, so browser CSRF does not carry credentials unless the operator enables `AllowAnyOrigin` with a cookie scheme. Do not add cookies. Keep `AllowCredentials` false. |
| Swagger left exposed | Remains Development-only. |

### 10.3 Secrets that may exist on disk

`appsettings.json` may contain a PFX password and database connection strings because the process must read them to honor the Admin Panel model. Mitigations inside Phase 1:

- API redaction.
- Log redaction.
- Document that production should use environment variables or user secrets for `DataServer__Https__PfxPassword` and connection strings.
- Do not copy those values into the sample, README, or exception messages.

Phase 1 does not integrate a secret manager.

---

## 11. Logging, monitoring, and validation

### 11.1 Categories

| Category | Events |
| --- | --- |
| `PivotController.DataServer.Configuration` | load, update, validation failure, restart required |
| `PivotController.DataServer.Authentication` | success Information, failure Warning |
| `PivotController.DataServer.Indexing` | load start/end, retry, reload, cache hit/miss |
| `PivotController.DataServer.Database` | provider, timeout, row count; never the query text if it might contain literals from a connection string, and never the connection string |
| `PivotController.DataServer.Reports` | save, read, delete, quota |
| `PivotController.DataServer.Https` | certificate source type, thumbprint, not password |

Minimum level filter compares the event level to `General.MinimumLogLevel` before write. ASP.NET hosting logs stay on the existing appsettings `Logging` section so framework noise does not have to adopt Info/Warn/Error/Fatal exclusively.

### 11.2 Required log fields

Structured properties: `Index`, `Action`, `ElapsedMs`, `CacheHit`, `Attempt`, `Section`. No `Password`, `PfxPassword`, `ConnectionString`, `Authorization`.

### 11.3 Monitoring in Phase 1

No dashboard. The only machine-readable signals are:

- `GET /api/dataserver/health`
- `GET /api/dataserver/indexes` runtime status
- console logs

A future monitoring UI must be able to use those two GETs without further server work. That is why status is on the index DTO now.

### 11.4 Validation catalog

Implement `IValidateOptions<DataServerOptions>` plus per-index validation that does not fail the host.

| Code | Condition |
| --- | --- |
| `PortOutOfRange` | port < 1 or > 65535 |
| `LogLevelInvalid` | not in the four-level set |
| `RetryAttemptsOutOfRange` | < 0 or > 30 |
| `CacheSizeInvalid` | < 0 |
| `CommandTimeLimitInvalid` | < 0 |
| `OriginInvalid` | not `*` and not an absolute http(s) origin |
| `CertificateMissing` | HTTPS on and source file/store entry missing |
| `IndexNameInvalid` | pattern mismatch |
| `IndexNameDuplicate` | same name twice |
| `MultipleDefaults` | more than one `IsDefault` |
| `SourceMismatch` | `SourceType` is Csv but `Csv` is null, and the analogous cases |
| `PathEscape` | resolved path outside the allowed root |
| `DelimiterInvalid` | CSV delimiter rule |
| `QueryRejected` | query guard |
| `ProviderUnsupported` | stored, index status only, not a startup failure |
| `UsernameInvalid` | user name rules |
| `PasswordWeak` | write-time only |
| `ReportPathEscape` | storage path outside content root |

Startup uses this catalog. APIs return the same codes in `details.errors`.

---

## 12. Error handling

### 12.1 Exception mapping

| Exception | HTTP | Code |
| --- | --- | --- |
| `IndexNotFoundException` | 404 | `IndexNotFound` |
| `IndexUnavailableException` | 409 | `IndexUnavailable` / `IndexDisabled` / `ProviderNotAvailable` |
| `IndexLoadException` | 409 | `IndexLoadFailed` or `CommandTimeout` or `EmptyData` |
| `ConfigurationValidationException` | 400 | `ConfigurationInvalid` |
| `UnauthorizedAccessException` from auth handler | 401 | `Unauthorized` |
| `ReportSharingDisabledException` | 404 | `ReportSharingDisabled` |
| `ReportNotFoundException` | 404 | `ReportNotFound` |
| `ReportStorageFullException` | 409 | `ReportStorageFull` |
| `BadHttpRequestException` | 400 | `MalformedRequest` |
| any other | 500 | `Unhandled` |

The existing `AddProblemDetails` + `UseExceptionHandler` pipeline stays. The handler writes the envelope above with `Content-Type: application/json` instead of an HTML error page. It does not include stack traces unless `ASPNETCORE_ENVIRONMENT` is Development, and even then stack traces go to the log, not the response. Development convenience does not justify returning connection strings.

### 12.2 Pivot-specific failures

If the index loads and the engine throws because the report names a field that is not in the data, return 400 `UnknownField` with the field name. Do not wrap that as 500. This is detectable when the engine exception message contains the missing member; if the engine type does not allow reliable detection, return 500 `PivotEngineFailed` and add a test that records the actual exception. Do not guess field names.

### 12.3 Partial failure

A failed index does not affect another index’s cache or serving. A failed configuration write does not change memory state. A failed reload with `KeepDataOnRefresh: true` does not evict the last good snapshot.

---

## 13. Controller modifications (implementation checklist)

This section is the file-level delta. It is specification, not the implementation.

### 13.1 `Program.cs`

- Bind `DataServerOptions`.
- Replace the hard-coded CORS lambda with `ICorsPolicyProvider` reading `Security.AllowedOrigins`.
- Replace hard-coded `SizeLimit = 100` with `DataStorage.CacheSizeLimit`.
- Register config store, catalog, loader, report store, password hasher.
- Add authentication only when `BasicAuthEnabled` is true at startup. If the flag can change live, register the scheme always and make the handler succeed-all when the flag is false, so toggling does not require a restart. Prefer the always-registered handler.
- Remove unconditional `UseHttpsRedirection`.
- Remove scoped `DynamicDataLoader` registration after the controller no longer needs it.
- Keep Swagger, controllers, and problem details.

### 13.2 `Controllers/PivotController.cs`

- Inject `IndexResolver` and `IMemoryCache`.
- In `Post`, read `index` from the raw body, resolve rows, then run the existing action switch.
- Change `GetData()` to `GetData(IndexDefinition index)`.
- Change cache keys as in §7.2.
- Use `CacheEntryTtlMinutes` instead of the literal 60.
- Keep export and member logic.
- Do not catch exceptions that the global handler maps, except where a pivot-specific code is required.

### 13.3 `DataSource/DynamicDataLoader.cs`

- Extract path, JSON, CSV, and SQLite methods so they accept an `IndexDefinition` and `DatabaseQuerySettings`.
- Keep the SELECT guard, header checks, and path confinement.
- Stop reading `PivotDataSource:*` inside the hot path.
- Leave a temporary adapter `FromLegacyConfiguration(IConfiguration)` that builds one `IndexDefinition` named `Sales-Data` if `DataServer:Indexes` is empty. This preserves current single-source deployments during upgrade.

### 13.4 New types

As listed in §5.2. No UI project. No EF Core. No new database for users or indexes; both live in `appsettings.json`.

### 13.5 `appsettings.json` and `appsettings.Development.json`

- Add the seed in §6.9 to `appsettings.json`.
- Do not duplicate indexes into the Development file unless a developer override is required. Development file may set `MinimumLogLevel` to `Info` only.

### 13.6 `Sample/pivot.js` and `Sample/pivot.html`

- Point `url` at `http://localhost:5285/api/pivot/post`.
- Set index `Sales-Data`.
- Bind CSV fields (`Country`, `ItemType`, `UnitsSold`, `TotalProfit`, `Region`) instead of the unused virtual product fields.
- Inject `index` on the POST body via `beforeServiceInvoke` or the sample-local Ajax adapter.
- Add a short comment showing the Flexmonster-equivalent shape and the source-level alternative.
- Do not add a second sample page.

### 13.7 `README.md`

Phase 1 implementation should update the run instructions to say the sample index is `Sales-Data` and the endpoint is `POST /api/pivot/post`. That README edit is in the implementation milestone, not in this specification-only task.

### 13.8 Explicitly untouched

- `DataSource/sales.csv`, `sales-analysis.json`, `sales.sql`, `sales.db` contents.
- `DataSource/DataSource.cs` legacy types, except a possible unused-code warning. Do not wire them back in.
- Export classes inside the engine package.
- `.github/workflows/gitleaks.yaml`. New sample config must not contain real secrets, so the workflow should stay green.

---

## 14. Default and customization catalog

| Section | Property | Default | Customizable by | Secret |
| --- | --- | --- | --- | --- |
| General | Port | 5285 | file, env, API | no |
| General | MinimumLogLevel | Info | file, env, API | no |
| DataStorage | RetryAttempts | 3 | file, env, API | no |
| DataStorage | RetryDelayMilliseconds | 200 | file, env, API | no |
| DataStorage | CacheSizeLimit | 100 | file, env, API | no |
| DataStorage | CacheEntryTtlMinutes | 60 | file, env, API | no |
| DataStorage | KeepDataOnRefresh | true | file, env, API | no |
| DatabaseQuery | CommandTimeoutEnabled | false | file, env, API | no |
| DatabaseQuery | CommandTimeLimitSeconds | 150 | file, env, API | no |
| Https | Enabled | false | file, env, API | no |
| Https | Source | None | file, env, API | no |
| Https | PfxPath | null | file, env, API | no |
| Https | PfxPassword | null | file, env, API | yes |
| Https | PemCertificatePath | null | file, env, API | no |
| Https | PemPrivateKeyPath | null | file, env, API | no |
| Https | SubjectName | null | file, env, API | no |
| Https | StoreLocation | CurrentUser | file, env, API | no |
| Https | StoreName | My | file, env, API | no |
| Https | AllowInvalid | false | file, env, API | no |
| Https | Protocols | Http1AndHttp2 | file, env, API | no |
| Security | BasicAuthEnabled | false | file, env, API | no |
| Security | AllowedOrigins | `["*"]` | file, env, API | no |
| Security | Users | empty | file, API | hash only |
| ReportSharing | EndpointEnabled | false | file, env, API | no |
| ReportSharing | StoragePath | App_Data/reports | file, env, API | no |
| ReportSharing | ReportExpirationMinutes | 0 (never) | file, env, API | no |
| ReportSharing | MaxFolderSizeMb | 0 (unlimited) | file, env, API | no |
| ReportSharing | AutoDelete | false | file, env, API | no |
| ReportSharing | MaxReportBytes | 1048576 | file, env, API | no |
| Indexes | seeded Sales-Data, Sales-Json, Sales-Db | as §6.9 | file, API | connection string yes |

---

## 15. Acceptance criteria

### 15.1 Configuration

- AC-1 Starting with the seeded `appsettings.json` binds every property to the documented default or seed.
- AC-2 Deleting the `DataServer` section still starts the host and synthesizes defaults plus a legacy `Sales-Data` index from `PivotDataSource` when that section exists.
- AC-3 `PUT /api/dataserver/config/general` with `{ "port": 9500, "minimumLogLevel": "Warn" }` persists to `appsettings.json` and returns `restartRequired: true`.
- AC-4 `PUT` with `{ "port": 0 }` returns 400 `PortOutOfRange` and does not modify the file.
- AC-5 GET config never contains `pfxPassword`, `passwordHash`, or a raw connection string.

### 15.2 Index and pivot

- AC-6 `POST /api/pivot/post` with `index: "Sales-Data"` and a valid engine body loads `sales.csv` and returns a pivot payload.
- AC-7 The same call with `index: "Sales-Json"` does not return CSV-only behavior; cache keys differ.
- AC-8 `index: "missing"` returns 404 `IndexNotFound` and creates no cache entry.
- AC-9 Omitting `index` uses `Sales-Data` because it is the only default.
- AC-10 After a second index is also marked default, omitting `index` returns 400 `IndexRequired`.
- AC-11 Reload with a temporarily unreadable file and `KeepDataOnRefresh: true` keeps serving the previous rows.
- AC-12 Reload with `KeepDataOnRefresh: false` removes cached rows before the attempt.
- AC-13 A database index whose query contains `;` is rejected at save time.
- AC-14 Command timeout enabled with limit 1 against a blocked SQLite query fails with `CommandTimeout` after the configured retries.
- AC-15 `CacheSizeLimit` is the memory-cache size limit observed at startup.

### 15.3 Security and HTTPS

- AC-16 With basic auth off, pivot POST succeeds without an Authorization header.
- AC-17 With basic auth on, missing credentials return 401 and a `WWW-Authenticate` header. Health still returns 200.
- AC-18 A created user’s password is not present in `appsettings.json`; a hash is.
- AC-19 Username cannot be changed via PUT.
- AC-20 `AllowedOrigins: ["https://example.com"]` does not emit `Access-Control-Allow-Origin` for `https://evil.example`.
- AC-21 `AllowedOrigins: ["*"]` preserves current allow-any behavior.
- AC-22 HTTPS disabled does not redirect `http://localhost:5285`.
- AC-23 HTTPS enabled with a missing PFX fails startup with `CertificateMissing`.
- AC-24 A valid PFX configuration is accepted by the validator. Live rebind is not required.

### 15.4 Report sharing

- AC-25 Endpoints return 404 `ReportSharingDisabled` when disabled.
- AC-26 When enabled, POST then GET round-trips JSON.
- AC-27 An id containing `..` returns 404 and does not create a file outside the store.
- AC-28 Expiration 1 minute hides the report after expiry.
- AC-29 `MaxFolderSizeMb` with `AutoDelete: false` returns 409 once exceeded.
- AC-30 `AutoDelete: true` removes the oldest file and then accepts the write.

### 15.5 Client sample

- AC-31 `Sample/pivot.js` contains `index: "Sales-Data"` and `url: "http://localhost:5285/api/pivot/post"`.
- AC-32 A captured POST body from the sample includes `"index":"Sales-Data"`.
- AC-33 Sample rows/columns/values are fields that exist on `sales.csv`.
- AC-34 The sample file comments show the source-level alternative and the Flexmonster-equivalent object, so a Pivot Table owner knows where to set the index if they are not using the sample page.

### 15.6 Logging

- AC-35 `MinimumLogLevel: Error` suppresses Information index-load logs and still writes load failures.
- AC-36 A failed basic-auth attempt does not log the password.

---

## 16. Implementation milestones

Specification approval is milestone 0 and is this document. Implementation is subsequent work.

| Milestone | Deliverable | Exit criteria |
| --- | --- | --- |
| M1 | Options classes, validator, seeded `appsettings.json`, options binding | AC-1, AC-2, unit tests for every validation code |
| M2 | `IndexCatalog`, `IndexDataLoader` refactor, cache key change, pivot `index` read | AC-6 through AC-13 |
| M3 | Config store atomic write and config/index/user APIs with redaction | AC-3, AC-4, AC-5, AC-18, AC-19 |
| M4 | CORS provider, basic auth handler, HTTPS startup configurator, conditional HTTPS redirection | AC-16, AC-17, AC-20 through AC-24 |
| M5 | Report share store and endpoints | AC-25 through AC-30 |
| M6 | Sample `pivot.js` index binding and CSV field layout | AC-31 through AC-34 |
| M7 | Logging filter and exception envelope | AC-35, AC-36, error-code table covered by tests |

Suggested PR slices are M1+M2, then M3, then M4+M5, then M6+M7, so the pivot index path lands before management APIs.

### 16.1 Test plan to accompany implementation

No test project exists today. Implementation adds `PivotController.Tests` (xUnit) covering:

- options validator table
- path escape
- query guard
- config file replace does not truncate on failure (simulate by validating before write)
- index cache isolation
- basic auth success and failure
- report id rejection

One WebApplicationFactory test covers `POST /api/pivot/post` against `Sales-Data` with a minimal `FetchData` body. If constructing `FetchData` is impractical, the test posts the JSON the sample sends and asserts HTTP 200 and a non-empty body. That test is the integration gate for M2.

### 16.2 Verification commands (for the implementation phase, not this task)

- `dotnet test` on `PivotController.Tests`
- `dotnet build PivotController/PivotController.csproj`
- Manual: run the http profile, open the sample, confirm the POST URL and index in the request payload

---

## 17. Phase boundary and follow-on work

Phase 1 is done when the acceptance criteria pass and the Admin Panel can be written against §8 without further controller redesign.

Later phases, not designed in detail here:

| Phase | Content |
| --- | --- |
| Phase 2 | Admin Panel UI that reads and writes the Phase 1 APIs. No new settings without a spec revision. |
| Phase 3 | Monitoring and diagnostics UI on top of health and index status. |
| Phase 4 | SQL Server, PostgreSQL, MySQL, MariaDB, and Oracle providers behind `IDatabaseProvider`. |
| Phase 5 | Optional live port/certificate restart, distributed cache, and secret-manager integration. |

Adding a setting in the Admin Panel before it exists in `DataServerOptions` is a spec violation.

---

## 18. Decisions

| Decision | Choice | Why | Rejected alternative |
| --- | --- | --- | --- |
| Persistence | `appsettings.json` `DataServer` section | Matches Flexmonster’s operational model and the user’s indexing note. One file operators already have. | Separate `config.json` plus `indexes.json`, which the draft suggested. That splits the source of truth and fights ASP.NET configuration. |
| Pivot URL | Keep `POST /api/pivot/post` | Already implemented and cited by the user as the endpoint to expose. | New Flexmonster-compatible `/` API on port 9500. |
| Default port | 5285 | Existing launch profile and the user’s example URL. | 9500, which would break the sample profile for visual similarity only. |
| Index identity | `Name` string | Client contract is `index: "Sales-Data"`. | Separate GUID `Id` plus display name. Extra indirection the client does not send. |
| Client injection | Sample-level `beforeServiceInvoke`, source-level adapter documented | `FetchData` cannot be modified inside the engine package from this repo. | Forking `Syncfusion.Pivot.Engine`. |
| External databases | Model and API now, execution later | Phase 1 must expose the Admin Panel settings without pretending untested drivers are done. | Pulling five provider packages into Phase 1 without a UI or tests. |
| Password hash | PBKDF2-SHA256 | In-box, no new package, constant-time compare. | BCrypt package, Argon2 native dependency. |
| Cache unit | Entry count, size 1 | Current code already works this way. | Byte-accurate cache limit, which `IMemoryCache` does not provide without a custom measurer. |
| HTTPS live reload | Persist only | Kestrel bind is process lifetime. | Unsupported recycle-from-request, which drops in-flight pivots. |
| Legacy `PivotDataSource` | Read only if no indexes are configured | Keeps current deployments working. | Silent dual-read that can disagree with `Sales-Data`. |

---

## 19. Glossary

| Term | Meaning |
| --- | --- |
| Data server | This Phase 1 controller role, not a separate product process. |
| Index | Named server-side data binding selected by the client. |
| Admin Panel | Future UI. Phase 1 exposes its settings and APIs only. |
| Engine | `Syncfusion.Pivot.Engine.PivotEngine<ExpandoObject>`. |
| Restart required | Saved configuration that Kestrel will not apply until the process starts again. |

---

## 20. Traceability to the previous draft

| Draft section | This document |
| --- | --- |
| General Settings port and log level | §6.2, default port corrected to 5285 |
| Data storage retry, cache, keep-on-refresh | §6.3, plus TTL and retry delay |
| Database query timeout | §6.4 and §7.5 |
| HTTPS PFX, PEM, store, protocols | §6.5 and §10 |
| Security basic auth, users, CORS | §6.6 and §8.4 |
| Report sharing | §6.7 and §8.6 |
| Index JSON/CSV/SQL | §6.8; external providers specified but not executed |
| REST sketch | §8, canonical `/api/dataserver` plus draft aliases |
| Persistence `config.json` | Replaced by `appsettings.json` in §18 |
| Acceptance criteria | §15, made testable |
| Admin Panel excluded | §3.2, unchanged |

End of Phase 1 specification.
