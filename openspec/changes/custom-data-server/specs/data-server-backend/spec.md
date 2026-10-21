# Syncfusion Pivot Data Server — Phase 1 Technical Specification

**Date:** 2024-12-19

**Version:** 2.2

**Implementation Order (reordered 2026-10):** Index Configuration (Step 1) → General Settings → Data Storage Settings → **HTTPS Settings (Step 4)** → Security Settings (Step 5) → Report Sharing Settings (Step 6) → Client-Side Integration (Step 7) → **Database Query Settings (Step 8 - FINAL)** — deferred to last because it requires DB provider support installed on the machine; no other step depends on it.

---

## IMPLEMENTATION STATUS (updated after Steps 1–3 implementation)

| Step | Section | Status | Notes |
|------|---------|--------|-------|
| 1 | §0 Index Configuration | ✅ Implemented & verified | All scenarios verified live; see deltas below |
| 2 | §1 General Settings | ✅ Implemented & verified | All scenarios verified live; see deltas below |
| 3 | §2 Data Storage Settings | ✅ Implemented & verified | CacheSizeLimit aligned with Flexmonster semantics; see deltas below |
| 4 | §4 HTTPS Settings | ✅ Implemented & verified (COMPLETE) | All three certificate sources (PFX / PEM fullchain / store thumbprint+subjectName with location fallback) verified live end-to-end; TLS 1.2+ enforced; HTTP/1 & HTTP/2 negotiation; merge-patch API; https.json precedence hardened; test PKI shipped (Cert/PEM-Cert/); verify-https.sh 33 checks; see deltas below |
| 5 | §5 Security Settings | ✅ Implemented & verified (COMPLETE) | Basic Auth (Authorization.Enabled + Data Server credentials) + CORS (AllowOrigin; specific origins required when auth enabled, never '*', fail-fast at startup, 422 via API); see deltas below |
| 6 | §6 Report Sharing | ✅ Implemented & verified (COMPLETE) | Report storage/expiration/quota/auto-delete; `ReportSharingOptions` appsettings + `reportsharing.json` persistence; auto-store on client pivot requests; see deltas below |
| 7 | §3 Database Query | ⬜ Not started | SQLite-only provider (mandatory); future providers incremental |
| 8 | §7 Client-Side Integration | ⬜ Not started | Sample sends index via `?index=` query |

### Implementation deltas (verified behavior, deviates from original spec text)


**Step 6 — Report Sharing Settings (COMPLETE — supersedes earlier §6 wording)**
- Schema is the appsettings `ReportSharingOptions` section: `{ EndpointEnabled, PathToFolder, ReportExpirationTime, MaxSizeOfFolder, AutoDeletingEnabled }`. Persisted changes (PUT /api/config/reportsharing) go to `{DataFolder}/reportsharing.json` with the same precedence model as Steps 2–5 (non-null persisted fields override appsettings; logged loudly at startup).
- `PathToFolder`: relative paths resolve against `{DataFolder}`; absolute paths allowed; path traversal (`../../`) → 422 (API) and startup fail-fast. Changing it requires restart (surfaced via `requiresRestartFields`).
- **Auto-store**: the client-side pivot table never calls the report API directly; `POST /api/pivot/post` auto-saves the request's report configuration through `ReportSharingService` when `EndpointEnabled` (spec §6.3.1 note). Auto-save failures are logged and never break the pivot response.
- **Report file format**: ONE `{reportId}.json` per report containing metadata (reportId, name, owner, createdAt, expiresAt, sizeBytes) + the report. No `.meta.json` sidecar (legacy sidecars ignored/swept).
- **Stored report structure is CLIENT-side** (user direction): camelCase properties (`AllowLabelFilter` → `allowLabelFilter`), string enums (`GrandTotalsPosition: 0` → `"Top"`, `SubTotalsPosition` → `"Top" | "Bottom"`, summary `Type` → `"Sum"` …), nulls omitted, and **`internalProperties` excluded**. All client-configured properties are preserved (rows/columns/values/filters with captions, drilledMembers, formatSettings, fieldMapping, groupSettings, sortSettings, conditionalFormatSettings, calculatedFieldSettings). Built by `ClientReportBuilder`.
- POST always creates a NEW report with a new GUID (no overwrite/update semantics). Payload is an opaque JSON blob — only JSON validity is checked, not the Pivot Report structure.
- **Expiration** (`ReportExpirationTime` minutes, 0 = never) is an INDEPENDENT cleanup mechanism: expired reports are ALWAYS removed by the sweep (and read as 410 Gone), regardless of `AutoDeletingEnabled`.
- **AutoDeletingEnabled governs ONLY quota eviction**: with `MaxSizeOfFolder` MB (0 = unlimited) reached, saving evicts oldest reports (CreatedAt-first) until the new report fits; with `AutoDeletingEnabled=false` the save returns **507 Insufficient Storage** and the folder is untouched.
- **Sweep worker**: every **15 seconds** (plus an immediate startup pass) — NOT the earlier 5-minute cadence — so a 1-minute `ReportExpirationTime` is honored promptly (verified live: expired report physically deleted within one cycle of expiry).
- Endpoints: `POST/GET/DELETE /api/reportsharing/reports[/{reportId}]` + `GET/PUT /api/config/reportsharing`; disabled endpoint → **404** on all report operations (config API stays available); expired read → **410**; validation failures → **422** (traversal, negative quota, scalar payload).
- All scenarios verified live end-to-end (404/200/201/204/410/422/507, evict-oldest, expiration within 15 s, persistence across restart).

**Step 5 — Security Settings (COMPLETE — supersedes earlier §5 wording)**
- Schema is the clarified appsettings structure: `Security: { Authorization: { Enabled, Username, Password }, CORS: { AllowOrigin } }`. Credentials are created in the Data Server configuration (appsettings.json) for now — Admin Panel later (Phase 2); they are never settable via the API, never persisted to `{DataFolder}/security.json`, and never returned by `GET /api/config/security`.
- **AllowOrigin rule (Flexmonster UG)**: when `Authorization.Enabled=true`, AllowOrigin must specify proper origin URLs (absolute `http://`/`https://` with host; several origins comma-separated) — never `*` and never missing. Violations: (a) startup does not start (exit 1, clear message — applies to appsettings.json AND a persisted security.json whose Enabled/AllowOrigin override appsettings pre-validation); (b) `PUT /api/config/security` returns 422; (c) a persisted security.json enabling auth with an invalid AllowOrigin is ignored on reload (appsettings kept, warning logged).
- Origins are compared **exactly as configured** — the earlier trailing-slash normalization was removed per user direction; canonical form has no trailing slash (e.g. `http://localhost:4000`).
- `Authorization.Enabled=false` lifts all AllowOrigin restrictions: `*` (default), single URL, or comma-separated URLs are all accepted.
- BasicAuth middleware protects all `/api/*` paths (including `/api/pivot/post`); static files (wwwroot) are not protected. CORS preflight OPTIONS requests are exempt from Basic Auth (browsers send them without credentials) and `UseCors` runs before the auth middleware so browsers can preflight.
- Auth applies immediately via the API (no restart): disable → requests pass without a header; re-enable → 401 again. Startup validation fails fast when Enabled without credentials.
- Native ASP.NET Core CORS behavior (3.G.2): disallowed origins receive no CORS headers and are blocked by the browser; no server-side 403.

**Step 4 — HTTPS Settings (COMPLETE — supersedes all earlier deltas for this step)**
- `https.json` (in `{DataFolder}`) is the persisted source of truth written by `PUT /api/config/https`. **Precedence hardened**: non-null https.json fields override appsettings.json; **null fields never wipe appsettings values**; a stored `enabled:false` cannot disable an appsettings-enabled config; the override is logged loudly at startup.
- `PfxPassword` is accepted by `PUT` but never returned by `GET` nor written to disk; it survives restarts only via appsettings.json. A restart without it degrades to an HTTP listener with a clear message (no crash).
- Certificate sources are mutually exclusive — `422` when more than one is configured: PFX / PEM pair / **store lookup by `thumbprint` XOR `subjectName`**.
- **Store lookup is cross-platform** (SChannel on Windows, OpenSSL-backed store on Linux/macOS — verified live on Linux) with a **`CurrentUser` ↔ `LocalMachine` location fallback** (configured location searched first); entries without a private key are skipped; a total miss yields a detailed error (thumbprint case/format guidance) and HTTP-only degradation.
- **PEM loading** uses `CreateFromPemFile` with fullchain support (leaf + issuing CA); on Windows the ephemeral key handle is normalized via an in-memory PFX round-trip (default key flags, falls back to the original cert on failure) — fixes connection-closed-before-response.
- **`AllowInvalidCertificates` REMOVED** (and `AllowHttpTunnelPort` removed): browsers enforce trust/validity themselves; the server verifies only loadability (private key present). An expired or self-signed within-period cert is SERVED; the browser shows its own warning.
- Kestrel binds `https://0.0.0.0:{General:Port}` when `Https:Enabled`; `SslProtocols = Tls12 | Tls13` (TLS 1.2+ enforced — TLS 1.1 handshake refused, verified live). `UseHttpsRedirection` is applied only when HTTPS is enabled.
- `requiresRestart: true` for all HTTPS changes (listener binds at startup); invalid settings fail startup fast with a clear message.
- **Test PKI shipped** at `PivotController/Cert/PEM-Cert/`: dedicated test CA (`test-ca.pem`, `CA:TRUE` — the ONLY file to import into client Trusted Root) + CA-signed SAN leaf (`server.pem`: `DNS:localhost, *.localhost, IP:127.0.0.1, 0.0.0.0`, `serverAuth`) + `localhost-server-fullchain.pem` served by default appsettings. `verify-https.sh`: 33-check end-to-end suite (all three sources, protocols, persistence, secrecy, 422 cases).

**Step 1 — Index Configuration**
- `CsvDelimiter`/`CsvDecimalSeparator`/`CsvThousandsSeparator` are **enums** (§0.6): `CsvThousandsSeparator ∈ {Comma, Dot, Space}`, `CsvDecimalSeparator ∈ {Comma, Dot}`. Both enum names (`"Dot"`) and raw symbols (`"."`, `","`, `" "`) are accepted in appsettings.json (TypeConverter), API bodies (System.Text.Json converter), and `indexes.json` (Newtonsoft).
- Field mapping endpoints added beyond §0.7: `GET /api/indexes/{index}/fields` (detected/mapped/effective types), `PUT /api/indexes/{index}/fields/{fieldName}` (`{"type":"Number|String|Date"}`), `DELETE` to revert. Mappings are injected into pivot requests via `CustomProperties.FieldType` + `DataOptions.FieldMapping`, so a Data Server type change reflects in the client pivot table on the next request (including demotion, e.g. datetime → string).
- Index routing accepts the index from the request **body or `?index=` query** (fallback to controller default `ServerIndexName`) — the query form allows a page to pin a grid to its index without body patching.
- SourceType immutability (§0.4) is strict: a PUT that supplies a different sourceType returns 422; when the body omits `sourceType`, the controller back-fills it from the existing definition.
- Bootstrap resilience: a persisted/appsettings index whose source is missing no longer crashes startup — it registers with `Failed` status and remains fixable via API.
- Per-attempt retry logging: each load failure logs `attempt n/N ... Retrying in X ms`.

**Step 2 — General Settings**
- Port binding: `General:Port` (appsettings) drives Kestrel; launchSettings `applicationUrl` was removed so appsettings wins locally; only an explicit `ASPNETCORE_URLS` overrides.
- MinimumLogLevel is enforced by a runtime-adjustable `MinimumLevelLoggerProvider` (sole console provider) — level changes (API PUT or `config.json` hot-reload via FileSystemWatcher, ~100ms) filter console output immediately. A test helper `POST /api/config/general/test-log` emits one entry per severity for local verification.
- DataFolder: `reports/` and `logs/` subdirectories created; writability probe at startup; `../../` traversal rejected with 422.
- Merge-patch: `PUT /api/config/general` — Port/DataFolder report `requiresRestart: true`, MinimumLogLevel `false`.

**Step 3 — Data Storage Settings**
- **`CacheSizeLimit` follows Flexmonster Data Server semantics** (supersedes the original §2.3 wording): *the maximum number of cached server responses for every index*. Per-index LRU (not a global entry count): exceeding the limit evicts that index's least-recently-used cached engine response only; other indexes are unaffected. Each pivot report (rows/columns/values) hashes to one cached response. **`0` disables response caching** (every request rebuilds from the warm per-index dataset).
- The per-index dataset cache (`data:{index}`) is NOT subject to CacheSizeLimit (it backs KeepDataOnRefresh and fast rebuilds).
- `datastorage.json` persistence: values set via `PUT` (CacheSizeLimit/MaxCacheItemSizeBytes require restart) are loaded at startup via `LoadPersisted()`; when the file is absent, appsettings.json values stand (logged explicitly).
- `GET /api/config/datastorage/cache-stats` observability endpoint: per-index cached-response counts vs the limit (e.g. `"Sales-Json (2 responses)"`).
- Retry (§2.2): exponential backoff 250ms→500ms→1000ms… (±20% jitter) on transient IO failures; `RetryAttempts` runtime-changeable; failures log per-attempt lines.

---

## 0. Index Configuration (STEP 1 - FOUNDATIONAL) (IMPLEMENTED & VERIFIED)

**Critical:** This is the first implementation step. All other features depend on named indexes and IndexManager.

### 0.1 Index Definition Model

```csharp
public enum DataSourceType { Json, Csv, SqlServer, PostgreSQL, MySql, Oracle, MariaDb, Sqlite }
public enum DatabaseType { MySql, MariaDB, MSSQL, PostgreSQL, Oracle, Sqlite }
public enum MappingDataType { Number, String, Date }

public class IndexDefinition
{
    public required string Index { get; set; }        // e.g., "Sales-Data" (unique, immutable)
    public required string Name { get; set; }         // display name
    public DataSourceType SourceType { get; set; }    // immutable after creation
    public string? SourcePath { get; set; }           // JSON/CSV path under DataFolder
    public char CsvDelimiter { get; set; } = ',';
    public char CsvDecimalSeparator { get; set; } = '.';
    public char CsvThousandsSeparator { get; set; } = ',';
    public DatabaseType? DatabaseType { get; set; }
    public string? ConnectionStringName { get; set; }
    public string? Query { get; set; }
    public bool AutoReload { get; set; } = false;
    public int ReloadIntervalMinutes { get; set; }    // >0 requires AutoReload=true
    public bool MappingEnabled { get; set; } = false;
    public List<IndexFieldMapping>? FieldMappings { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class IndexFieldMapping
{
    public required string FieldName { get; set; }
    public MappingDataType Type { get; set; }  // Number | String | Date
}

public enum IndexStatus { Ready, Refreshing, Failed, Disabled }

public class IndexStatusResponse
{
    public string Index { get; set; }
    public IndexStatus Status { get; set; }
    public int RowCount { get; set; }
    public DateTime? LoadedAt { get; set; }
    public string? LastError { get; set; }
}
```

### 0.2 Index Registration from appsettings.json (Bootstrap)

**SHALL** load index definitions from `DataServer:Indexes` array in `appsettings.json` at startup and register them immediately.

**Scenario 0.2.1: Index Declared in appsettings.json**
- Given: `appsettings.json` contains `DataServer:Indexes[0]` with `Index: "Sales-Data"`, `SourceType: "Csv"`, `SourcePath: "sales.csv"`
- When: Application starts
- Then: Index is loaded and available via `GET /api/indexes/Sales-Data`
- And: Status is `Ready` with `rowCount > 0`

**Scenario 0.2.2: Multiple Indexes Bootstrap**
- Given: appsettings defines 3 indexes (JSON, CSV, Database)
- When: Application starts
- Then: `GET /api/indexes` returns 200 with all 3 indexes listed
- And: Each has status `Ready` or `Failed` (depending on data availability)

**Scenario 0.2.3: Disabled Index at Bootstrap**
- Given: Index with `Enabled: false` in appsettings
- When: Application starts
- Then: Index is not preloaded
- And: `GET /api/indexes/{index}` returns status `Disabled`
- And: Pivot requests to it return 423

**Scenario 0.2.4: Legacy PivotDataSource Migration**
- Given: Old config has `PivotDataSource` section (legacy)
- When: Application starts
- Then: Legacy section migrated to index named `default`
- And: `DefaultIndexId: "default"` set automatically
- And: Migration persisted to `DataServer/indexes.json` (one-time only)

### 0.3 Index-Aware Pivot Routing (Controller-Side Testing)

**TESTING NOTE (Phase 1 Verification):** For initial testing, declare the index name **directly in the controller** to verify indexing works before implementing client-side integration (Step 8).

**SHALL** route pivot requests to the correct index based on index name.
Implemented precedence: request body `index` field → `?index=` query parameter (added for browser-side pivot grids whose URL can carry the index) → controller constant `ServerIndexName` (`"Sales-Json"`). Step 8 will additionally honor client-sent report metadata.

**Scenario 0.3.1: Controller-Side Index Declaration (Testing Phase) — IMPLEMENTED**
- Given: Multiple indexes exist (Sales-Data, Sales-Db)
- When: Controller code declares: `const string indexName = "Sales-Data";`
- And: Pivot request sent to `/api/pivot/post`
- Then: Pivot calculations use data from "Sales-Data" index
- And: Results correctly reflect Sales-Data values
- **Note:** This is for Phase 1 testing only; client sends index in Step 8

**Scenario 0.3.2: Fallback to DefaultIndexId**
- Given: `DefaultIndexId: "default"` configured
- When: Pivot request sent without index parameter
- Then: Server routes to "default" index
- And: Returns correct data from default index

**Scenario 0.3.3: Unknown Index**
- Given: No index named "unknown"
- When: Controller requests index "unknown"
- Then: Returns 404 with error and lists valid indexes

**Scenario 0.3.4: Disabled Index Request**
- Given: Index "Sales-Data" with `Enabled: false`
- When: Controller attempts to use "Sales-Data"
- Then: Returns 423 "Index is disabled"

### 0.4 Index Lifecycle: Create, Edit, Reload, Enable, Disable, Delete

**SHALL** support full index lifecycle via REST API and enforce immutability of SourceType.

#### **0.4.1 Create Index**

**Scenario 0.4.1.1: Create CSV Index**
- Given: No index named "new-csv"
- When: `POST /api/indexes { index: "new-csv", name: "New CSV", sourceType: "Csv", sourcePath: "new.csv" }`
- Then: Returns 201
- And: Response includes `{ status: "Ready", rowCount: <N>, loadedAt: <timestamp> }`
- And: Index available in `GET /api/indexes`

**Scenario 0.4.1.2: Create Index with Auto-Reload**
- Given: No index named "auto-reload-index"
- When: `POST /api/indexes { index: "auto-reload-index", sourceType: "Csv", autoReload: true, reloadIntervalMinutes: 60 }`
- Then: Returns 201 with status `Ready`
- And: Auto-reload scheduled every 60 minutes from server start

**Scenario 0.4.1.3: Create Index with Field Mapping**
- Given: CSV file with Price and OrderDate columns
- When: `POST /api/indexes { index: "mapped-index", sourceType: "Csv", mappingEnabled: true, fieldMappings: [{ fieldName: "Price", type: "Number" }, { fieldName: "OrderDate", type: "Date" }] }`
- Then: Returns 201
- And: Fields enforce specified types on load

**Scenario 0.4.1.4: Duplicate Index Name**
- Given: Index "Sales-Data" exists
- When: `POST /api/indexes { index: "sales-data" ... }` (case-insensitive match)
- Then: Returns 409 "Index already exists"

#### **0.4.2 Edit Index (All Properties Except SourceType)**

**Scenario 0.4.2.1: Edit Index Name and CSV Delimiter**
- Given: Index "Sales-Data" with name "Sales Data" and delimiter ","
- When: `PUT /api/indexes/Sales-Data { name: "Sales Data (Updated)", csvDelimiter: ";" }`
- Then: Returns 200
- And: Metadata updated
- And: Data reloaded with new delimiter
- And: Status transitions: `Refreshing` → `Ready`

**Scenario 0.4.2.2: Attempt to Change SourceType (Should Fail)**
- Given: Index "Sales-Data" with `sourceType: "Csv"`
- When: `PUT /api/indexes/Sales-Data { sourceType: "Json" }`
- Then: Returns 422 with error "SourceType is immutable"
- And: Index metadata unchanged

**Scenario 0.4.2.3: Enable/Disable Mapping**
- Given: Index with `mappingEnabled: false`
- When: `PUT /api/indexes/{index} { mappingEnabled: true, fieldMappings: [{ fieldName: "Price", type: "Number" }] }`
- Then: Returns 200
- And: Data reloaded with field mapping applied
- When: `PUT /api/indexes/{index} { mappingEnabled: false }`
- Then: Mappings deleted, autodetect restored

**Scenario 0.4.2.4: Update CSV Separators**
- Given: Index using default CSV separators
- When: `PUT /api/indexes/{index} { csvDelimiter: ";", csvDecimalSeparator: ",", csvThousandsSeparator: "." }`
- Then: Returns 200
- And: Data reloaded with new separators honored

#### **0.4.3 Reload Index (Manual + Auto-Reload)**

**Scenario 0.4.3.1: Manual Reload**
- Given: Index "Sales-Data" with loaded data
- When: Source file updated externally (new rows added)
- And: `POST /api/indexes/Sales-Data/reload`
- Then: Returns 200
- And: Status transitions: `Refreshing` → `Ready`
- And: `rowCount` reflects new data (increased)

**Scenario 0.4.3.2: Reload with Keep-Data-on-Refresh Enabled**
- Given: Index with `keepDataOnRefresh: true`, data loaded
- When: Source becomes unavailable (network error)
- And: `POST /api/indexes/Sales-Data/reload` triggered
- Then: Returns 502 "Unable to reload"
- And: Status remains `Ready` (old data continues serving)
- And: Subsequent pivot requests use old data
- And: Next successful reload replaces old data

**Scenario 0.4.3.3: Reload with Keep-Data-on-Refresh Disabled**
- Given: Index with `keepDataOnRefresh: false`, data loaded
- When: Source becomes unavailable
- And: `POST /api/indexes/Sales-Data/reload` triggered
- Then: Returns 502 "Unable to reload"
- And: Status changes to `Failed`
- And: Cache evicted; subsequent pivot requests return 502

**Scenario 0.4.3.4: Auto-Reload at Scheduled Interval**
- Given: Index with `autoReload: true`, `reloadIntervalMinutes: 60`
- When: Server starts at 10:00 AM
- Then: Auto-reload scheduled for 11:00 AM, 12:00 PM, etc.
- When: 11:00 AM arrives
- Then: Reload triggered automatically
- And: Status: `Refreshing` → `Ready` (or `Failed` if source unavailable)
- And: Logged as automatic reload (not manual)

**Scenario 0.4.3.5: Retry Policy on Reload Failure**
- Given: Index reload with `retryAttempts: 3`
- When: Database temporarily unavailable
- Then: Retries at 250ms, 500ms, 1000ms (with jitter)
- When: Database becomes available at retry 2
- Then: Reload succeeds, data cached, status `Ready`

#### **0.4.4 Enable/Disable Index**

**Scenario 0.4.4.1: Disable Index (Data Evicted)**
- Given: Index "Sales-Data" with `enabled: true`, data cached
- When: `POST /api/indexes/Sales-Data/disable`
- Then: Returns 200
- And: Status becomes `Disabled`
- And: Cache entries `data:Sales-Data` and `engine:Sales-Data:*` evicted
- And: Pivot requests return 423 "Index is disabled"
- And: `GET /api/indexes/Sales-Data` shows status `Disabled`

**Scenario 0.4.4.2: Enable Index (Data Loaded)**
- Given: Disabled index "Sales-Data"
- When: `POST /api/indexes/Sales-Data/enable`
- Then: Returns 200
- And: Status becomes `Refreshing`, then `Ready`
- And: Data loaded from source
- And: Pivot requests work normally
- And: Cache populated with data

**Scenario 0.4.4.3: Enable Index with Keep-Data-on-Refresh**
- Given: Disabled index, then enable requested
- When: Load fails (source unavailable)
- Then: Returns 502
- And: Status remains `Disabled` (no data to keep)

#### **0.4.5 Delete Index**

**Scenario 0.4.5.1: Delete Index**
- Given: Index "Sales-Data" exists
- When: `DELETE /api/indexes/Sales-Data`
- Then: Returns 204 No Content
- And: Metadata removed from `DataServer/indexes.json`
- And: Cache entries evicted (`data:Sales-Data`, `engine:Sales-Data:*`)
- And: Subsequent `GET /api/indexes/Sales-Data` returns 404

### 0.5 Index Naming & Validation

**SHALL** enforce case-insensitive unique index names matching pattern `[A-Za-z0-9._-]{1,64}`.

**Scenario 0.5.1: Valid Index Names**
- Given: No indexes
- When: Create indexes with names: "Sales-Data", "sales_json", "prod.index.1"
- Then: All succeed
- And: All stored and retrievable

**Scenario 0.5.2: Invalid Index Names**
- Given: Various invalid names
- When: Attempt `POST /api/indexes { index: "sales@data!" }` (contains special chars)
- Then: Returns 422 "Invalid index name pattern"
- When: Attempt `POST /api/indexes { index: "" }` (empty)
- Then: Returns 422 "Index name cannot be empty"

**Scenario 0.5.3: Case-Insensitive Uniqueness**
- Given: Index "Sales-Data" exists
- When: `POST /api/indexes { index: "sales-data" }` (different case)
- Then: Returns 409 "Index already exists"

### 0.6 CSV Separator Configuration (IMPLEMENTED — enum-typed separators)

**SHALL** honor CSV delimiter (free string, default `","`), decimal separator, and thousands separator on load.
Implemented types: `CsvDecimalSeparator ∈ { Comma, Dot }`, `CsvThousandsSeparator ∈ { Comma, Dot, Space }` — enums accepting both names (`"Comma"`) and raw symbols (`","`, `"."`, `" "`) in appsettings.json (TypeConverter), API bodies, and `indexes.json`. Numeric autodetection honors the configured separators, including the ambiguous case where the decimal and thousands symbols coincide.

**Scenario 0.6.1: European Format CSV**
- Given: CSV with semicolon delimiter, comma as decimal
- When: Index created with `csvDelimiter: ";"`, `csvDecimalSeparator: ","`, `csvThousandsSeparator: "."`
- Then: Data loads correctly
- And: Field values parsed with correct separators (e.g., "1.234,56" → 1234.56)

**Scenario 0.6.2: Custom Delimiters on Edit**
- Given: Index using default separators
- When: `PUT /api/indexes/{index} { csvDelimiter: "|" }`
- Then: Data reloaded with pipe delimiter
- And: Field count and values correct

### 0.7 Field Mapping (Type Inference) (IMPLEMENTED)

**SHALL** support optional per-field type mapping (Number, String, Date); disabled by default (autodetect).
Implemented endpoints: `GET /api/indexes/{index}/fields` (list with `detectedType`/`mappedType`/`effectiveType`), `PUT /api/indexes/{index}/fields/{fieldName}` body `{"type":"Number|String|Date"}`, `DELETE /api/indexes/{index}/fields/{fieldName}` (falls back to detected). Mappings apply to pivot requests immediately (injected into `CustomProperties.FieldType` + `DataSourceSettings.FieldMapping`), evicting that index's engine cache; they also gate CSV value conversion. `MappingEnabled` is `bool?` so a PUT can explicitly disable it.

**Scenario 0.7.1: Mapping Disabled (Autodetect)**
- Given: CSV with mixed types
- When: Index created with `mappingEnabled: false`
- Then: Types autodetected
- And: All fields accessible with inferred types

**Scenario 0.7.2: Mapping Enabled**
- Given: Index with `mappingEnabled: true`, `fieldMappings: [{ fieldName: "Amount", type: "Number" }]`
- When: Data loads
- Then: "Amount" field enforced as Number
- And: Other fields autodetected

**Scenario 0.7.3: Disable Mapping**
- Given: Index with active field mappings
- When: `PUT /api/indexes/{index} { mappingEnabled: false }`
- Then: Mappings deleted
- And: Autodetect restored on next load

### 0.8 Database Test Connection

**SHALL** test database connection without loading data.

**Scenario 0.8.1: Connection Success**
- Given: Index with valid database connection string
- When: `POST /api/indexes/{index}/test-connection`
- Then: Returns 200 `{ success: true, latencyMs: 42 }`

**Scenario 0.8.2: Connection Failure**
- Given: Index with invalid connection string
- When: `POST /api/indexes/{index}/test-connection`
- Then: Returns 502 `{ success: false, error: "Unable to connect: login failed for user 'sa'." }`

### 0.9 Cache Key Scheme (Index-Prefixed)

**SHALL** prefix all cache keys with index name to prevent data collision between indexes.

**Scenario 0.9.1: Separate Cache Keys**
- Given: Indexes "Sales-Data" and "Sales-Db"
- When: Both loaded
- Then: Cache keys are:
  - `data:Sales-Data` (CSV data)
  - `data:Sales-Db` (DB data)
  - `engine:Sales-Data:{hash}` (pivot engine for CSV)
  - `engine:Sales-Db:{hash}` (pivot engine for DB)
- And: Data never collides (separate cache slots)

**Scenario 0.9.2: Multiple Pivot Requests Same Index**
- Given: Two concurrent pivot requests to "Sales-Data"
- When: Both use same hash
- Then: Both use same `engine:Sales-Data:{hash}` cache entry
- And: No duplicate processing

---

## 1. General Settings Configuration (STEP 2) (IMPLEMENTED & VERIFIED)

**Follows after Index Configuration. All indexes must be functional before general settings.**

### 1.1 Configuration Model

```csharp
public enum LogLevelOption { Info, Warn, Error, Fatal }

public class GeneralSettings
{
    public int Port { get; set; } = 9500;
    public LogLevelOption MinimumLogLevel { get; set; } = LogLevelOption.Info;
    public string DataFolder { get; set; } = "DataSource";
}
```

### 1.2 Port Configuration (IMPLEMENTED)

**SHALL** accept valid port numbers (1–65535) and bind the Kestrel listener.
Precedence (implemented): `ASPNETCORE_URLS` env var > `General:Port` (appsettings.json). The launchSettings `applicationUrl` was removed so `General:Port` drives local `dotnet run` / F5; a persisted `config.json` General section (written via `PUT`) is loaded at startup and also applies.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| **Port** | 9500 | 1–65535 | **Yes** |

**Scenario 1.2.1: Default Port**
- Given: Port not specified
- When: Application starts
- Then: Server binds to 9500

**Scenario 1.2.2: Custom Port**
- Given: `appsettings.json` has `General: { Port: 5285 }`
- When: Application starts
- Then: Server binds to 5285

**Scenario 1.2.3: Port Update Requires Restart**
- Given: Running on port 5285
- When: `PUT /api/config/general { port: 5286 }`
- Then: Returns 200 with `requiresRestart: true`
- And: Old port still active until restart

**Scenario 1.2.4: Invalid Port (Out of Range)**
- Given: Port update attempted
- When: `PUT /api/config/general { port: 99999 }`
- Then: Returns 422 "Port must be between 1 and 65535"

**Scenario 1.2.5: Port Already in Use**
- Given: Another service uses port 5285
- When: Application tries to start with port 5285
- Then: Startup fails with error "Address already in use"
- And: Exit code non-zero

### 1.3 Minimum Log Level Configuration (IMPLEMENTED)

**SHALL** accept Info, Warn, Error, or Fatal and apply immediately (no restart).
Implementation: `MinimumLevelLoggerProvider` is the sole console logging provider (default providers cleared); `GeneralSettingsService.ApplyLogLevel()` flips its threshold at runtime, and `GeneralSettingsFileWatcher` (FileSystemWatcher on `{DataFolder}/config.json`) hot-reloads external edits within ~100ms. A `POST /api/config/general/test-log` helper emits one entry per severity for local verification.

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| **MinimumLogLevel** | Info | Info/Warn/Error/Fatal | **No** |

**Scenario 1.3.1: Default Info Level**
- Given: MinimumLogLevel not specified
- When: Application starts
- Then: All INFO, WARN, ERROR, FATAL logged
- And: DEBUG filtered

**Scenario 1.3.2: Change to Error Level**
- Given: Currently at Info level
- When: `PUT /api/config/general { minimumLogLevel: "Error" }`
- Then: Returns 200 with `requiresRestart: false`
- And: Filter applied immediately
- And: Next logs at Error level and above only

**Scenario 1.3.3: Case-Insensitive Parsing**
- Given: Log level update
- When: `PUT /api/config/general { minimumLogLevel: "error" }` (lowercase)
- Then: Parses as "Error" successfully
- And: Returns 200

**Scenario 1.3.4: Invalid Log Level**
- Given: Log level update
- When: `PUT /api/config/general { minimumLogLevel: "Debug" }`
- Then: Returns 422 "MinimumLogLevel must be one of: Info, Warn, Error, Fatal"

**Scenario 1.3.5: Hot-Reload Log Level**
- Given: MinimumLogLevel at Info, `config.json` exists
- When: External process updates `config.json` to `"Error"`
- Then: File watcher detects change within 100ms
- And: Filter updated immediately
- And: No restart required
- And: Subsequent logs filtered at Error level

### 1.4 Data Folder Configuration

**SHALL** validate folder writable and create subdirectories (reports, logs).

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| **DataFolder** | DataSource | non-empty, writable | **Yes** |

**Scenario 1.4.1: Default DataFolder**
- Given: DataFolder not specified
- When: Application starts
- Then: `DataSource/` directory created if missing
- And: Subdirectories `DataSource/reports`, `DataSource/logs` created

**Scenario 1.4.2: Custom DataFolder**
- Given: `DataFolder: "/custom/data"`
- When: Application starts
- Then: `/custom/data/` used as root
- And: Subdirectories created

**Scenario 1.4.3: DataFolder Write Permission Check**
- Given: `DataFolder: "/readonly"` (no write permission)
- When: Application starts
- Then: Startup fails with error "Access denied to DataFolder"
- And: Exit code non-zero

**Scenario 1.4.4: Path Traversal Protection**
- Given: DataFolder update attempted
- When: `PUT /api/config/general { dataFolder: "../../" }`
- Then: Returns 422 "DataFolder path traversal not allowed"

**Scenario 1.4.5: DataFolder Update Requires Restart**
- Given: Currently using `DataSource`
- When: `PUT /api/config/general { dataFolder: "NewFolder" }`
- Then: Returns 200 with `requiresRestart: true`
- And: New folder not used until restart

### 1.5 General Settings API (IMPLEMENTED — GET/PUT /api/config/general, merge-patch; only supplied properties change; response includes `requiresRestart`)

**Scenario 1.5.1: Get General Settings**
- Given: Server running
- When: `GET /api/config/general`
- Then: Returns 200 with `{ port, minimumLogLevel, dataFolder }`

**Scenario 1.5.2: Update Single Property (Merge-Patch)**
- Given: Current settings
- When: `PUT /api/config/general { minimumLogLevel: "Error" }`
- Then: Port and DataFolder unchanged
- And: MinimumLogLevel updated

**Scenario 1.5.3: Update Multiple Properties**
- Given: Current settings
- When: `PUT /api/config/general { port: 5286, minimumLogLevel: "Warn" }`
- Then: Both updated
- And: Response shows `requiresRestart: true` (due to port)

---

## 2. Data Storage Settings Configuration (STEP 3) (IMPLEMENTED & VERIFIED)

**Follows General Settings. Manages caching and retry behavior.**

### 2.1 Configuration Model (IMPLEMENTED — bound from `DataStorage` in appsettings.json)

```csharp
public class DataStorageSettings
{
    public int RetryAttempts { get; set; } = 3;       // 0–30
    public int CacheSizeLimit { get; set; } = 100;    // >= 0 (0 disables)
    public bool KeepDataOnRefresh { get; set; } = true;
    public long MaxCacheItemSizeBytes { get; set; }   // 0 = unlimited
}
```

### 2.2 Retry Attempts Configuration

**SHALL** retry transient failures with exponential backoff; fail fast on validation errors.

**Scenario 2.2.1: Retry on Transient IO Failure**
- Given: File temporarily locked, `retryAttempts: 3`
- When: Index load attempted
- Then: Retries at 250ms, 500ms, 1000ms (±20% jitter)
- And: Succeeds when file available

**Scenario 2.2.2: No Retry on Validation Error**
- Given: CSV with mismatched columns
- When: Load attempted with `retryAttempts: 3`
- Then: Fails immediately (422), no retry

**Scenario 2.2.3: Retry Policy Exhausted**
- Given: Database unavailable, `retryAttempts: 3`
- When: Index reload triggered
- Then: Retries 3 times, then fails
- And: Returns 502

**Scenario 2.2.4: Update Retry Attempts at Runtime**
- Given: Current `retryAttempts: 3`
- When: `PUT /api/config/datastorage { retryAttempts: 5 }`
- Then: Returns 200, applied immediately
- And: Next load uses value 5

### 2.3 Cache Size Limit Configuration (IMPLEMENTED — Flexmonster semantics)

**SHALL** enforce the maximum number of cached **server responses for every index** (Flexmonster Data Server semantics). A "response" is the computed pivot-engine result for one report (the report's rows/columns/values/filters hash to the cache key `engine:{index}:{hash}`). The limit applies **per index, independently**: exceeding it evicts that index's least-recently-used response only, never touching another index's entries. The per-index dataset cache (`data:{index}`) is NOT subject to this limit (it stays warm to back KeepDataOnRefresh and fast rebuilds).

**Scenario 2.3.1: Default Cache Limit**
- Given: `cacheSizeLimit: 100` (default)
- When: Up to 100 distinct reports requested against one index
- Then: All responses cached for that index

**Scenario 2.3.2: Cache Limit Exceeded (Per-Index LRU Eviction)**
- Given: `cacheSizeLimit: 3`, 3 responses cached for `Sales-Json`
- When: 4th distinct report requested against `Sales-Json`
- Then: That index's least-recently-used response is evicted
- And: The 4th response is cached (count stays 3 for `Sales-Json`)
- And: Other indexes' cached responses are unaffected
- And: A pivot requesting an evicted response still returns 200 (transparent rebuild, ~5–50 ms from the warm dataset)

**Scenario 2.3.3: Cache Disabled**
- Given: `cacheSizeLimit: 0`
- When: Any pivot request served
- Then: No engine responses cached (cache-stats reports 0 responses)
- And: Every request rebuilds from the per-index dataset

**Scenario 2.3.4: Update Cache Limit Requires Restart**
- Given: Current `cacheSizeLimit: 100`
- When: `PUT /api/config/datastorage { cacheSizeLimit: 50 }`
- Then: Returns 200 with `requiresRestart: true`
- And: The value persists to `datastorage.json` and loads on next startup

**Scenario 2.3.5: Observability (implemented)**
- When: `GET /api/config/datastorage/cache-stats`
- Then: Returns 200 with `cacheSizeLimit`, `cachedCount`, `cachedIndexes` (per-index response counts) and `evictedIndexes`

### 2.4 Keep Data on Refresh Configuration

**SHALL** retain old data if reload fails (when enabled).

**Scenario 2.4.1: Keep Data on Refresh Enabled**
- Given: Index with `keepDataOnRefresh: true`, data cached
- When: Reload fails (source unavailable)
- Then: Returns 502
- And: Old data continues serving
- And: Status remains `Ready`

**Scenario 2.4.2: Keep Data on Refresh Disabled**
- Given: Index with `keepDataOnRefresh: false`, data cached
- When: Reload fails
- Then: Returns 502
- And: Cache evicted
- And: Status changes to `Failed`
- And: Pivot requests fail until reload succeeds

**Scenario 2.4.3: Update Keep Data on Refresh at Runtime**
- Given: Current `keepDataOnRefresh: true`
- When: `PUT /api/config/datastorage { keepDataOnRefresh: false }`
- Then: Returns 200, applied immediately

### 2.5 Max Cache Item Size Configuration (IMPLEMENTED)

**SHALL** enforce maximum size of the cached **dataset** entry (`data:{index}`), estimated in memory. Enforcement requires `CacheSizeLimit > 0` and `MaxCacheItemSizeBytes > 0`; an oversize dataset is not cached (loaded per request, with a warning log).

**Scenario 2.5.1: Unlimited Item Size**
- Given: `maxCacheItemSizeBytes: 0` (unlimited)
- When: Large index loaded
- Then: Cached regardless of size

**Scenario 2.5.2: Item Size Limit Enforcement**
- Given: `maxCacheItemSizeBytes: 10485760` (10 MB)
- When: Index data exceeds 10 MB
- Then: Not cached
- And: Loaded on each request

**Scenario 2.5.3: Update Max Item Size Requires Restart**
- Given: Current `maxCacheItemSizeBytes: 0`
- When: `PUT /api/config/datastorage { maxCacheItemSizeBytes: 5242880 }`
- Then: Returns 200 with `requiresRestart: true`

### 2.6 Data Storage API

**Scenario 2.6.1: Get Data Storage Settings**
- Given: Server running
- When: `GET /api/config/datastorage`
- Then: Returns 200 with all settings

**Scenario 2.6.2: Update Data Storage Settings**
- Given: Current settings
- When: `PUT /api/config/datastorage { retryAttempts: 5, keepDataOnRefresh: false }`
- Then: Returns 200 with updated settings
- And: Changes applied per restart requirements

---

## 3. Database Query Settings Configuration (STEP 4)

### 3.1 Configuration Model

```csharp
public class DatabaseQuerySettings
{
    public bool CommandTimeoutEnabled { get; set; } = false;
    public int CommandTimeLimit { get; set; } = 150;  // seconds
}
```

### 3.2 Command Timeout Configuration

**Scenario 3.2.1: Timeout Disabled (Default)**
- Given: `commandTimeoutEnabled: false`
- When: Query executed
- Then: No timeout enforced

**Scenario 3.2.2: Timeout Enabled**
- Given: `commandTimeoutEnabled: true`, `commandTimeLimit: 30`
- When: Query executes
- Then: Command timeout set to 30 seconds

**Scenario 3.2.3: Query Exceeds Timeout**
- Given: Query running, timeout 30 seconds
- When: 30 seconds elapsed
- Then: Command canceled
- And: Returns 504 Gateway Timeout

**Scenario 3.2.4: Enable Timeout at Runtime**
- Given: Current `commandTimeoutEnabled: false`
- When: `PUT /api/config/databasequery { commandTimeoutEnabled: true }`
- Then: Returns 200, applied immediately

### 3.3 Database Query API

**Scenario 3.3.1: Get Database Query Settings**
- When: `GET /api/config/databasequery`
- Then: Returns 200 with settings

**Scenario 3.3.2: Update Database Query Settings**
- When: `PUT /api/config/databasequery { commandTimeoutEnabled: true, commandTimeLimit: 60 }`
- Then: Returns 200 with updatue, commandTimeLimit: 60 }`
- Then: Returns 200 with updated settings

---

## 4. HTTPS Settings Configuration (STEP 4; REORDERED — IMPLEMENTED & VERIFIED)

### 4.1 Configuration Model

```csharp
public enum HttpProtocolsOption { HTTP1, HTTP2, HTTP1_AND_HTTP2 }
public enum CertificateStoreLocationOption { CurrentUser, LocalMachine }

public class HttpsSettings
{
    public bool Enabled { get; set; } = false;
    public string? PfxPath { get; set; }
    public string? PfxPassword { get; set; }           // secret, never persisted or returned
    public string? PemCertPath { get; set; }
    public string? PemKeyPath { get; set; }
    public CertificateStoreLocationOption StoreLocation { get; set; } = CertificateStoreLocationOption.CurrentUser;
    public string StoreName { get; set; } = "My";
    public string? Thumbprint { get; set; }            // store lookup by thumbprint (exact)
    public string? SubjectName { get; set; }            // store lookup by subject name (e.g. "CN=localhost")
    public HttpProtocolsOption Protocols { get; set; } = HttpProtocolsOption.HTTP1_AND_HTTP2;
}
```

**Removed option:** `allowInvalidCertificates` — browsers/clients block invalid or untrusted
certificates themselves, so the server never gates certificates on trust or validity. The
server verifies only **loadability** (§4.4).

**Certificate sources:** exactly one of PFX (`pfxPath`+`pfxPassword`), PEM
(`pemCertPath`+`pemKeyPath`), or certificate-store lookup (`thumbprint` XOR `subjectName`).
Configuring two sources returns 422. Explicit JSON `null` for a path field clears it;
omission leaves it unchanged.

### 4.2 HTTPS Enable/Disable

**Scenario 4.2.1: HTTPS Disabled (Default)**
- Given: `enabled: false`
- When: Application starts
- Then: Only HTTP listener configured
- And: Requests use HTTP

**Scenario 4.2.2: Enable HTTPS with PFX**
- Given: `enabled: true`, `pfxPath: "cert.pfx"`, `pfxPassword: "secret"`
- When: Application starts
- Then: Certificate loaded from PFX
- And: HTTPS listener configured
- And: Requests use HTTPS
- Note: the PFX password survives restarts only via appsettings.json (§4.5: never persisted
  by the API); after restart without it, startup degrades to an HTTP listener with a clear
  message (no crash)

**Scenario 4.2.3: Enable HTTPS with PEM**
- Given: `enabled: true`, `pemCertPath: "cert.pem"`, `pemKeyPath: "key.pem"` (fullchain file
  accepted: leaf + issuing CA concatenated)
- When: Application starts
- Then: Certificate loaded from PEM
- And: HTTPS listener configured

**Scenario 4.2.4: Enable HTTPS with Certificate Store**
- Given: `enabled: true`, `thumbprint: "<hex>"` (or `subjectName: "CN=localhost"`),
  `storeLocation`, `storeName: "My"`
- When: Application starts
- Then: Certificate loaded from the store — the configured location is searched first, then
  the other location (`CurrentUser` ↔ `LocalMachine` fallback)
- And: A key-less store entry is skipped; the first match WITH a private key is served
- And: A total miss produces a detailed error (thumbprint case/format guidance) —
  startup degrades to HTTP-only (no crash)
- Note: cross-platform (SChannel on Windows, OpenSSL-backed store on Linux/macOS)

**Scenario 4.2.5: Requires Restart**
- Given: HTTPS disabled
- When: `PUT /api/config/https { enabled: true, pfxPath: "cert.pfx", pfxPassword: "secret" }`
- Then: Returns 200 with `requiresRestart: true`
- And: HTTPS not active until restart

### 4.3 HTTP Protocol Selection

**Scenario 4.3.1: HTTP/2 Only**
- Given: `protocols: "HTTP2"`
- When: HTTP/1 client connects
- Then: Connection negotiates HTTP/2
- Or: Client falls back (browser support)

**Scenario 4.3.2: HTTP/1 Only**
- Given: `protocols: "HTTP1"`
- When: HTTP/2 client connects
- Then: Uses HTTP/1

**Scenario 4.3.3: HTTP/1 and HTTP/2 (Default)**
- Given: `protocols: "HTTP1_AND_HTTP2"` (default)
- When: HTTP/1 and HTTP/2 clients connect
- Then: Both work (client chooses)

### 4.4 Certificate Loadability (trust is client-side; no AllowInvalidCertificates option)

The server never checks chain trust, expiry, or "invalidity" — browsers enforce that
themselves (`net::ERR_CERT_AUTHORITY_INVALID` is a client trust decision). The server
verifies only that the certificate can actually serve TLS.

**Scenario 4.4.1: Loadable certificate accepted**
- Given: A configured certificate source that loads and has a private key
- When: Application starts / `PUT /api/config/https`
- Then: Certificate accepted; HTTPS served (trust/validity enforced by each client)

**Scenario 4.4.2: Unloadable or key-less certificate rejected**
- Given: Missing cert file, wrong password, or certificate without private key
- When: Application starts / `PUT /api/config/https`
- Then: 422 / HTTP-only fallback with a clear error message

**Scenario 4.4.3: Expired or self-signed certificate SERVED**
- Given: An expired or self-signed (within-period) certificate
- When: Application starts
- Then: HTTPS is served (server does not gate validity); the browser displays its own
  warning/error — the operator imports the issuing CA into the client trust store to clear it

### 4.5 HTTPS API

**Scenario 4.5.1: Get HTTPS Settings**
- When: `GET /api/config/https`
- Then: Returns 200 with settings (password not included)

**Scenario 4.5.2: Update HTTPS Settings (merge-patch)**
- When: `PUT /api/config/https { enabled: true, protocols: "HTTP2", ... }`
- Then: Returns 200 with `requiresRestart: true`
- And: Omitted properties are unchanged; explicit JSON `null` clears a path field
- And: Persisted to `{DataFolder}/https.json` (sanitized — password never written)

**Scenario 4.5.3: https.json Precedence at Startup**
- Given: `{DataFolder}/https.json` exists (written by a prior PUT)
- When: Application starts
- Then: Non-null https.json fields override appsettings.json; null fields do NOT wipe
  appsettings values; a stored `enabled: false` does not disable an appsettings-enabled config
- And: The override is logged loudly at startup (with "delete the file" guidance)

---


### 4.6 Implemented Code Reference (Step 4 — as shipped)

| Concern | File / Member |
|---|---|
| Settings model | `PivotController/Configuration/Models/HttpsSettings.cs` — `Enabled`, `PfxPath`, `PfxPassword` (never persisted/returned), `PemCertPath`, `PemKeyPath`, `StoreLocation`, `StoreName`, `Thumbprint`, `SubjectName`, `Protocols`, `ConfiguredSourceCount`, `Sanitized()` |
| Certificate resolution | `PivotController/Configuration/Services/CertificateService.cs` — `ResolveCertificate` (PFX → PEM → store), `Validate` (source XOR rules), `ResolveValidatedCertificate`/`TryResolveValidatedCertificate` (loadability: private key only), `MapProtocols`, `LoadPemCertificate` (fullchain; Windows in-memory PFX key normalization with safe fallback) |
| Store lookup | `CertificateService.ResolveCertificate` store branch — cross-platform `X509Store` (OpenSSL on Linux/macOS), `CurrentUser` ↔ `LocalMachine` fallback, key-less entries skipped, detailed miss errors |
| Settings service | `PivotController/Configuration/Services/HttpsSettingsService.cs` — merge-patch (explicit-null clears, omission unchanged), `https.json` persistence (sanitized), `LoadPersisted`, PUT-time loadability check (422) |
| API | `PivotController/Configuration/Controllers/HttpsConfigController.cs` — `GET /api/config/https` (no password), `PUT` merge-patch + 422 validation |
| Kestrel wiring | `PivotController/Program.cs` — pre-Build https.json load (guarded precedence + loud log), fail-fast `Validate`, `ConfigureKestrel` → `Listen(Any, General:Port)` + `UseHttps(cert)` + `SslProtocols = Tls12 \| Tls13` + `MapProtocols`; `UseHttpsRedirection` only when enabled; graceful HTTP-only degradation on unloadable certs |
| Test PKI | `PivotController/Cert/PEM-Cert/` — `test-ca.pem` (CA:TRUE, import into client Trusted Root), `test-ca.key`, `server.pem` (CA-signed SAN leaf), `server.key`, `localhost-server-fullchain.pem` (leaf + CA, what the server serves) |
| Verification | `PivotController/verify-https.sh` (33 checks), live store/pem/pfx end-to-end runs; tunnel samples `wwwroot/tunnel-completed.html`, `https-tunnel-simple.html`, `https-tunnel-sample.html`, `data-server.html` (EJ2 35.1.37, `Pivot.Engine` 35.1.41) |

Key resolution order (`ResolveCertificate`): PFX path → PEM pair → store (`thumbprint` XOR `subjectName`, location fallback) → error "no certificate source configured".

Startup (`Program.cs`): appsettings `Https:*` → guarded `https.json` merge (non-null wins; `enabled:true` only; enums always) → `Validate` fail-fast → `TryResolveValidatedCertificate` (loadability) → HTTPS listener or documented HTTP-only degradation.


## 5. Security Settings Configuration (STEP 5) (IMPLEMENTED & VERIFIED)

**Schema (clarified):** Security settings follow the appsettings.json structure below.
Credentials (username/password) are created in the Data Server configuration for now;
they will later be obtained from the Admin Panel (Phase 2).

```json
"Security": {
  "Authorization": {
    "Enabled": true
  },
  "CORS": {
    "AllowOrigin": "https://localhost:4200"
  }
}
```

### 5.1 Configuration Model

```csharp
public class SecuritySettings
{
    public SecurityAuthorizationSettings Authorization { get; set; } = new();
    public SecurityCorsSettings CORS { get; set; } = new();
}

public class SecurityAuthorizationSettings
{
    public bool Enabled { get; set; } = false;
    public string? Username { get; set; }   // Data Server credential (Admin Panel later)
    public string? Password { get; set; }    // Data Server credential (Admin Panel later)
}

public class SecurityCorsSettings
{
    public string AllowOrigin { get; set; } = "*";
}
```

| Property | Default | Validation | Requires Restart |
|----------|---------|------------|:---:|
| **Authorization.Enabled** | false | bool | **No** |
| **Authorization.Username** | — | required when Enabled | **No** |
| **Authorization.Password** | — | required when Enabled | **No** |
| **CORS.AllowOrigin** | "*" | URL, "*" or comma-separated origins | **No** (native CORS policy binds at startup) |

**AllowOrigin rule (Flexmonster UG, clarified):** When basic authorization is
enabled, Allow origin **must** specify proper origin URLs (`http://` / `https://`)
— `*` (allow all origins) is **not permitted**, and a missing/empty AllowOrigin is
a startup failure: the server does not start and reports that AllowOrigin must be
specified. Several origins are separated by a comma. This rule holds in
appsettings.json, in `{DataFolder}/security.json`, and in `PUT /api/config/security`
(which returns 422 for violations). Trailing slashes are part of the configured
value as-is (configure origins without a trailing slash, e.g. `http://localhost:4000`).

### 5.2 Basic Authentication Enable/Disable

**Scenario 5.2.5: Auth Enabled Without Proper AllowOrigin — Startup Failure**
- Given: `authorization.enabled: true` and `cors.allowOrigin` missing, empty, or `"*"`
- When: Application starts
- Then: **Server does not start** (non-zero exit)
- And: Error message states AllowOrigin must be specified with proper origin URLs
  (no `*`) when basic authorization is enabled

**Scenario 5.2.6: Auth Enabled With Proper AllowOrigin**
- Given: `authorization.enabled: true`, `cors.allowOrigin: "http://localhost:4000"`
  (or comma-separated specific origins)
- When: Application starts
- Then: Server starts; only the specified origins receive CORS headers

**Scenario 5.2.7: Auth Disabled — AllowOrigin Unrestricted**
- Given: `authorization.enabled: false`
- Then: `AllowOrigin` may be `*` (default), a single URL, or comma-separated URLs;
  the auth-enabled AllowOrigin restrictions do not apply

**Scenario 5.2.1: Auth Disabled (Default)**
- Given: `authorization.enabled: false`
- When: Request sent without Authorization header
- Then: Processed normally

**Scenario 5.2.2: Auth Enabled**
- Given: `authorization.enabled: true`, credentials configured in the Data Server
- When: Request sent without Authorization header
- Then: Returns 401 Unauthorized with `WWW-Authenticate: Basic realm`

**Scenario 5.2.3: Valid Credentials**
- Given: Auth enabled, Data Server credentials username/password configured
- When: Request with `Authorization: Basic <base64(username:password)>`
- Then: Processed successfully

**Scenario 5.2.4: Invalid Credentials**
- Given: Auth enabled
- When: Request with wrong credentials
- Then: Returns 401 with `WWW-Authenticate: Basic realm`

**Scenario 5.2.8: Enabled Without Credentials — Startup Failure**
- Given: `authorization.enabled: true`, no username/password in Data Server config
- When: Application starts
- Then: **Server does not start** (non-zero exit) with a clear message —
  credentials must be created in the Data Server configuration (Admin Panel later)

**Scenario 5.2.9: Startup Failure From Persisted security.json**
- Given: `{DataFolder}/security.json` enables Authorization with AllowOrigin missing,
  `"*"`, or a non-URL value
- When: Application starts
- Then: **Server does not start**; the persisted Enabled/AllowOrigin override is
  applied before validation, so an invalid persisted state also fails startup

**Scenario 5.2.10: API Rejection (PUT /api/config/security)**
- Given: Authorization currently enabled
- When: `PUT /api/config/security { authorization: { enabled: true }, cors: { allowOrigin: "*" } }`
  (or missing/empty/non-URL allowOrigin)
- Then: Returns 422 with the AllowOrigin rule error; settings unchanged

**Scenario 5.2.11: CORS Preflight Exemption**
- Given: Authorization enabled
- When: Browser sends a CORS preflight OPTIONS request (no Authorization header)
- Then: Preflight is answered with CORS headers (204) — preflights never require
  Basic credentials; only actual requests (GET/POST/...) require them

### 5.3 CORS Origin Validation

**Behavior (clarified):** The Data Server uses **native ASP.NET Core CORS middleware
behavior** — requests failing CORS validation do NOT receive the required CORS headers
and are blocked by the browser according to standard browser CORS enforcement
behavior. The server does NOT return an explicit `403 Forbidden` for CORS-blocked
requests.

**Scenario 5.3.1: Allow All Origins (Default)**
- Given: `allowOrigin: "*"`
- When: Request from any origin
- Then: CORS headers allow all; request proceeds

**Scenario 5.3.2: Specific Origin Only**
- Given: `allowOrigin: "https://localhost:4200"`
- When: Request from https://localhost:4200
- Then: CORS headers allow origin; request proceeds
- When: Request from any other origin
- Then: No CORS headers returned; browser blocks access per standard CORS enforcement

**Scenario 5.3.3: No Origin Header (Non-Browser Client)**
- Given: Any AllowOrigin configuration
- When: Request without Origin header (non-browser client such as curl)
- Then: Request processed normally (CORS is a browser enforcement mechanism)

### 5.4 Security API

**Scenario 5.4.1: Get Security Settings**
- When: `GET /api/config/security`
- Then: Returns 200 with `{ authorization: { enabled }, cors: { allowOrigin } }`
- And: Password is never returned

**Scenario 5.4.2: Update Security Settings**
- When: `PUT /api/config/security { authorization: { enabled: true }, cors: { allowOrigin: "https://localhost:4200" } }`
- Then: Returns 200, applied immediately
- And: Credentials (username/password) come from the Data Server configuration; they
  are not set via this API (Admin Panel later)

---

## 6. Report Sharing Settings Configuration (STEP 6)

### 6.1 Configuration Model (appsettings.json `ReportSharingOptions`)

```json
{
  "ReportSharingOptions": {
    "EndpointEnabled": false,
    "PathToFolder": "DataServer/reports",
    "ReportExpirationTime": 0,
    "MaxSizeOfFolder": 0,
    "AutoDeletingEnabled": false
  }
}
```

- `EndpointEnabled` — enables/disables the report sharing endpoint (default disabled).
- `PathToFolder` — report storage folder. Relative paths resolve against `{DataFolder}`; absolute paths allowed. Path traversal (e.g. `../../`) → 422 (startup fail). Changing requires restart.
- `ReportExpirationTime` — minutes; 0 = reports never expire.
- `MaxSizeOfFolder` — MB; 0 = unlimited storage.
- `AutoDeletingEnabled` — governs ONLY quota eviction (oldest-report removal when `MaxSizeOfFolder` is reached); it does NOT control expiration cleanup.

### 6.2 Endpoint Enable/Disable

**Scenario 6.2.1: Endpoint Disabled (Default)**
- Given: `EndpointEnabled: false`
- When: any `/api/reportsharing/*` report request (`POST`/`GET`/`DELETE`)
- Then: Returns 404 "Report sharing disabled"
- And: `GET /api/config/reportsharing` still returns the settings (config API always available)

**Scenario 6.2.2: Enable Endpoint**
- Given: `EndpointEnabled: false`
- When: `PUT /api/config/reportsharing { "endpointEnabled": true }`
- Then: Returns 200, applied immediately (no restart)
- And: Report endpoints now work

### 6.3 Report Lifecycle

**Scenario 6.3.1: Save Report**
- Given: Endpoint enabled
- When: `POST /api/reportsharing/reports { "name": "Q4-Review", "owner": "analyst@corp", "payload": { ... } }`
- Then: Returns 201 with `{ "reportId": "<new GUID>", "name", "owner", "createdAt", "expiresAt", "sizeBytes" }`
- And: POST ALWAYS creates a new report with a new GUID — no overwrite/update semantics
- And: The payload is an opaque JSON blob — only valid JSON is required; the Pivot Report structure is NOT validated
- And: When the Data Server receives a pivot table request carrying a report payload, the report is stored automatically (server-side save; client-side trigger UI deferred to Step 7)

**Scenario 6.3.2: Retrieve Report**
- Given: Report saved
- When: `GET /api/reportsharing/reports/{reportId}`
- Then: Returns 200 with report payload

**Scenario 6.3.3: List Reports**
- When: `GET /api/reportsharing/reports`
- Then: Returns 200 with metadata (reportId, name, owner, createdAt, expiresAt, sizeBytes)
- And: Payloads not included

**Scenario 6.3.4: Delete Report**
- When: `DELETE /api/reportsharing/reports/{reportId}`
- Then: Returns 204

### 6.4 Report Expiration (independent cleanup mechanism)

Expiration is a SEPARATE cleanup mechanism from Auto-Delete. When `ReportExpirationTime > 0`, expired reports are ALWAYS removed during the cleanup sweep — regardless of `AutoDeletingEnabled`.

**Scenario 6.4.1: Permanent Reports (Default)**
- Given: `ReportExpirationTime: 0`
- When: Report saved
- Then: `expiresAt: null`
- And: Report never expires

**Scenario 6.4.2: Reports with Expiration**
- Given: `ReportExpirationTime: 1440` (24 hours)
- When: Report saved at 10:00 AM
- Then: `expiresAt: tomorrow 10:00 AM`

**Scenario 6.4.3: Read Expired Report**
- Given: Report expired
- When: `GET /api/reportsharing/reports/{reportId}`
- Then: Returns 410 Gone

**Scenario 6.4.4: Expired Reports Removed on Cleanup (regardless of AutoDeletingEnabled)**
- Given: `ReportExpirationTime: 60`, report saved 10:00 AM
- When: Cleanup sweep runs (every 5 minutes) after 11:00 AM
- Then: Report file is physically deleted
- And: Works identically with `AutoDeletingEnabled: true` and `AutoDeletingEnabled: false`

### 6.5 Storage Quota

**Scenario 6.5.1: Unlimited Quota (Default)**
- Given: `MaxSizeOfFolder: 0`
- When: Reports saved
- Then: No size limit enforced

**Scenario 6.5.2: Quota Exceeded — Auto-Delete Disabled**
- Given: `MaxSizeOfFolder: 100` (MB), `AutoDeletingEnabled: false`, folder at 95 MB
- When: New 20 MB report save attempted
- Then: Returns 507 Insufficient Storage
- And: Existing reports are NOT removed

**Scenario 6.5.3: Quota Exceeded — Auto-Delete Enabled (oldest evicted)**
- Given: `MaxSizeOfFolder: 100`, `AutoDeletingEnabled: true`, folder at 95 MB
- When: New 20 MB report save attempted
- Then: Oldest reports are deleted until enough space is available
- And: New report saved successfully (201)
- And: Folder size stays under the quota
- And: If ALL existing reports are evicted and the new report STILL exceeds the quota → 507

### 6.6 Auto-Delete Eviction (quota management ONLY)

`AutoDeletingEnabled` governs ONLY the Max-Folder-Size eviction policy. It does NOT affect expiration cleanup (§6.4).

**Scenario 6.6.1: Auto-Delete Disabled**
- Given: `AutoDeletingEnabled: false`
- When: Folder quota exceeded on save
- Then: Save returns 507; nothing is auto-removed

**Scenario 6.6.2: Auto-Delete Enabled**
- Given: `AutoDeletingEnabled: true`, `MaxSizeOfFolder: 100`
- When: Cleanup sweep runs every 5 minutes
- Then: Expired reports deleted (§6.4, regardless of this flag)
- And: If folder size exceeds quota, oldest reports evicted until under limit

### 6.7 Report Sharing Settings API

**Scenario 6.7.1: Get Report Sharing Settings**
- When: `GET /api/config/reportsharing`
- Then: Returns 200 with `{ endpointEnabled, pathToFolder, reportExpirationTime, maxSizeOfFolder, autoDeletingEnabled }`

**Scenario 6.7.2: Update Report Sharing Settings**
- When: `PUT /api/config/reportsharing { "endpointEnabled": true, "maxSizeOfFolder": 500, "autoDeletingEnabled": true }`
- Then: Returns 200, applied immediately (except `pathToFolder`, which requires restart)
- And: Negative `reportExpirationTime`/`maxSizeOfFolder` → 422
- And: `pathToFolder` with path traversal (`../../`) → 422

### 6.8 Settings Persistence & Precedence

- Persisted settings are written to `{DataFolder}/reportsharing.json` by `PUT /api/config/reportsharing` (same atomic temp-rename store as other settings).
- Precedence model (same as Steps 2–5): non-null fields in `reportsharing.json` override appsettings.json `ReportSharingOptions`; missing/null persisted fields keep the appsettings values; no persisted file → appsettings values stand.
- A persisted `endpointEnabled`/`reportExpirationTime`/`maxSizeOfFolder`/`autoDeletingEnabled` applies at startup (load-before-listen); `pathToFolder` is resolved relative to `{DataFolder}` when not absolute.
- Startup validation: invalid values (negative numbers, traversing `pathToFolder`) fail fast with a clear message (exit 1).

---

## 7. Pivot Request Routing & Cache Integration (STEP 8 - Final: Client-Side)

**FINAL STEP:** Client sends index name; controller routes to correct index.

### 7.1 Controller-Side Testing vs Client-Side Integration

**IMPORTANT:** During Phase 1 development:
1. **Test Phase (Early):** Declare index name directly in controller to verify indexing works
2. **Integration Phase (Final):** Client sends index name in request body

### 7.2 Client-Side Index Declaration (Final Step - Step 8)

**Scenario 7.2.1: Client Sends Index Name**
- Given: Multiple indexes exist
- When: Client sends `POST /api/pivot/post { index: "Sales-Data", action: "fetchFieldMembers", ... }`
- Then: Controller routes to "Sales-Data" index
- And: Pivot operations use correct data

**Scenario 7.2.2: Backward Compatibility (No Index Sent)**
- Given: `DefaultIndexId: "default"` configured
- When: Client sends `POST /api/pivot/post { action: "..." }` (no index)
- Then: Routes to "default" index

**Scenario 7.2.3: Pivot Sample Configuration (JavaScript)**
```javascript
// Sample/pivot.js
const PIVOT_INDEX = 'Sales-Data';              // ← declare index
const PIVOT_URL = 'http://localhost:5285/api/pivot/post';

var pivotGridObj = new ej.pivotview.PivotView({
  dataSourceSettings: {
    url: PIVOT_URL,
    mode: 'Server',
    type: 'API',
    index: PIVOT_INDEX,                        // ← send with every request
    // ... rest of config
  }
});
```

---

## 8. Error Handling & Validation

### 8.1 HTTP Status Codes

| Status | Condition | Example |
|--------|-----------|---------|
| **200** | Success | Index loaded, config updated |
| **201** | Created | Index/report created |
| **204** | No content | Delete successful |
| **400** | Bad request | Unknown setting key |
| **401** | Unauthorized | Bad credentials |
| **403** | Forbidden | CORS blocked, path traversal |
| **404** | Not found | Index/report not found |
| **409** | Conflict | Duplicate index name |
| **422** | Validation error | Invalid port, SourceType immutable |
| **423** | Disabled | Index disabled, report endpoints disabled |
| **502** | Bad gateway | Database unavailable, cert loading failed |
| **504** | Gateway timeout | Query exceeded timeout |
| **507** | Insufficient storage | Quota exceeded |

### 8.2 Configuration Validation Rules

| Setting | Rule | Error |
|---------|------|-------|
| Port | 1–65535 | 422 if out of range | ✅ implemented |
| MinimumLogLevel | Info/Warn/Error/Fatal | 422 if invalid | ✅ implemented |
| DataFolder | non-empty, writable, no path traversal | 422 if invalid, Fatal if not writable at startup | ✅ implemented |
| RetryAttempts | 0–30 | 422 if out of range | ✅ implemented |
| CacheSizeLimit | >= 0 (0 disables response caching; per-index LRU) | 422 if negative | ✅ implemented |
| KeepDataOnRefresh | bool | — | ✅ implemented |
| MaxCacheItemSizeBytes | >= 0 (0 = unlimited; applies when CacheSizeLimit > 0) | 422 if negative | ✅ implemented |
| CommandTimeoutEnabled | bool | — |
| CommandTimeLimit | >= 0 | 422 if negative |
| Https.Enabled | bool | 422 if no cert source configured |
| BasicAuthEnabled | bool | — |
| AllowedOrigins | array | — |
| EndpointEnabled | bool | — |
| StoragePath | writable, within DataFolder | 422 if invalid |
| ReportExpirationTimeMinutes | >= 0 | 422 if negative |
| MaxFolderSizeMB | >= 0 | 422 if negative |
| AutoDelete | bool | — |

---

## 9. Testing Strategy for Phase 1

### 9.1 Controller-Side Index Testing (Before Client Integration)

**Purpose:** Verify indexing works before adding client-side complexity.

**Setup:**
1. Declare index name directly in `PivotController.cs`
2. No client-side index parameter yet
3. Test all pivot actions with declared index

**Test Cases:**
- ✅ Index loads correctly on startup
- ✅ Pivot requests route to correct index
- ✅ Field members retrieved from correct index
- ✅ Raw data returned from correct index
- ✅ Reload works (manual + auto)
- ✅ Enable/disable works
- ✅ Field mapping works
- ✅ CSV separators work

**Example Controller Code (Phase 1 Testing):**
```csharp
[Route("/api/pivot/post")]
[HttpPost]
public async Task<object> Post([FromBody] object args)
{
    // PHASE 1 TESTING: Declare index directly
    const string indexName = "Sales-Data";  // ← testing value, not from client
    
    FetchData param = JsonConvert.DeserializeObject<FetchData>(args.ToString());
    
    // Verify index exists
    var indexStatus = await _indexManager.GetIndexStatus(indexName);
    if (indexStatus == null) return StatusCode(404, "Index not found");
    if (indexStatus.Status == IndexStatus.Disabled) return StatusCode(423, "Index disabled");
    
    // Get data for this index
    var data = await _indexManager.GetDataAsync(indexName);
    
    // Route action to correct index with proper cache prefix
    // ... rest of implementation
}
```

### 9.2 Index Management Test Cases

**Reload Verification:**
- [ ] Manual reload: `POST /api/indexes/{index}/reload` updates data
- [ ] Auto-reload: Scheduled reload runs at configured interval
- [ ] Reload with keep-data-on-refresh: Old data serves on failure
- [ ] Reload with keep-data-on-refresh disabled: Cache cleared on failure

**Enable/Disable Verification:**
- [ ] Disable index: Pivot requests return 423
- [ ] Enable index: Data reloads, pivot requests work
- [ ] Status transitions correctly (Ready → Disabled → Ready)

**Refresh Verification:**
- [ ] Data updates when source changes
- [ ] Field count matches updated source
- [ ] Mapping applied correctly after refresh
- [ ] CSV separators applied correctly

### 9.3 Client-Side Integration Test (Step 8 - Final)

**After all backend features working:**
1. Update `Sample/pivot.js` to send index name
2. Test with multiple indexes
3. Test fallback to DefaultIndexId
4. Verify backward compatibility

---

**END OF SPECIFICATION**
rify backward compatibility

---

**END OF SPECIFICATION**
