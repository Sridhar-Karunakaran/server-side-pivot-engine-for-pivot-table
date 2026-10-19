# Syncfusion Pivot Data Server — Phase 1 Exploration

**Change Type:** Feature

**Date:** 2024-12-19

**Specification Reference:** Syncfusion_Pivot_Data_Server_Phase1_Specification.md (v2.1)

---

## Executive Summary

This exploration documents the backend implementation requirements for the Syncfusion Pivot Table Data Server (Phase 1), which parallels the Flexmonster Data Server architecture. The feature enables configuration-driven index management, multi-source data connectivity, security controls, HTTPS support, and report sharing capabilities—all managed server-side without requiring an Admin Panel UI in Phase 1.

---

## Current State Analysis

### Existing Architecture

**Repository:** `server-side-pivot-engine-for-pivot-table`

**Current Application Stack:**
- Framework: ASP.NET Core (net10.0)
- Primary Components:
  - `PivotController/Controllers/PivotController.cs` — handles pivot data requests
  - `PivotController/DataSource/DataSource.cs` — data provider implementations (JSON, CSV, DataTable)
  - `PivotController/Program.cs` — application startup and configuration
  - `Sample/pivot.js` — client-side pivot table integration

**Current Limitations:**
- Single, hardcoded data source per controller instance
- No index/named-dataset concept
- No configuration management API
- Limited security (no authentication, minimal CORS)
- No HTTPS certificate management
- No report sharing capability
- No audit logging or monitoring

---

## Requirements from Specification

### Phase 1 Scope

Phase 1 delivers **backend infrastructure only**, with these key objectives:

1. **Index Configuration** (Step 1 — foundational)
   - Named indexes declared in `appsettings.json`
   - Index lifecycle: create, edit, reload, enable/disable, delete
   - Support JSON, CSV, and multiple database sources
   - Indexes-tab parity with Flexmonster (edit all except source type)
   - Per-field mapping and CSV separator options
   - Test-connection endpoint for databases

2. **Configuration Management** (7 settings sections)
   - General Settings (port, log level, data folder)
   - Data Storage Settings (retry, cache, keep-on-refresh)
   - Database Query Settings (timeout configuration)
   - HTTPS Settings (certificate management)
   - Security Settings (basic auth, CORS)
   - Report Sharing Settings (storage, expiry, quota)
   - All configurable via REST API and JSON file

3. **Data Connectivity**
   - JSON file provider
   - CSV file provider with delimiter/separator options
   - Database provider abstraction (mandatory: SQLite; optional: SQL Server, PostgreSQL, MySQL, Oracle, MariaDB)

4. **Security & Authentication**
   - Basic Authentication support
   - User management (CRUD) with PBKDF2 hashing
   - CORS origin validation
   - Constant-time password comparison

5. **Advanced Features**
   - HTTPS support (PFX, PEM, Windows certificate store)
   - Report sharing with expiration and quota management
   - Structured logging with category-based organization
   - Retry policy with exponential backoff
   - Keep-data-on-refresh semantics
   - Hot-reload configuration file watching

6. **Client Integration**
   - Client passes `index` name on every pivot request
   - Backward compatibility: requests without index use `DefaultIndexId`
   - Single endpoint: `http://localhost:5285/api/pivot/post`

### Phase 1 Implementation Order (8 Steps)

| Step | Milestone | Key Deliverable |
|------|-----------|-----------------|
| 1 | **Index Configuration** | Named indexes, IndexManager, index-aware routing |
| 2 | General & Data Storage Settings | Configuration models and persistence |
| 3 | Database Query Settings | Timeout enforcement, IDatabaseQueryProvider |
| 4 | HTTPS Settings | Certificate resolution, Kestrel binding |
| 5 | Security Settings | User model, Basic Auth, CORS validation |
| 6 | Report Sharing Settings | ReportSharingService, expiry/quota worker |
| 7 | Persistence, Logging, APIs, Hot-Reload | Full configuration API, file watcher, exception handling |
| 8 | **Client-Side Sample** | Update Sample/pivot.js to send index (final step) |

---

## Architectural Overview

### High-Level Data Flow

```
Client (Sample/pivot.js)
  ↓ POST /api/pivot/post { index: "Sales-Data", action, ... }
  ↓
PivotController (index-aware routing)
  ├─ Extract FetchData.Index
  ├─ IndexManager.Resolve("Sales-Data")
  └─ Route to data provider
    ↓
Index Manager (cache hub)
  ├─ Resolve index metadata
  ├─ Get cached data (data:{index})
  ├─ Load on cache miss
  └─ Retry policy on transient failure
    ↓
Data Provider (pluggable)
  ├─ JsonDataProvider
  ├─ CsvDataProvider (with separator options)
  ├─ DatabaseDataProvider
  └─ IDatabaseQueryProvider (provider-specific)
    ↓
Data Source
  ├─ JSON files
  ├─ CSV files
  └─ Databases (SQLite, SQL Server, PostgreSQL, MySQL, Oracle, MariaDB)
```

### Layer Architecture

```
┌─────────────────────────────────────────────┐
│  HTTP Pipeline (Kestrel + HTTPS)            │
└──────────────┬──────────────────────────────┘
               ↓
┌─────────────────────────────────────────────┐
│  API Controllers                            │
│  ├─ PivotController                         │
│  ├─ ConfigController                        │
│  ├─ IndexController                         │
│  ├─ SecurityController                      │
│  └─ ReportsController                       │
└──────────────┬──────────────────────────────┘
               ↓
┌─────────────────────────────────────────────┐
│  Application Services Layer                 │
│  ├─ ConfigService + ConfigurationPersistence│
│  ├─ IndexManager (cache hub)                │
│  ├─ UserService                             │
│  ├─ ReportSharingService                    │
│  ├─ CertificateService                      │
│  └─ OriginValidator                         │
└──────────────┬──────────────────────────────┘
               ↓
┌─────────────────────────────────────────────┐
│  Data Access Layer                          │
│  ├─ IDataSourceProvider                     │
│  │  ├─ JsonDataProvider                     │
│  │  ├─ CsvDataProvider                      │
│  │  └─ DatabaseDataProvider                 │
│  ├─ IDatabaseQueryProvider                  │
│  │  └─ SqliteQueryProvider (+ optional)     │
│  └─ MemoryCache with SizeLimit              │
└──────────────┬──────────────────────────────┘
               ↓
┌─────────────────────────────────────────────┐
│  Logging & Persistence                      │
│  ├─ Console Logger                          │
│  ├─ Rolling JSON File Logger                │
│  ├─ Configuration Persistence (JSON files)  │
│  ├─ Index Management Store                  │
│  └─ User & Report Storage                   │
└─────────────────────────────────────────────┘
```

---

## Data Model Overview

### Index Definition
- `Index` (immutable, unique key; e.g., "Sales-Data")
- `Name` (display name)
- `SourceType` (immutable: Json, Csv, Database)
- `SourcePath` (for JSON/CSV)
- `DatabaseType`, `ConnectionStringName`, `Query` (for Database)
- CSV options: `CsvDelimiter`, `CsvDecimalSeparator`, `CsvThousandsSeparator`
- Mapping: `MappingEnabled`, `FieldMappings[]` (Number/String/Date per field)
- Auto-reload: `AutoReload`, `ReloadIntervalMinutes`
- Status: `Enabled` (disabled indexes not preloaded)

### Configuration Hierarchy
```
DataServerConfiguration (root)
├── ConfigVersion = "1.0"
├── DefaultIndexId = "default"
├── GeneralSettings
├── DataStorageSettings
├── DatabaseQuerySettings
├── HttpsSettings
├── SecuritySettings
├── ReportSharingSettings
└── Indexes[]
```

---

## Persistence Strategy

| Artifact | Location | Initial Source | Runtime Mgmt |
|----------|----------|-----------------|-------------|
| Indexes | `DataServer/indexes.json` | `appsettings.json` bootstrap | /api/indexes/* + file hot-reload |
| Configuration | `DataServer/config.json` | Defaults + `appsettings.json` | PUT /api/config + file hot-reload |
| Users | `DataServer/users.json` | Empty (admin created via API) | /api/security/users |
| Reports | `DataServer/reports/` | — | /api/reportsharing/* |

**Atomic writes:** serialize → temp file in same directory → rename (`File.Replace`)
**Corrupt files:** quarantined as `*.corrupt.{timestamp}`
**Secrets:** never persisted (connection strings, PFX passwords stored in `appsettings.json` or user-secrets)

---

## REST API Surface

### Core Endpoints

**Pivot Data Endpoint** (existing, enhanced):
- `POST /api/pivot/post` — accepts `index` field; routes to named index

**Configuration APIs:**
- `GET /api/config` — full configuration
- `GET /api/config/{section}` — single section
- `PUT /api/config` — partial merge-patch update

**Index Management APIs:**
- `GET /api/indexes` — list all indexes with status
- `GET /api/indexes/{index}` — index detail
- `POST /api/indexes` — create index
- `PUT /api/indexes/{index}` — edit index (all except sourceType)
- `POST /api/indexes/{index}/reload` — manual reload
- `POST /api/indexes/{index}/enable` — enable + load
- `POST /api/indexes/{index}/disable` — disable + evict
- `POST /api/indexes/{index}/test-connection` — test DB connection
- `DELETE /api/indexes/{index}` — delete + evict

**Security APIs:**
- `GET /api/security/users` — list users
- `POST /api/security/users` — create user
- `PUT /api/security/users/{username}` — update password
- `DELETE /api/security/users/{username}` — delete user

**Report Sharing APIs:**
- `POST /api/reportsharing/reports` — save report
- `GET /api/reportsharing/reports/{reportId}` — retrieve report
- `GET /api/reportsharing/reports` — list reports
- `DELETE /api/reportsharing/reports/{reportId}` — delete report

**Health Endpoint:**
- `GET /health` — liveness check

---

## Key Design Decisions from Specification

1. **Index Configuration First** — Index registration (Step 1) is foundational; all other features depend on named indexes
2. **Client-Supplied Index** — Every pivot request includes the index name; server routes accordingly
3. **Appsettings Bootstrap** — Indexes declared in `appsettings.json` at startup; persisted to `indexes.json` for runtime management
4. **Immutable Source Type** — Once an index is created, its source type cannot be changed (Flexmonster-like rule)
5. **Backward Compatibility** — Legacy `PivotDataSource` block auto-migrates to index "default"; clients without index field use DefaultIndexId
6. **Hot-Reload Configuration** — Runtime JSON file watcher applies changes without restart (except port, HTTPS, cache-sizing options)
7. **Keep-Data-on-Refresh** — Reload failures don't blank the display; old data served if new load fails
8. **Atomic Persistence** — All JSON writes use temp-file-rename pattern to prevent corruption
9. **Secrets Never Persisted** — Connection strings and certificates referenced by name, never stored in config.json

---

## Technology Stack

### Framework & Dependencies
- ASP.NET Core 10.0
- MemoryCache (built-in; sized by `DataStorage.CacheSizeLimit`)
- ProblemDetails (RFC 7807 error envelope)
- FileSystemWatcher (config file hot-reload)

### Database Providers (pluggable)
- **Mandatory:** SQLite (package reference already present)
- **Optional (reflection-registered if package present):**
  - SQL Server (System.Data.SqlClient)
  - PostgreSQL (Npgsql)
  - MySQL (MySqlConnector)
  - Oracle (Oracle.ManagedDataAccess.Core)
  - MariaDB (MySqlConnector variant)

### Cryptography & Security
- PBKDF2 (100,000 iterations, SHA-256 — built-in; no additional packages)
- X509Certificate2 (built-in)
- Kestrel HTTPS/TLS 1.2+ (built-in)

### Logging
- Console provider (built-in)
- Rolling file logger (custom implementation, JSON format)

---

## Files to Create/Modify

### Modified Files
1. `PivotController/Program.cs` — Bootstrap, DI, HTTPS, logging, cache sizing
2. `PivotController/Controllers/PivotController.cs` — Index-aware routing
3. `PivotController/appsettings.json` — DataServer section with Indexes
4. `Sample/pivot.js` — Pass index on every request (final step)

### New Directories & Files (~40 new files)
```
PivotController/Configuration/
  Models/
  Validation/
  Services/
  Controllers/
PivotController/Core/
  Persistence/
  Retry/
  Security/
  Http/
  Logging/
PivotController/Indexing/
PivotController/Data/Providers/
PivotController/Security/
PivotController/Reports/
PivotController/DataServer/  (runtime; gitignored)
```

---

## Out of Scope (Phase 2+)

- Admin Panel UI
- Monitoring UI
- Diagnostics UI
- Runtime configuration visual editor

---

## Clarification Questions

The specification is comprehensive and detailed. The following questions have been identified for confirmation before implementation:

1. **PivotDataSource Migration** — Should the legacy block be automatically migrated to index "default" at startup?
2. **Optional DB Providers** — Beyond SQLite, should other DB packages be included in .csproj or documented as optional runtime swaps?
3. **DataFolder Default** — Keep as `DataSource` (current) or move to `DataServer/data`?
4. **Log File Location** — Proposed `logs/dataserver-{yyyyMMdd}.jsonl` with 30-day retention — acceptable?
5. **Primary Port** — Use `5285` (current launchSettings.json) or `9500` (Flexmonster reference)?

---

## Acceptance Criteria for Exploration

- [x] Current architecture reviewed and documented
- [x] Specification requirements understood and mapped to layers
- [x] Data model and API surface clarified
- [x] Implementation order (8 steps) established
- [x] File structure and new components identified
- [x] Persistence strategy defined
- [x] Technology stack confirmed (no new external packages required beyond optional DB providers)
- [x] Out-of-scope items identified for Phase 2
- [x] Clarification questions documented

---

## Next Steps

This exploration serves as the foundation for:
1. **Proposal.md** — Feature scope, success metrics, risk assessment
2. **Design.md** — Detailed component design, API contracts, integration patterns
3. **Spec.md** — Testable requirements with acceptance scenarios
4. **Tasks.md** — Implementation task breakdown with verification criteria
