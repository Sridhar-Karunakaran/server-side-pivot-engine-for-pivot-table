# Syncfusion Pivot Data Server — Phase 1 Design

**Date:** 2024-12-19

---

## Design Overview

The Syncfusion Pivot Data Server is implemented as a layered, service-oriented architecture within the existing ASP.NET Core application. All components integrate with the current `PivotController` structure while introducing new services and data providers that enable multi-source, multi-index data management.

---

## Layer Architecture

```
┌────────────────────────────────────────────────────┐
│  HTTP Pipeline (Kestrel)                           │
│  - Port configuration                              │
│  - HTTPS/Protocols from settings                   │
│  - CORS middleware (from Security.AllowedOrigins)  │
│  - BasicAuth middleware (if Security.Enabled)     │
└──────────────────────┬─────────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────────┐
│  API Controllers                                   │
│  - PivotController (/api/pivot/post)              │
│  - ConfigController (/api/config/*)               │
│  - IndexController (/api/indexes/*)               │
│  - SecurityController (/api/security/users)       │
│  - ReportsController (/api/reportsharing/*)       │
│  - HealthController (/health)                     │
└──────────────────────┬─────────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────────┐
│  Application Services                              │
│  ├─ ConfigService (update orchestration)          │
│  ├─ ConfigurationPersistenceService (load/save)   │
│  ├─ ConfigurationFileWatcher (hot-reload)         │
│  ├─ IndexManager (IIndexManager - cache hub)      │
│  ├─ UserService (CRUD + hashing)                  │
│  ├─ ReportSharingService (save/retrieve/quota)    │
│  ├─ CertificateService (certificate resolution)   │
│  └─ OriginValidator (CORS enforcement)            │
└──────────────────────┬─────────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────────┐
│  Data Access Layer                                 │
│  ├─ IDataSourceProvider (factory pattern)          │
│  │  ├─ JsonDataProvider                           │
│  │  ├─ CsvDataProvider                            │
│  │  └─ DatabaseDataProvider                       │
│  ├─ IDatabaseQueryProvider (abstraction)           │
│  │  └─ SqliteQueryProvider (+ optional others)    │
│  ├─ IIndexStore (metadata persistence)            │
│  ├─ MemoryCache (SizeLimit: CacheSizeLimit)       │
│  └─ RetryPolicy (exponential backoff)             │
└──────────────────────┬─────────────────────────────┘
                       │
┌──────────────────────▼─────────────────────────────┐
│  Persistence & Infrastructure                      │
│  ├─ AtomicJsonStore (config/indexes/users)        │
│  ├─ RollingFileLogger (JSON format, 30d retention)│
│  ├─ FileSystemWatcher (config.json changes)       │
│  └─ Problem Details Handler (RFC 7807)            │
└────────────────────────────────────────────────────┘
```

---

## Component Design

### 1. IndexManager (IIndexManager)

**Responsibility:** Central cache hub for all index data; manages index lifecycle

**Key Methods:**
```csharp
Task<IReadOnlyList<ExpandoObject>> GetDataAsync(string indexName, CancellationToken ct)
Task<IndexStatus> CreateIndexAsync(IndexDefinition definition, CancellationToken ct)
Task<IndexStatus> EditIndexAsync(string indexName, IndexDefinition patch, CancellationToken ct)
Task<IndexStatus> ReloadIndexAsync(string indexName, CancellationToken ct)
Task<IndexStatus> EnableIndexAsync(string indexName, CancellationToken ct)
Task<IndexStatus> DisableIndexAsync(string indexName, CancellationToken ct)
Task DeleteIndexAsync(string indexName)
IndexStatus GetIndexStatus(string indexName)
IReadOnlyList<IndexStatus> ListIndexes()
```

**Cache Keys:**
- Data: `data:{index}` (one slot per index)
- Engine: `engine:{index}:{parameter.Hash}` (existing pattern, prefixed by index)

**Status Values:** `Ready | Refreshing | Failed | Disabled`

**Key Features:**
- Per-index SemaphoreSlim for concurrent reload protection
- Keep-data-on-refresh: dual-slot cache entry (current + new)
- Auto-reload via PeriodicTimer per index
- Retry policy on transient failures

### 2. Data Provider Pattern

**Interface Hierarchy:**
```csharp
public interface IDataSourceProvider
{
    Task<IReadOnlyList<ExpandoObject>> LoadAsync(
        IndexDefinition definition, 
        DatabaseQuerySettings querySettings,
        CancellationToken ct);
}

// Implementations
public class JsonDataProvider : IDataSourceProvider
public class CsvDataProvider : IDataSourceProvider
public class DatabaseDataProvider : IDataSourceProvider

// Database-specific
public interface IDatabaseQueryProvider
{
    DataSourceType SourceType { get; }
    DbConnection CreateConnection(string connectionString);
}

public class SqliteQueryProvider : IDatabaseQueryProvider
// Optional: SqlServerQueryProvider, PostgresqlQueryProvider, MySqlQueryProvider, OracleQueryProvider, MariadbQueryProvider
```

**Factory Pattern:**
```csharp
public class DataSourceProviderFactory
{
    public IDataSourceProvider CreateProvider(DataSourceType sourceType)
    {
        return sourceType switch
        {
            DataSourceType.Json => _jsonProvider,
            DataSourceType.Csv => _csvProvider,
            DataSourceType.SqlServer | DataSourceType.PostgreSQL | ... => _dbProvider,
            _ => throw InvalidOperationException("Unsupported source type")
        };
    }
}
```

### 3. Configuration Service

**ConfigService Orchestration:**
```
User calls PUT /api/config
  ↓
ConfigController.UpdateConfig(patch)
  ↓
ConfigService.UpdateAsync(patch)
  ├─ Merge patch into current config
  ├─ Validate merged config (ConfigurationValidator)
  ├─ Persist to DataServer/config.json (atomic write)
  ├─ Apply runtime changes (per-option rules)
  └─ Return { config, requiresRestart }
```

**Per-Option Rules:**
- **Runtime Apply:** Security settings, ReportSharing.Enabled, MinimumLogLevel, DatabaseQuery.*, Indexes
- **Requires Restart:** General.Port, General.DataFolder, Https.*, DataStorage.CacheSizeLimit

### 4. Configuration Persistence

**AtomicJsonStore:**
```csharp
public class AtomicJsonStore
{
    // Atomic write with temp-file-rename
    public void Save<T>(string filePath, T obj, JsonSerializerOptions? options = null)
    {
        var tempFile = filePath + ".tmp";
        File.WriteAllText(tempFile, JsonSerializer.Serialize(obj, options));
        File.Replace(tempFile, filePath, filePath + ".corrupt." + Timestamp);
    }
    
    // Load with corruption handling
    public T Load<T>(string filePath, T defaultValue)
    {
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(filePath)); }
        catch (Exception ex)
        {
            Logger.Error($"Corrupt file {filePath}, quarantined");
            File.Move(filePath, filePath + ".corrupt." + Timestamp);
            return defaultValue;
        }
    }
}
```

**Files Managed:**
- `DataServer/config.json` — All 7 settings sections + Indexes
- `DataServer/indexes.json` — Index metadata only
- `DataServer/users.json` — User list (username, hash, timestamps)
- `DataServer/reports/*.json` — Report payloads
- `DataServer/reports/*.meta.json` — Report metadata

### 5. Security Layer

**User Model:**
```csharp
public class User
{
    public string Username { get; set; }
    public string PasswordHash { get; set; } // PBKDF2$100000$SHA256$saltB64$hashB64
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

**Password Hashing (PBKDF2, built-in):**
```csharp
public class PBKDF2PasswordHasher
{
    public string Hash(string password, int iterations = 100000)
    {
        // Generate random salt
        byte[] salt = new byte[16];
        using (var rng = RandomNumberGenerator.Create())
            rng.GetBytes(salt);
        
        // Hash with PBKDF2
        using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256))
        {
            byte[] hash = pbkdf2.GetBytes(32);
            return $"PBKDF2${iterations}$SHA256${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }
    }
    
    public bool Verify(string password, string hash)
    {
        var parts = hash.Split('$');
        // Constant-time comparison
        return ConstantTimeComparer.Compare(ComputeHash(password, parts), hash);
    }
}
```

**Basic Auth Middleware:**
```
Request with Authorization header
  ├─ Decode Base64
  ├─ Split on first ':'
  ├─ Lookup user
  ├─ Verify password (constant-time)
  └─ Set ClaimsPrincipal or return 401
```

**CORS Origin Validator:**
- AllowedOrigins=["*"] → AllowAnyOrigin
- Otherwise, exact match against list
- No-Origin requests rejected (CSRF defense)

### 6. Index Configuration Bootstrap

**Startup Sequence (Program.cs):**
```csharp
// Step 1: Ensure persistence files exist
await ConfigurationPersistenceService.EnsureFilesAsync();

// Step 2: Load/validate configuration
var config = await ConfigurationPersistenceService.LoadAsync();

// Step 3: One-time legacy migration
if (LegacyPivotDataSourceExists)
{
    var defaultIndex = MigrateToDefaultIndex();
    config.Indexes.Add(defaultIndex);
    await ConfigurationPersistenceService.SaveAsync(config);
}

// Step 4: Register all enabled indexes
await IndexManager.RegisterAllAsync(config.Indexes);

// Step 5: Configure Kestrel
builder.WebHost.ConfigureKestrel((context, options) =>
{
    options.Listen(IPAddress.Any, config.General.Port, listenOptions =>
    {
        if (config.Https.Enabled)
        {
            var cert = CertificateService.ResolveCertificate(config.Https);
            listenOptions.UseHttps(cert);
        }
        listenOptions.Protocols = config.Https.Protocols.ToHttpProtocols();
    });
});

// Step 6: Configure services
builder.Services.AddSingleton(config);
builder.Services.AddScoped<IIndexManager>(sp => new IndexManager(
    cache: sp.GetRequiredService<IMemoryCache>(),
    config: sp.GetRequiredService<DataServerConfiguration>(),
    factory: sp.GetRequiredService<DataSourceProviderFactory>(),
    logger: sp.GetRequiredService<ILogger<IndexManager>>()
));
```

### 7. Hot-Reload Configuration Watcher

**ConfigurationFileWatcher:**
```csharp
public class ConfigurationFileWatcher : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        var watcher = new FileSystemWatcher(Path.Combine(AppContext.BaseDirectory, "DataServer"))
        {
            Filter = "config.json",
            NotifyFilter = NotifyFilters.LastWrite
        };
        
        watcher.Changed += async (s, e) =>
        {
            // Debounce: wait 500ms for editor write operations to complete
            await Task.Delay(500, ct);
            
            // Load and validate new config
            var newConfig = AtomicJsonStore.Load<DataServerConfiguration>(e.FullPath, _currentConfig);
            
            if (newConfig == null || newConfig.ConfigVersion != "1.0")
            {
                Logger.Error("Invalid config.json; reverting to last-known-good");
                return;
            }
            
            // Compute delta
            var delta = ComputeDelta(_currentConfig, newConfig);
            
            // Apply runtime-applicable options
            foreach (var change in delta)
            {
                var requiresRestart = !ApplyRuntime(change);
                if (requiresRestart)
                    Logger.Warn($"Setting {change.Key} requires restart to take effect");
            }
            
            _currentConfig = newConfig;
            Logger.Info($"Config reloaded: applied {delta.Count} changes");
        };
        
        watcher.EnableRaisingEvents = true;
    }
}
```

### 8. Index Lifecycle

**Create Index:**
```
POST /api/indexes { index, name, sourceType, ... }
  ├─ Validate definition
  ├─ Check uniqueness (index name)
  ├─ Persist metadata to indexes.json (atomic)
  ├─ Create provider by sourceType
  ├─ LoadAsync(def, timeout) with retry
  │  ├─ Success → Set(data:{index}, rows) → 201 { status: Ready, rowCount }
  │  ├─ Transient → Retry (backoff, ≤ RetryAttempts)
  │  └─ Persistent → 502 { status: Failed, lastError }
  └─ Return IndexStatus
```

**Edit Index:**
```
PUT /api/indexes/{index} { ...patch... }
  ├─ Load current definition
  ├─ sourceType changed? → 422 (immutable)
  ├─ Merge patch (all fields except sourceType)
  ├─ Persist metadata → Reload(index) semantics → 200
  └─ Return IndexStatus
```

**Reload Index:**
```
POST /api/indexes/{index}/reload
  ├─ Acquire per-index SemaphoreSlim
  ├─ If KeepDataOnRefresh: Pin old cache entry
  ├─ LoadAsync(def, timeout) with retry
  │  ├─ Success → Atomic cache swap
  │  └─ Failure && KeepDataOnRefresh → Log error; serve old data
  ├─ Return 200 { status: Ready/Failed, rowCount, lastError }
  └─ Release semaphore
```

**Enable/Disable Index:**
```
POST /api/indexes/{index}/disable
  ├─ Evict data:{index} and engine:{index}:*
  ├─ Mark metadata Enabled=false
  ├─ Persist to indexes.json
  └─ Return 200 { status: Disabled }

POST /api/indexes/{index}/enable
  ├─ Mark metadata Enabled=true
  ├─ Persist to indexes.json
  ├─ LoadAsync(...) with retry
  └─ Return 200 { status: Ready/Failed, rowCount }
```

### 9. Database Data Source Integration (REVISED 2026-10 — fetching/indexing layer, verified live; independent of the Command timeout / Command time limit step)

**Provider Abstraction (provider-agnostic registry keyed by `DatabaseType`):**
```csharp
public interface IDatabaseQueryProvider
{
    DatabaseType DatabaseType { get; }
    DbConnection CreateConnection(string connectionString);
}

// Registry (DI-registered; optional providers feature-detected)
SqliteQueryProvider      // Microsoft.Data.Sqlite   — verified (live PoC)
PostgresqlQueryProvider  // Npgsql                  — verified (live PoC vs PostgreSQL 17)
MySqlQueryProvider       // MySqlConnector          — optional stub
MariadbQueryProvider     // MySqlConnector          — optional stub
SqlServerQueryProvider   // Microsoft.Data.SqlClient— optional stub
OracleQueryProvider      // Oracle.ManagedDataAccess.Core — optional stub

// Implementation example (Npgsql — the provider mandated by the Syncfusion PostgreSQL UG)
public class PostgresqlQueryProvider : IDatabaseQueryProvider
{
    public DatabaseType DatabaseType => DatabaseType.PostgreSQL;
    public DbConnection CreateConnection(string connectionString)
        => new NpgsqlConnection(connectionString);
}
```
If a requested provider's NuGet package is not present, `Create(DatabaseType)` returns
501 "provider not available" — never a crash (verified requirement 4.1 of tasks.md).

**Connection String Resolution (per user direction 2026-10):** inline `ConnectionString` on the
index definition takes precedence; `ConnectionStringName` from `ConnectionStrings` in
appsettings/user-secrets (PostgreSQL UG: `Host`, `Port`, `Database`, `Username`, `Password`) is the
fallback. At least one is required. Connection string values are never logged.

**Query Rules:** single read-only SELECT only; no `;` multi-statements; DML/DDL keywords
rejected (422). No write-backs — server-side pivot (filter/sort/aggregate/report) is read-only
over the fetched snapshot.

**Query Execution Pipeline:**
```
Validate Query (single SELECT, no ';', no DML/DDL)        [422 on create/edit]
  ↓
Resolve ConnectionString (config:ConnectionStrings / user-secrets, by name)
  ↓
CreateConnection via IDatabaseQueryProvider for definition.DatabaseType   [501 if pkg absent]
  ↓
OpenAsync(timeout budget starts; retried per DataStorage.RetryAttempts w/ backoff)
  ↓
CommandTimeout = DatabaseQuery.CommandTimeLimit (when CommandTimeoutEnabled; 0 = infinite)
  ↓
ExecuteReaderAsync(ct)
  ├─ Timeout → Cancel command → DatabaseTimeoutException → Retry logic (504 surface)
  ├─ Transient → Retry (backoff)
  └─ Success → Map to List<ExpandoObject>
    ├─ Column names = property names; duplicate/blank → load failure
    ├─ DBNull → null; types from driver (or FieldMappings when MappingEnabled)
    └─ Return rows → IndexManager (identical pipeline to CSV/JSON)
```

**Verified live (PostgreSQL 17 + Npgsql, sandbox PoC):** connection by connection string to
`salesdb`, parameterized `SELECT * FROM salesdata WHERE quantity > @min` → 7 rows mapped to
`{ country, product, year, quantity }` ExpandoObjects (identical shape to CSV/JSON providers);
`CommandTimeout=1` on `SELECT pg_sleep(60)` canceled the command as required for §3.2 timeout
semantics. This confirms the architecture supports PostgreSQL retrieval through connection
strings and query execution with zero pipeline changes — only the provider class differs.

**Query Execution Pipeline:**
```
Validate Query (single SELECT, no ';')
  ↓
Resolve ConnectionString (from config:ConnectionStrings or user-secrets)
  ↓
OpenAsync(timeout budget starts; retried per policy)
  ↓
CommandTimeout = DatabaseQuery.CommandTimeLimit (when enabled; 0 = infinite)
  ↓
ExecuteReaderAsync(ct)
  ├─ Timeout → Cancel command → DatabaseTimeoutException → Retry logic
  ├─ Transient → Retry (backoff)
  └─ Success → Map to IReadOnlyList<ExpandoObject>
    ├─ Validate headers (no duplicates, no blanks)
    ├─ Convert types per Mapping (if enabled)
    └─ Return rows
```

### 10. Report Sharing Architecture

**Configuration (appsettings.json `ReportSharingOptions`, persisted to `{DataFolder}/reportsharing.json`):**
```
{ "EndpointEnabled": false, "PathToFolder": "DataServer/reports",
  "ReportExpirationTime": 0, "MaxSizeOfFolder": 0, "AutoDeletingEnabled": false }
```
- Relative `PathToFolder` resolves against `{DataFolder}`; path traversal → 422 / startup fail-fast.
- Payloads are opaque JSON blobs (validity-checked only, no Pivot Report structure validation).
- POST always creates a new GUID — no overwrite.

**Storage Layout:**
```
{DataFolder}/{PathToFolder}/
├── {reportId}.json           # report payload (opaque)
└── {reportId}.meta.json      # { reportId, name, owner, createdAt, expiresAt, sizeBytes }
```

**Expiry & Quota Sweep (BackgroundService — every 5 minutes):**
1. **Expiration cleanup (ALWAYS, independent of AutoDeletingEnabled):** delete every report
   whose `ExpiresAt < UtcNow` (`ExpiresAt == null` → permanent, never deleted).
2. **Quota eviction (only when `MaxSizeOfFolder > 0 && AutoDeletingEnabled`):** if total folder
   size exceeds the quota, delete oldest (`CreatedAt`-first) reports until under the limit.

```csharp
public class ReportSweepWorker : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // 1. Expiration cleanup — runs regardless of AutoDeletingEnabled
            var expired = LoadReportMetadata().Where(m => m.ExpiresAt < DateTime.UtcNow).ToList();
            foreach (var report in expired)
                DeleteReport(report.ReportId);

            // 2. Quota eviction — only when AutoDeletingEnabled manages the storage limit
            if (_settings.AutoDeletingEnabled && _settings.MaxSizeOfFolder > 0)
            {
                var maxBytes = _settings.MaxSizeOfFolder * 1024L * 1024L;
                var reports = LoadReportMetadata().OrderBy(m => m.CreatedAt).ToList();
                while (CalculateFolderSize() > maxBytes && reports.Count > 0)
                {
                    DeleteReport(reports[0].ReportId);
                    reports.RemoveAt(0);
                }
            }
            await Task.Delay(TimeSpan.FromMinutes(5), ct);
        }
    }
}
```

**Save-time quota enforcement (synchronous, in ReportSharingService):**
```
POST /api/reportsharing/reports
  ├─ EndpointEnabled=false? → 404 "Report sharing disabled"
  ├─ MaxSizeOfFolder == 0 → save (unlimited)
  ├─ AutoDeletingEnabled=true → evict oldest until the new report fits; still not → 507
  └─ AutoDeletingEnabled=false → new report would exceed quota? → 507 Insufficient Storage
```

**Read Semantics:**
```
GET /api/reportsharing/reports/{reportId}
  ├─ Load metadata
  ├─ ExpiresAt < now? → 410 Gone
  ├─ Load payload
  └─ Return 200
```

### 11. PivotController Index-Aware Routing

**Modified POST Handler:**
```csharp
[HttpPost("post")]
public async Task<object> Post([FromBody] object args)
{
    var param = JsonConvert.DeserializeObject<FetchData>(args.ToString());
    
    // NEW: Extract index from FetchData; fall back to DefaultIndexId
    string indexName = param.Index ?? _config.DefaultIndexId;
    
    // Resolve index
    var indexStatus = _indexManager.GetIndexStatus(indexName);
    if (indexStatus == null)
        return Problem(status: 404, detail: $"Index '{indexName}' not found");
    if (indexStatus.Status == IndexStatus.Disabled)
        return Problem(status: 423, detail: "Index is disabled");
    
    // Get data for this index (existing logic, prefixed by index)
    var data = await _indexManager.GetDataAsync(indexName, ct);
    
    // Continue with existing pivot processing
    _pivotEngine.Data = data;
    EngineProperties engine = await GetEngine(param, indexName);  // prefix cache key
    
    // ... existing switch(param.Action) ...
}

// Helper to prefix cache keys
private async Task<EngineProperties> GetEngine(FetchData param, string indexName)
{
    var cacheKey = $"engine:{indexName}:{param.Hash}";  // NEW: index prefix
    return await _cache.GetOrCreateAsync(cacheKey, async (entry) =>
    {
        // ... existing logic ...
    });
}
```

### 12. Error Handling (RFC 7807 Problem Details)

**Exception Filter:**
```csharp
public class DataServerExceptionFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        var problem = new ProblemDetails
        {
            Type = "https://dataserver.local/errors/" + ex.GetType().Name,
            Title = ex.GetType().Name,
            Detail = ex.Message,
            Status = MapExceptionToStatus(ex),
            Extensions = new Dictionary<string, object>
            {
                { "category", ex is ConfigException ? "Config" : "General" },
                { "correlationId", context.HttpContext.TraceIdentifier }
            }
        };
        
        if (ex is ValidationException valEx)
            problem.Extensions["errors"] = valEx.Errors;  // field-level details
        
        context.Result = new ObjectResult(problem) { StatusCode = problem.Status };
    }
}
```

### 13. Logging Infrastructure

**Rolling File Logger:**
```csharp
public class RollingFileLoggerProvider : ILoggerProvider
{
    // Logs to logs/dataserver-{yyyyMMdd}.jsonl
    // 30-day retention
    // JSON format: { timestamp, level, category, message, correlationId, ... }
}
```

**Log Categories:**
```
DataServer.Config
DataServer.Security
DataServer.Indexing
DataServer.DatabaseQuery
DataServer.ReportSharing
DataServer.Https
DataServer.Pivot
```

**Minimum Level Filter:** Driven by `General.MinimumLogLevel` (Info/Warn/Error/Fatal); framework logs pinned to Warning.

---

## File-by-File Changes

### Modified Files

1. **Program.cs**
   - Bootstrap configuration and index registration
   - Kestrel configuration (port, HTTPS, protocols)
   - CORS setup from Security.AllowedOrigins
   - DI registration of all managers and services
   - Health endpoint
   - Logging configuration with rolling file provider
   - Cache sizing from DataStorage.CacheSizeLimit

2. **Controllers/PivotController.cs**
   - Extract `FetchData.Index`
   - Route to `IIndexManager`
   - Prefix cache keys with index
   - Handle 404/423 responses

3. **appsettings.json**
   - Add `DataServer` section
   - Keep legacy `PivotDataSource` for one-time migration

4. **Sample/pivot.js** (final step)
   - Define `PIVOT_INDEX` constant
   - Pass `index` field in every request

### New Files (~40 total)

**Configuration/**
- `Models/DataServerConfiguration.cs` (root model with all 7 sections)
- `Models/GeneralSettings.cs`, `DataStorageSettings.cs`, etc.
- `Models/IndexDefinition.cs`, `IndexFieldMapping.cs`
- `Validation/ConfigurationValidator.cs` (per-section and cross-field rules)
- `Services/ConfigurationService.cs` (update orchestration)
- `Services/ConfigurationPersistenceService.cs` (load/save atomic)
- `Services/ConfigurationFileWatcher.cs` (hot-reload, file watching)
- `Controllers/ConfigController.cs` (GET /api/config, PUT /api/config)

**Core/**
- `Persistence/AtomicJsonStore.cs` (atomic writes)
- `Persistence/JsonFileRepository.cs` (user and index repositories)
- `Retry/RetryPolicy.cs` (exponential backoff)
- `Security/PBKDF2PasswordHasher.cs`
- `Security/ConstantTimeComparer.cs`
- `Security/OriginValidator.cs`
- `Security/PivotBasicAuthMiddleware.cs`
- `Http/DataServerExceptionFilter.cs` (exception → problem details)
- `Logging/RollingFileLoggerProvider.cs`

**Indexing/**
- `IIndexManager.cs` / `IndexManager.cs` (cache hub, lifecycle)
- `IndexStatus.cs` (enum and status model)
- `IndexReloadWorker.cs` (BackgroundService for auto-reload)
- `PivotEngineCache.cs` (cache key prefixing)

**Data/Providers/**
- `IDataSourceProvider.cs` (interface)
- `JsonDataProvider.cs`
- `CsvDataProvider.cs` (with separator handling)
- `DatabaseDataProvider.cs`
- `IDatabaseQueryProvider.cs` (interface)
- `SqliteQueryProvider.cs` (mandatory)
- `SqlServerQueryProvider.cs` (optional stub)
- `PostgresqlQueryProvider.cs` (optional stub)
- `MySqlQueryProvider.cs` (optional stub)
- `OracleQueryProvider.cs` (optional stub)
- `MariadbQueryProvider.cs` (optional stub)
- `DataSourceProviderFactory.cs`

**Security/**
- `Controllers/SecurityController.cs` (user management)
- `Services/UserService.cs` (CRUD, hashing, verification)

**Reports/**
- `Services/ReportSharingService.cs` (save/get/delete/expire)
- `Services/ReportSweepWorker.cs` (expiry/quota background worker)
- `Controllers/ReportsController.cs` (API endpoints)
- `Models/ReportMetadata.cs`

**DataServer/** (runtime; gitignored)
- `config.json`
- `indexes.json`
- `users.json`
- `reports/` (directory)

---

## Integration Points

### With Existing PivotController
- `FetchData` deserialization unchanged (added optional `Index` field)
- Pivot cache keys prefixed with index name
- Existing pivot actions (fetchFieldMembers, fetchRawData, exports) routed via IndexManager

### With Program.cs
- New service registration (IIndexManager, ConfigService, UserService, etc.)
- New middleware (BasicAuth)
- New background workers (IndexReloadWorker, ReportSweepWorker)
- New exception filter (DataServerExceptionFilter)

### With ASP.NET Core
- Built-in MemoryCache with SizeLimit
- Built-in ProblemDetails (RFC 7807)
- Built-in X509Certificate2 and Kestrel HTTPS
- Built-in FileSystemWatcher
- Built-in Logging (Console + custom RollingFileLogger)

---

## Design Decisions & Rationale

1. **IndexManager as Cache Hub** — Centralized per-index data management prevents race conditions and simplifies reload semantics
2. **Keep-Data-on-Refresh** — Two-slot cache entry prevents blank displays if reload fails
3. **Atomic JSON Writes** — Temp-file-rename pattern prevents corruption from partial writes
4. **PBKDF2 Built-In** — No additional crypto packages required; 100,000 iterations + SHA-256
5. **Provider Factory Pattern** — Easy to add new data source types without modifying controllers
6. **Configuration as Code** — JSON file editable by users; hot-reload applied without restart
7. **Index Immutability** — Cannot change source type after creation (Flexmonster-like rule)
8. **Secrets by Reference** — Passwords/certificates referenced by name, never persisted

---

## Next Steps

1. **Finalize spec.md** — Detailed testable requirements with scenarios
2. **Create tasks.md** — Implementation task breakdown with dependencies
3. **Begin Step 1** — Index Configuration (IndexManager, bootstrap, endpoints)
